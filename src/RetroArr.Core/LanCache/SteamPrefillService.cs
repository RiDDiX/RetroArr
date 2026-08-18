using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.LanCache
{
    // Orchestrates the bundled SteamPrefill CLI (https://github.com/tpill90/steam-lancache-prefill)
    // as a subprocess to warm a LanCache with a Steam library. This service is
    // deliberately defensive: if the binary is missing or the user has not logged
    // in yet, every method degrades gracefully instead of throwing. The one-time
    // interactive Steam login is done by the user (docker exec); afterwards the
    // cached session lets `prefill` run non-interactively.
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public sealed class SteamPrefillService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly string _binaryPath;
        private readonly string _configDir;
        private static readonly SemaphoreSlim _runLock = new(1, 1);

        // Live progress of the most recent / current run (in-memory only).
        private readonly object _stateLock = new();
        private bool _running;
        private readonly List<string> _recentLog = new();
        private DateTime? _lastRunUtc;
        private int? _lastExitCode;

        public SteamPrefillService(ConfigurationService configService)
        {
            _binaryPath = Environment.GetEnvironmentVariable("RETROARR_STEAMPREFILL_BIN")
                          ?? "/opt/steamprefill/SteamPrefill";
            // SteamPrefill keeps its session + state in <binaryDir>/Config; the container
            // entrypoint symlinks that to the persistent config volume.
            _configDir = Path.Combine(Path.GetDirectoryName(_binaryPath) ?? "/opt/steamprefill", "Config");
        }

        public bool IsAvailable() => TryStat(_binaryPath);

        // A cached Steam session means `prefill` can run without interactive login.
        public bool IsLoggedIn() => TryStat(Path.Combine(_configDir, "account.config"));

        // AppIds SteamPrefill has already successfully prefilled (used to reconcile
        // the owned library against what is warmed in the cache).
        public HashSet<int> GetPrefilledAppIds()
        {
            var result = new HashSet<int>();
            var path = Path.Combine(_configDir, "successfullyDownloadedDepots.json");
            try
            {
                if (!File.Exists(path)) return result;
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                CollectInts(doc.RootElement, result);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[SteamPrefill] could not read prefill state: {ex.Message}");
            }
            return result;
        }

        public PrefillStatus GetStatus()
        {
            lock (_stateLock)
            {
                return new PrefillStatus
                {
                    Available = IsAvailable(),
                    LoggedIn = IsLoggedIn(),
                    Running = _running,
                    PrefilledCount = GetPrefilledAppIds().Count,
                    LastRunUtc = _lastRunUtc,
                    LastExitCode = _lastExitCode,
                    RecentLog = _recentLog.ToList()
                };
            }
        }

        public async Task<PrefillRunResult> RunPrefillAsync(LanCacheSettings settings, CancellationToken ct = default)
        {
            if (!IsAvailable())
                return PrefillRunResult.Fail("SteamPrefill binary not found. Rebuild/pull the image so it is bundled.");
            if (!IsLoggedIn())
                return PrefillRunResult.Fail("Not logged in to Steam. Run the one-time login first: docker exec -it retroarr /opt/steamprefill/SteamPrefill select-apps");

            if (!await _runLock.WaitAsync(0, ct).ConfigureAwait(false))
                return PrefillRunResult.Fail("A prefill run is already in progress.");

            try
            {
                lock (_stateLock) { _running = true; _recentLog.Clear(); }

                var args = new List<string> { "prefill", "--no-ansi", "--force" };
                if (settings.PrefillAllOwned) args.Add("--all");
                if (settings.PrefillRecent) args.Add("--recent");
                var os = string.IsNullOrWhiteSpace(settings.PrefillOs) ? "windows" : settings.PrefillOs;
                args.Add("--os"); args.Add(os);

                var psi = new ProcessStartInfo
                {
                    FileName = _binaryPath,
                    WorkingDirectory = Path.GetDirectoryName(_binaryPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                _logger.Info($"[SteamPrefill] running: {_binaryPath} {string.Join(' ', args)}");
                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (_, e) => Append(e.Data);
                proc.ErrorDataReceived += (_, e) => Append(e.Data);

                if (!proc.Start())
                    return PrefillRunResult.Fail("Failed to start SteamPrefill.");
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                await proc.WaitForExitAsync(ct).ConfigureAwait(false);

                lock (_stateLock)
                {
                    _lastExitCode = proc.ExitCode;
                    _lastRunUtc = DateTime.UtcNow;
                }
                return proc.ExitCode == 0
                    ? PrefillRunResult.Ok(GetPrefilledAppIds().Count)
                    : PrefillRunResult.Fail($"SteamPrefill exited with code {proc.ExitCode}.");
            }
            catch (OperationCanceledException)
            {
                return PrefillRunResult.Fail("Prefill run cancelled.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[SteamPrefill] run failed: {ex.Message}");
                return PrefillRunResult.Fail($"Prefill run failed: {ex.Message}");
            }
            finally
            {
                lock (_stateLock) { _running = false; }
                _runLock.Release();
            }
        }

        private void Append(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (_stateLock)
            {
                _recentLog.Add(line);
                // Keep only the last 200 lines to bound memory.
                if (_recentLog.Count > 200) _recentLog.RemoveRange(0, _recentLog.Count - 200);
            }
        }

        private static bool TryStat(string path)
        {
            try { return File.Exists(path); } catch { return false; }
        }

        private static void CollectInts(JsonElement el, HashSet<int> acc)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Number:
                    if (el.TryGetInt32(out var n)) acc.Add(n);
                    break;
                case JsonValueKind.String:
                    if (int.TryParse(el.GetString(), out var s)) acc.Add(s);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray()) CollectInts(item, acc);
                    break;
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out var key)) acc.Add(key);
                        CollectInts(prop.Value, acc);
                    }
                    break;
            }
        }
    }

    public sealed class PrefillStatus
    {
        public bool Available { get; set; }
        public bool LoggedIn { get; set; }
        public bool Running { get; set; }
        public int PrefilledCount { get; set; }
        public DateTime? LastRunUtc { get; set; }
        public int? LastExitCode { get; set; }
        [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<string> RecentLog { get; set; } = new();
    }

    public sealed class PrefillRunResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public int PrefilledCount { get; set; }
        public static PrefillRunResult Ok(int count) => new() { Success = true, PrefilledCount = count };
        public static PrefillRunResult Fail(string msg) => new() { Success = false, Message = msg };
    }
}

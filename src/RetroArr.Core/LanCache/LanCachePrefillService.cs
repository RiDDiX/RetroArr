using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.LanCache
{
    // Orchestrates tpill90's *-lancache-prefill CLIs (Steam / Battle.net / Epic)
    // as subprocesses to warm a LanCache. All three tools share the same shape
    // (CliFx, a Config dir next to the binary, a `prefill --no-ansi` command, a
    // session file and a successfullyDownloaded* state file), so one generic
    // service drives all of them. Deliberately defensive: a missing binary or a
    // missing login degrades gracefully instead of throwing.
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public sealed class LanCachePrefillService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);

        private sealed class ProviderDef
        {
            public string Id = "";
            public string Name = "";
            public string BinaryPath = "";
            public string SessionFile = "";   // empty => no login required (public CDN)
            public string StateFile = "";
            public bool SupportsOs;
            public string ConfigDir => Path.Combine(Path.GetDirectoryName(BinaryPath) ?? "/opt", "Config");
            public bool RequiresLogin => !string.IsNullOrEmpty(SessionFile);
        }

        private sealed class RunState
        {
            public readonly SemaphoreSlim Lock = new(1, 1);
            public bool Running;
            public readonly List<string> Log = new();
            public DateTime? LastRunUtc;
            public int? LastExitCode;
        }

        private readonly Dictionary<string, ProviderDef> _providers;
        private readonly Dictionary<string, RunState> _state;
        private readonly object _sync = new();

        public LanCachePrefillService(ConfigurationService configService)
        {
            string Bin(string env, string dflt) => Environment.GetEnvironmentVariable(env) ?? dflt;
            _providers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["steam"] = new ProviderDef {
                    Id = "steam", Name = "Steam",
                    BinaryPath = Bin("RETROARR_STEAMPREFILL_BIN", "/opt/steamprefill/SteamPrefill"),
                    SessionFile = "account.config", StateFile = "successfullyDownloadedDepots.json", SupportsOs = true },
                ["battlenet"] = new ProviderDef {
                    Id = "battlenet", Name = "Battle.net",
                    BinaryPath = Bin("RETROARR_BATTLENETPREFILL_BIN", "/opt/battlenetprefill/BattleNetPrefill"),
                    SessionFile = "", StateFile = "successfullyDownloadedApps.json", SupportsOs = false },
                ["epic"] = new ProviderDef {
                    Id = "epic", Name = "Epic",
                    BinaryPath = Bin("RETROARR_EPICPREFILL_BIN", "/opt/epicprefill/EpicPrefill"),
                    SessionFile = "userAccount.json", StateFile = "successfullyDownloadedApps.json", SupportsOs = false },
            };
            _state = new(StringComparer.OrdinalIgnoreCase);
            foreach (var k in _providers.Keys) _state[k] = new RunState();
        }

        private static bool TryStat(string path)
        {
            try { return File.Exists(path); } catch { return false; }
        }

        private bool IsAvailable(ProviderDef p) => TryStat(p.BinaryPath);
        private bool IsLoggedIn(ProviderDef p) => !p.RequiresLogin || TryStat(Path.Combine(p.ConfigDir, p.SessionFile));

        public HashSet<int> GetPrefilledAppIds(string providerId)
        {
            var result = new HashSet<int>();
            if (!_providers.TryGetValue(providerId, out var p)) return result;
            try
            {
                var path = Path.Combine(p.ConfigDir, p.StateFile);
                if (!File.Exists(path)) return result;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                CollectInts(doc.RootElement, result);
            }
            catch (Exception ex) { _logger.Warn($"[Prefill:{providerId}] state read failed: {ex.Message}"); }
            return result;
        }

        // The app-selection file SteamPrefill's `select-apps` writes: a bare JSON
        // array of uint appIds. RetroArr reads/writes the SAME file so the Web-GUI
        // picker and the CLI stay interchangeable. Only Steam uses this exact
        // format, so selection I/O is Steam-only for now.
        private const string SelectedAppsFileName = "selectedAppsToPrefill.json";

        public List<uint> GetSelectedAppIds(string providerId)
        {
            var result = new List<uint>();
            if (!string.Equals(providerId, "steam", StringComparison.OrdinalIgnoreCase)) return result;
            if (!_providers.TryGetValue(providerId, out var p)) return result;
            try
            {
                var path = Path.Combine(p.ConfigDir, SelectedAppsFileName);
                if (!File.Exists(path)) return result;
                var parsed = JsonSerializer.Deserialize<List<uint>>(File.ReadAllText(path));
                if (parsed != null) result = parsed;
            }
            catch (Exception ex) { _logger.Warn($"[Prefill:{providerId}] selection read failed: {ex.Message}"); }
            return result;
        }

        public bool SetSelectedAppIds(string providerId, IEnumerable<uint> appIds)
        {
            if (!string.Equals(providerId, "steam", StringComparison.OrdinalIgnoreCase)) return false;
            if (!_providers.TryGetValue(providerId, out var p)) return false;
            try
            {
                var list = new List<uint>();
                var seen = new HashSet<uint>();
                foreach (var id in appIds)
                    if (id != 0 && seen.Add(id)) list.Add(id);

                Directory.CreateDirectory(p.ConfigDir);
                var path = Path.Combine(p.ConfigDir, SelectedAppsFileName);
                File.WriteAllText(path, JsonSerializer.Serialize(list));
                _logger.Info($"[Prefill:{providerId}] saved {list.Count} selected app(s) to {path}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"[Prefill:{providerId}] selection write failed: {ex.Message}");
                return false;
            }
        }

        public List<PrefillProviderStatus> GetAllStatus()
        {
            var list = new List<PrefillProviderStatus>();
            foreach (var p in _providers.Values)
            {
                var st = _state[p.Id];
                lock (_sync)
                {
                    list.Add(new PrefillProviderStatus
                    {
                        Id = p.Id,
                        Name = p.Name,
                        RequiresLogin = p.RequiresLogin,
                        SupportsOs = p.SupportsOs,
                        Available = IsAvailable(p),
                        LoggedIn = IsLoggedIn(p),
                        Running = st.Running,
                        PrefilledCount = GetPrefilledAppIds(p.Id).Count,
                        LastRunUtc = st.LastRunUtc,
                        LastExitCode = st.LastExitCode,
                        LoginCommand = p.RequiresLogin ? $"docker exec -it retroarr {p.BinaryPath} select-apps" : null,
                        RecentLog = new List<string>(st.Log)
                    });
                }
            }
            return list;
        }

        public bool IsAvailable(string providerId) => _providers.TryGetValue(providerId, out var p) && IsAvailable(p);
        public bool IsLoggedIn(string providerId) => _providers.TryGetValue(providerId, out var p) && IsLoggedIn(p);
        public bool IsRunning(string providerId) => _state.TryGetValue(providerId, out var s) && s.Running;

        public async Task<PrefillRunResult> RunPrefillAsync(string providerId, LanCacheSettings settings, CancellationToken ct = default)
        {
            if (!_providers.TryGetValue(providerId, out var p))
                return PrefillRunResult.Fail($"Unknown prefill provider '{providerId}'.");
            if (!IsAvailable(p))
                return PrefillRunResult.Fail($"{p.Name}Prefill binary not found. Rebuild/pull the image so it is bundled.");
            if (!IsLoggedIn(p))
                return PrefillRunResult.Fail($"Not logged in. One-time login: docker exec -it retroarr {p.BinaryPath} select-apps");

            var st = _state[p.Id];
            if (!await st.Lock.WaitAsync(0, ct).ConfigureAwait(false))
                return PrefillRunResult.Fail("A prefill run is already in progress for this provider.");

            try
            {
                lock (_sync) { st.Running = true; st.Log.Clear(); }

                var args = new List<string> { "prefill", "--no-ansi", "--force" };
                if (settings.PrefillAllOwned) args.Add("--all");
                if (p.Id == "steam")
                {
                    if (settings.PrefillRecent) args.Add("--recent");
                    if (p.SupportsOs)
                    {
                        args.Add("--os");
                        args.Add(string.IsNullOrWhiteSpace(settings.PrefillOs) ? "windows" : settings.PrefillOs);
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = p.BinaryPath,
                    WorkingDirectory = Path.GetDirectoryName(p.BinaryPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                _logger.Info($"[Prefill:{p.Id}] running: {p.BinaryPath} {string.Join(' ', args)}");
                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (_, e) => Append(st, e.Data);
                proc.ErrorDataReceived += (_, e) => Append(st, e.Data);

                if (!proc.Start()) return PrefillRunResult.Fail($"Failed to start {p.Name}Prefill.");
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);

                lock (_sync) { st.LastExitCode = proc.ExitCode; st.LastRunUtc = DateTime.UtcNow; }
                return proc.ExitCode == 0
                    ? PrefillRunResult.Ok(GetPrefilledAppIds(p.Id).Count)
                    : PrefillRunResult.Fail($"{p.Name}Prefill exited with code {proc.ExitCode}.");
            }
            catch (OperationCanceledException) { return PrefillRunResult.Fail("Prefill run cancelled."); }
            catch (Exception ex)
            {
                _logger.Error($"[Prefill:{p.Id}] run failed: {ex.Message}");
                return PrefillRunResult.Fail($"Prefill run failed: {ex.Message}");
            }
            finally
            {
                lock (_sync) { st.Running = false; }
                st.Lock.Release();
            }
        }

        private void Append(RunState st, string? line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (_sync)
            {
                st.Log.Add(line);
                if (st.Log.Count > 200) st.Log.RemoveRange(0, st.Log.Count - 200);
            }
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

    public sealed class PrefillProviderStatus
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool RequiresLogin { get; set; }
        public bool SupportsOs { get; set; }
        public bool Available { get; set; }
        public bool LoggedIn { get; set; }
        public bool Running { get; set; }
        public int PrefilledCount { get; set; }
        public DateTime? LastRunUtc { get; set; }
        public int? LastExitCode { get; set; }
        public string? LoginCommand { get; set; }
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

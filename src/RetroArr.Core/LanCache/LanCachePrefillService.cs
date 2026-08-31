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
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.LanCachePrefill);

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
            // Every game the current run touched (parsed from the tool's "Starting X"
            // lines as they stream, so it survives the 200-line log cap).
            public readonly List<string> Games = new();
            public DateTime? LastRunUtc;
            public int? LastExitCode;
            // Live handle of the running prefill process, so a run can be stopped.
            public Process? Process;
            public bool StopRequested;
            public DateTime? NextRunUtc;
        }

        private readonly Dictionary<string, ProviderDef> _providers;
        private readonly Dictionary<string, RunState> _state;
        private readonly object _sync = new();
        private readonly string _historyPath;
        private readonly object _historySync = new();
        private const int MaxHistoryPerProvider = 50;

        public LanCachePrefillService(ConfigurationService configService)
        {
            _historyPath = Path.Combine(configService.GetConfigDirectory(), "prefill-history.json");
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
                        NextRunUtc = st.NextRunUtc,
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

        // CLI args for one prefill run. internal static for unit tests
        // (RetroArr.Core.Test has InternalsVisibleTo).
        // --force re-downloads every selected app; that is a manual reseed/benchmark
        // knob, not a nightly one. Scheduled runs therefore use the tools' default
        // incremental mode, which only fetches new/updated (or previously failed)
        // content.
        internal static List<string> BuildPrefillArgs(string providerId, bool supportsOs, LanCacheSettings settings,
                                                      bool hasSelection, string trigger)
        {
            var args = new List<string> { "prefill", "--no-ansi" };
            if (string.Equals(trigger, "manual", StringComparison.OrdinalIgnoreCase)) args.Add("--force");
            // A saved app selection ALWAYS wins: passing --all would download the
            // whole library and defeat the point. Only fall back to --all when the
            // user asked for it AND there is no selection to honor.
            if (settings.PrefillAllOwned && !hasSelection) args.Add("--all");
            if (string.Equals(providerId, "steam", StringComparison.OrdinalIgnoreCase))
            {
                if (settings.PrefillRecent) args.Add("--recent");
                if (supportsOs)
                {
                    args.Add("--os");
                    args.Add(string.IsNullOrWhiteSpace(settings.PrefillOs) ? "windows" : settings.PrefillOs);
                }
            }
            return args;
        }

        public async Task<PrefillRunResult> RunPrefillAsync(string providerId, LanCacheSettings settings, string trigger = "manual", CancellationToken ct = default)
        {
            var startedUtc = DateTime.UtcNow;
            if (!_providers.TryGetValue(providerId, out var p))
                return PrefillRunResult.Fail($"Unknown prefill provider '{providerId}'.");
            if (!IsAvailable(p))
            {
                RecordRun(p.Id, startedUtc, trigger, "skipped", null, "Binary not bundled.", null, null);
                return PrefillRunResult.Fail($"{p.Name}Prefill binary not found. Rebuild/pull the image so it is bundled.");
            }
            if (!IsLoggedIn(p))
            {
                RecordRun(p.Id, startedUtc, trigger, "skipped", null, "Not logged in.", null, null);
                return PrefillRunResult.Fail($"Not logged in. One-time login: docker exec -it retroarr {p.BinaryPath} select-apps");
            }

            var st = _state[p.Id];
            if (!await st.Lock.WaitAsync(0, ct).ConfigureAwait(false))
            {
                RecordRun(p.Id, startedUtc, trigger, "skipped", null, "Already running.", null, null);
                return PrefillRunResult.Fail("A prefill run is already in progress for this provider.");
            }

            try
            {
                lock (_sync) { st.Running = true; st.StopRequested = false; st.Log.Clear(); st.Games.Clear(); }

                var args = BuildPrefillArgs(p.Id, p.SupportsOs, settings, GetSelectedAppIds(p.Id).Count > 0, trigger);

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

                if (!proc.Start())
                {
                    RecordRun(p.Id, startedUtc, trigger, "failed", null, "Failed to start process.", null, null);
                    return PrefillRunResult.Fail($"Failed to start {p.Name}Prefill.");
                }
                lock (_sync) { st.Process = proc; }
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);

                bool stopped;
                List<string> games;
                int exitCode;
                lock (_sync)
                {
                    st.LastExitCode = proc.ExitCode;
                    st.LastRunUtc = DateTime.UtcNow;
                    st.Process = null;
                    stopped = st.StopRequested;
                    games = new List<string>(st.Games);
                    exitCode = proc.ExitCode;
                }

                if (stopped)
                {
                    RecordRun(p.Id, startedUtc, trigger, "stopped", exitCode, "Stopped by user / end of window.",
                              games, games.Count > 0 ? games[^1] : null);
                    return PrefillRunResult.Fail("Prefill stopped by user.");
                }
                if (exitCode == 0)
                {
                    RecordRun(p.Id, startedUtc, trigger, "completed", exitCode, null, games, null);
                    return PrefillRunResult.Ok(GetPrefilledAppIds(p.Id).Count);
                }
                RecordRun(p.Id, startedUtc, trigger, "failed", exitCode, $"Exited with code {exitCode}.", games, null);
                return PrefillRunResult.Fail($"{p.Name}Prefill exited with code {exitCode}.");
            }
            catch (OperationCanceledException)
            {
                RecordRun(p.Id, startedUtc, trigger, "stopped", null, "Cancelled.",
                          GetGamesSnapshot(st), null);
                return PrefillRunResult.Fail("Prefill run cancelled.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Prefill:{p.Id}] run failed: {ex.Message}");
                RecordRun(p.Id, startedUtc, trigger, "failed", null, ex.Message, GetGamesSnapshot(st), null);
                return PrefillRunResult.Fail($"Prefill run failed: {ex.Message}");
            }
            finally
            {
                lock (_sync) { st.Running = false; st.Process = null; }
                st.Lock.Release();
            }
        }

        // Stop a running prefill for one provider. Kills the whole process tree
        // (the tools spawn download workers). Safe to call when nothing runs.
        public bool StopPrefill(string providerId)
        {
            if (!_providers.ContainsKey(providerId)) return false;
            if (!_state.TryGetValue(providerId, out var st)) return false;

            Process? proc;
            lock (_sync)
            {
                if (!st.Running || st.Process == null) return false;
                st.StopRequested = true;
                proc = st.Process;
            }

            try
            {
                proc.Kill(entireProcessTree: true);
                _logger.Info($"[Prefill:{providerId}] stop requested by user.");
                Append(st, "[RetroArr] Prefill stopped by user.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Prefill:{providerId}] stop failed: {ex.Message}");
                return false;
            }
        }

        // Scheduler bookkeeping: the hosted service publishes the next planned run so
        // the UI can show it.
        public void SetNextRunUtc(string providerId, DateTime? nextUtc)
        {
            if (_state.TryGetValue(providerId, out var st)) lock (_sync) { st.NextRunUtc = nextUtc; }
        }

        private void Append(RunState st, string? line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (_sync)
            {
                st.Log.Add(line);
                if (st.Log.Count > 200) st.Log.RemoveRange(0, st.Log.Count - 200);

                // Track processed games from "... Starting <name>" lines. Keeps the
                // full list even after the log buffer is trimmed.
                var game = ExtractStartingGame(line);
                if (game != null) st.Games.Add(game);
            }
        }

        // Pull the game name out of a "[timestamp] Starting <name>" progress line.
        // internal for unit tests (RetroArr.Core.Test has InternalsVisibleTo).
        internal static string? ExtractStartingGame(string line)
        {
            var s = line;
            if (s.StartsWith("[", StringComparison.Ordinal))
            {
                var close = s.IndexOf(']');
                if (close > 0 && close < s.Length - 1) s = s.Substring(close + 1);
            }
            s = s.TrimStart();
            const string marker = "Starting";
            if (!s.StartsWith(marker, StringComparison.Ordinal)) return null;
            // Require a separator so "StartingSomething" isn't treated as a game.
            if (s.Length > marker.Length && !char.IsWhiteSpace(s[marker.Length])) return null;
            var name = s.Substring(marker.Length).Trim();
            if (name.Length == 0) return null;
            // Ignore the tool's own "Starting login!" banner.
            if (name.StartsWith("login", StringComparison.OrdinalIgnoreCase)) return null;
            return name;
        }

        // ---- Persistent run history (one JSON file, capped per provider) ----

        private Dictionary<string, List<PrefillRunRecord>> LoadHistory()
        {
            lock (_historySync)
            {
                try
                {
                    if (!File.Exists(_historyPath)) return new(StringComparer.OrdinalIgnoreCase);
                    var json = File.ReadAllText(_historyPath);
                    return JsonSerializer.Deserialize<Dictionary<string, List<PrefillRunRecord>>>(json)
                           ?? new(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[Prefill] history read failed: {ex.Message}");
                    return new(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        private void AppendHistory(PrefillRunRecord rec)
        {
            lock (_historySync)
            {
                try
                {
                    var all = LoadHistory();
                    if (!all.TryGetValue(rec.Provider, out var list)) { list = new(); all[rec.Provider] = list; }
                    list.Insert(0, rec); // newest first
                    if (list.Count > MaxHistoryPerProvider)
                        list.RemoveRange(MaxHistoryPerProvider, list.Count - MaxHistoryPerProvider);
                    File.WriteAllText(_historyPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[Prefill] history write failed: {ex.Message}");
                }
            }
        }

        public Dictionary<string, List<PrefillRunRecord>> GetAllHistory() => LoadHistory();

        private List<string> GetGamesSnapshot(RunState st)
        {
            lock (_sync) { return new List<string>(st.Games); }
        }

        private void RecordRun(string providerId, DateTime startedUtc, string trigger, string outcome,
                               int? exitCode, string? message, List<string>? games, string? stoppedAt)
        {
            AppendHistory(new PrefillRunRecord
            {
                Provider = providerId,
                StartedUtc = startedUtc,
                FinishedUtc = DateTime.UtcNow,
                Trigger = trigger,
                Outcome = outcome,
                ExitCode = exitCode,
                Message = message,
                StoppedAt = stoppedAt,
                Games = games ?? new List<string>()
            });
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
        public DateTime? NextRunUtc { get; set; }
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

    // One persisted prefill run (kept in prefill-history.json, newest first).
    public sealed class PrefillRunRecord
    {
        public string Provider { get; set; } = "";
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public string Trigger { get; set; } = "manual";  // manual | scheduled
        public string Outcome { get; set; } = "";         // completed | stopped | failed | skipped
        public int? ExitCode { get; set; }
        public string? Message { get; set; }
        public string? StoppedAt { get; set; }            // last game when stopped mid-run
        [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<string> Games { get; set; } = new();  // games processed this run
    }
}

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.LanCache
{
    // Runs each provider's prefill on its own schedule (local server time).
    // Ticks once a minute, re-reading settings so schedule edits apply without a
    // restart. A provider fires when its HH:mm minute is reached on a matching
    // weekday and no run is already in progress for it.
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public sealed class PrefillSchedulerService : BackgroundService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.LanCachePrefill);
        private readonly ConfigurationService _config;
        private readonly LanCachePrefillService _prefill;

        // Remember the last minute we fired per provider so a run triggers once.
        private readonly System.Collections.Generic.Dictionary<string, DateTime> _lastFired =
            new(StringComparer.OrdinalIgnoreCase);

        public PrefillSchedulerService(ConfigurationService config, LanCachePrefillService prefill)
        {
            _config = config;
            _prefill = prefill;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let the rest of the app finish wiring up first.
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false); }
            catch (TaskCanceledException) { return; }

            LogActiveSchedules();

            while (!stoppingToken.IsCancellationRequested)
            {
                try { Tick(stoppingToken); }
                catch (Exception ex) { _logger.Warn($"[PrefillScheduler] tick failed: {ex.Message}"); }

                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false); }
                catch (TaskCanceledException) { break; }
            }
        }

        // One startup line per provider so the log proves the scheduler is alive and
        // which windows it will honor (the user asked to be able to see this).
        private void LogActiveSchedules()
        {
            try
            {
                var settings = _config.LoadLanCacheSettings();
                if (!settings.Enabled)
                {
                    _logger.Info("[PrefillScheduler] started - LanCache integration is disabled, no runs will fire.");
                    return;
                }
                if (settings.Schedules == null || settings.Schedules.Count == 0)
                {
                    _logger.Info("[PrefillScheduler] started - no schedules configured.");
                    return;
                }
                foreach (var kv in settings.Schedules)
                {
                    var s = kv.Value;
                    if (s == null) continue;
                    var days = (s.Days == null || s.Days.Count == 0) ? "every day" : string.Join(",", s.Days);
                    _logger.Info(s.Enabled
                        ? $"[PrefillScheduler] {kv.Key}: enabled, window {s.StartTime}{(string.IsNullOrWhiteSpace(s.EndTime) ? "" : $"-{s.EndTime}")} on {days}."
                        : $"[PrefillScheduler] {kv.Key}: schedule disabled.");
                }
            }
            catch (Exception ex) { _logger.Warn($"[PrefillScheduler] could not log schedules: {ex.Message}"); }
        }

        private void Tick(CancellationToken ct)
        {
            var settings = _config.LoadLanCacheSettings();
            if (!settings.Enabled || settings.Schedules == null) return;

            var now = DateTime.Now;
            foreach (var kv in settings.Schedules)
            {
                var providerId = kv.Key;
                var sched = kv.Value;
                if (sched == null || !sched.Enabled) continue;

                var next = ComputeNextRun(sched, now);
                _prefill.SetNextRunUtc(providerId, next?.ToUniversalTime());

                // Stop at the end of the window (if still running).
                if (IsAtMinute(sched.EndTime, now) && _prefill.IsRunning(providerId))
                {
                    _logger.Info($"[PrefillScheduler] end of window for {providerId} - stopping.");
                    _prefill.StopPrefill(providerId);
                    continue;
                }

                if (!IsDueToStart(sched, now)) continue;

                // Fire at most once per minute slot.
                var slot = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Local);
                if (_lastFired.TryGetValue(providerId, out var last) && last == slot) continue;
                _lastFired[providerId] = slot;

                if (_prefill.IsRunning(providerId))
                {
                    _logger.Info($"[PrefillScheduler] {providerId} is due but already running - skipping.");
                    continue;
                }

                _logger.Info($"[PrefillScheduler] starting scheduled prefill for {providerId}.");
                _ = Task.Run(async () =>
                {
                    try { await _prefill.RunPrefillAsync(providerId, settings, "scheduled", ct).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.Warn($"[PrefillScheduler] {providerId} run failed: {ex.Message}"); }
                }, ct);
            }
        }

        private static bool IsDueToStart(PrefillSchedule sched, DateTime now)
        {
            if (!IsAtMinute(sched.StartTime, now)) return false;
            return sched.Days == null || sched.Days.Count == 0 || sched.Days.Contains((int)now.DayOfWeek);
        }

        private static bool IsAtMinute(string? time, DateTime now)
        {
            if (!TryParseTime(time, out var h, out var m)) return false;
            return now.Hour == h && now.Minute == m;
        }

        // Next planned local start time, for display in the UI.
        public static DateTime? ComputeNextRun(PrefillSchedule sched, DateTime now)
        {
            if (sched == null || !sched.Enabled) return null;
            if (!TryParseTime(sched.StartTime, out var h, out var m)) return null;

            for (int i = 0; i < 8; i++)
            {
                var day = now.Date.AddDays(i);
                var candidate = day.AddHours(h).AddMinutes(m);
                if (candidate <= now) continue;
                if (sched.Days != null && sched.Days.Count > 0 && !sched.Days.Contains((int)candidate.DayOfWeek)) continue;
                return candidate;
            }
            return null;
        }

        private static bool TryParseTime(string? value, out int hour, out int minute)
        {
            hour = 0; minute = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var parts = value.Split(':');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minute)) return false;
            return hour is >= 0 and <= 23 && minute is >= 0 and <= 59;
        }
    }
}

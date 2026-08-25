const fs = require('fs');
const path = require('path');
const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

const svc = read('src', 'RetroArr.Core', 'LanCache', 'LanCachePrefillService.cs');
const sched = read('src', 'RetroArr.Core', 'LanCache', 'PrefillSchedulerService.cs');
const ctrl = read('src', 'RetroArr.Api.V3', 'LanCache', 'LanCacheController.cs');
const logsvc = read('src', 'RetroArr.Core', 'Logging', 'AppLoggerService.cs');
const client = read('frontend', 'src', 'api', 'client.ts');
const tab = read('frontend', 'src', 'components', 'settings', 'LanCacheTab.tsx');

// --- Persistent run history ---
assert(svc.includes('prefill-history.json'), 'history must persist to prefill-history.json');
assert(svc.includes('class PrefillRunRecord'), 'PrefillRunRecord model required');
for (const field of ['StartedUtc', 'FinishedUtc', 'Trigger', 'Outcome', 'StoppedAt', 'Games']) {
  assert(svc.includes(field), `history record must carry ${field}`);
}
assert(svc.includes('MaxHistoryPerProvider'), 'history must be capped per provider');
assert(svc.includes('GetAllHistory'), 'service must expose the history');

// Every terminal path of a run has to be recorded, otherwise the user cannot tell
// whether a scheduled job fired.
for (const outcome of ['"completed"', '"stopped"', '"failed"', '"skipped"']) {
  assert(svc.includes(`trigger, ${outcome}`), `run outcome ${outcome} must be recorded`);
}

// Games are collected from the streaming output (not the capped log buffer).
assert(svc.includes('ExtractStartingGame'), 'processed games must be parsed from progress output');
assert(/internal static string\? ExtractStartingGame/.test(svc), 'parser must be internal so it can be unit tested');
assert(svc.includes('st.Games.Clear()'), 'per-run game list must reset at run start');

// --- Trigger provenance: scheduled runs must be labelled as such ---
assert(sched.includes('"scheduled"'), 'scheduler must tag its runs as scheduled');
assert(/RunPrefillAsync\(string providerId, LanCacheSettings settings, string trigger/.test(svc),
  'RunPrefillAsync must take a trigger');

// --- Dedicated logger channel so runs are visible in the log settings ---
assert(logsvc.includes('LanCachePrefill') && logsvc.includes('lancache__prefill'),
  'prefill needs its own log channel/file');
assert(svc.includes('AppLoggerService.LanCachePrefill') && sched.includes('AppLoggerService.LanCachePrefill'),
  'prefill service and scheduler must log to that channel');
assert(sched.includes('LogActiveSchedules'), 'scheduler must log its active schedules on startup');

// --- API + UI ---
assert(ctrl.includes('[HttpGet("prefill/history")]'), 'history endpoint required');
assert(client.includes("apiClient.get<Record<string, PrefillRunRecord[]>>('/lancache/prefill/history')"),
  'client wrapper for history required');
assert(tab.includes('run history'), 'UI must expose the run history');
assert(tab.includes('Games processed'), 'UI must list the games a run processed');
assert(tab.includes('Stopped at:'), 'UI must show where a stopped run got to');

console.log('lancache-history: all contract checks passed');

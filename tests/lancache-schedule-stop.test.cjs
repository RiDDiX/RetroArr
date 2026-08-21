const fs = require('fs');
const path = require('path');
const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// Stop: process handle + kill tree + endpoint
const svc = read('src', 'RetroArr.Core', 'LanCache', 'LanCachePrefillService.cs');
assert(svc.includes('public bool StopPrefill(string providerId)'), 'StopPrefill required');
assert(svc.includes('entireProcessTree: true'), 'stop must kill the whole process tree');
assert(svc.includes('StopRequested'), 'stop must be distinguishable from a normal exit');
const ctrl = read('src', 'RetroArr.Api.V3', 'LanCache', 'LanCacheController.cs');
assert(ctrl.includes('[HttpPost("prefill/{provider}/stop")]'), 'per-provider stop endpoint required');

// Schedule: per-provider settings + hosted scheduler
const cfg = read('src', 'RetroArr.Core', 'Configuration', 'ConfigurationService.cs');
assert(cfg.includes('class PrefillSchedule') && cfg.includes('Schedules'), 'per-provider schedules in settings');
const sched = read('src', 'RetroArr.Core', 'LanCache', 'PrefillSchedulerService.cs');
assert(sched.includes('BackgroundService'), 'scheduler must be a hosted service');
assert(sched.includes('ComputeNextRun') && sched.includes('IsDue'), 'scheduler needs due/next-run logic');
assert(sched.includes('IsRunning(providerId)'), 'scheduler must not start a second run for a provider');
const program = read('src', 'RetroArr.Host', 'Program.cs');
assert(program.includes('AddHostedService<RetroArr.Core.LanCache.PrefillSchedulerService>'), 'scheduler must be registered');

// Frontend wiring
const client = read('frontend', 'src', 'api', 'client.ts');
assert(client.includes('stopPrefill:') && client.includes('/stop`'), 'client stopPrefill wrapper');
assert(client.includes('interface PrefillSchedule'), 'PrefillSchedule type exported');
const tab = read('frontend', 'src', 'components', 'settings', 'LanCacheTab.tsx');
assert(tab.includes('Stop') && tab.includes('stopPrefill('), 'UI stop button');
assert(tab.includes('on a schedule') && tab.includes('type="time"'), 'UI per-provider schedule editor');

console.log('lancache-schedule-stop: all contract checks passed');

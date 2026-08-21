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
assert(sched.includes('ComputeNextRun') && sched.includes('IsDueToStart'), 'scheduler needs start/next-run logic');
assert(sched.includes('IsAtMinute(sched.EndTime') && sched.includes('StopPrefill(providerId)'), 'scheduler must stop the run at the window end');
assert(sched.includes('IsRunning(providerId)'), 'scheduler must not start a second run for a provider');
const program = read('src', 'RetroArr.Host', 'Program.cs');
assert(program.includes('AddHostedService<RetroArr.Core.LanCache.PrefillSchedulerService>'), 'scheduler must be registered');

// Frontend wiring
const client = read('frontend', 'src', 'api', 'client.ts');
assert(client.includes('stopPrefill:') && client.includes('/stop`'), 'client stopPrefill wrapper');
assert(client.includes('interface PrefillSchedule') && client.includes('startTime') && client.includes('endTime'), 'PrefillSchedule window type exported');
const tab = read('frontend', 'src', 'components', 'settings', 'LanCacheTab.tsx');
assert(tab.includes('Stop') && tab.includes('stopPrefill('), 'UI stop button');
assert(tab.includes('on a schedule') && tab.includes('type="time"') && tab.includes('startTime') && tab.includes('endTime'), 'UI per-provider schedule window editor');
const cfg2 = read('src', 'RetroArr.Core', 'Configuration', 'ConfigurationService.cs');
assert(cfg2.includes('StartTime') && cfg2.includes('EndTime'), 'schedule model must be a time window');
assert(tab.includes('persistNoState') && tab.includes('saved automatically'), 'schedule edits must persist immediately');

console.log('lancache-schedule-stop: all contract checks passed');

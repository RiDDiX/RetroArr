const fs = require('fs');
const path = require('path');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// The re-check after a repair clears the last result, so the repair sets what it applied after it.
const settings = read('frontend', 'src', 'components', 'DatabaseSettings.tsx');
const repair = settings.slice(settings.indexOf('const handleRepair'), settings.indexOf('const beginReset'));
assert(repair.indexOf('await handleHealthCheck();') >= 0 && repair.indexOf('await handleHealthCheck();') < repair.indexOf('setRepairResult(response.data);'),
  'the repair must show what it applied after the re-check, which clears it');
assert(repair.includes("setMessage((current) => current ?? { type: 'success', text: 'Repair complete.' });"),
  'a failed re-check must keep its error');
assert(settings.includes('{repairResult.containerOrphansRemoved} container orphans removed'),
  'the result must say how many container orphans the repair removed');
console.log('database-repair-result: ok');

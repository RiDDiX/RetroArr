const fs = require('fs');
const path = require('path');

const source = fs.readFileSync(
  path.join(__dirname, '..', 'frontend', 'src', 'components', 'settings', 'AccountsTab.tsx'),
  'utf8'
);
const translations = fs.readFileSync(
  path.join(__dirname, '..', 'frontend', 'src', 'i18n', 'translations.ts'),
  'utf8'
);

if (!source.includes("apiClient.get('/epic/settings')")) {
  throw new Error('Epic settings must load via GET /epic/settings');
}

if (!source.includes("apiClient.get('/epic/auth/url')")) {
  throw new Error('Epic login must fetch GET /epic/auth/url');
}

if (!source.includes("apiClient.post('/epic/auth/code', { code: epicAuthCode.trim() })")) {
  throw new Error('Epic auth must POST /epic/auth/code with the code payload');
}

if (!source.includes("apiClient.post('/epic/sync', null, { timeout: 600000 })")) {
  throw new Error('Epic sync must POST /epic/sync with timeout 600000');
}

if (!source.includes("apiClient.delete('/epic/settings')")) {
  throw new Error('Epic disconnect must DELETE /epic/settings');
}

if (!source.includes("apiClient.get<EpicFreeGame[]>('/epic/free-games'")) {
  throw new Error('Epic free games must load via GET /epic/free-games');
}

if (!source.includes("setEpicDisplayName(epicRes.data.displayName || epicRes.data.accountId || '')")) {
  throw new Error('Epic settings success path must use displayName or accountId');
}

if (!source.includes("setEpicDisplayName(res.data.displayName || res.data.accountId || '')")) {
  throw new Error('Epic auth success path must use displayName or accountId');
}

for (const key of [
  'epicEnterCodeAlert',
  'epicAuthFailed',
  'epicSyncFailed',
  'epicSyncDone',
  'epicLoginStep1',
  'epicLoginStep2',
  'epicLoginStep3',
  'epicFreeGames',
  'epicFreeLoad',
  'epicFreeNow',
  'epicFreeSoon',
  'epicFreeClaim',
  'epicFreeNone',
  'epicFreeUntil',
  'epicFreeWindow',
  'epicAutoClaim',
  'epicAutoClaimHint',
  'added',
  'skipped',
  'failed',
]) {
  if (!translations.includes(`${key}:`)) {
    throw new Error(`Missing translation key: ${key}`);
  }
}

console.log('epic account contract ok');

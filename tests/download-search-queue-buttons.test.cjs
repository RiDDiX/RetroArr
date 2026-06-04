const fs = require('fs');
const path = require('path');

const gameDetails = fs.readFileSync(
  path.join(__dirname, '..', 'frontend', 'src', 'pages', 'GameDetails.tsx'),
  'utf8'
);

if (!gameDetails.includes("t('downloadsQueue')")) {
  throw new Error('game details search results must render a visible queue button');
}

if (!gameDetails.includes("handleDownloadWithPlatform(result.magnetUrl || result.downloadUrl")) {
  throw new Error('game details queue button must use the search result download source');
}

const monitorPanel = fs.readFileSync(
  path.join(__dirname, '..', 'frontend', 'src', 'components', 'MonitorPanel.tsx'),
  'utf8'
);

if (!monitorPanel.includes("className=\"monitor-result-queue\"")) {
  throw new Error('monitor search results must render a queue button');
}

if (!monitorPanel.includes("apiClient.post('/downloadclient/add'")) {
  throw new Error('monitor queue button must post to the download client add endpoint');
}

if (!monitorPanel.includes("const url = release.magnetUrl || release.downloadUrl;")) {
  throw new Error('monitor queue button must fall back from magnet to direct download');
}

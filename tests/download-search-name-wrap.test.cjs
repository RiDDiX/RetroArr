const fs = require('fs');
const path = require('path');

const css = fs.readFileSync(
  path.join(__dirname, '..', 'frontend', 'src', 'pages', 'GameDetails.css'),
  'utf8'
);

const titleBlock = css.match(/\.col-title \.title-link\s*\{([\s\S]*?)\}/);
if (!titleBlock) {
  throw new Error('title link style was not found');
}

if (/text-overflow\s*:\s*ellipsis/.test(titleBlock[1])) {
  throw new Error('download result titles must not be clipped');
}

if (!/(overflow-wrap|word-break)\s*:/.test(titleBlock[1])) {
  throw new Error('download result titles must wrap');
}

const indexerBlock = css.match(/\.col-indexer \.indexer-name\s*\{([\s\S]*?)\}/);
if (!indexerBlock) {
  throw new Error('indexer name style was not found');
}

if (!/(overflow-wrap|word-break)\s*:/.test(indexerBlock[1])) {
  throw new Error('indexer names must wrap');
}



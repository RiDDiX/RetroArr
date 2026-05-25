const fs = require('fs');
const path = require('path');

const source = fs.readFileSync(
  path.join(__dirname, '..', 'frontend', 'src', 'pages', 'GameDetails.tsx'),
  'utf8'
);

if (!source.includes('className="col-age sortable"')) {
  throw new Error('download search needs a sortable age column');
}

if (!source.includes("handleSort('publishDate')")) {
  throw new Error('age column must sort by publish date');
}

if (!/className="age"[\s\S]*result\.formattedAge/.test(source)) {
  throw new Error('download search must render formatted age');
}



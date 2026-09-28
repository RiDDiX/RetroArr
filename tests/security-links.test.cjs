const fs = require('fs');
const path = require('path');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// The API key stays out of URLs: native loads (emulator ROM, <img>/<video>, download links)
// get short-lived signed links from the server instead.
const client = read('frontend', 'src', 'api', 'client.ts');
assert(!client.includes('withApiKey') && !client.includes("'apiKey=' +"),
  'client.ts must not append the API key to URLs');

const details = read('frontend', 'src', 'pages', 'GameDetails.tsx');
assert(!details.includes('withApiKey'), 'GameDetails must not put the API key into URLs');
assert(details.includes('romUrl: response.data.romUrl'),
  'The emulator must use the signed ROM link from /emulator/{id}/playable');
assert(!details.includes('romUrl: `/api/v3/emulator/${id}/rom`'),
  'The emulator must not build an unsigned ROM link');
assert(details.includes('/files/download-link'),
  'File downloads must ask the server for a signed link');

const downloaders = read('frontend', 'src', 'components', 'settings', 'DownloadersTab.tsx');
assert(downloaders.includes('id: editingClient?.id,'),
  'Testing a saved download client must send its id so the server can use the stored secret');

const middleware = read('src', 'RetroArr.Api.V3', 'Auth', 'ApiKeyAuthMiddleware.cs');
assert(!middleware.includes('EndsWith("/rom"'), 'The ROM route must not be anonymous');
assert(middleware.includes('LocalRequest.IsLocal(context)'),
  'The loopback exemption must use the socket peer recorded before UseForwardedHeaders');

const program = read('src', 'RetroArr.Host', 'Program.cs');
const capture = program.indexOf('LocalRequest.CaptureSocketPeer(context)');
const forwarded = program.indexOf('app.UseForwardedHeaders(forwardedOpts)');
assert(capture > 0 && forwarded > capture, 'The socket peer must be recorded before UseForwardedHeaders');
assert(!/app\.UseDeveloperExceptionPage\(\);\s*\/\/ FORCE DEBUG/.test(program),
  'The developer exception page must not run in production');
assert(!program.includes('AllowAnyOrigin'), 'CORS must not be open to any origin');

console.log('security-links: all contract checks passed');

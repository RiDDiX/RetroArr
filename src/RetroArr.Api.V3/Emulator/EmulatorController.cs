using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Api.V3.Emulator
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class EmulatorController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.General);
        private readonly RetroArrDbContext _context;

        // Supported extensions for web emulation
        private static readonly string[] SupportedExtensions = new[]
        {
            // Nintendo
            ".nes", ".fds", ".sfc", ".smc", ".z64", ".n64", ".v64",
            ".gb", ".gbc", ".gba", ".nds", ".vb",
            // Sega
            ".sms", ".md", ".gen", ".smd", ".gg", ".32x",
            // Sony
            ".bin", ".iso", ".pbp", ".cso", ".chd",
            // Atari
            ".a26", ".a52", ".a78", ".lnx", ".j64",
            // Other
            ".pce", ".zip"
        };

        // PlatformDefinitions.Id to EmulatorJS core mapping
        // IMPORTANT: Keys are internal Platform IDs from PlatformDefinitions, NOT IGDB IDs
        private static readonly System.Collections.Generic.Dictionary<int, string> PlatformIdToCore = new()
        {
            // Nintendo
            { 40, "nes" },      // NES (PlatformDefinitions.Id=40)
            { 41, "snes" },     // SNES (PlatformDefinitions.Id=41)
            { 42, "n64" },      // N64 (PlatformDefinitions.Id=42)
            { 48, "nes" },      // Famicom Disk System (PlatformDefinitions.Id=48, uses NES core)
            { 49, "snes" },     // Super Famicom (PlatformDefinitions.Id=49, uses SNES core)
            { 50, "gb" },       // Game Boy (PlatformDefinitions.Id=50)
            { 51, "gbc" },      // Game Boy Color (PlatformDefinitions.Id=51)
            { 52, "gba" },      // Game Boy Advance (PlatformDefinitions.Id=52)
            { 53, "nds" },      // Nintendo DS (PlatformDefinitions.Id=53)
            { 55, "vb" },       // Virtual Boy (PlatformDefinitions.Id=55)
            // Sega
            { 61, "segaMS" },   // Master System (PlatformDefinitions.Id=61)
            { 62, "segaMD" },   // Mega Drive / Genesis (PlatformDefinitions.Id=62)
            { 63, "segaCD" },   // Sega CD (PlatformDefinitions.Id=63)
            { 64, "sega32x" },  // 32X (PlatformDefinitions.Id=64)
            { 65, "segaGG" },   // Game Gear (PlatformDefinitions.Id=65)
            { 66, "segaSaturn" }, // Saturn (PlatformDefinitions.Id=66)
            // Sony
            { 20, "psx" },      // PlayStation 1 (PlatformDefinitions.Id=20)
            { 25, "psp" },      // PSP (PlatformDefinitions.Id=25)
            // Atari
            { 80, "atari2600" }, // Atari 2600 (PlatformDefinitions.Id=80)
            { 81, "atari5200" }, // Atari 5200 (PlatformDefinitions.Id=81)
            { 82, "atari7800" }, // Atari 7800 (PlatformDefinitions.Id=82)
            { 83, "jaguar" },   // Atari Jaguar (PlatformDefinitions.Id=83)
            { 85, "lynx" },     // Atari Lynx (PlatformDefinitions.Id=85)
            // Arcade / Other
            { 100, "arcade" },  // Arcade MAME (PlatformDefinitions.Id=100)
            { 90, "pce" },      // PC Engine / TurboGrafx-16 (PlatformDefinitions.Id=90)
        };

        private readonly ConfigurationService _configService;

        // Whitelist of BIOS filenames that can be served. Anything else is rejected.
        private static readonly HashSet<string> KnownBiosFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "scph5500.bin", "scph5501.bin", "scph5502.bin", "scph7001.bin", "scph1001.bin", "ps1_rom.bin",
            "bios_CD_U.bin", "bios_CD_E.bin", "bios_CD_J.bin",
            "gba_bios.bin", "gb_bios.bin", "gbc_bios.bin", "sgb_bios.bin",
            "saturn_bios.bin", "mpr-17933.bin", "mpr-18811-mx1.bin", "mpr-19367-mx1.bin",
            "3do_bios.bin", "PSP_bios.bin", "disksys.rom", "lynxboot.img",
            "neogeo.zip", "pcfx.rom", "pce-cd-bios.bin"
        };

        public EmulatorController(RetroArrDbContext context, ConfigurationService configService)
        {
            _context = context;
            _configService = configService;
        }

        [HttpGet("bios")]
        public ActionResult ListBios()
        {
            var mediaSettings = _configService.LoadMediaSettings();
            var biosDir = string.IsNullOrWhiteSpace(mediaSettings.BiosPath)
                ? Path.Combine(_configService.GetConfigDirectory(), "bios")
                : mediaSettings.BiosPath;

            try { Directory.CreateDirectory(biosDir); } catch { }

            var items = KnownBiosFiles
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(filename => new
                {
                    filename,
                    present = System.IO.File.Exists(Path.Combine(biosDir, filename))
                })
                .ToList();

            return Ok(new { biosDirectory = biosDir, files = items });
        }

        [HttpGet("bios/{filename}")]
        public ActionResult GetBios(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename) || !KnownBiosFiles.Contains(filename))
            {
                return NotFound(new { message = "Unknown BIOS filename." });
            }

            var mediaSettings = _configService.LoadMediaSettings();
            var biosDir = string.IsNullOrWhiteSpace(mediaSettings.BiosPath)
                ? Path.Combine(_configService.GetConfigDirectory(), "bios")
                : mediaSettings.BiosPath;

            string biosDirFull, candidate;
            try
            {
                biosDirFull = Path.GetFullPath(biosDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                candidate = Path.GetFullPath(Path.Combine(biosDirFull, filename));
            }
            catch
            {
                return BadRequest(new { message = "Invalid BIOS path configuration." });
            }

            if (!candidate.StartsWith(biosDirFull, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Path traversal rejected." });
            }

            if (!System.IO.File.Exists(candidate))
            {
                return NotFound(new { message = "BIOS file not present in configured folder.", biosDirectory = biosDir });
            }

            var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "application/octet-stream", filename);
        }

        /// <summary>
        /// Check if a game is playable in the web emulator
        /// </summary>
        [HttpGet("{gameId}/playable")]
        public async Task<ActionResult> IsPlayable(int gameId)
        {
            var game = await _context.Games.FindAsync(gameId);
            if (game == null)
            {
                return NotFound(new { playable = false, message = "Game not found" });
            }

            var isPlayable = IsGamePlayable(game);
            var core = GetCoreForPlatform(game.PlatformId);

            return Ok(new
            {
                playable = isPlayable,
                core = core,
                platformId = game.PlatformId,
                romPath = game.Path,
                message = isPlayable ? "Game can be played in browser" : "Platform not supported for web emulation"
            });
        }

        /// <summary>
        /// Get the unified platform-to-core mapping for EmulatorJS.
        /// Single source of truth consumed by both backend and frontend.
        /// </summary>
        [HttpGet("cores/mapping")]
        public ActionResult GetCoreMapping()
        {
            var platforms = _context.Platforms.ToList();
            var mapping = new List<object>();

            foreach (var kvp in PlatformIdToCore)
            {
                var platform = platforms.FirstOrDefault(p => p.Id == kvp.Key);
                if (platform != null)
                {
                    mapping.Add(new
                    {
                        platformId = kvp.Key,
                        slug = platform.Slug,
                        name = platform.Name,
                        core = kvp.Value
                    });
                }
            }

            return Ok(mapping);
        }

        /// <summary>
        /// Serve a ROM file for the emulator
        /// </summary>
        [HttpGet("{gameId}/rom")]
        public async Task<ActionResult> GetRom(int gameId)
        {
            var game = await _context.Games.FindAsync(gameId);
            if (game == null)
            {
                return NotFound(new { error = "Game not found" });
            }

            if (string.IsNullOrEmpty(game.Path))
            {
                return NotFound(new { error = "Game has no file path" });
            }

            // Find the ROM file
            string? romPath = null;

            if (System.IO.File.Exists(game.Path))
            {
                romPath = game.Path;
            }
            else if (Directory.Exists(game.Path))
            {
                // Search for ROM files in the directory
                var files = Directory.GetFiles(game.Path, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .ToList();

                if (files.Any())
                {
                    romPath = files.First();
                }
            }

            if (romPath == null || !System.IO.File.Exists(romPath))
            {
                return NotFound(new { error = "ROM file not found" });
            }

            // Get MIME type based on extension
            var extension = Path.GetExtension(romPath).ToLower();
            var mimeType = GetMimeType(extension);

            // Return the file
            var fileStream = new FileStream(romPath, FileMode.Open, FileAccess.Read);
            return File(fileStream, mimeType, Path.GetFileName(romPath));
        }

        /// <summary>
        /// Get emulator configuration for a game
        /// </summary>
        [HttpGet("{gameId}/config")]
        public async Task<ActionResult> GetEmulatorConfig(int gameId)
        {
            var game = await _context.Games.FindAsync(gameId);
            if (game == null)
            {
                return NotFound(new { error = "Game not found" });
            }

            var core = GetCoreForPlatform(game.PlatformId);
            if (core == null)
            {
                return BadRequest(new { error = "Platform not supported for web emulation" });
            }

            return Ok(new
            {
                gameId = game.Id,
                title = game.Title,
                core = core,
                romUrl = $"/api/v3/emulator/{gameId}/rom",
                platformId = game.PlatformId
            });
        }

        /// <summary>
        /// Get list of all supported platforms for web emulation
        /// </summary>
        [HttpGet("supported-platforms")]
        public ActionResult GetSupportedPlatforms()
        {
            var platforms = PlatformIdToCore.Select(kvp => new
            {
                platformId = kvp.Key,
                core = kvp.Value,
                platformName = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == kvp.Key)?.Name ?? "Unknown"
            }).ToList();

            return Ok(platforms);
        }

        private static readonly string SaveStatesPath = Environment.GetEnvironmentVariable("RetroArr_SAVESTATES_PATH") 
            ?? Path.Combine(AppContext.BaseDirectory, "savestates");

        private static readonly string EmulatorJsPath = Environment.GetEnvironmentVariable("RetroArr_EMULATORJS_PATH") 
            ?? Path.Combine("/app/config", "emulatorjs");

        private static readonly HttpClient _httpClient = new HttpClient();
        private const string EmulatorJsGitHubApi = "https://api.github.com/repos/EmulatorJS/EmulatorJS/releases/latest";
        // We use GitHub's zipball URL which provides source code as standard zip

        /// <summary>
        /// Get save state for a game
        /// </summary>
        [HttpGet("{gameId}/state")]
        public Task<ActionResult> GetSaveState(int gameId)
        {
            var savePath = Path.Combine(SaveStatesPath, $"{gameId}.state");
            if (!System.IO.File.Exists(savePath))
            {
                return Task.FromResult<ActionResult>(NotFound(new { error = "No save state found" }));
            }

            var fileStream = new FileStream(savePath, FileMode.Open, FileAccess.Read);
            return Task.FromResult<ActionResult>(File(fileStream, "application/octet-stream", $"{gameId}.state"));
        }

        /// <summary>
        /// Save state for a game
        /// </summary>
        [HttpPost("{gameId}/state")]
        public async Task<ActionResult> SaveState(int gameId)
        {
            try
            {
                Directory.CreateDirectory(SaveStatesPath);
                var savePath = Path.Combine(SaveStatesPath, $"{gameId}.state");
                
                using var memoryStream = new MemoryStream();
                await Request.Body.CopyToAsync(memoryStream);
                await System.IO.File.WriteAllBytesAsync(savePath, memoryStream.ToArray());
                
                return Ok(new { message = "State saved", path = savePath });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to save state: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get SRAM save for a game
        /// </summary>
        [HttpGet("{gameId}/save")]
        public Task<ActionResult> GetSave(int gameId)
        {
            var savePath = Path.Combine(SaveStatesPath, $"{gameId}.sav");
            if (!System.IO.File.Exists(savePath))
            {
                return Task.FromResult<ActionResult>(NotFound(new { error = "No save found" }));
            }

            var fileStream = new FileStream(savePath, FileMode.Open, FileAccess.Read);
            return Task.FromResult<ActionResult>(File(fileStream, "application/octet-stream", $"{gameId}.sav"));
        }

        /// <summary>
        /// Save SRAM for a game
        /// </summary>
        [HttpPost("{gameId}/save")]
        public async Task<ActionResult> SaveSram(int gameId)
        {
            try
            {
                Directory.CreateDirectory(SaveStatesPath);
                var savePath = Path.Combine(SaveStatesPath, $"{gameId}.sav");

                using var memoryStream = new MemoryStream();
                await Request.Body.CopyToAsync(memoryStream);
                await System.IO.File.WriteAllBytesAsync(savePath, memoryStream.ToArray());

                return Ok(new { message = "Save stored", path = savePath });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to save: {ex.Message}" });
            }
        }

        // ── Multi-slot save state management ───────────────────────────────

        private static bool IsValidSlot(int slot) => slot >= 0 && slot <= 32;

        private bool TryGetSlotPath(int gameId, int slot, out string fullPath)
        {
            fullPath = string.Empty;
            if (gameId <= 0 || !IsValidSlot(slot)) return false;

            string rootFull;
            try
            {
                Directory.CreateDirectory(SaveStatesPath);
                rootFull = Path.GetFullPath(SaveStatesPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                fullPath = Path.GetFullPath(Path.Combine(rootFull, $"{gameId}_slot{slot}.state"));
            }
            catch
            {
                return false;
            }
            return fullPath.StartsWith(rootFull, StringComparison.Ordinal);
        }

        [HttpGet("{gameId}/states")]
        public ActionResult ListSaveStates(int gameId)
        {
            if (gameId <= 0) return BadRequest(new { error = "Invalid game id." });

            try { Directory.CreateDirectory(SaveStatesPath); } catch { }

            var entries = new List<object>();
            if (Directory.Exists(SaveStatesPath))
            {
                var pattern = $"{gameId}_slot*.state";
                foreach (var file in Directory.EnumerateFiles(SaveStatesPath, pattern))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var slotToken = name.Substring(name.IndexOf("_slot", StringComparison.Ordinal) + 5);
                    if (!int.TryParse(slotToken, out var slot)) continue;

                    var info = new FileInfo(file);
                    entries.Add(new
                    {
                        slot,
                        size = info.Length,
                        modified = info.LastWriteTimeUtc
                    });
                }
            }

            return Ok(entries.OrderBy(e => ((dynamic)e).slot));
        }

        [HttpGet("{gameId}/states/{slot:int}")]
        public ActionResult GetSaveStateSlot(int gameId, int slot)
        {
            if (!TryGetSlotPath(gameId, slot, out var savePath))
                return BadRequest(new { error = "Invalid slot or path." });
            if (!System.IO.File.Exists(savePath))
                return NotFound(new { error = "Slot is empty." });

            var stream = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "application/octet-stream", Path.GetFileName(savePath));
        }

        // Save states from modern cores (PS2, Dreamcast) can push past Kestrel's
        // 30 MB default. 128 MB covers everything realistic without opening
        // the endpoint to abuse.
        private const long SaveStateMaxBytes = 128L * 1024 * 1024;

        [HttpPost("{gameId}/states/{slot:int}")]
        [RequestSizeLimit(SaveStateMaxBytes)]
        public async Task<ActionResult> PutSaveStateSlot(int gameId, int slot)
        {
            if (!TryGetSlotPath(gameId, slot, out var savePath))
                return BadRequest(new { error = "Invalid slot or path." });

            if (Request.ContentLength is long cl && cl > SaveStateMaxBytes)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    error = $"Save state exceeds {SaveStateMaxBytes / (1024 * 1024)} MB limit.",
                    maxBytes = SaveStateMaxBytes,
                });
            }

            try
            {
                Directory.CreateDirectory(SaveStatesPath);
                using var memoryStream = new MemoryStream();
                await Request.Body.CopyToAsync(memoryStream);
                await System.IO.File.WriteAllBytesAsync(savePath, memoryStream.ToArray());
                return Ok(new { slot, size = memoryStream.Length });
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    error = $"Save state exceeds {SaveStateMaxBytes / (1024 * 1024)} MB limit.",
                    maxBytes = SaveStateMaxBytes,
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to save slot: {ex.Message}" });
            }
        }

        [HttpDelete("{gameId}/states/{slot:int}")]
        public ActionResult DeleteSaveStateSlot(int gameId, int slot)
        {
            if (!TryGetSlotPath(gameId, slot, out var savePath))
                return BadRequest(new { error = "Invalid slot or path." });
            if (!System.IO.File.Exists(savePath))
                return NotFound(new { error = "Slot is empty." });

            try { System.IO.File.Delete(savePath); return NoContent(); }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        private bool IsGamePlayable(Game game)
        {
            if (string.IsNullOrEmpty(game.Path))
                return false;

            // Check if platform is supported
            if (!PlatformIdToCore.ContainsKey(game.PlatformId))
                return false;

            // Check if file exists and has supported extension
            if (System.IO.File.Exists(game.Path))
            {
                var ext = Path.GetExtension(game.Path).ToLower();
                return SupportedExtensions.Contains(ext);
            }

            // Check if directory contains supported files
            if (Directory.Exists(game.Path))
            {
                return Directory.GetFiles(game.Path, "*.*", SearchOption.TopDirectoryOnly)
                    .Any(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()));
            }

            return false;
        }

        private string? GetCoreForPlatform(int platformId)
        {
            return PlatformIdToCore.TryGetValue(platformId, out var core) ? core : null;
        }

        private string GetMimeType(string extension)
        {
            return extension switch
            {
                ".zip" => "application/zip",
                ".iso" => "application/octet-stream",
                ".bin" => "application/octet-stream",
                ".chd" => "application/octet-stream",
                _ => "application/octet-stream"
            };
        }

        // ============== EmulatorJS Self-Hosting Endpoints ==============

        // Cores that need SharedArrayBuffer (multi-threaded WASM)
        private static readonly HashSet<string> ThreadedCores = new(StringComparer.OrdinalIgnoreCase)
        {
            "psp", "nds", "n64", "segaSaturn", "3do"
        };

        /// <summary>
        /// Serve a self-contained emulator page with COOP/COEP headers so
        /// SharedArrayBuffer works for threaded cores (PSP, NDS, etc.).
        /// </summary>
        [HttpGet("player")]
        public async Task<ActionResult> GetEmulatorPlayer(
            [FromQuery] string rom,
            [FromQuery] string core,
            [FromQuery] string title = "Game")
        {
            if (string.IsNullOrEmpty(rom) || string.IsNullOrEmpty(core))
                return BadRequest(new { error = "rom and core are required" });

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var needsThreads = ThreadedCores.Contains(core);
            var safeTitle = title.Replace("'", "\\'").Replace("\"", "&quot;").Replace("<", "&lt;");
            var safeRom = rom.Replace("'", "\\'");
            var safeCore = core.Replace("'", "\\'");

            // Browsers only honour COOP/COEP (and expose SharedArrayBuffer) on
            // "secure contexts": HTTPS or localhost. Over plain http on a LAN
            // IP, threaded cores (psp, nds, n64) cannot work — fail early with
            // a clear message instead of letting EmulatorJS crash cryptically.
            var host = Request.Host.Host;
            var isSecure = Request.IsHttps
                || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || host == "127.0.0.1"
                || host == "::1";

            if (needsThreads && !isSecure)
            {
                var errorHtml = $@"<!DOCTYPE html>
<html><head><meta charset=""UTF-8""><title>{safeTitle}</title>
<style>
body {{ background:#1e1e2e; color:#cdd6f4; font-family:system-ui,sans-serif; padding:2rem; line-height:1.5; }}
h1 {{ color:#f38ba8; margin-bottom:1rem; }}
code {{ background:#313244; padding:0.1rem 0.4rem; border-radius:3px; }}
a {{ color:#89b4fa; }}
</style></head><body>
<h1>This core needs a secure connection</h1>
<p>The <code>{safeCore}</code> core uses multi-threaded WebAssembly, which requires
<code>SharedArrayBuffer</code>. Browsers only expose that on HTTPS or localhost.</p>
<p>You're connected over plain HTTP on a LAN IP, so it can't run here. Options:</p>
<ul>
<li>Access RetroArr via <code>http://localhost:2727</code> (only works on the host machine)</li>
<li>Put RetroArr behind an HTTPS reverse proxy (Caddy, Traefik, nginx)</li>
<li>Use a non-threaded platform for now (NES, SNES, GB/GBA, Genesis, PS1, etc.)</li>
</ul>
</body></html>";
                return Content(errorHtml, "text/html");
            }

            var html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{safeTitle}</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ background: #1e1e2e; overflow: hidden; }}
        #game {{ width: 100vw; height: 100vh; }}
    </style>
</head>
<body>
    <div id=""game""></div>
    <script>
        EJS_player = '#game';
        EJS_gameUrl = '{baseUrl}{safeRom}';
        EJS_core = '{safeCore}';
        EJS_gameName = '{safeTitle}';
        EJS_pathtodata = '{baseUrl}/api/v3/emulator/assets/';
        EJS_startOnLoaded = true;
        EJS_color = '#89b4fa';
        EJS_backgroundColor = '#1e1e2e';
        EJS_language = 'en-US';
        EJS_threads = {(needsThreads ? "true" : "false")};
        EJS_AdUrl = '';
    </script>
    <script src=""{baseUrl}/api/v3/emulator/assets/loader.js""></script>
</body>
</html>";

            // Write the response body directly so COOP/COEP headers are guaranteed
            // to flush with the response — Content() has sometimes swallowed them.
            Response.StatusCode = 200;
            Response.ContentType = "text/html; charset=utf-8";
            Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            Response.Headers["Cache-Control"] = "no-store";
            await Response.WriteAsync(html);
            return new EmptyResult();
        }

        /// <summary>
        /// Serve EmulatorJS static files with proper COOP/COEP headers for SharedArrayBuffer.
        /// Falls back to CDN if file not found locally (for on-demand core downloads).
        /// </summary>
        [HttpGet("assets/{**path}")]
        public async Task<ActionResult> GetEmulatorAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return BadRequest(new { error = "Path is required" });
            }

            var filePath = Path.Combine(EmulatorJsPath, path);
            
            // Security: prevent directory traversal
            var fullPath = Path.GetFullPath(filePath);
            var basePath = Path.GetFullPath(EmulatorJsPath);
            if (!fullPath.StartsWith(basePath))
            {
                return BadRequest(new { error = "Invalid path" });
            }

            // Set COOP/COEP headers for SharedArrayBuffer support
            Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

            // Serve from local file if exists
            if (System.IO.File.Exists(fullPath))
            {
                var contentType = GetAssetContentType(path);
                var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                return File(fileStream, contentType);
            }

            // Fallback to CDN for missing files (cores, etc.)
            try
            {
                var cdnUrl = $"https://cdn.emulatorjs.org/stable/data/{path}";
                _logger.Info($"[EmulatorJS] Proxying from CDN: {path}");
                
                var response = await _httpClient.GetAsync(cdnUrl);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    
                    // Cache the file locally for future requests
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    await System.IO.File.WriteAllBytesAsync(fullPath, content);
                    _logger.Info($"[EmulatorJS] Cached: {path} ({content.Length} bytes)");
                    
                    var contentType = GetAssetContentType(path);
                    return File(content, contentType);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[EmulatorJS] CDN proxy failed for {path}: {ex.Message}");
            }

            return NotFound(new { error = $"Asset not found: {path}" });
        }

        /// <summary>
        /// Get EmulatorJS status and version info
        /// </summary>
        [HttpGet("status")]
        public ActionResult GetEmulatorStatus()
        {
            // Check for loader.js which is the main EmulatorJS file
            var installed = System.IO.File.Exists(Path.Combine(EmulatorJsPath, "loader.js"));
            string? version = null;
            
            var versionFile = Path.Combine(EmulatorJsPath, "version.txt");
            if (System.IO.File.Exists(versionFile))
            {
                version = System.IO.File.ReadAllText(versionFile).Trim();
            }

            return Ok(new
            {
                installed,
                version,
                path = EmulatorJsPath,
                assetsUrl = "/api/v3/emulator/assets/"
            });
        }

        /// <summary>
        /// Health check: reports loader status, cached cores, and BIOS files.
        /// </summary>
        [HttpGet("health")]
        public ActionResult GetEmulatorHealth()
        {
            var loaderExists = System.IO.File.Exists(Path.Combine(EmulatorJsPath, "loader.js"));
            var coresDir = Path.Combine(EmulatorJsPath, "cores");

            // List cached cores
            var cachedCores = new List<string>();
            if (Directory.Exists(coresDir))
            {
                cachedCores = Directory.GetFiles(coresDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Distinct()
                    .ToList();
            }

            // Detect BIOS files (common BIOS filenames for RetroArch / EmulatorJS cores)
            var knownBios = new Dictionary<string, string>
            {
                { "scph5500.bin", "PlayStation (JP)" },
                { "scph5501.bin", "PlayStation (US)" },
                { "scph5502.bin", "PlayStation (EU)" },
                { "bios_CD_U.bin", "Sega CD (US)" },
                { "bios_CD_E.bin", "Sega CD (EU)" },
                { "bios_CD_J.bin", "Sega CD (JP)" },
                { "disksys.rom", "Famicom Disk System" },
                { "gba_bios.bin", "Game Boy Advance" },
                { "gb_bios.bin", "Game Boy" },
                { "gbc_bios.bin", "Game Boy Color" },
                { "sgb_bios.bin", "Super Game Boy" },
                { "saturn_bios.bin", "Sega Saturn" },
                { "sega_101.bin", "Sega Saturn (JP)" },
                { "mpr-17933.bin", "Sega Saturn (US/EU)" },
                { "lynxboot.img", "Atari Lynx" },
                { "3do_bios.bin", "3DO" },
                { "syscard3.pce", "PC Engine CD" },
            };

            var biosDir = Path.Combine(EmulatorJsPath, "bios");
            var biosStatus = new List<object>();
            foreach (var kvp in knownBios)
            {
                var found = false;
                // Check in bios/ subdirectory and in the root emulatorjs directory
                if (Directory.Exists(biosDir) && System.IO.File.Exists(Path.Combine(biosDir, kvp.Key)))
                    found = true;
                else if (System.IO.File.Exists(Path.Combine(EmulatorJsPath, kvp.Key)))
                    found = true;

                biosStatus.Add(new { file = kvp.Key, system = kvp.Value, found });
            }

            // Determine which supported cores are missing locally
            var requiredCores = PlatformIdToCore.Values.Distinct().ToList();
            var missingCores = requiredCores.Where(c => !cachedCores.Contains(c)).ToList();

            return Ok(new
            {
                healthy = loaderExists,
                loaderPresent = loaderExists,
                cachedCores,
                missingCores,
                totalSupportedCores = requiredCores.Count,
                bios = biosStatus,
                biosDirectory = biosDir
            });
        }

        /// <summary>
        /// Check for EmulatorJS updates
        /// </summary>
        [HttpGet("check-update")]
        public async Task<ActionResult> CheckForUpdate()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RetroArr/1.0");
                var response = await _httpClient.GetStringAsync(EmulatorJsGitHubApi);
                var json = JsonDocument.Parse(response);
                var latestVersion = json.RootElement.GetProperty("tag_name").GetString();

                string? currentVersion = null;
                var versionFile = Path.Combine(EmulatorJsPath, "version.txt");
                if (System.IO.File.Exists(versionFile))
                {
                    currentVersion = System.IO.File.ReadAllText(versionFile).Trim();
                }

                var updateAvailable = currentVersion != latestVersion;

                return Ok(new
                {
                    currentVersion,
                    latestVersion,
                    updateAvailable
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to check for updates: {ex.Message}" });
            }
        }

        /// <summary>
        /// Download and install EmulatorJS from the official CDN
        /// </summary>
        [HttpPost("install")]
        public async Task<ActionResult> InstallEmulatorJs([FromQuery] string? version = null)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RetroArr/1.0");
                
                // Get version from CDN
                var cdnBase = "https://cdn.emulatorjs.org/stable/data";
                var versionJson = await _httpClient.GetStringAsync($"{cdnBase}/version.json");
                var versionInfo = JsonDocument.Parse(versionJson);
                version = versionInfo.RootElement.GetProperty("version").GetString() ?? "unknown";
                
                _logger.Info($"[EmulatorJS] Installing version {version} from CDN");

                // Backup existing installation
                if (Directory.Exists(EmulatorJsPath))
                {
                    var backupPath = EmulatorJsPath + ".backup";
                    if (Directory.Exists(backupPath))
                    {
                        Directory.Delete(backupPath, true);
                    }
                    Directory.Move(EmulatorJsPath, backupPath);
                }

                // Create EmulatorJS directory structure
                Directory.CreateDirectory(EmulatorJsPath);
                Directory.CreateDirectory(Path.Combine(EmulatorJsPath, "cores"));
                Directory.CreateDirectory(Path.Combine(EmulatorJsPath, "localization"));

                // Essential files to download from CDN
                var essentialFiles = new[]
                {
                    "loader.js",
                    "emulator.min.js",
                    "emulator.min.css",
                    "version.json",
                    "GameManager.js",
                    "gamepad.js",
                    "nipplejs.js",
                    "shaders.js",
                    "storage.js",
                    "socket.io.min.js"
                };

                // Download essential files
                foreach (var file in essentialFiles)
                {
                    try
                    {
                        var content = await _httpClient.GetByteArrayAsync($"{cdnBase}/{file}");
                        await System.IO.File.WriteAllBytesAsync(Path.Combine(EmulatorJsPath, file), content);
                        _logger.Info($"[EmulatorJS] Downloaded: {file}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"[EmulatorJS] Warning: Could not download {file}: {ex.Message}");
                    }
                }

                // Download common localization files
                var localizations = new[] { "en-US.json", "de-DE.json", "es-ES.json", "fr-FR.json", "it-IT.json", "ja-JP.json", "pt-BR.json", "zh-CN.json" };
                foreach (var loc in localizations)
                {
                    try
                    {
                        var content = await _httpClient.GetByteArrayAsync($"{cdnBase}/localization/{loc}");
                        await System.IO.File.WriteAllBytesAsync(Path.Combine(EmulatorJsPath, "localization", loc), content);
                        _logger.Info($"[EmulatorJS] Downloaded localization: {loc}");
                    }
                    catch
                    {
                        // Localization file not found, skip
                    }
                }

                // Save version info
                await System.IO.File.WriteAllTextAsync(
                    Path.Combine(EmulatorJsPath, "version.txt"), 
                    version
                );

                // Remove backup on success
                var oldBackup = EmulatorJsPath + ".backup";
                if (Directory.Exists(oldBackup))
                {
                    Directory.Delete(oldBackup, true);
                }

                // Log what was installed
                var installedFiles = Directory.GetFiles(EmulatorJsPath, "*.*", SearchOption.AllDirectories).Length;
                _logger.Info($"[EmulatorJS] Installed: {installedFiles} files");

                return Ok(new
                {
                    message = $"EmulatorJS {version} installed successfully. Cores will be downloaded on-demand.",
                    version,
                    path = EmulatorJsPath,
                    files = installedFiles,
                    note = "Emulator cores are downloaded automatically when needed from CDN"
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"[EmulatorJS] Install failed: {ex.Message}");
                
                // Restore backup on failure
                var backupPath = EmulatorJsPath + ".backup";
                if (Directory.Exists(backupPath))
                {
                    if (Directory.Exists(EmulatorJsPath))
                    {
                        Directory.Delete(EmulatorJsPath, true);
                    }
                    Directory.Move(backupPath, EmulatorJsPath);
                }

                return StatusCode(500, new { error = $"Failed to install EmulatorJS: {ex.Message}" });
            }
        }

        /// <summary>
        /// Uninstall EmulatorJS
        /// </summary>
        [HttpDelete("uninstall")]
        public ActionResult UninstallEmulatorJs()
        {
            try
            {
                if (Directory.Exists(EmulatorJsPath))
                {
                    Directory.Delete(EmulatorJsPath, true);
                }

                return Ok(new { message = "EmulatorJS uninstalled successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to uninstall EmulatorJS: {ex.Message}" });
            }
        }

        private string GetAssetContentType(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".js" => "application/javascript",
                ".wasm" => "application/wasm",
                ".json" => "application/json",
                ".css" => "text/css",
                ".html" => "text/html",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                _ => "application/octet-stream"
            };
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                System.IO.File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }
    }
}

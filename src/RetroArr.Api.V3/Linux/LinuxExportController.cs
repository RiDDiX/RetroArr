using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;
using RetroArr.Core.Linux;

namespace RetroArr.Api.V3.Linux
{
    [ApiController]
    [Route("api/v3/linux/export")]
    public class LinuxExportController : ControllerBase
    {
        private readonly IGameRepository _gameRepository;
        private readonly LutrisExportService _lutrisExport;
        private readonly SteamShortcutExportService _steamShortcutExport;
        private readonly DesktopEntryExportService _desktopEntryExport;

        public LinuxExportController(
            IGameRepository gameRepository,
            LutrisExportService lutrisExport,
            SteamShortcutExportService steamShortcutExport,
            DesktopEntryExportService desktopEntryExport)
        {
            _gameRepository = gameRepository;
            _lutrisExport = lutrisExport;
            _steamShortcutExport = steamShortcutExport;
            _desktopEntryExport = desktopEntryExport;
        }

        [HttpGet("lutris/{gameId}")]
        public async Task<IActionResult> ExportLutris(int gameId, [FromQuery] string? runner = null)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null) return NotFound("Game not found.");

            try
            {
                var yaml = _lutrisExport.GenerateInstallerYaml(game, runner);
                return Content(yaml, "application/x-yaml");
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // for Steam Deck Game Mode
        [HttpGet("steam-shortcut/{gameId}")]
        public async Task<IActionResult> ExportSteamShortcut(int gameId, [FromQuery] string? launchOptions = null)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null) return NotFound("Game not found.");

            try
            {
                var shortcut = _steamShortcutExport.GenerateShortcut(game, launchOptions);
                return Ok(shortcut);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // bulk: every game with an executable path
        [HttpGet("steam-shortcuts")]
        public async Task<IActionResult> ExportAllSteamShortcuts()
        {
            var games = await _gameRepository.GetAllAsync();
            var withExe = games.Where(g => !string.IsNullOrEmpty(g.ExecutablePath)).ToList();

            var shortcuts = _steamShortcutExport.GenerateShortcuts(withExe);
            return Ok(new
            {
                count = shortcuts.Count,
                steamUserDataPaths = SteamShortcutExportService.GetSteamShortcutPaths(),
                shortcuts
            });
        }

        [HttpGet("desktop-entry/{gameId}")]
        public async Task<IActionResult> ExportDesktopEntry(int gameId, [FromQuery] string? icon = null, [FromQuery] string? runner = null)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null) return NotFound("Game not found.");

            try
            {
                var entry = _desktopEntryExport.GenerateDesktopEntry(game, icon, runner);
                var fileName = $"{LutrisExportService.GenerateSlug(game.Title)}.desktop";
                return Content(entry, "application/x-desktop", System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // reports available runner options + detected Proton installs
        [HttpGet("runners")]
        public IActionResult GetRunnerInfo()
        {
            var protonPath = RetroArr.Core.Launcher.NativeLaunchStrategy.FindProtonPath();
            var steamPath = RetroArr.Core.Launcher.NativeLaunchStrategy.FindSteamInstallPath();

            return Ok(new
            {
                runners = new[] { "auto", "wine", "proton", "native" },
                detectedProton = protonPath,
                detectedSteamPath = steamPath,
                isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Linux)
            });
        }
    }
}

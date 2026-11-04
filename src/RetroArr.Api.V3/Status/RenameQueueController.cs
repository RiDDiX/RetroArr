using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;

namespace RetroArr.Api.V3.Status
{
    [ApiController]
    [Route("api/v3/rename")]
    public class RenameQueueController : ControllerBase
    {
        private readonly InstallerScannerService _installerScanner;

        public RenameQueueController(InstallerScannerService installerScanner)
        {
            _installerScanner = installerScanner;
        }

        [HttpGet("installers")]
        public async Task<IActionResult> GetInstallerMatches()
        {
            var matches = await _installerScanner.ScanGogInstallersAsync();
            return Ok(new
            {
                count = matches.Count,
                items = matches.Select(m => new
                {
                    m.FolderName,
                    m.FolderPath,
                    installerCount = m.InstallerFiles.Count,
                    installers = m.InstallerFiles.Select(f => new { f.FileName, f.FilePath, size = FormatSize(f.Size) }),
                    matchedGame = m.MatchedGame != null ? new { m.MatchedGame.Id, m.MatchedGame.Title } : null,
                    confidence = Math.Round(m.MatchConfidence * 100, 1)
                })
            });
        }

        [HttpGet("updates")]
        public async Task<IActionResult> GetUpdateMatches()
        {
            var matches = await _installerScanner.ScanSwitchUpdatesAsync();
            return Ok(new
            {
                count = matches.Count,
                items = matches.Select(m => new
                {
                    m.FileName,
                    m.FilePath,
                    m.Version,
                    m.TitleId,
                    size = FormatSize(m.Size),
                    type = m.Type.ToString(),
                    matchedGame = m.MatchedGame != null ? new { m.MatchedGame.Id, m.MatchedGame.Title } : null,
                    confidence = Math.Round(m.MatchConfidence * 100, 1)
                })
            });
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}

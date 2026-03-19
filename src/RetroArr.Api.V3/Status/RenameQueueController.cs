using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Download;
using RetroArr.Core.Games;

namespace RetroArr.Api.V3.Status
{
    [ApiController]
    [Route("api/v3/rename")]
    public class RenameQueueController : ControllerBase
    {
        private readonly RenameQueueService _renameQueue;
        private readonly InstallerScannerService _installerScanner;

        public RenameQueueController(RenameQueueService renameQueue, InstallerScannerService installerScanner)
        {
            _renameQueue = renameQueue;
            _installerScanner = installerScanner;
        }

        /// <summary>
        /// Get all pending renames that need user confirmation
        /// </summary>
        [HttpGet("pending")]
        public IActionResult GetPendingRenames()
        {
            var pending = _renameQueue.PendingRenames;
            return Ok(new
            {
                count = pending.Count,
                items = pending.Select(p => new
                {
                    p.Id,
                    p.OriginalName,
                    p.SuggestedName,
                    p.ExpectedGameTitle,
                    p.Platform,
                    confidence = Math.Round(p.Confidence * 100, 1),
                    p.DateAdded
                })
            });
        }

        /// <summary>
        /// Approve a pending rename (optionally with custom name)
        /// </summary>
        [HttpPost("approve/{id}")]
        public IActionResult ApproveRename(string id, [FromBody] ApproveRenameRequest? request)
        {
            var success = _renameQueue.ApproveRename(id, request?.CustomName);
            if (success)
            {
                return Ok(new { success = true, message = "File renamed successfully" });
            }
            return BadRequest(new { success = false, message = "Failed to rename file" });
        }

        /// <summary>
        /// Reject a pending rename (keep original name)
        /// </summary>
        [HttpPost("reject/{id}")]
        public IActionResult RejectRename(string id)
        {
            var success = _renameQueue.RejectRename(id);
            if (success)
            {
                return Ok(new { success = true, message = "Rename rejected, original name kept" });
            }
            return BadRequest(new { success = false, message = "Pending rename not found" });
        }

        /// <summary>
        /// Get GOG installer matches
        /// </summary>
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

        /// <summary>
        /// Get Switch update matches
        /// </summary>
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

    public class ApproveRenameRequest
    {
        public string? CustomName { get; set; }
    }
}

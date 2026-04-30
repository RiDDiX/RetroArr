using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Debug;
using RetroArr.Core.Games;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace RetroArr.Api.V3.Debug
{
    [ApiController]
    [Route("api/v3/debug")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class DebugController : ControllerBase
    {
        private readonly DebugLogService _debugLog;
        private readonly MediaScannerService _scanner;

        public DebugController(DebugLogService debugLog, MediaScannerService scanner)
        {
            _debugLog = debugLog;
            _scanner = scanner;
        }

        [HttpGet("logs")]
        public IActionResult GetLogs([FromQuery] int count = 100, [FromQuery] string? level = null, [FromQuery] string? category = null)
        {
            LogLevel? minLevel = level?.ToLower() switch
            {
                "debug" => LogLevel.Debug,
                "info" => LogLevel.Info,
                "warning" => LogLevel.Warning,
                "error" => LogLevel.Error,
                _ => null
            };

            var logs = _debugLog.GetLogs(count, minLevel, category);
            return Ok(logs.Select(l => new {
                timestamp = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                level = l.Level.ToString().ToLower(),
                category = l.Category,
                message = l.Message
            }));
        }

        [HttpGet("scan-progress")]
        public IActionResult GetScanProgress()
        {
            return Ok(new {
                isScanning = _scanner.IsScanning,
                currentDirectory = _scanner.CurrentScanDirectory,
                currentFile = _scanner.CurrentScanFile,
                filesScanned = _scanner.FilesScannedCount,
                gamesFound = _scanner.GamesAddedCount,
                lastGameFound = _scanner.LastGameFound
            });
        }

        [HttpDelete("logs")]
        public IActionResult ClearLogs()
        {
            _debugLog.ClearLogs();
            return Ok(new { message = "Logs cleared" });
        }
    }
}
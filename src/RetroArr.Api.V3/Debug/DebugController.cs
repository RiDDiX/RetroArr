using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Prowlarr;
using RetroArr.Core.Debug;
using RetroArr.Core.Games;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace RetroArr.Api.V3.Debug
{
    [ApiController]
    [Route("api/v3/debug")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class DebugController : ControllerBase
    {
        private readonly ProwlarrSettings _prowlarrSettings;
        private readonly DebugLogService _debugLog;
        private readonly MediaScannerService _scanner;

        public DebugController(ProwlarrSettings prowlarrSettings, DebugLogService debugLog, MediaScannerService scanner)
        {
            _prowlarrSettings = prowlarrSettings;
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

        [HttpGet("prowlarr-raw")]
        public async Task<IActionResult> GetProwlarrRaw([FromQuery] string query = "game")
        {
            if (!_prowlarrSettings.IsConfigured)
            {
                return BadRequest("Prowlarr not configured");
            }

            try
            {
                using var httpClient = new HttpClient { BaseAddress = new Uri(_prowlarrSettings.Url) };
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/search?query={Uri.EscapeDataString(query)}");
                request.Headers.Add("X-Api-Key", _prowlarrSettings.ApiKey);

                var response = await httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                
                return Ok(new { 
                    StatusCode = (int)response.StatusCode,
                    ContentType = response.Content.Headers.ContentType?.ToString(),
                    RawContent = content,
                    ContentLength = content.Length
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
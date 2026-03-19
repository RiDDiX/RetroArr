using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Configuration;
using RetroArr.Core.Logging;

namespace RetroArr.Api.V3.Settings
{
    [ApiController]
    [Route("api/v3/settings/logging")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class LoggingController : ControllerBase
    {
        private readonly ConfigurationService _configService;
        private readonly AppLoggerService _loggerService;

        public LoggingController(ConfigurationService configService, AppLoggerService loggerService)
        {
            _configService = configService;
            _loggerService = loggerService;
        }

        [HttpGet]
        public IActionResult GetSettings()
        {
            var settings = _configService.LoadLoggingSettings();
            var effectiveDir = _configService.GetEffectiveLogDirectory();
            var logFiles = _loggerService.GetLogFiles()
                .Select(f => new FileInfo(f))
                .Where(fi => fi.Exists)
                .Select(fi => new
                {
                    name = fi.Name,
                    sizeMb = Math.Round(fi.Length / (1024.0 * 1024.0), 2),
                    lastModified = fi.LastWriteTimeUtc.ToString("o")
                })
                .OrderByDescending(f => f.lastModified)
                .ToList();

            return Ok(new
            {
                settings,
                effectiveLogDirectory = effectiveDir,
                defaultLogDirectory = _configService.GetDefaultLogDirectory(),
                logFiles
            });
        }

        [HttpPost]
        public IActionResult SaveSettings([FromBody] LoggingSettings settings)
        {
            // Validate log directory if custom path specified
            if (!string.IsNullOrWhiteSpace(settings.LogDirectory))
            {
                try
                {
                    Directory.CreateDirectory(settings.LogDirectory);
                    var testFile = Path.Combine(settings.LogDirectory, ".write_test");
                    System.IO.File.WriteAllText(testFile, "test");
                    System.IO.File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    return BadRequest(new { error = $"Log directory is not writable: {ex.Message}" });
                }
            }

            // Clamp values
            settings.MaxDays = Math.Clamp(settings.MaxDays, 1, 365);
            settings.MaxTotalSizeMb = Math.Clamp(settings.MaxTotalSizeMb, 10, 10000);
            settings.RotateSizeMb = Math.Clamp(settings.RotateSizeMb, 1, 1000);

            _configService.SaveLoggingSettings(settings);
            _loggerService.Reconfigure();

            return Ok(new { message = "Logging settings saved", effectiveLogDirectory = _configService.GetEffectiveLogDirectory() });
        }

        [HttpGet("files")]
        public IActionResult GetLogFileList()
        {
            var files = _loggerService.GetLogFiles()
                .Select(f => new FileInfo(f))
                .Where(fi => fi.Exists)
                .Select(fi => new
                {
                    name = fi.Name,
                    path = fi.FullName,
                    sizeMb = Math.Round(fi.Length / (1024.0 * 1024.0), 2),
                    lastModified = fi.LastWriteTimeUtc.ToString("o")
                })
                .OrderByDescending(f => f.lastModified)
                .ToList();

            return Ok(files);
        }

        [HttpPost("export")]
        public IActionResult ExportDiagnostics([FromBody] ExportRequest request)
        {
            var logDir = _configService.GetEffectiveLogDirectory();
            if (!Directory.Exists(logDir))
                return NotFound(new { error = "Log directory does not exist" });

            var cutoff = request.TimeRange switch
            {
                "24h" => DateTime.UtcNow.AddHours(-24),
                "7d" => DateTime.UtcNow.AddDays(-7),
                "30d" => DateTime.UtcNow.AddDays(-30),
                _ => DateTime.UtcNow.AddDays(-7)
            };

            var settings = _configService.LoadLoggingSettings();
            var files = Directory.GetFiles(logDir, "*.log")
                .Select(f => new FileInfo(f))
                .Where(fi => fi.LastWriteTimeUtc >= cutoff)
                .ToList();

            if (!files.Any())
                return NotFound(new { error = "No log files found for the selected time range" });

            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var file in files)
                {
                    var entry = archive.CreateEntry(file.Name);
                    using var entryStream = entry.Open();
                    var content = System.IO.File.ReadAllText(file.FullName);

                    // Redact sensitive data
                    if (settings.RedactTokens)
                    {
                        content = LogRedactor.Redact(content);
                    }

                    using var writer = new StreamWriter(entryStream);
                    writer.Write(content);
                }
            }

            memoryStream.Position = 0;
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            return File(memoryStream.ToArray(), "application/zip", $"retroarr_logs_{timestamp}.zip");
        }

        [HttpPost("validate-directory")]
        public IActionResult ValidateDirectory([FromBody] DirectoryValidation request)
        {
            if (string.IsNullOrWhiteSpace(request.Path))
                return Ok(new { valid = true, message = "Will use default directory" });

            try
            {
                Directory.CreateDirectory(request.Path);
                var testFile = Path.Combine(request.Path, ".write_test");
                System.IO.File.WriteAllText(testFile, "test");
                System.IO.File.Delete(testFile);
                return Ok(new { valid = true, message = "Directory is writable" });
            }
            catch (Exception ex)
            {
                return Ok(new { valid = false, message = ex.Message });
            }
        }
    }

    public class ExportRequest
    {
        public string TimeRange { get; set; } = "7d";
    }

    public class DirectoryValidation
    {
        public string Path { get; set; } = string.Empty;
    }
}

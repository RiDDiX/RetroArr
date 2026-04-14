using System;
using System.IO;
using System.Net;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Configuration;

namespace RetroArr.Api.V3.SystemInfo
{
    [ApiController]
    [Route("api/v3/[controller]")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class SystemController : ControllerBase
    {
        private readonly ApiKeyService _apiKeyService;

        public SystemController(ApiKeyService apiKeyService)
        {
            _apiKeyService = apiKeyService;
        }

        [HttpGet("apikey/bootstrap")]
        public ActionResult BootstrapApiKey()
        {
            var ip = HttpContext.Connection.RemoteIpAddress;
            if (ip != null && !IPAddress.IsLoopback(ip))
            {
                return NotFound();
            }
            return Ok(new { apiKey = _apiKeyService.GetApiKey() });
        }

        [HttpPost("apikey/rotate")]
        public ActionResult RotateApiKey()
        {
            var newKey = _apiKeyService.Regenerate();
            return Ok(new { apiKey = newKey });
        }

        [HttpGet("status")]
        public ActionResult GetStatus()
        {
            var assembly = Assembly.GetEntryAssembly();
            var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? assembly?.GetName().Version?.ToString()
                          ?? "unknown";

            return Ok(new
            {
                version,
                startTime = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString()
            });
        }

        [HttpGet("changelog")]
        public ActionResult GetChangelog()
        {
            try
            {
                // Look for CHANGELOG.md relative to the application base directory
                var basePath = AppContext.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(basePath, "CHANGELOG.md"),
                    Path.Combine(basePath, "..", "CHANGELOG.md"),
                    Path.Combine(basePath, "..", "..", "CHANGELOG.md"),
                    Path.Combine(basePath, "..", "..", "..", "CHANGELOG.md"),
                    Path.Combine(basePath, "..", "..", "..", "..", "CHANGELOG.md"),
                    "/app/CHANGELOG.md" // Docker path
                };

                foreach (var path in candidates)
                {
                    var fullPath = Path.GetFullPath(path);
                    if (System.IO.File.Exists(fullPath))
                    {
                        var content = System.IO.File.ReadAllText(fullPath);
                        return Ok(new { changelog = content });
                    }
                }

                return Ok(new { changelog = (string?)null });
            }
            catch (Exception ex)
            {
                return Ok(new { changelog = (string?)null, error = ex.Message });
            }
        }
    }
}

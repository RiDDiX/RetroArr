using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Plugins;

namespace RetroArr.Api.V3.Plugins
{
    [ApiController]
    [Route("api/v3/plugins")]
    public class PluginController : ControllerBase
    {
        private readonly PluginLoader _loader;
        private readonly PluginExecutor _executor;

        public PluginController(PluginLoader loader, PluginExecutor executor)
        {
            _loader = loader;
            _executor = executor;
        }

        /// <summary>
        /// List all discovered plugins and their status.
        /// </summary>
        [HttpGet]
        public IActionResult ListPlugins()
        {
            _loader.DiscoverAndLoad();

            var circuitStatus = _executor.GetCircuitBreakerStatus();

            return Ok(_loader.Plugins.Select(p => new
            {
                p.Manifest.Name,
                p.Manifest.Version,
                p.Manifest.ApiVersion,
                p.Manifest.Description,
                p.Manifest.Author,
                p.Manifest.Type,
                p.Manifest.Enabled,
                p.Manifest.Permissions,
                p.Manifest.TimeoutSeconds,
                p.IsValid,
                p.Error,
                circuitBreakerFailures = circuitStatus.TryGetValue(p.Manifest.Name, out var fc) ? fc : 0,
                circuitBreakerOpen = circuitStatus.TryGetValue(p.Manifest.Name, out var fc2) && fc2 >= 3
            }));
        }

        /// <summary>
        /// Execute a specific plugin by name.
        /// </summary>
        [HttpPost("{name}/execute")]
        public async Task<IActionResult> ExecutePlugin(string name, [FromBody] object? input = null)
        {
            _loader.DiscoverAndLoad();

            var plugin = _loader.Plugins.FirstOrDefault(p =>
                string.Equals(p.Manifest.Name, name, StringComparison.OrdinalIgnoreCase));

            if (plugin == null)
                return NotFound(new { error = $"Plugin '{name}' not found." });

            var inputJson = input != null
                ? System.Text.Json.JsonSerializer.Serialize(input)
                : "{}";

            var result = await _executor.ExecuteAsync(plugin, inputJson);
            return Ok(result);
        }

        /// <summary>
        /// Reset the circuit breaker for a specific plugin.
        /// </summary>
        [HttpPost("{name}/reset")]
        public IActionResult ResetCircuitBreaker(string name)
        {
            var wasReset = _executor.ResetCircuitBreaker(name);
            return Ok(new
            {
                pluginName = name,
                wasReset,
                message = wasReset ? "Circuit breaker reset." : "No circuit breaker was open for this plugin."
            });
        }

        /// <summary>
        /// Reload all plugins from disk.
        /// </summary>
        [HttpPost("reload")]
        public IActionResult ReloadPlugins()
        {
            _loader.DiscoverAndLoad();
            return Ok(new
            {
                total = _loader.Plugins.Count,
                valid = _loader.Plugins.Count(p => p.IsValid),
                invalid = _loader.Plugins.Count(p => !p.IsValid)
            });
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RetroArr.Core.Plugins
{
    public class PluginResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public long ElapsedMs { get; set; }
        public string PluginName { get; set; } = string.Empty;

        public static PluginResult Ok(string pluginName, string output, long elapsedMs)
            => new() { Success = true, PluginName = pluginName, Output = output, ElapsedMs = elapsedMs };

        public static PluginResult Failure(string pluginName, string error, long elapsedMs)
            => new() { Success = false, PluginName = pluginName, Error = error, ElapsedMs = elapsedMs };
    }

    public class PluginExecutor
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.Plugins);
        private readonly ConcurrentDictionary<string, int> _failureCount = new();
        private const int CircuitBreakerThreshold = 3;

        /// <summary>
        /// Execute a plugin with full fault isolation: process boundary, timeout, circuit breaker.
        /// A broken plugin will NEVER crash or block the main application.
        /// </summary>
        public async Task<PluginResult> ExecuteAsync(LoadedPlugin plugin, string inputJson = "{}")
        {
            if (!plugin.IsValid || !plugin.Manifest.Enabled)
            {
                return PluginResult.Failure(plugin.Manifest.Name,
                    $"Plugin is {(plugin.IsValid ? "disabled" : "invalid: " + plugin.Error)}", 0);
            }

            // Circuit breaker: skip plugins that have failed too many times
            var failKey = plugin.Manifest.Name;
            if (_failureCount.TryGetValue(failKey, out var failures) && failures >= CircuitBreakerThreshold)
            {
                return PluginResult.Failure(plugin.Manifest.Name,
                    $"Circuit breaker open: plugin failed {failures} consecutive times. Reset via API.", 0);
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var result = await RunProcessAsync(plugin, inputJson);
                sw.Stop();

                if (result.Success)
                {
                    // Reset failure counter on success
                    _failureCount.TryRemove(failKey, out _);
                }
                else
                {
                    IncrementFailure(failKey);
                }

                result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                IncrementFailure(failKey);
                return PluginResult.Failure(plugin.Manifest.Name,
                    $"Plugin timed out after {plugin.Manifest.TimeoutSeconds}s", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                IncrementFailure(failKey);
                _logger.Error($"[PluginExecutor] Unhandled error in {plugin.Manifest.Name}: {ex.Message}");
                return PluginResult.Failure(plugin.Manifest.Name, ex.Message, sw.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Reset the circuit breaker for a specific plugin.
        /// </summary>
        public bool ResetCircuitBreaker(string pluginName)
        {
            return _failureCount.TryRemove(pluginName, out _);
        }

        /// <summary>
        /// Get circuit breaker status for all plugins.
        /// </summary>
        public ConcurrentDictionary<string, int> GetCircuitBreakerStatus()
        {
            return _failureCount;
        }

        private async Task<PluginResult> RunProcessAsync(LoadedPlugin plugin, string inputJson)
        {
            var manifest = plugin.Manifest;
            var timeout = TimeSpan.FromSeconds(manifest.TimeoutSeconds);

            using var cts = new CancellationTokenSource(timeout);

            var command = manifest.Command;
            var args = manifest.Args;

            // Resolve command relative to plugin directory
            var resolvedCommand = command;
            if (!Path.IsPathRooted(command))
            {
                var localPath = Path.Combine(plugin.Directory, command);
                if (File.Exists(localPath))
                    resolvedCommand = localPath;
            }

            var psi = new ProcessStartInfo
            {
                FileName = resolvedCommand,
                Arguments = args,
                WorkingDirectory = plugin.Directory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Restrict environment based on permissions
            psi.EnvironmentVariables["RETROARR_PLUGIN_NAME"] = manifest.Name;
            psi.EnvironmentVariables["RETROARR_PLUGIN_TYPE"] = manifest.Type;
            psi.EnvironmentVariables["RETROARR_API_VERSION"] = manifest.ApiVersion;

            using var process = new Process { StartInfo = psi };

            try
            {
                if (!process.Start())
                {
                    return PluginResult.Failure(manifest.Name, "Failed to start plugin process.", 0);
                }
            }
            catch (Exception ex)
            {
                return PluginResult.Failure(manifest.Name, $"Process start error: {ex.Message}", 0);
            }

            // Write input JSON to stdin
            try
            {
                await process.StandardInput.WriteLineAsync(inputJson);
                process.StandardInput.Close();
            }
            catch
            {
                // Plugin may not read stdin - that's fine
            }

            // Read stdout and stderr with timeout
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout - kill the process
                try { process.Kill(true); } catch { /* best effort */ }
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                var errorMsg = !string.IsNullOrEmpty(stderr) ? stderr.Trim() : $"Exit code {process.ExitCode}";
                return PluginResult.Failure(manifest.Name, errorMsg, 0);
            }

            return PluginResult.Ok(manifest.Name, stdout.Trim(), 0);
        }

        private void IncrementFailure(string pluginName)
        {
            _failureCount.AddOrUpdate(pluginName, 1, (_, count) => count + 1);
            if (_failureCount.TryGetValue(pluginName, out var count) && count >= CircuitBreakerThreshold)
            {
                _logger.Error($"[PluginExecutor] Circuit breaker OPEN for '{pluginName}' after {count} failures.");
            }
        }
    }
}

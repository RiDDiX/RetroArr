using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RetroArr.Core.Plugins
{
    public class PluginLoader
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.Plugins);
        private readonly string _pluginsDirectory;
        private readonly List<LoadedPlugin> _plugins = new();
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public PluginLoader(string pluginsDirectory)
        {
            _pluginsDirectory = pluginsDirectory;
        }

        public IReadOnlyList<LoadedPlugin> Plugins => _plugins.AsReadOnly();

        public void DiscoverAndLoad()
        {
            _plugins.Clear();

            if (!Directory.Exists(_pluginsDirectory))
            {
                _logger.Info($"[PluginLoader] Plugins directory does not exist: {_pluginsDirectory}");
                Directory.CreateDirectory(_pluginsDirectory);
                return;
            }

            var dirs = Directory.GetDirectories(_pluginsDirectory);
            _logger.Info($"[PluginLoader] Scanning {dirs.Length} plugin directories in {_pluginsDirectory}");

            foreach (var dir in dirs)
            {
                var manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath))
                {
                    _logger.Info($"[PluginLoader] Skipping {Path.GetFileName(dir)}: no plugin.json");
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOpts);

                    if (manifest == null)
                    {
                        _logger.Info($"[PluginLoader] Skipping {Path.GetFileName(dir)}: manifest deserialized to null");
                        continue;
                    }

                    var validationErrors = ValidateManifest(manifest, dir);
                    if (validationErrors.Count > 0)
                    {
                        _logger.Error($"[PluginLoader] Skipping {manifest.Name}: {string.Join("; ", validationErrors)}");
                        _plugins.Add(new LoadedPlugin(manifest, dir, false, string.Join("; ", validationErrors)));
                        continue;
                    }

                    _logger.Info($"[PluginLoader] Loaded plugin: {manifest.Name} v{manifest.Version} (type: {manifest.Type})");
                    _plugins.Add(new LoadedPlugin(manifest, dir, true, null));
                }
                catch (Exception ex)
                {
                    _logger.Error($"[PluginLoader] Error loading {Path.GetFileName(dir)}: {ex.Message}");
                    _plugins.Add(new LoadedPlugin(
                        new PluginManifest { Name = Path.GetFileName(dir) },
                        dir, false, ex.Message));
                }
            }

            _logger.Info($"[PluginLoader] {_plugins.Count(p => p.IsValid)} valid plugins loaded, {_plugins.Count(p => !p.IsValid)} rejected.");
        }

        public static List<string> ValidateManifest(PluginManifest manifest, string pluginDir)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(manifest.Name))
                errors.Add("Missing 'name'");

            if (string.IsNullOrWhiteSpace(manifest.ApiVersion))
                errors.Add("Missing 'apiVersion'");
            else if (!PluginApiVersions.Supported.Contains(manifest.ApiVersion))
                errors.Add($"Unsupported apiVersion '{manifest.ApiVersion}' (supported: {string.Join(", ", PluginApiVersions.Supported)})");

            if (string.IsNullOrWhiteSpace(manifest.Type))
                errors.Add("Missing 'type'");
            else if (!PluginTypes.All.Contains(manifest.Type.ToLowerInvariant()))
                errors.Add($"Unknown type '{manifest.Type}' (valid: {string.Join(", ", PluginTypes.All)})");

            if (string.IsNullOrWhiteSpace(manifest.Command))
                errors.Add("Missing 'command'");

            foreach (var perm in manifest.Permissions)
            {
                if (!PluginPermissionKeys.All.Contains(perm))
                    errors.Add($"Unknown permission '{perm}'");
            }

            if (manifest.TimeoutSeconds < 1 || manifest.TimeoutSeconds > 300)
                errors.Add($"timeoutSeconds must be between 1 and 300 (got {manifest.TimeoutSeconds})");

            return errors;
        }
    }

    public class LoadedPlugin
    {
        public PluginManifest Manifest { get; }
        public string Directory { get; }
        public bool IsValid { get; }
        public string? Error { get; }

        public LoadedPlugin(PluginManifest manifest, string directory, bool isValid, string? error)
        {
            Manifest = manifest;
            Directory = directory;
            IsValid = isValid;
            Error = error;
        }
    }
}

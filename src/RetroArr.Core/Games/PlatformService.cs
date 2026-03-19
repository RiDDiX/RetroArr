using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RetroArr.Core.Games
{
    public class PlatformService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private static readonly string _settingsPath = ResolveSettingsPath();

        private static string ResolveSettingsPath()
        {
            // Docker: /app/config is the mounted persistent volume
            var dockerConfig = Path.Combine(AppContext.BaseDirectory, "config");
            if (Directory.Exists(dockerConfig))
                return Path.Combine(dockerConfig, "platform_settings.json");

            // Fallback: ApplicationData (native installs)
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
                return Path.Combine(appData, "RetroArr", "config", "platform_settings.json");

            return Path.Combine(dockerConfig, "platform_settings.json");
        }

        private static Dictionary<int, bool> _enabledOverrides = new();
        private static readonly object _lock = new();

        static PlatformService()
        {
            LoadSettings();
        }

        private static void LoadSettings()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_settingsPath))
                    {
                        var json = File.ReadAllText(_settingsPath);
                        _enabledOverrides = JsonSerializer.Deserialize<Dictionary<int, bool>>(json) ?? new();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"[PlatformService] Error loading settings: {ex.Message}");
                    _enabledOverrides = new();
                }
            }
        }

        private static void SaveSettings()
        {
            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_settingsPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var json = JsonSerializer.Serialize(_enabledOverrides, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_settingsPath, json);
                }
                catch (Exception ex)
                {
                    _logger.Error($"[PlatformService] Error saving settings: {ex.Message}");
                }
            }
        }

        public static bool IsEnabled(int platformId, bool defaultValue)
        {
            lock (_lock)
            {
                return _enabledOverrides.TryGetValue(platformId, out var enabled) ? enabled : defaultValue;
            }
        }

        public static void SetEnabled(int platformId, bool enabled)
        {
            lock (_lock)
            {
                _enabledOverrides[platformId] = enabled;
                SaveSettings();
            }
        }

        public static void EnablePlatformByFolderName(string folderName, List<Platform> allPlatforms)
        {
            lock (_lock)
            {
                var platform = allPlatforms.FirstOrDefault(p => 
                    p.MatchesFolderName(folderName));
                
                if (platform != null && !IsEnabled(platform.Id, platform.Enabled))
                {
                    _logger.Info($"[PlatformService] Auto-enabling platform '{platform.Name}' (folder: {folderName})");
                    _enabledOverrides[platform.Id] = true;
                    SaveSettings();
                }
            }
        }

        public static List<string> GetEnabledFolderNames(List<Platform> allPlatforms)
        {
            return allPlatforms
                .Where(p => IsEnabled(p.Id, p.Enabled))
                .Select(p => p.FolderName)
                .Distinct()
                .ToList();
        }
    }
}

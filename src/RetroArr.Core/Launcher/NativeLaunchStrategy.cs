using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using RetroArr.Core.Games;

namespace RetroArr.Core.Launcher
{
    public class NativeLaunchStrategy : ILaunchStrategy
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.Launcher);
        public bool IsSupported(Game game)
        {
            // Support if we have an explicit ExecutablePath file
            return !string.IsNullOrEmpty(game.ExecutablePath) && File.Exists(game.ExecutablePath);
        }

        public Task LaunchAsync(Game game)
        {
            if (string.IsNullOrEmpty(game.ExecutablePath))
            {
                throw new InvalidOperationException("Game executable path is not set.");
            }

            var path = game.ExecutablePath;
            var directory = Path.GetDirectoryName(path);
            
            _logger.Info($"[NativeLaunchStrategy] Launching: {path}");

            var startInfo = new ProcessStartInfo();
            startInfo.WorkingDirectory = directory;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo.FileName = path;
                startInfo.UseShellExecute = true; 
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                startInfo.FileName = "open";
                startInfo.Arguments = $"\"{path}\"";
                startInfo.UseShellExecute = false; 
            }
            else
            {
                // Linux logic with Proton/Wine/Native support
                ConfigureLinuxLaunch(startInfo, path, game.PreferredRunner);
            }

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                _logger.Error($"[NativeLaunchStrategy] Error: {ex.Message}");
                throw;
            }

            return Task.CompletedTask;
        }

        public static void ConfigureLinuxLaunch(ProcessStartInfo startInfo, string path, string? preferredRunner)
        {
            var isWindowsExe = Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
            startInfo.UseShellExecute = false;

            if (!isWindowsExe)
            {
                // Native Linux binary
                startInfo.FileName = path;
                _logger.Info("[NativeLaunchStrategy] Linux native binary detected.");
                return;
            }

            // Windows .exe on Linux - resolve runner
            var runner = ResolveRunner(preferredRunner);
            _logger.Info($"[NativeLaunchStrategy] Windows exe on Linux. Runner: {runner}");

            switch (runner)
            {
                case "proton":
                    var protonPath = FindProtonPath();
                    if (protonPath != null)
                    {
                        startInfo.FileName = protonPath;
                        startInfo.Arguments = $"run \"{path}\"";
                        startInfo.EnvironmentVariables["STEAM_COMPAT_DATA_PATH"] = 
                            Path.Combine(Path.GetDirectoryName(path) ?? "/tmp", "pfx");
                        startInfo.EnvironmentVariables["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = 
                            FindSteamInstallPath() ?? "";
                        _logger.Info($"[NativeLaunchStrategy] Using Proton: {protonPath}");
                    }
                    else
                    {
                        _logger.Info("[NativeLaunchStrategy] Proton not found, falling back to Wine.");
                        startInfo.FileName = "wine";
                        startInfo.Arguments = $"\"{path}\"";
                    }
                    break;

                case "wine":
                default:
                    startInfo.FileName = "wine";
                    startInfo.Arguments = $"\"{path}\"";
                    break;
            }
        }

        public static string ResolveRunner(string? preferredRunner)
        {
            if (string.IsNullOrEmpty(preferredRunner) || preferredRunner == "auto")
            {
                // Auto-detect: prefer Proton if available, else Wine
                if (FindProtonPath() != null) return "proton";
                return "wine";
            }
            return preferredRunner.ToLowerInvariant();
        }

        public static string? FindProtonPath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var searchPaths = new[]
            {
                Path.Combine(home, ".steam", "steam", "steamapps", "common"),
                Path.Combine(home, ".local", "share", "Steam", "steamapps", "common"),
                Path.Combine(home, ".steam", "root", "steamapps", "common"),
                "/usr/share/steam/steamapps/common"
            };

            foreach (var basePath in searchPaths)
            {
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    // Find the newest Proton version
                    var protonDirs = Directory.GetDirectories(basePath, "Proton*")
                        .OrderByDescending(d => d)
                        .ToArray();

                    foreach (var dir in protonDirs)
                    {
                        var protonBin = Path.Combine(dir, "proton");
                        if (File.Exists(protonBin))
                        {
                            return protonBin;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"[NativeLaunchStrategy] Error scanning {basePath}: {ex.Message}");
                }
            }

            return null;
        }

        public static string? FindSteamInstallPath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var paths = new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".steam", "root")
            };

            foreach (var p in paths)
            {
                if (Directory.Exists(p)) return p;
            }
            return null;
        }
    }
}

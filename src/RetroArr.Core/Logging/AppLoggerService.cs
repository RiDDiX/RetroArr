using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Logging
{
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class AppLoggerService
    {
        private readonly ConfigurationService _configService;
        private string _logDirectory;
        private LoggingSettings _settings;

        // Feature logger name constants
        public const string LibraryOverview = "RetroArr.Api.Game";
        public const string ReleaseSearch = "RetroArr.Api.Search";
        public const string DownloadClient = "RetroArr.Api.DownloadClient";
        public const string ScannerMedia = "RetroArr.Scanner.Media";
        public const string ScannerMetadata = "RetroArr.Scanner.Metadata";
        public const string DownloadsImport = "RetroArr.Downloads.Import";
        public const string DownloadsMonitor = "RetroArr.Downloads.Monitor";
        public const string GogDownloads = "RetroArr.Gog";
        public const string HttpRequest = "RetroArr.Http.Request";
        public const string General = "RetroArr.General";
        public const string Switch = "RetroArr.Switch";
        public const string Configuration = "RetroArr.Configuration";
        public const string Plugins = "RetroArr.Plugins";
        public const string Launcher = "RetroArr.Launcher";

        // Feature → filename mapping
        private static readonly Dictionary<string, string> FeatureFileMap = new()
        {
            { LibraryOverview, "library__overview" },
            { ReleaseSearch, "releasesearch" },
            { DownloadClient, "downloads__client" },
            { ScannerMedia, "scanner__media" },
            { ScannerMetadata, "scanner__metadata" },
            { DownloadsImport, "downloads__import" },
            { DownloadsMonitor, "downloads__monitor" },
            { GogDownloads, "gog__downloads" },
            { HttpRequest, "api__requests" },
            { Switch, "switch__usb" },
            { Configuration, "configuration" },
            { Plugins, "plugins" },
            { Launcher, "launcher" },
        };

        public AppLoggerService(ConfigurationService configService)
        {
            _configService = configService;
            _settings = configService.LoadLoggingSettings();
            _logDirectory = configService.GetEffectiveLogDirectory();
        }

        public string LogDirectory => _logDirectory;

        public void Configure()
        {
            _settings = _configService.LoadLoggingSettings();
            _logDirectory = _configService.GetEffectiveLogDirectory();

            if (!_settings.Enabled)
            {
                LogManager.Configuration = new LoggingConfiguration();
                return;
            }

            try
            {
                Directory.CreateDirectory(_logDirectory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logging] Cannot create log directory '{_logDirectory}': {ex.Message}");
                return;
            }

            var config = new LoggingConfiguration();
            var minLevel = ParseLevel(_settings.LogLevel);
            var archiveSize = _settings.RotateSizeMb * 1024 * 1024;

            // Layout with correlation ID support
            var layout = new SimpleLayout(
                "${longdate}|${level:uppercase=true:padding=-5}|${scopeproperty:item=RequestId:whenEmpty=-}|${logger}|${message}${onexception:inner=|${exception:format=tostring}}");

            // App.log - catches everything from RetroArr loggers
            var appTarget = CreateFileTarget("app", "app", layout, archiveSize);
            config.AddTarget(appTarget);

            // Console target (keep stdout working)
            var consoleTarget = new ConsoleTarget("console")
            {
                Layout = new SimpleLayout("[${level:uppercase=true:padding=-3}][${logger:shortName=true}] ${message}")
            };
            config.AddTarget(consoleTarget);

            // 1) Suppress noisy framework loggers FIRST (final=true stops further rule processing)
            //    EF Core produces ~20 DEBUG lines per DB operation → catastrophic at 15K games
            config.AddRule(NLog.LogLevel.Warn, NLog.LogLevel.Fatal, appTarget, "Microsoft.EntityFrameworkCore*", true);
            config.AddRule(NLog.LogLevel.Warn, NLog.LogLevel.Fatal, appTarget, "Microsoft.*", true);
            config.AddRule(NLog.LogLevel.Warn, NLog.LogLevel.Fatal, consoleTarget, "Microsoft.*", true);

            // 2) Per-feature file targets (before catch-all so they match first)
            if (_settings.PerFeatureFiles)
            {
                foreach (var kvp in FeatureFileMap)
                {
                    var target = CreateFileTarget(kvp.Value, kvp.Value, layout, archiveSize);
                    config.AddTarget(target);
                    config.AddRule(minLevel, NLog.LogLevel.Fatal, target, kvp.Key + "*");
                }
            }

            // 3) Catch-all rules (everything else)
            config.AddRule(minLevel, NLog.LogLevel.Fatal, appTarget);
            config.AddRule(minLevel, NLog.LogLevel.Fatal, consoleTarget);

            LogManager.Configuration = config;
        }

        public void Reconfigure()
        {
            Configure();
        }

        public List<string> GetLogFiles()
        {
            var files = new List<string>();
            if (Directory.Exists(_logDirectory))
            {
                foreach (var f in Directory.GetFiles(_logDirectory, "*.log"))
                {
                    files.Add(f);
                }
            }
            return files;
        }

        private FileTarget CreateFileTarget(string name, string filePrefix, Layout layout, long archiveSize)
        {
            return new FileTarget(name)
            {
                FileName = Path.Combine(_logDirectory, $"{filePrefix}.log"),
                Layout = layout,
                ArchiveFileName = Path.Combine(_logDirectory, $"{filePrefix}.{{#}}.log"),
                ArchiveAboveSize = archiveSize,
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = Math.Max(1, _settings.MaxTotalSizeMb / Math.Max(1, _settings.RotateSizeMb)),
                MaxArchiveDays = _settings.MaxDays,
                ConcurrentWrites = true,
                KeepFileOpen = true,
            };
        }

        private static NLog.LogLevel ParseLevel(string level)
        {
            return level?.ToUpperInvariant() switch
            {
                "DEBUG" => NLog.LogLevel.Debug,
                "INFO" => NLog.LogLevel.Info,
                "WARN" => NLog.LogLevel.Warn,
                "WARNING" => NLog.LogLevel.Warn,
                "ERROR" => NLog.LogLevel.Error,
                _ => NLog.LogLevel.Info
            };
        }
    }
}

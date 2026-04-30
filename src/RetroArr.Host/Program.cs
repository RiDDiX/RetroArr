using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;
using System.IO;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Steam;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.Download;
using RetroArr.Core.Prowlarr;
using RetroArr.Core.Jackett;
using RetroArr.Core.Configuration;
using RetroArr.Core.Logging;
using System.Linq;
using Photino.NET;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using NLog.Extensions.Logging;
using Microsoft.AspNetCore.ResponseCompression;
using RetroArr.Host.Middleware;

namespace RetroArr.Host
{
    [SuppressMessage("Microsoft.Design", "CA1052:StaticHolderTypesShouldBeSealed")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters")]
    [SuppressMessage("Microsoft.Globalization", "CA1310:SpecifyStringComparison")]
    [SuppressMessage("Microsoft.Usage", "CA2012:UseValueTasksCorrectly")]
    public class Program
    {
        private static string? _logPath;

        public static void Log(string message)
        {
            var logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Console.WriteLine(logLine);
            if (_logPath != null)
            {
                try 
                {
                    // Log Rotation: Keep it under 10MB
                    var fileInfo = new FileInfo(_logPath);
                    if (fileInfo.Exists && fileInfo.Length > 10 * 1024 * 1024)
                    {
                        var oldLog = _logPath + ".old";
                        if (File.Exists(oldLog)) File.Delete(oldLog);
                        File.Move(_logPath, oldLog);
                    }
                    File.AppendAllText(_logPath, logLine + Environment.NewLine); 
                } 
                catch { }
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            try 
            {
                var exePath = AppContext.BaseDirectory;
                
                // Temporary log path until config service is ready
                _logPath = Path.Combine(Path.GetTempPath(), "RetroArr_startup.log");
                Log("=== RetroArr Startup Started ===");

                // LibUsb logic removed (Moved to RetroArr.UsbHelper)

                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    Args = args,
                    ContentRootPath = exePath
                });
            // Add services
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(RetroArr.Api.V3.Games.GameController).Assembly)
                .AddNewtonsoftJson(options => {
                     options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
                     options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
                });

            // DEBUG: Print all discovered controllers to console
            var feature = new Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature();
            builder.Services.AddMvc().PartManager.PopulateFeature(feature);
            Console.WriteLine($"[Startup-Debug] Discovered {feature.Controllers.Count} controllers:");
            foreach (var c in feature.Controllers)
            {
                Console.WriteLine($"[Startup-Debug] Controller: {c.Name} ({c.Namespace})");
            }
            
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddHttpClient(); // Register IHttpClientFactory

            builder.Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/json" });
            });

            // Add CORS for development
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            // Configuration service for persistence
            var configPath = Path.Combine(exePath, "config");

            // In development/build scenarios, the exe is deep in _output/net8.0/osx-arm64/
            // We want to look for the 'config' folder in the project root so it persists across builds (which wipe _output)
            if (!Directory.Exists(configPath))
            {
                // Try to find project root by looking for the 'config' folder up the tree
                var candidate = exePath;
                bool found = false;
 
                // 1. Try relative search (works for Terminal runs)
                for (int i = 0; i < 10; i++)
                {
                    candidate = Path.GetDirectoryName(candidate);
                    if (candidate == null) break;
                    
                    var checkPath = Path.Combine(candidate, "config");
                    if (Directory.Exists(checkPath))
                    {
                        configPath = checkPath;
                        exePath = candidate; 
                        found = true;
                        break;
                    }
                }
 
                // 2. Fallback for macOS App Translocation / Sandbox (works for .app double-click)
                if (!found)
                {
                     // Use standard ApplicationData folder as ultimate fallback for config
                     var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                     var appDataConfig = Path.Combine(appData, "RetroArr", "config");
                     
                     if (Directory.Exists(appDataConfig))
                     {
                         configPath = appDataConfig;
                         // exePath remains where the executable is
                     }
                }
            }
            
            // DataProtection-backed secret protector for credentials-at-rest.
            // Key material is kept in the same config directory so the same user/container owns it.
            var bootstrapConfig = new ConfigurationService(exePath);
            var keyDir = Path.Combine(bootstrapConfig.GetConfigDirectory(), "keys");
            SecretProtector? secretProtector = null;
            try { secretProtector = new SecretProtector(keyDir); }
            catch (Exception ex) { Log($"[Startup] Warning: could not initialize SecretProtector - secrets will stay plaintext: {ex.Message}"); }

            // Note: ConfigurationService adds "/config" to the path passed to it
            var configService = new ConfigurationService(exePath, secretProtector);
            builder.Services.AddSingleton(configService);
            if (secretProtector != null) builder.Services.AddSingleton(secretProtector);

            // API key (persisted under configDir/apikey.json; generated on first boot)
            var apiKeyService = new ApiKeyService(configService);
            builder.Services.AddSingleton(apiKeyService);

            // Initialize Log Path
            _logPath = Path.Combine(configService.GetConfigDirectory(), "RetroArr.log");
            try { File.WriteAllText(_logPath, $"--- RetroArr Startup {DateTime.Now} ---" + Environment.NewLine); } catch { }
            Log($"[Startup] EXE Path: {exePath}");
            Log($"[Startup] Config Path: {configService.GetConfigDirectory()}");

            // Initialize structured logging via NLog
            var appLoggerService = new AppLoggerService(configService);
            appLoggerService.Configure();
            builder.Services.AddSingleton(appLoggerService);
            builder.Logging.ClearProviders();
            builder.Logging.AddNLog();

            // Database: use DatabaseSettings (supports SQLite, PostgreSQL, MariaDB)
            builder.Services.AddRetroArrDatabase(configService);

            // Cache: optional Redis layer (cache-aside pattern)
            var cacheSettings = configService.LoadCacheSettings();
            builder.Services.AddSingleton(cacheSettings);
            if (cacheSettings.Enabled)
            {
                try
                {
                    var redisCache = new RetroArr.Core.Cache.RedisCacheService(cacheSettings);
                    builder.Services.AddSingleton<RetroArr.Core.Cache.ICacheService>(redisCache);
                    Console.WriteLine("[Cache] Redis cache enabled.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Cache] Redis connection failed, falling back to no-cache: {ex.Message}");
                    builder.Services.AddSingleton<RetroArr.Core.Cache.ICacheService>(new RetroArr.Core.Cache.NullCacheService());
                }
            }
            else
            {
                builder.Services.AddSingleton<RetroArr.Core.Cache.ICacheService>(new RetroArr.Core.Cache.NullCacheService());
            }

            // Game repository with cache decorator
            builder.Services.AddSingleton<SqliteGameRepository>();
            builder.Services.AddSingleton<IGameRepository>(sp =>
                new RetroArr.Core.Cache.CachedGameRepository(
                    sp.GetRequiredService<SqliteGameRepository>(),
                    sp.GetRequiredService<RetroArr.Core.Cache.ICacheService>(),
                    sp.GetRequiredService<RetroArr.Core.Configuration.CacheSettings>()));
            builder.Services.AddSingleton<IGameMetadataServiceFactory, GameMetadataServiceFactory>();
            builder.Services.AddSingleton<RetroArr.Core.Debug.DebugLogService>();
            builder.Services.AddSingleton<TitleCleanerService>();
            builder.Services.AddSingleton<MediaScannerService>();
            builder.Services.AddSingleton<RetroArr.Core.Games.DuplicateGameMergeService>();
            builder.Services.AddSingleton<TrashService>();
            builder.Services.AddHostedService<TrashPurgeService>();

            // SignalR progress hub + notifier
            builder.Services.AddSignalR();
            builder.Services.AddSingleton<RetroArr.SignalR.IProgressNotifier, RetroArr.SignalR.ProgressNotifier>();
            
            // IO Services
            builder.Services.AddSingleton<RetroArr.Core.IO.IFileMoverService, RetroArr.Core.IO.FileMoverService>();
            builder.Services.AddSingleton<RetroArr.Core.IO.IArchiveService, RetroArr.Core.IO.ArchiveService>();

            // Post-Download Management
            builder.Services.AddSingleton(new RetroArr.Core.Download.DownloadPlatformTracker(configService.GetConfigDirectory()));
            builder.Services.AddSingleton(new RetroArr.Core.Download.TrackedDownloads.TrackedDownloadService(configService.GetConfigDirectory()));
            builder.Services.AddSingleton<RetroArr.Core.Download.PostDownloadProcessor>();
            builder.Services.AddSingleton<RetroArr.Core.Download.TrackedDownloads.CompletedDownloadService>();
            builder.Services.AddSingleton<RetroArr.Core.Download.DownloadMonitorService>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<RetroArr.Core.Download.DownloadMonitorService>());
            builder.Services.AddSingleton<RetroArr.Core.Download.ImportStatusService>();
            builder.Services.AddSingleton<RetroArr.Core.Download.RenameQueueService>();
            builder.Services.AddSingleton<RetroArr.Core.Download.History.DownloadHistoryRepository>();
            builder.Services.AddSingleton<RetroArr.Core.Download.History.DownloadBlacklistRepository>();
            builder.Services.AddSingleton<RetroArr.Core.Games.DiscoveredGameRepository>();

            // GOG Download Tracker
            builder.Services.AddSingleton<RetroArr.Core.MetadataSource.Gog.GogDownloadTracker>();

            // Library Resort / Rename
            builder.Services.AddSingleton<RetroArr.Core.Games.LibraryResortService>();

            // Import Review workflow
            builder.Services.AddSingleton<RetroArr.Core.Games.ReviewItemService>();

            // Local Media Export (downloads metadata images/videos to platform folders)
            builder.Services.AddSingleton<RetroArr.Core.MetadataSource.LocalMediaExportService>(sp =>
                new RetroArr.Core.MetadataSource.LocalMediaExportService(
                    sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient("LocalMediaExport")));


            // Database migration service
            builder.Services.AddSingleton<RetroArr.Core.Data.DatabaseMigrationService>();

            // Installer & Update Scanner
            builder.Services.AddSingleton<RetroArr.Core.Games.InstallerScannerService>();

            // Switch USB
            builder.Services.AddSingleton<RetroArr.Core.Switch.ISwitchUsbService, RetroArr.Core.Switch.SwitchUsbService>();

            // Webhook Notifications
            builder.Services.AddScoped<RetroArr.Core.Notifications.IWebhookService, RetroArr.Core.Notifications.WebhookService>();

            // Launch Services
            builder.Services.AddSingleton<RetroArr.Core.Launcher.ILaunchStrategy, RetroArr.Core.Launcher.SteamLaunchStrategy>();
            builder.Services.AddSingleton<RetroArr.Core.Launcher.ILaunchStrategy, RetroArr.Core.Launcher.GogLaunchStrategy>();
            builder.Services.AddSingleton<RetroArr.Core.Launcher.ILaunchStrategy, RetroArr.Core.Launcher.NativeLaunchStrategy>();
            builder.Services.AddSingleton<RetroArr.Core.Launcher.ILauncherService, RetroArr.Core.Launcher.LauncherService>();

            // Linux Gaming Export Services
            builder.Services.AddSingleton<RetroArr.Core.Linux.LutrisExportService>();
            builder.Services.AddSingleton<RetroArr.Core.Linux.SteamShortcutExportService>();
            builder.Services.AddSingleton<RetroArr.Core.Linux.DesktopEntryExportService>();

            // Plugin Engine
            builder.Services.AddSingleton<RetroArr.Core.Plugins.PluginLoader>(sp =>
                new RetroArr.Core.Plugins.PluginLoader(
                    System.IO.Path.Combine(configService.GetConfigDirectory(), "plugins")));
            builder.Services.AddSingleton<RetroArr.Core.Plugins.PluginExecutor>();
            
            // Register SteamClient for direct usage (e.g. Settings Test/Sync)
            builder.Services.AddTransient<SteamClient>();
            
            
            // Show IGDB status at startup
            var igdbSettings = configService.LoadIgdbSettings();
            if (!igdbSettings.IsConfigured)
            {
                Console.WriteLine("WARNING: IGDB credentials not configured. Game search will return 0 results. Configure via Settings API.");
            }
            else
            {
                Console.WriteLine("IGDB credentials loaded from persistent configuration.");
            }
            
            // Configure Prowlarr settings - load from persistent config
            var prowlarrSettings = configService.LoadProwlarrSettings();
            builder.Services.AddSingleton(prowlarrSettings);

            // Configure Jackett settings - load from persistent config
            var jackettSettings = configService.LoadJackettSettings();
            builder.Services.AddSingleton(jackettSettings);

            // Configure Kestrel to use a dynamic port (0) to avoid conflicts (Address already in use)
            // This is crucial for desktop apps where we can't guarantee a specific port is free
            // CHECK IF RUNNING IN CONTAINER OR HEADLESS
            var envVar = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
            var isHeadless = args.Contains("--headless") || 
                             envVar == "true" || 
                             builder.Configuration.GetValue<bool>("HeadlessMode");
            
            Console.WriteLine($"[Startup] Checking Headless Mode: Args={string.Join(",", args)}, Config={builder.Configuration.GetValue<bool>("HeadlessMode")}, EnvVar={envVar}, Result={isHeadless}");

            if (!isHeadless)
            {
                builder.WebHost.ConfigureKestrel(serverOptions =>
                {
                    // PROFESSIONAL: Try a fixed range of ports first, then fallback to dynamic
                    // This is more professional than total randomness as it helps with firewall rules.
                    int[] preferredPorts = { 5002, 5003, 5004, 5005 };
                    bool bound = false;
                    foreach (var port in preferredPorts)
                    {
                        try {
                            serverOptions.Listen(System.Net.IPAddress.Loopback, port);
                            bound = true;
                            break;
                        } catch { }
                    }
                    
                    if (!bound) {
                        serverOptions.Listen(System.Net.IPAddress.Loopback, 0); // Total fallback
                    }
                });
            }
            // ELSE: Let Kestrel use default config (ASPNETCORE_URLS) which is ideal for Docker

            var app = builder.Build();

            // Read x-forwarded-* from swag/traefik/caddy/nginx so IsHttps and
            // the client ip come out right when a proxy terminates tls.
            // Trusts rfc1918 + loopback by default - override with
            // RETROARR_TRUSTED_PROXIES="172.20.0.0/16,10.1.2.3/32" if you
            // need to tighten it.
            var forwardedOpts = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
            {
                ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                                 | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                                 | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
                ForwardLimit = null
            };
            forwardedOpts.KnownNetworks.Clear();
            forwardedOpts.KnownProxies.Clear();

            var trustedEnv = Environment.GetEnvironmentVariable("RETROARR_TRUSTED_PROXIES");
            var trustedRanges = !string.IsNullOrWhiteSpace(trustedEnv)
                ? trustedEnv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128" };

            foreach (var range in trustedRanges)
            {
                try
                {
                    var slash = range.IndexOf('/');
                    var addr = slash > 0 ? range.Substring(0, slash) : range;
                    var bits = slash > 0 ? int.Parse(range.Substring(slash + 1), System.Globalization.CultureInfo.InvariantCulture) : 32;
                    forwardedOpts.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse(addr), bits));
                }
                catch (Exception ex)
                {
                    Log($"[ForwardedHeaders] ignoring bad proxy range '{range}': {ex.Message}");
                }
            }
            app.UseForwardedHeaders(forwardedOpts);

            // Configure middleware
            app.UseResponseCompression();
            app.UseDeveloperExceptionPage(); // FORCE DEBUG
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // Initialize database
            using (var scope = app.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<RetroArrDbContext>();
                try
                {
                    context.Database.EnsureCreated();
                    Console.WriteLine("[Database] Schema ensured.");

                    // Consolidated schema migrations (columns, tables, indexes)
                    var dbSettings = configService.LoadDatabaseSettings();
                    RetroArr.Core.Data.DatabaseMigrator.ApplyMigrations(context, dbSettings.Type);

                    // Ensure ALL platforms from PlatformDefinitions exist in DB
                    Console.WriteLine("[Database] Verifying all platforms from PlatformDefinitions...");
                    
                    bool changesMade = false;
                    foreach (var platform in RetroArr.Core.Games.PlatformDefinitions.AllPlatforms)
                    {
                        if (!context.Platforms.Any(p => p.Id == platform.Id))
                        {
                            Console.WriteLine($"[Database] Seeding missing platform: {platform.Name} (ID: {platform.Id})");
                            context.Platforms.Add(platform);
                            changesMade = true;
                        }
                    }

                    if (changesMade)
                    {
                        context.SaveChanges();
                        Console.WriteLine("[Database] Platforms updated.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Database] Error initializing database: {ex.Message}");
                }
            }

            app.UseCors();
            
            // Configure static files - Look for _output/UI relative to the EXECUTABLE
            // In dev: AppContext.BaseDirectory is usually bin/Debug/net8.0/
            // In prod (single file): AppContext.BaseDirectory is where the .exe is.
            
            // Try to find the UI folder. 
            // 1. Production: ./_output/UI (next to exe)
            // 2. Dev: ../../../../../_output/UI (relative to bin debug)
            
            var uiPath = Path.Combine(exePath, "_output", "UI");
            
            // Search strategy for UI folder:
            // 1. Next to exe in _output/UI 
            // 2. One level up in UI (if bin is in _output/net8.0/)
            // 3. Dev environment (5 levels up from bin/Debug/net8.0/...)
            
            if (!Directory.Exists(uiPath))
            {
                 // Try one level up (common if exe is in _output/net8.0/ and UI is in _output/UI)
                 var parentPath = Path.GetFullPath(Path.Combine(exePath, "..", "UI"));
                 if (Directory.Exists(parentPath))
                 {
                     uiPath = parentPath;
                 }
                 else
                 {
                     // Fallback for development if not found next to dll
                     var potentialDevPath = Path.GetFullPath(Path.Combine(exePath, "..", "..", "..", "..", "..", "_output", "UI"));
                     if (Directory.Exists(potentialDevPath))
                     {
                         uiPath = potentialDevPath;
                     }
                     else
                     {
                         // Fallback using CurrentDirectory (useful for dotnet run)
                         // PWD = src/RetroArr.Host
                         // Target = _output/UI (in project root)
                         // Path = ../../_output/UI
                         var pwdPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "_output", "UI"));
                         if (Directory.Exists(pwdPath))
                         {
                             uiPath = pwdPath;
                         }
                     }
                 }
            }
            
            if (Directory.Exists(uiPath))
            {
                Console.WriteLine($"[UI] Serving static files from: {uiPath}");
                var fileProvider = new PhysicalFileProvider(uiPath);
                
                var defaultFilesOptions = new DefaultFilesOptions
                {
                    FileProvider = fileProvider
                };
                defaultFilesOptions.DefaultFileNames.Clear();
                defaultFilesOptions.DefaultFileNames.Add("index.html");
                app.UseDefaultFiles(defaultFilesOptions);
                
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = fileProvider
                });
            }
            else
            {
                Console.WriteLine($"WARNING: UI directory not found at {uiPath}. Ensure _output/UI is copied next to the executable.");
            }
            
            // Serve platform icons from frontend/public/platforms (development only)
            // In Docker/production, icons are bundled in _output/UI/platforms/ and served via the UI static files above
            var platformsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "frontend", "public", "platforms"));
            if (!Directory.Exists(platformsPath))
            {
                platformsPath = Path.GetFullPath(Path.Combine(exePath, "..", "..", "..", "..", "..", "frontend", "public", "platforms"));
            }
            if (Directory.Exists(platformsPath))
            {
                Console.WriteLine($"[Platforms] Serving platform icons from: {platformsPath}");
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(platformsPath),
                    RequestPath = "/platforms"
                });
            }
            
            app.UseRouting();
            app.UseAuthorization();

            // API key gate for non-loopback requests.
            app.UseMiddleware<RetroArr.Api.V3.Auth.ApiKeyAuthMiddleware>();

            // Correlation ID + structured request logging
            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseMiddleware<RequestLoggingMiddleware>();

            app.MapControllers();
            app.MapHub<RetroArr.SignalR.ProgressHub>("/hubs/progress");

            var progressNotifier = app.Services.GetRequiredService<RetroArr.SignalR.IProgressNotifier>();
            var scannerForHub = app.Services.GetRequiredService<MediaScannerService>();

            void Fire(System.Threading.Tasks.Task task, string label)
            {
                task.ContinueWith(
                    t => Log($"[SignalR] {label} notify failed: {t.Exception?.GetBaseException().Message}"),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted
                    | System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
            }

            scannerForHub.OnScanStarted += () => Fire(progressNotifier.ScanStartedAsync(), "scanStarted");
            scannerForHub.OnScanFinished += gamesAdded =>
            {
                Fire(progressNotifier.ScanFinishedAsync(gamesAdded), "scanFinished");
                Fire(progressNotifier.LibraryUpdatedAsync(), "libraryUpdated");
            };
            // Notify per batch (every ~20 games) instead of per game to
            // avoid flooding the frontend with hundreds of SignalR events.
            scannerForHub.OnBatchFinished += () => Fire(progressNotifier.LibraryUpdatedAsync(), "libraryUpdated");

            // Serve frontend - fallback to index.html for SPA routing.
            // Add coop/coep here so the spa page is cross-origin isolated
            // (needed for SharedArrayBuffer in the embedded emulator iframe).
            // credentialless so igdb/steam cover cdns keep loading.
            if (Directory.Exists(uiPath))
            {
                app.MapFallback(context =>
                {
                    var indexPath = Path.Combine(uiPath, "index.html");
                    var html = File.ReadAllText(indexPath);
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
                    context.Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
                    return context.Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(html)).AsTask();
                });
            }

            // Helper to open browser
            Action<string> OpenBrowser = (url) => 
            {
                try
                {
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                    }
                    else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                    {
                        System.Diagnostics.Process.Start("xdg-open", url);
                    }
                    else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                    {
                        System.Diagnostics.Process.Start("open", url);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to open browser: {ex.Message}");
                }
            };

            // Kestrel is configured via appsettings or LaunchSettings to listen on 5001
            // We need to start the app non-blocking
            app.Start();
            Log("[Startup] Kestrel server started.");

            var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
            var addressFeature = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

            // PROFESSIONAL: Get the assigned address and normalize to localhost
            string? rawAddress = addressFeature?.Addresses.FirstOrDefault();
            
            // Wait for address population if dynamic
            if (string.IsNullOrEmpty(rawAddress))
            {
                 Log("[Startup] Waiting for Kestrel address to be populated...");
                 for (int i = 0; i < 5 && string.IsNullOrEmpty(rawAddress); i++)
                 {
                     System.Threading.Thread.Sleep(100);
                     rawAddress = addressFeature?.Addresses.FirstOrDefault();
                 }
            }

            rawAddress ??= "http://localhost:5001"; // Fallback
            
            // Use 127.0.0.1 for internal alive-check
            string internalAddress = rawAddress;
            if (internalAddress.Contains("localhost")) internalAddress = internalAddress.Replace("localhost", "127.0.0.1");
            
            // Define the final UI address
            string address = rawAddress;
            if (address.Contains("127.0.0.1")) address = address.Replace("127.0.0.1", "localhost");
            if (address.Contains("[::1]")) address = address.Replace("[::1]", "localhost");

            // Wait for the server to actually be alive and serving content before
            // we hand off to Photino / wait-for-shutdown. GetAwaiter().GetResult()
            // makes the sync-over-async intent explicit (deadlock-free here:
            // we're on the main console thread, no SynchronizationContext).
            Log($"[Startup] Waiting for backend at {internalAddress}...");
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(1);
                    for (int i = 0; i < 30; i++)
                    {
                        try
                        {
                            var response = client.GetAsync(internalAddress).GetAwaiter().GetResult();
                            if (response.IsSuccessStatusCode)
                            {
                                Log($"[Startup] Backend is ALIVE on {internalAddress}");
                                break;
                            }
                        }
                        catch { }
                        System.Threading.Thread.Sleep(100);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[Startup] Warning: Alive-check failed to execute: {ex.Message}");
            }

            Log($"[Startup] RetroArr backend ready on: {address}");
            
            if (isHeadless)
            {
                 Log("Running in Headless Mode (Docker/Server). Press Ctrl+C to exit.");
                 app.WaitForShutdown();
            }
            else
            {
                // Launch Photino Window
                // This blocks until the window is closed
                try 
                {
                   // DEBUG: List all endpoints
                   var dataSources = app.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>();
                   foreach (var dataSource in dataSources)
                   {
                       foreach (var endpoint in dataSource.Endpoints)
                       {
                           if (endpoint is Microsoft.AspNetCore.Routing.RouteEndpoint routeEndpoint)
                           {
                               Log($"[Route] {routeEndpoint.RoutePattern.RawText} -> {routeEndpoint.DisplayName}");
                           }
                       }
                   }

                   Log("[UI] Initializing Photino Window...");
                   var window = new Photino.NET.PhotinoWindow()
                       .SetTitle("RetroArr")
                       .SetUseOsDefaultSize(false)
                       .SetSize(new System.Drawing.Size(1280, 800))
                       .Center()
                       .SetResizable(true)
                       .SetDevToolsEnabled(true);
                   
                   bool isClosing = false;
                   window.WindowClosing += (s, e) => { 
                       isClosing = true; 
                       Log("[UI] Window is closing...");
                       return false; // Allow close
                   };
    
                   // Real-time library updates: Subscribe to scanner events
                   var scannerService = app.Services.GetRequiredService<MediaScannerService>();
                   
                   // Update library UI when a batch is finished
                    scannerService.OnBatchFinished += () => {
                        if (isClosing) return;
                        try {
                            window.Invoke(() => {
                                if (isClosing) return;
                                Log("[UI] Sending LIBRARY_UPDATED signal to frontend...");
                                try { window.SendWebMessage("LIBRARY_UPDATED"); } catch { }
                            });
                        } catch { }
                    };
    
                   // Fix for CS8622: Use object? for sender
                   window.RegisterWebMessageReceivedHandler((object? sender, string message) => {
                           if (sender is not Photino.NET.PhotinoWindow windowInstance) return;
    
                           // Handle messages from frontend
                           if (message.StartsWith("OPEN_URL:", StringComparison.OrdinalIgnoreCase))
                           {
                               var url = message.Substring("OPEN_URL:".Length);
                               OpenBrowser(url);
                           }
                           else if (message.StartsWith("SELECT_FOLDER"))
                           {
                               var folders = windowInstance.ShowOpenFolder();
                               if (folders != null && folders.Length > 0)
                               {
                                   var selectedPath = folders[0];
                                   
                                   try 
                                   {
                                        var currentMediaSettings = configService.LoadMediaSettings();
                                        
                                        if (message.Contains("DOWNLOAD"))
                                        {
                                            currentMediaSettings.DownloadPath = selectedPath;
                                        }
                                        else if (message.Contains("DESTINATION"))
                                        {
                                            currentMediaSettings.DestinationPath = selectedPath;
                                        }
                                        else
                                        {
                                            currentMediaSettings.FolderPath = selectedPath;
                                        }

                                        configService.SaveMediaSettings(currentMediaSettings);
                                                                                // Notify UI that settings have changed (this triggers SETTINGS_UPDATED_EVENT in JS)
                                         if (!isClosing)
                                         {
                                             windowInstance.Invoke(() => {
                                                 if (isClosing) return;
                                                 try {
                                                     windowInstance.SendWebMessage("SETTINGS_UPDATED");
                                                     windowInstance.SendWebMessage($"FOLDER_SELECTED:{selectedPath}");
                                                 } catch { }
                                             });
                                         }
                                   }
                                   catch (Exception ex)
                                   {
                                        Console.WriteLine($"Error saving folder selection: {ex.Message}");
                                   }
                               }
                           }
                       })
                        .Load(address); // Removed query string to avoid SPA routing issues
                       
                    window.WaitForClose(); // Blocks main thread
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to launch Photino Window: {ex.Message}");
                    // If native window fails (e.g. headless linux), keep running as console
                    Console.WriteLine("Running in Console mode (Server only). Press Ctrl+C to exit.");
                    app.WaitForShutdown(); 
                }
                finally
                {
                    // Ensure the app shuts down completely when the window is closed
                    Console.WriteLine("Window closed. Shutting down application...");
                    
                    // Graceful shutdown of Kestrel
                    try 
                    {
                        app.StopAsync().GetAwaiter().GetResult();
                        app.DisposeAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception shutdownEx)
                    {
                        Console.WriteLine($"Error during shutdown: {shutdownEx.Message}");
                    }
    
                    // Force process exit to ensure no background threads (like Kestrel) keep the process alive
                    Environment.Exit(0);
                }
            } // Close else
            } // Close try
            catch (Exception fatalEx)
            {
                Log($"[CRITICAL] Application failed to start: {fatalEx.Message}");
                Log(fatalEx.StackTrace ?? "No stack trace available.");
                // Ensure the console stays open if run manually
                Console.WriteLine("Press any key to exit...");
                try { Console.ReadKey(); } catch { }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Games;
using RetroArr.Core.Indexers;
using RetroArr.Core.Jackett;
using RetroArr.Core.Prowlarr;

namespace RetroArr.Core.Search
{
    public sealed class MonitorSearchResult
    {
        public int GameId { get; set; }
        public string GameTitle { get; set; } = string.Empty;
        public bool AutoQueued { get; set; }
        public string? AutoQueuedRelease { get; set; }
        public int? AutoQueuedScore { get; set; }
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<ScoredRelease> Scored { get; set; } = new();
        public string? Error { get; set; }
    }

    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    public sealed class MonitoredGameSearchService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ReleaseSearch);
        private readonly RetroArrDbContext _db;
        private readonly ConfigurationService _config;
        private readonly ReleaseScorer _scorer;
        private readonly DownloadPlatformTracker _platformTracker;

        public MonitoredGameSearchService(
            RetroArrDbContext db,
            ConfigurationService config,
            ReleaseScorer scorer,
            DownloadPlatformTracker platformTracker)
        {
            _db = db;
            _config = config;
            _scorer = scorer;
            _platformTracker = platformTracker;
        }

        public async Task<MonitorSearchResult> SearchAndMaybeDispatchAsync(
            int gameId, bool allowAutoDispatch, CancellationToken ct = default)
        {
            var game = await _db.Games
                .Include(g => g.Platform)
                .FirstOrDefaultAsync(g => g.Id == gameId, ct)
                .ConfigureAwait(false);

            if (game == null)
            {
                return new MonitorSearchResult { GameId = gameId, Error = "game not found" };
            }

            var settings = _config.LoadMonitorSettings();
            var result = new MonitorSearchResult { GameId = gameId, GameTitle = game.Title };

            // Build the query: game title plus platform tag helps narrow the hit-rate.
            var query = BuildQuery(game);
            if (string.IsNullOrWhiteSpace(query))
            {
                result.Error = "could not build search query";
                return result;
            }

            List<SearchResult> raw;
            try
            {
                raw = await RunSearchAsync(query, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Monitor] indexer search failed for game {gameId}: {ex.Message}");
                result.Error = "indexer search failed";
                return result;
            }

            // Platform detection
            foreach (var r in raw)
            {
                PlatformDetector.DetectPlatform(r);
            }

            // De-duplicate by title + size (same as SearchController)
            var unique = raw
                .GroupBy(r => new { r.Title, r.Size })
                .Select(g => g.First())
                .ToList();

            // Score
            var scored = unique
                .Select(r => _scorer.Score(r, game, settings))
                .Where(s => s.Decision != ReleaseDecision.Reject)
                .OrderByDescending(s => s.Score)
                .ToList();

            result.Scored = scored;

            // Auto-dispatch if eligible
            if (allowAutoDispatch && settings.Enabled)
            {
                var top = scored.FirstOrDefault(s => s.Decision == ReleaseDecision.AutoDownload);
                if (top != null)
                {
                    try
                    {
                        var dispatched = await DispatchToDownloadClientAsync(top.Release, game, ct).ConfigureAwait(false);
                        if (dispatched)
                        {
                            result.AutoQueued = true;
                            result.AutoQueuedRelease = top.Release.Title;
                            result.AutoQueuedScore = top.Score;
                            _logger.Info($"[Monitor] auto-queued '{top.Release.Title}' for game {gameId} (score={top.Score})");
                        }
                        else
                        {
                            _logger.Warn($"[Monitor] dispatch returned false for game {gameId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"[Monitor] auto-dispatch failed for game {gameId}: {ex.Message}");
                    }
                }
            }

            return result;
        }

        public async Task RunSweepAsync(CancellationToken ct = default)
        {
            var settings = _config.LoadMonitorSettings();
            if (!settings.Enabled)
            {
                _logger.Info("[Monitor] sweep skipped: disabled in settings.");
                return;
            }

            var monitored = await _db.Games
                .Where(g => g.Monitored && g.Status != GameStatus.Downloaded)
                .Include(g => g.Platform)
                .OrderBy(g => g.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            _logger.Info($"[Monitor] sweep starting: {monitored.Count} monitored game(s).");

            foreach (var g in monitored)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    await SearchAndMaybeDispatchAsync(g.Id, allowAutoDispatch: true, ct).ConfigureAwait(false);
                    // Polite stagger between games to be kind to indexers.
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.Warn($"[Monitor] sweep tick failed for game {g.Id}: {ex.Message}");
                }
            }
            _logger.Info("[Monitor] sweep done.");
        }

        // ---- Internals ----

        private static string BuildQuery(Game game)
        {
            // Strip punctuation that breaks Newznab queries, like SearchController does.
            var raw = game.Title ?? string.Empty;
            var sanitized = System.Text.RegularExpressions.Regex.Replace(raw, @"[:\(\)\[\]\{\}""'™®©\p{Pd}]", " ");
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\s{2,}", " ").Trim();
            return sanitized;
        }

        private async Task<List<SearchResult>> RunSearchAsync(string query, CancellationToken ct)
        {
            var results = new List<SearchResult>();
            var prowlarr = _config.LoadProwlarrSettings();
            var jackett = _config.LoadJackettSettings();
            var hydraConfigs = _config.LoadHydraIndexers().Where(h => h.Enabled).ToList();

            var tasks = new List<Task<List<SearchResult>>>();

            if (prowlarr.IsConfigured && prowlarr.Enabled)
            {
                var client = new ProwlarrClient(prowlarr.Url, prowlarr.ApiKey);
                tasks.Add(client.SearchAsync(query, indexerIds: null, categories: null));
            }

            if (jackett.IsConfigured && jackett.Enabled)
            {
                var jClient = new JackettClient(jackett.Url, jackett.ApiKey);
                tasks.Add(jClient.SearchAsync(query, categories: null).ContinueWith(t =>
                {
                    if (t.IsFaulted || t.Result == null) return new List<SearchResult>();
                    return t.Result.Select(j => new SearchResult
                    {
                        Title = j.Title,
                        Guid = j.Guid,
                        Size = j.Size,
                        IndexerName = j.Tracker,
                        Seeders = j.Seeders,
                        Leechers = j.Leechers,
                        PeersFromIndexer = j.Peers,
                        PublishDate = j.PublishDate,
                        DownloadUrl = j.DownloadUrl,
                        MagnetUrl = j.MagnetUri,
                        InfoUrl = j.Guid,
                        Protocol = j.Protocol,
                        Provider = "Jackett"
                    }).ToList();
                }, ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
            }

            using var sharedClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            foreach (var hydra in hydraConfigs)
            {
                var hydraClient = new HydraIndexer(sharedClient, hydra.Name, hydra.Url);
                tasks.Add(hydraClient.SearchAsync(query));
            }

            if (tasks.Count == 0) return results;

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60), ct);
            var allTask = Task.WhenAll(tasks);
            var done = await Task.WhenAny(allTask, timeoutTask).ConfigureAwait(false);

            foreach (var t in tasks)
            {
                if (t.IsCompletedSuccessfully && t.Result != null)
                {
                    results.AddRange(t.Result);
                }
            }

            return results;
        }

        private async Task<bool> DispatchToDownloadClientAsync(SearchResult release, Game game, CancellationToken ct)
        {
            var clients = _config.LoadDownloadClients()
                .Where(c => c.Enable)
                .ToList();
            if (clients.Count == 0)
            {
                _logger.Warn("[Monitor] dispatch skipped: no enabled download client.");
                return false;
            }

            var isNzb = string.Equals(release.Protocol, "nzb", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(release.Protocol, "usenet", StringComparison.OrdinalIgnoreCase)
                     || (release.DownloadUrl?.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase) ?? false);

            var client = isNzb
                ? clients.Where(c => c.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase)
                                  || c.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(c => c.Priority).ThenBy(c => c.Id).FirstOrDefault()
                : clients.Where(c => !c.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase)
                                  && !c.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(c => c.Priority).ThenBy(c => c.Id).FirstOrDefault();

            if (client == null)
            {
                _logger.Warn($"[Monitor] dispatch skipped: no enabled {(isNzb ? "NZB" : "torrent")} client.");
                return false;
            }

            var url = !string.IsNullOrEmpty(release.MagnetUrl) ? release.MagnetUrl : release.DownloadUrl;
            if (string.IsNullOrEmpty(url))
            {
                _logger.Warn("[Monitor] dispatch skipped: release has no download URL.");
                return false;
            }

            var platformFolder = game.Platform?.FolderName;
            _platformTracker.Track(url, platformFolder, game.Id, null);

            bool sent = false;
            try
            {
                switch (client.Implementation?.ToLowerInvariant())
                {
                    case "qbittorrent":
                        sent = await new QBittorrentClient(client.Host, client.Port, client.Username ?? string.Empty, client.Password ?? string.Empty, client.UrlBase)
                            .AddTorrentAsync(url, client.Category ?? string.Empty).ConfigureAwait(false);
                        break;
                    case "transmission":
                        sent = await new TransmissionClient(client.Host, client.Port, client.Username ?? string.Empty, client.Password ?? string.Empty)
                            .AddTorrentAsync(url, client.Category ?? string.Empty).ConfigureAwait(false);
                        break;
                    case "deluge":
                        sent = await new DelugeClient(client.Host, client.Port, client.Password ?? string.Empty)
                            .AddTorrentAsync(url, client.Category ?? string.Empty).ConfigureAwait(false);
                        break;
                    case "sabnzbd":
                        sent = await new SabnzbdClient(client.Host, client.Port, client.ApiKey ?? string.Empty, client.UrlBase)
                            .AddNzbAsync(url, client.Category ?? string.Empty).ConfigureAwait(false);
                        break;
                    case "nzbget":
                        sent = await new NzbgetClient(client.Host, client.Port, client.Username ?? string.Empty, client.Password ?? string.Empty, client.UrlBase)
                            .AddNzbAsync(url, client.Category ?? string.Empty).ConfigureAwait(false);
                        break;
                    default:
                        _logger.Warn($"[Monitor] dispatch skipped: unknown client implementation '{client.Implementation}'.");
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Monitor] dispatch HTTP call failed: {ex.Message}");
                return false;
            }

            return sent;
        }
    }
}

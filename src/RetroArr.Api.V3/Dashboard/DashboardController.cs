using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Api.V3.Dashboard
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly RetroArrDbContext _context;
        private readonly IGameRepository _gameRepository;

        public DashboardController(RetroArrDbContext context, IGameRepository gameRepository)
        {
            _context = context;
            _gameRepository = gameRepository;
        }

        [HttpGet("stats")]
        public async Task<ActionResult> GetStats()
        {
            // Counted in SQL: loading every game to count them took seconds on a large library
            var byStatus = await _context.Games
                .GroupBy(g => g.Status)
                .Select(grp => new
                {
                    Status = grp.Key,
                    Count = grp.Count(),
                    Installed = grp.Sum(g => g.InstallPath != null && g.InstallPath.Length > 0 ? 1 : 0),
                    External = grp.Sum(g => g.IsExternal ? 1 : 0),
                    Excellent = grp.Sum(g => g.Rating >= 80 ? 1 : 0),
                    Good = grp.Sum(g => g.Rating >= 60 && g.Rating < 80 ? 1 : 0),
                    Average = grp.Sum(g => g.Rating >= 40 && g.Rating < 60 ? 1 : 0),
                    Poor = grp.Sum(g => g.Rating > 0 && g.Rating < 40 ? 1 : 0),
                    Unrated = grp.Sum(g => g.Rating == null || g.Rating == 0 ? 1 : 0)
                })
                .ToListAsync();

            var totalGames = byStatus.Sum(s => s.Count);
            var installedGames = byStatus.Sum(s => s.Installed);
            var externalGames = byStatus.Sum(s => s.External);
            var favoriteGames = 0; // Reviews track favorites
            
            // Genres is a JSON column, so only that column is read and counted here
            var genreStats = (await _context.Games.Select(g => g.Genres).ToListAsync())
                .Where(g => g != null)
                .SelectMany(g => g)
                .GroupBy(g => g)
                .Select(grp => new { Genre = grp.Key, Count = grp.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();

            // Platform distribution
            var platformStats = (await _context.Games
                    .Where(g => g.PlatformId > 0)
                    .GroupBy(g => g.PlatformId)
                    .Select(grp => new { PlatformId = grp.Key, Count = grp.Count() })
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.PlatformId)
                    .Take(10)
                    .ToListAsync())
                .Select(x => new
                { 
                    x.PlatformId,
                    Platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == x.PlatformId)?.Name ?? "Unknown",
                    x.Count
                })
                .ToList();

            // Year distribution
            var yearStats = await _context.Games
                .Where(g => g.Year > 1970)
                .GroupBy(g => g.Year)
                .Select(grp => new { Year = grp.Key, Count = grp.Count() })
                .OrderByDescending(x => x.Year)
                .Take(20)
                .ToListAsync();

            // Recently added games
            var recentlyAdded = await _context.Games
                .OrderByDescending(g => g.Added)
                .ThenBy(g => g.Id)
                .Take(10)
                .Select(g => new 
                {
                    g.Id,
                    g.Title,
                    g.Added,
                    CoverUrl = g.Images != null ? g.Images.CoverUrl : null
                })
                .ToListAsync();

            // Rating distribution
            var ratingStats = new
            {
                Excellent = byStatus.Sum(s => s.Excellent),
                Good = byStatus.Sum(s => s.Good),
                Average = byStatus.Sum(s => s.Average),
                Poor = byStatus.Sum(s => s.Poor),
                Unrated = byStatus.Sum(s => s.Unrated)
            };

            // Status distribution
            var statusStats = byStatus
                .Select(s => new { Status = s.Status.ToString(), s.Count })
                .ToList();

            return Ok(new
            {
                TotalGames = totalGames,
                InstalledGames = installedGames,
                ExternalGames = externalGames,
                FavoriteGames = favoriteGames,
                GenreStats = genreStats,
                PlatformStats = platformStats,
                YearStats = yearStats,
                RecentlyAdded = recentlyAdded,
                RatingStats = ratingStats,
                StatusStats = statusStats
            });
        }

        [HttpGet("random")]
        public async Task<ActionResult> GetRandomGame([FromQuery] int? platformId = null, [FromQuery] string? genre = null)
        {
            var query = _context.Games.AsNoTracking();

            if (platformId.HasValue)
            {
                query = query.Where(g => g.PlatformId == platformId.Value);
            }

            Game? randomGame = null;
            if (!string.IsNullOrEmpty(genre))
            {
                // Genres is a JSON column, so this can't run in SQL; only ids and genres are read
                var ids = (await query.Select(g => new { g.Id, g.Genres }).ToListAsync())
                    .Where(g => g.Genres != null && g.Genres.Contains(genre))
                    .Select(g => g.Id)
                    .ToList();
                if (ids.Count > 0)
                {
                    var id = ids[Random.Shared.Next(ids.Count)];
                    randomGame = await query.FirstOrDefaultAsync(g => g.Id == id);
                }
            }
            else
            {
                var count = await query.CountAsync();
                if (count > 0)
                {
                    randomGame = await query.OrderBy(g => g.Id).Skip(Random.Shared.Next(count)).FirstOrDefaultAsync();
                }
            }
            
            if (randomGame == null)
            {
                return NotFound(new { message = "No games found matching criteria" });
            }

            // Populate platform
            if (randomGame.PlatformId > 0)
            {
                randomGame.Platform = PlatformDefinitions.AllPlatforms
                    .FirstOrDefault(p => p.Id == randomGame.PlatformId);
            }

            return Ok(randomGame);
        }

        [HttpGet("shuffle")]
        public async Task<ActionResult> GetShuffledGames([FromQuery] int count = 5)
        {
            // Picks among the ids and loads only the picked games
            var ids = await _context.Games.Select(g => g.Id).ToListAsync();
            var picked = ids.OrderBy(_ => Random.Shared.Next()).Take(Math.Min(count, ids.Count)).ToList();
            var games = await _context.Games.AsNoTracking().Where(g => picked.Contains(g.Id)).ToListAsync();
            var shuffled = games.OrderBy(_ => Random.Shared.Next()).ToList();

            foreach (var game in shuffled)
            {
                if (game.PlatformId > 0)
                {
                    game.Platform = PlatformDefinitions.AllPlatforms
                        .FirstOrDefault(p => p.Id == game.PlatformId);
                }
            }

            return Ok(shuffled);
        }

        // An export runs a few KB per game, so Kestrel's 30 MB default turned large libraries away
        private const long ImportMaxBytes = 1L << 30;

        private sealed class ImportFile
        {
            public List<Game?>? Games { get; set; }
        }

        // Games are streamed to the response: the whole library as one string ran out of memory
        [HttpGet("export")]
        public async Task ExportLibrary()
        {
            var collections = await _context.Collections.AsNoTracking().Select(c => new
            {
                c.Id,
                c.Name,
                c.Description,
                c.CoverUrl,
                GameIds = c.CollectionGames.Select(cg => cg.GameId).ToList()
            }).ToListAsync();
            var tags = await _context.Tags.AsNoTracking().Select(t => new
            {
                t.Id,
                t.Name,
                t.Color,
                GameIds = t.GameTags.Select(gt => gt.GameId).ToList()
            }).ToListAsync();
            var reviews = await _context.GameReviews.AsNoTracking().Select(r => new
            {
                r.Id,
                r.GameId,
                r.UserRating,
                r.Notes,
                r.IsFavorite,
                r.IsWishlisted,
                r.CompletionStatus,
                r.StartedAt,
                r.CompletedAt,
                r.CreatedAt,
                r.UpdatedAt
            }).ToListAsync();

            var exportData = new
            {
                ExportDate = DateTime.UtcNow,
                Version = "1.0",
                Games = ExportGames(HttpContext.RequestAborted),
                Collections = collections,
                Tags = tags,
                Reviews = reviews
            };

            Response.ContentType = "application/json";
            Response.Headers.ContentDisposition = $"attachment; filename=RetroArr-export-{DateTime.UtcNow:yyyyMMdd}.json";
            await JsonSerializer.SerializeAsync(Response.Body, exportData, new JsonSerializerOptions
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }, HttpContext.RequestAborted);
        }

        // Read in batches by id: one open reader held SQLite's lock against every writer for the whole
        // download, and the retrying PostgreSQL/MySQL strategy buffers a whole result before the first row
        private async IAsyncEnumerable<object> ExportGames([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            const int batchSize = 500;
            var lastId = int.MinValue;
            while (true)
            {
                var batch = await _context.Games.AsNoTracking()
                    .Where(g => g.Id > lastId)
                    .OrderBy(g => g.Id)
                    .Take(batchSize)
                    .Select(g => new
                    {
                        g.Id,
                        g.Title,
                        g.Overview,
                        g.Storyline,
                        g.Year,
                        g.ReleaseDate,
                        g.Developer,
                        g.Publisher,
                        g.Genres,
                        g.PlatformId,
                        g.IgdbId,
                        g.SteamId,
                        g.GogId,
                        g.EpicId,
                        g.Rating,
                        g.Status,
                        g.InstallPath,
                        g.IsExternal,
                        g.Region,
                        g.Added,
                        Images = g.Images
                    })
                    .ToListAsync(cancellationToken);
                foreach (var game in batch) yield return game;
                if (batch.Count < batchSize) yield break;
                lastId = batch[^1].Id;
            }
        }

        [HttpPost("import")]
        [RequestSizeLimit(ImportMaxBytes)]
        public async Task<ActionResult> ImportLibrary()
        {
            try
            {
                // Read from the body stream: binding it to a JObject first kept three copies of a large export
                var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var file = await JsonSerializer.DeserializeAsync<ImportFile>(Request.Body, jsonOptions);
                if (file?.Games == null)
                {
                    return BadRequest(new { success = false, message = "Import failed: the file has no games list" });
                }
                var importedCount = 0;
                var skippedCount = 0;

                // Same keys as the unique indexes, so a game on two platforms or in two regions comes back twice
                var platformIds = (await _context.Platforms.Select(p => p.Id).ToListAsync()).ToHashSet();
                var existing = await _context.Games.Select(g => new { g.Title, g.PlatformId, g.Region, g.IgdbId }).ToListAsync();
                var titles = existing.Select(g => (g.Title, g.PlatformId, g.Region ?? string.Empty)).ToHashSet();
                var igdbIds = existing.Where(g => g.IgdbId != null).Select(g => (g.IgdbId!.Value, g.PlatformId)).ToHashSet();

                // The export writes Game's own property names
                foreach (var game in file.Games)
                {
                    if (game == null || string.IsNullOrEmpty(game.Title) || !platformIds.Contains(game.PlatformId))
                    {
                        skippedCount++;
                        continue;
                    }

                    game.Region ??= string.Empty;
                    var key = (game.Title, game.PlatformId, game.Region);
                    if (titles.Contains(key) || (game.IgdbId is int igdbId && igdbIds.Contains((igdbId, game.PlatformId))))
                    {
                        skippedCount++;
                        continue;
                    }
                    titles.Add(key);
                    if (game.IgdbId is int newIgdbId) igdbIds.Add((newIgdbId, game.PlatformId));

                    // Row ids, files and the folder belong to the install the export came from
                    game.Id = 0;
                    game.Path = null;
                    game.Platform = null;
                    game.GameFiles = new();
                    game.Genres ??= new();
                    game.Images ??= new();
                    if (game.Added == default) game.Added = DateTime.UtcNow;

                    _context.Games.Add(game);
                    importedCount++;
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"Import completed: {importedCount} games imported, {skippedCount} skipped (already in the library or unknown platform)"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Import failed: {ex.Message}" });
            }
        }

        [HttpGet("activity")]
        public async Task<ActionResult> GetRecentActivity([FromQuery] int count = 20)
        {
            var recentGames = await _context.Games
                .OrderByDescending(g => g.Added)
                .Take(count)
                .Select(g => new
                {
                    g.Id,
                    g.Title,
                    g.Added,
                    g.PlatformId,
                    CoverUrl = g.Images != null ? g.Images.CoverUrl : null,
                    Type = "game_added"
                })
                .ToListAsync();

            return Ok(recentGames);
        }
    }
}

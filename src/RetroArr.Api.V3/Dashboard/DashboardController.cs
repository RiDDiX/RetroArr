using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
            var games = await _context.Games.ToListAsync();
            
            var totalGames = games.Count;
            var installedGames = games.Count(g => !string.IsNullOrEmpty(g.InstallPath));
            var externalGames = games.Count(g => g.IsExternal);
            var favoriteGames = 0; // Reviews track favorites
            
            // Genre distribution
            var genreStats = games
                .Where(g => g.Genres != null && g.Genres.Any())
                .SelectMany(g => g.Genres!)
                .GroupBy(g => g)
                .Select(grp => new { Genre = grp.Key, Count = grp.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();

            // Platform distribution
            var platformStats = games
                .Where(g => g.PlatformId > 0)
                .GroupBy(g => g.PlatformId)
                .Select(grp => new 
                { 
                    PlatformId = grp.Key,
                    Platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == grp.Key)?.Name ?? "Unknown",
                    Count = grp.Count() 
                })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();

            // Year distribution
            var yearStats = games
                .Where(g => g.Year > 1970)
                .GroupBy(g => g.Year)
                .Select(grp => new { Year = grp.Key, Count = grp.Count() })
                .OrderByDescending(x => x.Year)
                .Take(20)
                .ToList();

            // Recently added games
            var recentlyAdded = games
                .OrderByDescending(g => g.Added)
                .Take(10)
                .Select(g => new 
                {
                    g.Id,
                    g.Title,
                    g.Added,
                    CoverUrl = g.Images?.CoverUrl
                })
                .ToList();

            // Rating distribution
            var ratingStats = new
            {
                Excellent = games.Count(g => g.Rating.HasValue && g.Rating >= 80),
                Good = games.Count(g => g.Rating.HasValue && g.Rating >= 60 && g.Rating < 80),
                Average = games.Count(g => g.Rating.HasValue && g.Rating >= 40 && g.Rating < 60),
                Poor = games.Count(g => g.Rating.HasValue && g.Rating > 0 && g.Rating < 40),
                Unrated = games.Count(g => !g.Rating.HasValue || g.Rating == 0)
            };

            // Status distribution
            var statusStats = games
                .GroupBy(g => g.Status)
                .Select(grp => new { Status = grp.Key.ToString(), Count = grp.Count() })
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
            var query = _context.Games.AsQueryable();

            if (platformId.HasValue)
            {
                query = query.Where(g => g.PlatformId == platformId.Value);
            }

            if (!string.IsNullOrEmpty(genre))
            {
                query = query.Where(g => g.Genres != null && g.Genres.Contains(genre));
            }

            var games = await query.ToListAsync();
            
            if (!games.Any())
            {
                return NotFound(new { message = "No games found matching criteria" });
            }

            var random = new Random();
            var randomGame = games[random.Next(games.Count)];

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
            var games = await _context.Games.ToListAsync();
            
            if (!games.Any())
            {
                return Ok(new List<Game>());
            }

            var random = new Random();
            var shuffled = games.OrderBy(x => random.Next()).Take(Math.Min(count, games.Count)).ToList();

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

        [HttpGet("export")]
        public async Task<ActionResult> ExportLibrary()
        {
            var games = await _context.Games.ToListAsync();
            var collections = await _context.Collections.Include(c => c.CollectionGames).ToListAsync();
            var tags = await _context.Tags.Include(t => t.GameTags).ToListAsync();
            var reviews = await _context.GameReviews.ToListAsync();

            var exportData = new
            {
                ExportDate = DateTime.UtcNow,
                Version = "1.0",
                Games = games.Select(g => new
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
                    g.Rating,
                    g.Status,
                    g.InstallPath,
                    g.IsExternal,
                    g.Added,
                    Images = g.Images
                }),
                Collections = collections.Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Description,
                    c.CoverUrl,
                    GameIds = c.CollectionGames?.Select(cg => cg.GameId).ToList()
                }),
                Tags = tags.Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Color,
                    GameIds = t.GameTags?.Select(gt => gt.GameId).ToList()
                }),
                Reviews = reviews.Select(r => new
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
                })
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"RetroArr-export-{DateTime.UtcNow:yyyyMMdd}.json");
        }

        [HttpPost("import")]
        public async Task<ActionResult> ImportLibrary([FromBody] JsonElement importData)
        {
            try
            {
                var gamesElement = importData.GetProperty("games");
                var importedCount = 0;
                var skippedCount = 0;

                foreach (var gameElement in gamesElement.EnumerateArray())
                {
                    var title = gameElement.GetProperty("title").GetString();
                    
                    // Check if game already exists
                    var existingGame = await _context.Games.FirstOrDefaultAsync(g => g.Title == title);
                    if (existingGame != null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var game = new Game
                    {
                        Title = title ?? "Unknown",
                        Overview = gameElement.TryGetProperty("overview", out var ov) ? ov.GetString() : null,
                        Year = gameElement.TryGetProperty("year", out var yr) && yr.ValueKind != JsonValueKind.Null ? yr.GetInt32() : 0,
                        Developer = gameElement.TryGetProperty("developer", out var dev) ? dev.GetString() : null,
                        Publisher = gameElement.TryGetProperty("publisher", out var pub) ? pub.GetString() : null,
                        PlatformId = gameElement.TryGetProperty("platformId", out var plat) ? plat.GetInt32() : 6,
                        Added = DateTime.UtcNow,
                        Status = GameStatus.Released
                    };

                    if (gameElement.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
                    {
                        game.Genres = genres.EnumerateArray().Select(g => g.GetString()!).Where(s => s != null).ToList();
                    }

                    _context.Games.Add(game);
                    importedCount++;
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"Import completed: {importedCount} games imported, {skippedCount} skipped (already exist)"
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

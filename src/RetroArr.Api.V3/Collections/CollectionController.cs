using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Api.V3.Collections
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class CollectionController : ControllerBase
    {
        private readonly RetroArrDbContext _context;

        public CollectionController(RetroArrDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetAll()
        {
            var collections = await _context.Collections
                .Include(c => c.CollectionGames)
                    .ThenInclude(cg => cg.Game)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            return Ok(collections.Select(c => new
            {
                c.Id,
                c.Name,
                c.Description,
                c.Icon,
                c.Color,
                c.CoverUrl,
                c.SortOrder,
                c.IsSmartCollection,
                c.SmartRules,
                c.CreatedAt,
                c.UpdatedAt,
                GameCount = c.CollectionGames.Count,
                Games = c.CollectionGames
                    .OrderBy(cg => cg.SortOrder)
                    .Select(cg => new
                    {
                        cg.Game!.Id,
                        cg.Game.Title,
                        CoverUrl = cg.Game.Images.CoverUrl,
                        cg.Game.Year,
                        Platform = cg.Game.Platform?.Name,
                        cg.AddedAt
                    })
            }));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetById(int id)
        {
            var collection = await _context.Collections
                .Include(c => c.CollectionGames)
                    .ThenInclude(cg => cg.Game)
                        .ThenInclude(g => g!.Platform)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (collection == null)
                return NotFound();

            return Ok(new
            {
                collection.Id,
                collection.Name,
                collection.Description,
                collection.Icon,
                collection.Color,
                collection.CoverUrl,
                collection.SortOrder,
                collection.IsSmartCollection,
                collection.SmartRules,
                collection.CreatedAt,
                collection.UpdatedAt,
                GameCount = collection.CollectionGames.Count,
                Games = collection.CollectionGames
                    .OrderBy(cg => cg.SortOrder)
                    .Select(cg => new
                    {
                        cg.Game!.Id,
                        cg.Game.Title,
                        CoverUrl = cg.Game.Images.CoverUrl,
                        BackgroundUrl = cg.Game.Images.BackgroundUrl,
                        cg.Game.Year,
                        cg.Game.Rating,
                        Platform = cg.Game.Platform?.Name,
                        cg.Game.Genres,
                        cg.AddedAt,
                        cg.SortOrder
                    })
            });
        }

        [HttpPost]
        public async Task<ActionResult<Collection>> Create([FromBody] CreateCollectionRequest request)
        {
            var collection = new Collection
            {
                Name = request.Name,
                Description = request.Description,
                Icon = request.Icon,
                Color = request.Color,
                IsSmartCollection = request.IsSmartCollection,
                SmartRules = request.SmartRules,
                SortOrder = await _context.Collections.CountAsync()
            };

            _context.Collections.Add(collection);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = collection.Id }, collection);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<Collection>> Update(int id, [FromBody] UpdateCollectionRequest request)
        {
            var collection = await _context.Collections.FindAsync(id);
            if (collection == null)
                return NotFound();

            if (request.Name != null) collection.Name = request.Name;
            if (request.Description != null) collection.Description = request.Description;
            if (request.Icon != null) collection.Icon = request.Icon;
            if (request.Color != null) collection.Color = request.Color;
            if (request.CoverUrl != null) collection.CoverUrl = request.CoverUrl;
            if (request.SortOrder.HasValue) collection.SortOrder = request.SortOrder.Value;
            if (request.SmartRules != null) collection.SmartRules = request.SmartRules;
            
            collection.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(collection);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id)
        {
            var collection = await _context.Collections.FindAsync(id);
            if (collection == null)
                return NotFound();

            _context.Collections.Remove(collection);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("{id}/games")]
        public async Task<ActionResult> AddGame(int id, [FromBody] AddGameToCollectionRequest request)
        {
            var collection = await _context.Collections.FindAsync(id);
            if (collection == null)
                return NotFound("Collection not found");

            var game = await _context.Games.FindAsync(request.GameId);
            if (game == null)
                return NotFound("Game not found");

            var existing = await _context.CollectionGames
                .FirstOrDefaultAsync(cg => cg.CollectionId == id && cg.GameId == request.GameId);

            if (existing != null)
                return BadRequest("Game already in collection");

            var maxOrder = await _context.CollectionGames
                .Where(cg => cg.CollectionId == id)
                .MaxAsync(cg => (int?)cg.SortOrder) ?? -1;

            var collectionGame = new CollectionGame
            {
                CollectionId = id,
                GameId = request.GameId,
                SortOrder = maxOrder + 1
            };

            _context.CollectionGames.Add(collectionGame);
            
            // Update collection cover if not set
            if (string.IsNullOrEmpty(collection.CoverUrl) && game.Images?.CoverUrl != null)
            {
                collection.CoverUrl = game.Images.CoverUrl;
            }
            
            collection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Game added to collection" });
        }

        [HttpDelete("{id}/games/{gameId}")]
        public async Task<ActionResult> RemoveGame(int id, int gameId)
        {
            var collectionGame = await _context.CollectionGames
                .FirstOrDefaultAsync(cg => cg.CollectionId == id && cg.GameId == gameId);

            if (collectionGame == null)
                return NotFound();

            _context.CollectionGames.Remove(collectionGame);

            var collection = await _context.Collections.FindAsync(id);
            if (collection != null)
                collection.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPut("{id}/games/reorder")]
        public async Task<ActionResult> ReorderGames(int id, [FromBody] ReorderGamesRequest request)
        {
            var collectionGames = await _context.CollectionGames
                .Where(cg => cg.CollectionId == id)
                .ToListAsync();

            foreach (var item in request.GameOrders)
            {
                var cg = collectionGames.FirstOrDefault(c => c.GameId == item.GameId);
                if (cg != null)
                    cg.SortOrder = item.SortOrder;
            }

            await _context.SaveChangesAsync();
            return Ok();
        }
    }

    public class CreateCollectionRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Icon { get; set; }
        public string? Color { get; set; }
        public bool IsSmartCollection { get; set; }
        public string? SmartRules { get; set; }
    }

    public class UpdateCollectionRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Icon { get; set; }
        public string? Color { get; set; }
        public string? CoverUrl { get; set; }
        public int? SortOrder { get; set; }
        public string? SmartRules { get; set; }
    }

    public class AddGameToCollectionRequest
    {
        public int GameId { get; set; }
    }

    public class ReorderGamesRequest
    {
        public List<GameOrderItem> GameOrders { get; set; } = new();
    }

    public class GameOrderItem
    {
        public int GameId { get; set; }
        public int SortOrder { get; set; }
    }
}

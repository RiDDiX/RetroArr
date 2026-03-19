using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Api.V3.Tags
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class TagController : ControllerBase
    {
        private readonly RetroArrDbContext _context;

        public TagController(RetroArrDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetAll()
        {
            var tags = await _context.Tags
                .Include(t => t.GameTags)
                .OrderBy(t => t.Name)
                .ToListAsync();

            return Ok(tags.Select(t => new
            {
                t.Id,
                t.Name,
                t.Color,
                t.Icon,
                t.CreatedAt,
                GameCount = t.GameTags.Count
            }));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetById(int id)
        {
            var tag = await _context.Tags
                .Include(t => t.GameTags)
                    .ThenInclude(gt => gt.Game)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tag == null)
                return NotFound();

            return Ok(new
            {
                tag.Id,
                tag.Name,
                tag.Color,
                tag.Icon,
                tag.CreatedAt,
                GameCount = tag.GameTags.Count,
                Games = tag.GameTags.Select(gt => new
                {
                    gt.Game!.Id,
                    gt.Game.Title,
                    CoverUrl = gt.Game.Images.CoverUrl,
                    gt.Game.Year,
                    gt.AddedAt
                })
            });
        }

        [HttpPost]
        public async Task<ActionResult<Tag>> Create([FromBody] CreateTagRequest request)
        {
            // Check for duplicate name
            var existing = await _context.Tags.FirstOrDefaultAsync(t => t.Name == request.Name);
            if (existing != null)
                return BadRequest("Tag with this name already exists");

            var tag = new Tag
            {
                Name = request.Name,
                Color = request.Color ?? "#6c7086",
                Icon = request.Icon
            };

            _context.Tags.Add(tag);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = tag.Id }, tag);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<Tag>> Update(int id, [FromBody] UpdateTagRequest request)
        {
            var tag = await _context.Tags.FindAsync(id);
            if (tag == null)
                return NotFound();

            if (request.Name != null)
            {
                // Check for duplicate name
                var existing = await _context.Tags.FirstOrDefaultAsync(t => t.Name == request.Name && t.Id != id);
                if (existing != null)
                    return BadRequest("Tag with this name already exists");
                tag.Name = request.Name;
            }
            
            if (request.Color != null) tag.Color = request.Color;
            if (request.Icon != null) tag.Icon = request.Icon;

            await _context.SaveChangesAsync();
            return Ok(tag);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id)
        {
            var tag = await _context.Tags.FindAsync(id);
            if (tag == null)
                return NotFound();

            _context.Tags.Remove(tag);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("game/{gameId}")]
        public async Task<ActionResult<IEnumerable<object>>> GetTagsForGame(int gameId)
        {
            var tags = await _context.GameTags
                .Where(gt => gt.GameId == gameId)
                .Include(gt => gt.Tag)
                .Select(gt => new
                {
                    gt.Tag!.Id,
                    gt.Tag.Name,
                    gt.Tag.Color,
                    gt.Tag.Icon,
                    gt.AddedAt
                })
                .ToListAsync();

            return Ok(tags);
        }

        [HttpPost("game/{gameId}")]
        public async Task<ActionResult> AddTagToGame(int gameId, [FromBody] AddTagRequest request)
        {
            var game = await _context.Games.FindAsync(gameId);
            if (game == null)
                return NotFound("Game not found");

            Tag? tag;
            
            if (request.TagId.HasValue)
            {
                tag = await _context.Tags.FindAsync(request.TagId.Value);
                if (tag == null)
                    return NotFound("Tag not found");
            }
            else if (!string.IsNullOrEmpty(request.TagName))
            {
                // Create new tag or find existing
                tag = await _context.Tags.FirstOrDefaultAsync(t => t.Name == request.TagName);
                if (tag == null)
                {
                    tag = new Tag
                    {
                        Name = request.TagName,
                        Color = request.Color ?? "#6c7086"
                    };
                    _context.Tags.Add(tag);
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                return BadRequest("Either TagId or TagName must be provided");
            }

            var existing = await _context.GameTags
                .FirstOrDefaultAsync(gt => gt.GameId == gameId && gt.TagId == tag.Id);

            if (existing != null)
                return BadRequest("Game already has this tag");

            var gameTag = new GameTag
            {
                GameId = gameId,
                TagId = tag.Id
            };

            _context.GameTags.Add(gameTag);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tag added to game", tag = new { tag.Id, tag.Name, tag.Color } });
        }

        [HttpDelete("game/{gameId}/{tagId}")]
        public async Task<ActionResult> RemoveTagFromGame(int gameId, int tagId)
        {
            var gameTag = await _context.GameTags
                .FirstOrDefaultAsync(gt => gt.GameId == gameId && gt.TagId == tagId);

            if (gameTag == null)
                return NotFound();

            _context.GameTags.Remove(gameTag);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    public class CreateTagRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
        public string? Icon { get; set; }
    }

    public class UpdateTagRequest
    {
        public string? Name { get; set; }
        public string? Color { get; set; }
        public string? Icon { get; set; }
    }

    public class AddTagRequest
    {
        public int? TagId { get; set; }
        public string? TagName { get; set; }
        public string? Color { get; set; }
    }
}

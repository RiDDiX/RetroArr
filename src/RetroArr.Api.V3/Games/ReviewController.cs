using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;

namespace RetroArr.Api.V3.Games
{
    [ApiController]
    [Route("api/v3/review")]
    public class ReviewController : ControllerBase
    {
        private readonly ReviewItemService _reviewService;
        private readonly IGameRepository _gameRepository;

        public ReviewController(ReviewItemService reviewService, IGameRepository gameRepository)
        {
            _reviewService = reviewService;
            _gameRepository = gameRepository;
        }

        [HttpGet("items")]
        public IActionResult GetAll([FromQuery] bool pendingOnly = false)
        {
            var items = pendingOnly ? _reviewService.GetPending() : _reviewService.GetAll();
            return Ok(new
            {
                count = items.Count,
                items = items.Select(i => new
                {
                    i.Id,
                    i.FilePaths,
                    i.DetectedPlatformKey,
                    i.DetectedPlatformId,
                    i.DetectedTitle,
                    i.DiskName,
                    i.Region,
                    i.Serial,
                    reason = i.Reason.ToString(),
                    i.ReasonDetail,
                    status = i.Status.ToString(),
                    i.CreatedAt,
                    i.AssignedPlatformId,
                    i.AssignedGameId,
                    i.OverrideTitle,
                    i.OverrideDiskName
                })
            });
        }

        [HttpGet("items/{id}")]
        public IActionResult GetById(string id)
        {
            var item = _reviewService.GetById(id);
            if (item == null) return NotFound(new { message = "Review item not found." });
            return Ok(item);
        }

        [HttpPost("items/{id}/map")]
        public IActionResult MapItem(string id, [FromBody] ReviewMapRequest request)
        {
            var success = _reviewService.UpdateMapping(
                id, request.PlatformId, request.GameId, request.OverrideTitle, request.OverrideDiskName);
            if (!success) return NotFound(new { message = "Review item not found." });
            return Ok(new { message = "Mapping updated.", id });
        }

        [HttpPost("items/{id}/finalize")]
        public async Task<IActionResult> FinalizeItem(string id)
        {
            var item = _reviewService.GetById(id);
            if (item == null) return NotFound(new { message = "Review item not found." });

            var platformId = item.AssignedPlatformId ?? item.DetectedPlatformId;
            if (!platformId.HasValue || platformId == 0)
                return BadRequest(new { message = "Platform must be assigned before finalizing." });

            if (!PlatformDefinitions.PlatformDictionary.ContainsKey(platformId.Value))
                return BadRequest(new { message = $"PlatformId {platformId} is not a known platform." });

            var title = item.OverrideTitle ?? item.DetectedTitle ?? "Unknown";

            // Link to existing game or create new
            Game? game = null;
            if (item.AssignedGameId.HasValue)
            {
                game = await _gameRepository.GetByIdAsync(item.AssignedGameId.Value);
                if (game == null)
                    return BadRequest(new { message = $"Assigned game {item.AssignedGameId} not found." });
            }

            if (game == null)
            {
                game = new Game
                {
                    Title = title,
                    PlatformId = platformId.Value,
                    Path = item.FilePaths.FirstOrDefault(),
                    ExecutablePath = item.FilePaths.FirstOrDefault(),
                    Region = item.Region,
                    Status = GameStatus.Downloaded,
                    Added = DateTime.UtcNow
                };
                await _gameRepository.AddAsync(game);
            }
            else
            {
                if (string.IsNullOrEmpty(game.Path))
                    game.Path = item.FilePaths.FirstOrDefault();
                if (string.IsNullOrEmpty(game.Region) && !string.IsNullOrEmpty(item.Region))
                    game.Region = item.Region;
                await _gameRepository.UpdateAsync(game.Id, game);
            }

            _reviewService.SetStatus(id, ReviewStatus.Finalized);
            return Ok(new { message = "Item finalized and linked to library.", gameId = game.Id, title = game.Title });
        }

        [HttpPost("items/{id}/ignore")]
        public IActionResult IgnoreItem(string id)
        {
            var success = _reviewService.SetStatus(id, ReviewStatus.Ignored);
            if (!success) return NotFound(new { message = "Review item not found." });
            return Ok(new { message = "Item ignored.", id });
        }

        [HttpPost("items/{id}/dismiss")]
        public IActionResult DismissItem(string id)
        {
            var success = _reviewService.Remove(id);
            if (!success) return NotFound(new { message = "Review item not found." });
            return Ok(new { message = "Item dismissed.", id });
        }
    }

    public class ReviewMapRequest
    {
        public int? PlatformId { get; set; }
        public int? GameId { get; set; }
        public string? OverrideTitle { get; set; }
        public string? OverrideDiskName { get; set; }
    }
}

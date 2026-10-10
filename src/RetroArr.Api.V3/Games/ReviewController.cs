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
        private readonly DuplicateGameMergeService _mergeService;

        public ReviewController(ReviewItemService reviewService, IGameRepository gameRepository, DuplicateGameMergeService mergeService)
        {
            _reviewService = reviewService;
            _gameRepository = gameRepository;
            _mergeService = mergeService;
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
            else if (await GameController.EntryOfAsync(_gameRepository, platformId.Value, title, item.Region) is { } entry)
            {
                game = await _gameRepository.GetByIdAsync(entry.Id);
            }

            var file = item.FilePaths.FirstOrDefault();
            if (game == null)
            {
                game = new Game
                {
                    Title = title,
                    PlatformId = platformId.Value,
                    Path = file,
                    ExecutablePath = System.IO.File.Exists(file) ? file : null,
                    Region = item.Region,
                    Status = GameStatus.Released,
                    Added = DateTime.UtcNow
                };
                await _gameRepository.AddAsync(game);
            }
            else
            {
                // A path whose content may still be there stays (the file may lie in it, its share may be offline); a
                // game without a path or whose content is lost is the file now
                var library = await _gameRepository.GetAllLightAsync();
                var saved = await _gameRepository.UpdateFieldsAsync(game.Id, g =>
                {
                    if ((System.IO.File.Exists(file) || System.IO.Directory.Exists(file)) && (string.IsNullOrEmpty(g.Path) || _mergeService.Lost(g, library)))
                        g.Path = file;
                    if (System.IO.File.Exists(file) && GameController.IsSubPath(file, g.Path ?? string.Empty))
                        g.ExecutablePath = file;
                    if (string.IsNullOrEmpty(g.Region) && !string.IsNullOrEmpty(item.Region))
                        g.Region = item.Region;
                });
                if (saved == null)
                    return BadRequest(new { message = $"Game {game.Id} not found." });
                game = saved;
            }

            // The game is in the library now, so the monitor stops searching for it
            if (_mergeService.CheckContent(game) == GameContent.Present)
                await _gameRepository.ApplyContentStateAsync(game.Id, GameContent.Present, DateTime.UtcNow, game.Path);

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

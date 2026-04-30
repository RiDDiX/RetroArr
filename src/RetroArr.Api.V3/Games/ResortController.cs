using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;
using RetroArr.Core.Configuration;

namespace RetroArr.Api.V3.Games
{
    [ApiController]
    [Route("api/v3/resort")]
    public class ResortController : ControllerBase
    {
        private readonly LibraryResortService _resortService;
        private readonly IGameRepository _gameRepository;
        private CancellationTokenSource? _applyCts;

        public ResortController(LibraryResortService resortService, IGameRepository gameRepository)
        {
            _resortService = resortService;
            _gameRepository = gameRepository;
        }

        [HttpPost("scan")]
        public async Task<IActionResult> Scan([FromBody] ResortScanRequest? request)
        {
            var issues = await _resortService.ScanAsync(request);
            return Ok(new
            {
                count = issues.Count,
                issues = issues.Select(i => new
                {
                    i.Id,
                    i.GameId,
                    i.GameTitle,
                    i.PlatformId,
                    i.PlatformName,
                    issueType = i.IssueType.ToString(),
                    i.RuleFailed,
                    i.Description,
                    i.CurrentPath,
                    i.ExpectedPath,
                    i.CurrentFolder,
                    proposedAction = i.ProposedAction.ToString()
                })
            });
        }

        [HttpGet("issues")]
        public IActionResult GetIssues()
        {
            var issues = _resortService.LastScanResults;
            return Ok(new
            {
                count = issues.Count,
                issues = issues.Select(i => new
                {
                    i.Id,
                    i.GameId,
                    i.GameTitle,
                    i.PlatformId,
                    i.PlatformName,
                    issueType = i.IssueType.ToString(),
                    i.RuleFailed,
                    i.Description,
                    i.CurrentPath,
                    i.ExpectedPath,
                    i.CurrentFolder,
                    proposedAction = i.ProposedAction.ToString()
                })
            });
        }

        [HttpPost("reassign-platform")]
        public async Task<IActionResult> ReassignPlatform([FromBody] ReassignPlatformRequest request)
        {
            if (request == null || request.GameId <= 0 || request.NewPlatformId <= 0)
                return BadRequest(new { message = "GameId and NewPlatformId are required." });

            var game = await _gameRepository.GetByIdAsync(request.GameId);
            if (game == null)
                return NotFound(new { message = $"Game {request.GameId} not found." });

            var newPlatform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == request.NewPlatformId);
            if (newPlatform == null)
                return BadRequest(new { message = $"Platform {request.NewPlatformId} not found." });

            var oldPlatformId = game.PlatformId;
            game.PlatformId = request.NewPlatformId;
            await _gameRepository.UpdateAsync(game.Id, game);

            return Ok(new
            {
                message = $"Platform reassigned from {oldPlatformId} to {request.NewPlatformId} ({newPlatform.Name}).",
                gameId = game.Id,
                oldPlatformId,
                newPlatformId = request.NewPlatformId,
                newPlatformName = newPlatform.Name
            });
        }

        [HttpPost("preview")]
        public IActionResult Preview([FromBody] ResortApplyRequest request)
        {
            if (request?.IssueIds == null || request.IssueIds.Count == 0)
                return BadRequest(new { message = "No issues selected." });

            var plan = _resortService.GeneratePreview(request.IssueIds, request.DefaultConflictResolution);
            return Ok(FormatPlan(plan));
        }

        [HttpPost("apply")]
        public async Task<IActionResult> Apply([FromBody] ResortApplyRequest request)
        {
            if (request?.IssueIds == null || request.IssueIds.Count == 0)
                return BadRequest(new { message = "No issues selected." });

            _applyCts = new CancellationTokenSource();
            var plan = await _resortService.ApplyAsync(
                request.IssueIds,
                request.DefaultConflictResolution,
                _applyCts.Token);

            return Ok(FormatPlan(plan));
        }

        [HttpPost("cancel")]
        public IActionResult Cancel()
        {
            _applyCts?.Cancel();
            return Ok(new { message = "Cancellation requested." });
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var progress = _resortService.Progress;
            var plan = _resortService.ActivePlan;
            return Ok(new
            {
                progress.IsRunning,
                progress.Total,
                progress.Completed,
                progress.Failed,
                progress.CurrentOperation,
                plan = plan != null ? FormatPlan(plan) : null
            });
        }

        [HttpGet("history")]
        public IActionResult GetHistory()
        {
            var plans = _resortService.GetHistory();
            return Ok(plans.Select(FormatPlan));
        }

        [HttpPost("resume")]
        public async Task<IActionResult> Resume()
        {
            var plan = _resortService.ActivePlan;
            if (plan == null || plan.IsComplete)
                return Ok(new { message = "No pending plan to resume." });

            var pendingIds = plan.Operations
                .Where(o => o.Status == OperationStatus.Pending)
                .Select(o => o.IssueId)
                .Distinct()
                .ToList();

            if (pendingIds.Count == 0)
                return Ok(new { message = "All operations already completed." });

            _applyCts = new CancellationTokenSource();
            var result = await _resortService.ApplyAsync(
                pendingIds,
                ConflictResolution.Skip,
                _applyCts.Token);

            return Ok(FormatPlan(result));
        }

        [HttpPost("fix-platforms")]
        public async Task<IActionResult> FixPlatformAssignments()
        {
            var fixes = await _resortService.FixPlatformAssignmentsAsync();
            return Ok(new
            {
                message = $"Fixed {fixes.Count} game(s) with wrong platform assignment.",
                count = fixes.Count,
                fixes = fixes.Select(f => new
                {
                    gameId = f.GameId,
                    title = f.Title,
                    oldPlatformId = f.OldPlatformId,
                    newPlatformId = f.NewPlatformId,
                    newPlatformName = f.NewPlatformName
                })
            });
        }

        [HttpGet("game/{gameId}/rename/preview")]
        public async Task<IActionResult> RenamePreview(int gameId)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null)
                return NotFound(new { message = $"Game {gameId} not found." });

            var issues = await _resortService.ScanAsync(new ResortScanRequest { GameId = gameId });
            // Exclude folder-level operations - renaming the game folder would break game.Path
            issues = issues.Where(i =>
                i.ProposedAction != OperationType.RenameGameFolder &&
                i.ProposedAction != OperationType.MoveGameFolder).ToList();
            if (issues.Count == 0)
                return Ok(new { message = "No rename needed - files are already correctly named.", operations = Array.Empty<object>() });

            var allIds = issues.Select(i => i.Id).ToList();
            var plan = _resortService.GeneratePreview(allIds, ConflictResolution.Skip);
            return Ok(FormatPlan(plan));
        }

        [HttpPost("game/{gameId}/rename/apply")]
        public async Task<IActionResult> RenameApply(int gameId, [FromBody] ResortApplyRequest? request)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null)
                return NotFound(new { message = $"Game {gameId} not found." });

            var issues = await _resortService.ScanAsync(new ResortScanRequest { GameId = gameId });
            // Exclude folder-level operations - renaming the game folder would break game.Path
            issues = issues.Where(i =>
                i.ProposedAction != OperationType.RenameGameFolder &&
                i.ProposedAction != OperationType.MoveGameFolder).ToList();
            if (issues.Count == 0)
                return Ok(new { message = "No rename needed.", operations = Array.Empty<object>() });

            var issueIds = request?.IssueIds ?? issues.Select(i => i.Id).ToList();
            var resolution = request?.DefaultConflictResolution ?? ConflictResolution.Skip;

            _applyCts = new CancellationTokenSource();
            var plan = await _resortService.ApplyAsync(issueIds, resolution, _applyCts.Token);
            return Ok(FormatPlan(plan));
        }

        [HttpGet("game/{gameId}/supplementary/rename/preview")]
        public async Task<IActionResult> SupplementaryRenamePreview(int gameId)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null)
                return NotFound(new { message = $"Game {gameId} not found." });

            var ops = await _resortService.PreviewSupplementaryRenameAsync(gameId);
            return Ok(new
            {
                gameId,
                gameTitle = game.Title,
                count = ops.Count,
                operations = ops.Select(o => new
                {
                    o.GameFileId,
                    o.FileType,
                    o.Version,
                    o.ContentName,
                    o.CurrentFileName,
                    o.NewFileName,
                    o.CurrentPath,
                    o.NewPath,
                    o.Conflict,
                    o.Status
                })
            });
        }

        [HttpPost("game/{gameId}/supplementary/rename/apply")]
        public async Task<IActionResult> SupplementaryRenameApply(int gameId)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null)
                return NotFound(new { message = $"Game {gameId} not found." });

            var result = await _resortService.ApplySupplementaryRenameAsync(gameId);
            return Ok(new
            {
                gameId,
                gameTitle = game.Title,
                result.Applied,
                result.Failed,
                result.Skipped,
                operations = result.Operations.Select(o => new
                {
                    o.GameFileId,
                    o.FileType,
                    o.Version,
                    o.ContentName,
                    o.CurrentFileName,
                    o.NewFileName,
                    o.Status,
                    o.Error,
                    o.Conflict
                })
            });
        }

        [HttpGet("platforms")]
        public IActionResult GetPlatforms()
        {
            var platforms = PlatformDefinitions.AllPlatforms
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.FolderName, p.Slug });
            return Ok(platforms);
        }

        private static object FormatPlan(OperationPlan plan)
        {
            return new
            {
                plan.Id,
                plan.CreatedAt,
                plan.TotalCount,
                plan.AppliedCount,
                plan.FailedCount,
                plan.SkippedCount,
                plan.PendingCount,
                plan.IsComplete,
                operations = plan.Operations.Select(o => new
                {
                    o.Id,
                    o.IssueId,
                    type = o.Type.ToString(),
                    o.SourcePath,
                    o.TargetPath,
                    o.GameId,
                    o.IssueType,
                    conflict = o.Conflict?.ToString(),
                    status = o.Status.ToString(),
                    o.ErrorMessage,
                    o.CompletedAt
                })
            };
        }
    }
}

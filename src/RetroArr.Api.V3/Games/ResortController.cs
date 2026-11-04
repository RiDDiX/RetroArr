using System;
using System.Linq;
using System.Collections.Generic;
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
                scanned = _resortService.Scanned,
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
            _resortService.Forget(game.Id);

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

            try
            {
                var plan = await _resortService.ApplyAsync(request.IssueIds, request.DefaultConflictResolution);

                return Ok(FormatPlan(plan));
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpPost("cancel")]
        public IActionResult Cancel()
        {
            _resortService.Cancel();
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
                scanning = _resortService.Scanning,
                // Counts only: the page polls it while a run goes, and a run has up to an operation per game
                plan = plan != null ? new { plan.Id, plan.CreatedAt, plan.TotalCount, plan.AppliedCount, plan.FailedCount, plan.SkippedCount, plan.PendingCount, plan.IsComplete } : null
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
            try
            {
                var result = await _resortService.ResumeAsync();
                if (result == null)
                    return Ok(new { message = "No pending plan to resume." });

                return Ok(FormatPlan(result));
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
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
            // Allow renaming the game folder to fix a mangled/illegal name — the engine
            // rewrites game.Path afterwards (UpdateGamePath). Only keep platform
            // relocations (MoveGameFolder) out of a per-game "rename".
            issues = issues.Where(i =>
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

            List<string> issueIds;
            if (request?.IssueIds is { Count: > 0 } previewed)
            {
                // The ids of a preview: a scan now would give its issues new ones. Only this game's renames go.
                var renames = _resortService.LastScanResults
                    .Where(i => i.GameId == gameId && i.ProposedAction != OperationType.MoveGameFolder)
                    .Select(i => i.Id).ToHashSet();
                issueIds = previewed.Where(renames.Contains).ToList();
                if (issueIds.Count == 0)
                    return Conflict(new { message = "The preview is out of date. Preview again." });
            }
            else
            {
                var issues = await _resortService.ScanAsync(new ResortScanRequest { GameId = gameId });
                // Allow renaming the game folder to fix a mangled/illegal name — the engine
                // rewrites game.Path afterwards (UpdateGamePath). Only keep platform
                // relocations (MoveGameFolder) out of a per-game "rename".
                issues = issues.Where(i =>
                    i.ProposedAction != OperationType.MoveGameFolder).ToList();
                if (issues.Count == 0)
                    return Ok(new { message = "No rename needed.", operations = Array.Empty<object>() });
                issueIds = issues.Select(i => i.Id).ToList();
            }
            var resolution = request?.DefaultConflictResolution ?? ConflictResolution.Skip;

            try
            {
                var plan = await _resortService.ApplyAsync(issueIds, resolution);
                return Ok(FormatPlan(plan));
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
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

            SupplementaryRenameResult result;
            try
            {
                result = await _resortService.ApplySupplementaryRenameAsync(gameId);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
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

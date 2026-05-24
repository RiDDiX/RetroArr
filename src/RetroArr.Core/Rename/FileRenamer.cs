using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;

namespace RetroArr.Core.Rename
{
    public sealed class RenameDecision
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string FinalPath { get; set; } = string.Empty;
        public bool Renamed { get; set; }
        public bool SkippedDueToPlatform { get; set; }
        public bool SkippedAlreadyMatches { get; set; }
        public bool SkippedConflict { get; set; }
        public string? ReleaseGroup { get; set; }
        public string? Reason { get; set; }
    }

    // Applies the configured rename templates to a single file. Idempotent,
    // platform-gated, conflict-aware. Designed to be called from
    // PostDownloadProcessor after the file lands in its destination folder
    // but before DB persistence so the saved RelativePath matches reality.
    //
    // The per-game mutex lives in this service rather than the caller so
    // callers don't have to remember to take it.
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public sealed class FileRenamer
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly TemplateRenderer _renderer;
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _gameLocks = new();

        public FileRenamer(TemplateRenderer renderer)
        {
            _renderer = renderer;
        }

        public async Task<RenameDecision> RenameAsync(
            string filePath,
            Game game,
            TitleCleanerService.SupplementaryContentInfo? classification,
            string? extractedReleaseGroup,
            MediaSettings settings,
            CancellationToken ct = default)
        {
            var decision = new RenameDecision { OriginalPath = filePath, FinalPath = filePath, ReleaseGroup = extractedReleaseGroup };

            if (!settings.RenameOnImport)
            {
                decision.Reason = "rename disabled in settings";
                return decision;
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                decision.Reason = "source file missing";
                return decision;
            }

            // Platform gate: only touch platforms the user has opted in.
            // Defaults to desktop slugs which don't carry load-bearing
            // markers in filenames (unlike Switch/PS4 etc.).
            var slug = game.Platform?.Slug ?? string.Empty;
            var allowedSlugs = settings.GetRenameTargetPlatformSlugs();
            if (allowedSlugs.Count == 0 || !allowedSlugs.Contains(slug))
            {
                decision.SkippedDueToPlatform = true;
                decision.Reason = $"platform '{slug}' not in rename scope";
                return decision;
            }

            var lockObj = _gameLocks.GetOrAdd(game.Id, _ => new SemaphoreSlim(1, 1));
            await lockObj.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await RenameLocked(filePath, game, classification, extractedReleaseGroup, settings, decision, ct).ConfigureAwait(false);
            }
            finally
            {
                lockObj.Release();
            }
        }

        private async Task<RenameDecision> RenameLocked(
            string filePath, Game game,
            TitleCleanerService.SupplementaryContentInfo? classification,
            string? extractedReleaseGroup,
            MediaSettings settings,
            RenameDecision decision,
            CancellationToken ct)
        {
            var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            var extension = Path.GetExtension(filePath);

            var fileType = classification?.FileType ?? "Main";
            var template = fileType switch
            {
                "Patch" => settings.UpdateFileTemplate,
                "DLC"   => settings.DlcFileTemplate,
                _       => settings.MainFileTemplate
            };
            if (string.IsNullOrWhiteSpace(template)) template = "{Title}";

            var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Title"]        = game.Title,
                ["Year"]         = game.Year > 0 ? game.Year.ToString() : null,
                ["Platform"]     = game.Platform?.Name,
                ["Version"]      = classification?.Version,
                ["ContentName"]  = classification?.ContentName,
                ["ReleaseGroup"] = extractedReleaseGroup,
                ["Region"]       = game.Region,
                ["Languages"]    = game.Languages,
                ["Revision"]     = game.Revision,
                ["Edition"]      = null
            };

            var stem = _renderer.RenderStem(template, variables);

            // Optional group suffix.
            if (settings.IncludeReleaseGroupInFilename && !string.IsNullOrWhiteSpace(extractedReleaseGroup))
            {
                var suffixVars = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ReleaseGroup"] = extractedReleaseGroup
                };
                var suffix = _renderer.RenderStem(settings.ReleaseGroupSuffix, suffixVars);
                if (!string.IsNullOrWhiteSpace(suffix))
                {
                    stem = stem + " " + suffix;
                }
            }

            var targetFile = stem + extension;
            var targetPath = Path.Combine(directory, targetFile);

            // Idempotency: already at the target path.
            if (string.Equals(Path.GetFileName(filePath), targetFile, StringComparison.Ordinal))
            {
                decision.SkippedAlreadyMatches = true;
                decision.Reason = "filename already matches template";
                return decision;
            }

            // Conflict resolution.
            if (File.Exists(targetPath) && !string.Equals(targetPath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                switch ((settings.FileConflictBehavior ?? "Skip").ToLowerInvariant())
                {
                    case "overwrite":
                        try { File.Delete(targetPath); }
                        catch (Exception ex)
                        {
                            decision.SkippedConflict = true;
                            decision.Reason = $"could not delete target for overwrite: {ex.Message}";
                            _logger.Warn($"[Rename] overwrite-delete failed: {ex.Message}");
                            return decision;
                        }
                        break;
                    case "suffix":
                        targetPath = MakeUniquePath(directory, stem, extension);
                        targetFile = Path.GetFileName(targetPath);
                        break;
                    default: // skip
                        decision.SkippedConflict = true;
                        decision.Reason = "target exists, conflict mode = Skip";
                        return decision;
                }
            }

            try
            {
                // File.Move on .NET preserves attributes and is atomic on the
                // same volume. Cross-volume Move falls back to copy+delete -
                // acceptable since both source and target live under the
                // library root and we don't span volumes here.
                File.Move(filePath, targetPath);
                decision.FinalPath = targetPath;
                decision.Renamed = true;
                decision.Reason = "renamed";
                _logger.Info($"[Rename] '{Path.GetFileName(filePath)}' -> '{targetFile}' (game={game.Id})");
                await Task.CompletedTask.ConfigureAwait(false);
                return decision;
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Rename] move failed: {ex.Message}");
                decision.Reason = $"move failed: {ex.Message}";
                return decision;
            }
        }

        private static string MakeUniquePath(string directory, string stem, string extension)
        {
            // " (1)", " (2)", ... so we don't collide with the existing file.
            for (var i = 1; i < 1000; i++)
            {
                var candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
                if (!File.Exists(candidate)) return candidate;
            }
            // Fall-through: timestamp suffix to guarantee uniqueness.
            var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            return Path.Combine(directory, $"{stem} ({ts}){extension}");
        }
    }
}

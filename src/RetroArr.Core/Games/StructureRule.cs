using System;
using System.Collections.Generic;

namespace RetroArr.Core.Games
{
    public enum IssueType
    {
        WrongPlatformFolder,
        WrongGameFolderName,
        InvalidFileExtension,
        MisplacedPatchOrDlc,
        DbPathMismatch,
        OrphanedFile,
        MissingGameFolder,
        CompatibilityModeMismatch,
        MissingGameSubfolder,
        ContainerRuleViolation
    }

    public enum OperationType
    {
        MoveGameFolder,
        RenameGameFolder,
        MoveFile,
        UpdateDbPath,
        LinkOrphan,
        DeleteEmptyDir,
        MoveFileSet
    }

    public enum OperationStatus
    {
        Pending,
        Applied,
        Skipped,
        Failed
    }

    public enum ConflictResolution
    {
        Skip,
        RenameSuffix,
        Overwrite
    }

    public class StructureIssue
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int? GameId { get; set; }
        public string GameTitle { get; set; } = string.Empty;
        public int PlatformId { get; set; }
        public string PlatformName { get; set; } = string.Empty;
        public IssueType IssueType { get; set; }
        public string RuleFailed { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CurrentPath { get; set; } = string.Empty;
        public string ExpectedPath { get; set; } = string.Empty;
        public string CurrentFolder { get; set; } = string.Empty;
        public OperationType ProposedAction { get; set; }
        public bool Selected { get; set; }
        public List<string> CompanionFiles { get; set; } = new();
    }

    public class StructureOperation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string IssueId { get; set; } = string.Empty;
        public OperationType Type { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public int? GameId { get; set; }
        public string IssueType { get; set; } = string.Empty;
        public ConflictResolution? Conflict { get; set; }
        public OperationStatus Status { get; set; } = OperationStatus.Pending;
        public string? ErrorMessage { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<string> CompanionFiles { get; set; } = new();
    }

    public class OperationPlan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<StructureOperation> Operations { get; set; } = new();
        public int TotalCount => Operations.Count;
        public int AppliedCount => Operations.FindAll(o => o.Status == OperationStatus.Applied).Count;
        public int FailedCount => Operations.FindAll(o => o.Status == OperationStatus.Failed).Count;
        public int SkippedCount => Operations.FindAll(o => o.Status == OperationStatus.Skipped).Count;
        public int PendingCount => Operations.FindAll(o => o.Status == OperationStatus.Pending).Count;
        public bool IsComplete => PendingCount == 0;
    }

    public class ResortScanRequest
    {
        public int? PlatformId { get; set; }
        public int? GameId { get; set; }
    }

    public class ResortApplyRequest
    {
        public List<string>? IssueIds { get; set; }
        public ConflictResolution DefaultConflictResolution { get; set; } = ConflictResolution.Skip;
    }

    public class ReassignPlatformRequest
    {
        public int GameId { get; set; }
        public int NewPlatformId { get; set; }
    }

    public class ResortProgress
    {
        public bool IsRunning { get; set; }
        public int Total { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public string? CurrentOperation { get; set; }
    }

    public class SupplementaryRenameOp
    {
        public int GameFileId { get; set; }
        public string FileType { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string? ContentName { get; set; }
        public string CurrentPath { get; set; } = string.Empty;
        public string CurrentFileName { get; set; } = string.Empty;
        public string NewFileName { get; set; } = string.Empty;
        public string NewPath { get; set; } = string.Empty;
        public string NewRelativePath { get; set; } = string.Empty;
        public bool Conflict { get; set; }
        public string Status { get; set; } = "Pending";
        public string? Error { get; set; }
    }

    public class SupplementaryRenameResult
    {
        public int Applied { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public List<SupplementaryRenameOp> Operations { get; set; } = new();
    }
}

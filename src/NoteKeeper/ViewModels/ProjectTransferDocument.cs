using NoteKeeper.Models;

namespace NoteKeeper.ViewModels;

public sealed class ProjectTransferDocument
{
    public const string ExpectedFormat = "NoteKeeperProject";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = ExpectedFormat;
    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = string.Empty;
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public List<ProjectTransferNote> Notes { get; set; } = [];
    public List<ProjectTransferTimeDay> TimeDays { get; set; } = [];
}

public sealed class ProjectTransferNote
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public NoteStatus Status { get; set; } = NoteStatus.Backlog;
    public int? EstimatedTimeMinutes { get; set; }
    public int? SpentTimeMinutes { get; set; }
    public bool IsTimeManagementPinned { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public List<NoteTransferBlock> Blocks { get; set; } = [];
}


public sealed class ProjectTransferTimeDay
{
    public DateTime Date { get; set; }
    public List<ProjectTransferTimeEntry> Entries { get; set; } = [];
}

public sealed class ProjectTransferTimeEntry
{
    public string? NoteKey { get; set; }
    public int TimeSpentMinutes { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public string? Comment { get; set; }
}

using NoteKeeper.Models;

namespace NoteKeeper.ViewModels;

public sealed class NoteEditorViewModel
{
    public int? Id { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; set; }
    public NoteStatus Status { get; set; } = NoteStatus.Backlog;
    public string EstimatedTime { get; set; } = string.Empty;
    public string SpentTime { get; set; } = string.Empty;
    public List<NoteBlockViewModel> Blocks { get; set; } = [];
}

public sealed class NoteBlockViewModel
{
    public BlockType Type { get; set; }
    public string? TextContent { get; set; }
    public string? Url { get; set; }
    public string? LinkTitle { get; set; }
    public string? ImagePath { get; set; }
    public string? Caption { get; set; }
}

public sealed class SaveNoteRequest
{
    public int? Id { get; set; }
    public int ProjectId { get; set; }
    public string? Title { get; set; }
    public NoteStatus Status { get; set; } = NoteStatus.Backlog;
    public string? EstimatedTime { get; set; }
    public string? SpentTime { get; set; }
    public List<SaveBlockRequest> Blocks { get; set; } = [];
}

public sealed class SaveBlockRequest
{
    public BlockType Type { get; set; }
    public string? TextContent { get; set; }
    public string? Url { get; set; }
    public string? LinkTitle { get; set; }
    public string? ImagePath { get; set; }
    public string? Caption { get; set; }
}

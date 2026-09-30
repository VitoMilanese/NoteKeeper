using NoteKeeper.Models;

namespace NoteKeeper.ViewModels;

public sealed class NoteTransferDocument
{
    public const string ExpectedFormat = "NoteKeeperNote";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = ExpectedFormat;
    public int Version { get; set; } = CurrentVersion;
    public string Title { get; set; } = string.Empty;
    public NoteStatus Status { get; set; } = NoteStatus.Backlog;
    public int? EstimatedTimeMinutes { get; set; }
    public int? SpentTimeMinutes { get; set; }
    public List<NoteTransferBlock> Blocks { get; set; } = [];
}

public sealed class NoteTransferBlock
{
    public BlockType Type { get; set; }
    public string? TextContent { get; set; }
    public string? Url { get; set; }
    public string? LinkTitle { get; set; }
    public string? Caption { get; set; }
    public string? ImageMimeType { get; set; }
    public string? ImageBase64 { get; set; }
}

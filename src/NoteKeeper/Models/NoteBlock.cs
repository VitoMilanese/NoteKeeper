using System.ComponentModel.DataAnnotations;

namespace NoteKeeper.Models;

public sealed class NoteBlock
{
    public int Id { get; set; }
    public int NoteId { get; set; }
    public Note Note { get; set; } = null!;

    public BlockType Type { get; set; }
    public int SortOrder { get; set; }

    public string? TextContent { get; set; }

    [MaxLength(2048)]
    public string? Url { get; set; }

    [MaxLength(300)]
    public string? LinkTitle { get; set; }

    [MaxLength(500)]
    public string? ImagePath { get; set; }

    [MaxLength(500)]
    public string? Caption { get; set; }
}

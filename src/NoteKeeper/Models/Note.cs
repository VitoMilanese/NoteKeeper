using System.ComponentModel.DataAnnotations;

namespace NoteKeeper.Models;

public sealed class Note
{
    public int Id { get; set; }

    [MaxLength(240)]
    public string Title { get; set; } = "Untitled";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public NoteStatus Status { get; set; } = NoteStatus.Backlog;
    public int? EstimatedTimeMinutes { get; set; }
    public int? SpentTimeMinutes { get; set; }

    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public List<NoteBlock> Blocks { get; set; } = [];
    public List<NoteTag> Tags { get; set; } = [];
}

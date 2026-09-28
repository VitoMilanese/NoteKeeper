using System.ComponentModel.DataAnnotations;

namespace NoteKeeper.Models;

public sealed class Note
{
    public int Id { get; set; }

    [MaxLength(240)]
    public string Title { get; set; } = "Без назви";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<NoteBlock> Blocks { get; set; } = [];
    public List<NoteTag> Tags { get; set; } = [];
}

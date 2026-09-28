using System.ComponentModel.DataAnnotations;

namespace NoteKeeper.Models;

public sealed class NoteTag
{
    public int Id { get; set; }
    public int NoteId { get; set; }
    public Note Note { get; set; } = null!;

    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;
}

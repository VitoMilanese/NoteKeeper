using System.ComponentModel.DataAnnotations;

namespace NoteKeeper.Models;

public sealed class Project
{
    public int Id { get; set; }

    [MaxLength(180)]
    public string Name { get; set; } = "Untitled project";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<Note> Notes { get; set; } = [];
    public List<TimeManagementDay> TimeManagementDays { get; set; } = [];
}

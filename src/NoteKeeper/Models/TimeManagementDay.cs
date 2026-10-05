using System.ComponentModel.DataAnnotations;

namespace NoteKeeper.Models;

public sealed class TimeManagementDay
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public DateTime Date { get; set; }

    public List<TimeManagementEntry> Entries { get; set; } = [];
}

public sealed class TimeManagementEntry
{
    public int Id { get; set; }
    public int TimeManagementDayId { get; set; }
    public TimeManagementDay Day { get; set; } = null!;

    public int? NoteId { get; set; }
    public Note? Note { get; set; }

    public int TimeSpentMinutes { get; set; }

    [MaxLength(240)]
    public string TaskTitle { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Comment { get; set; }
}

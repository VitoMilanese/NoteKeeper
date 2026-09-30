namespace NoteKeeper.ViewModels;

public sealed class TimeManagementViewModel
{
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public DateTime MonthStart { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public string MonthLabel { get; set; } = string.Empty;
    public string PreviousMonthKey { get; set; } = string.Empty;
    public string PreviousMonthLabel { get; set; } = string.Empty;
    public string NextMonthKey { get; set; } = string.Empty;
    public string NextMonthLabel { get; set; } = string.Empty;
    public string CurrentMonthKey { get; set; } = string.Empty;
    public DateTime DefaultNewDayDate { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<TimeTrackedNoteViewModel> TrackedNotes { get; set; } = [];
    public List<TimeNoteOptionViewModel> NoteOptions { get; set; } = [];
    public List<TimeNoteOptionViewModel> AvailableNotes { get; set; } = [];
    public List<TimeManagementDayViewModel> Days { get; set; } = [];
}

public sealed class TimeTrackedNoteViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EstimatedTime { get; set; } = string.Empty;
    public string SpentTime { get; set; } = string.Empty;
    public string RemainingTime { get; set; } = string.Empty;
}

public sealed class TimeNoteOptionViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed class TimeManagementDayViewModel
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string TotalTimeSpent { get; set; } = string.Empty;
    public List<TimeManagementEntryViewModel> Entries { get; set; } = [];
}

public sealed class TimeManagementEntryViewModel
{
    public int Id { get; set; }
    public int? NoteId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public string TimeSpent { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}

public sealed class TimePinRequest
{
    public bool IsPinned { get; set; }
}

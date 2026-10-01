namespace NoteKeeper.ViewModels;

public sealed class ProjectDashboardViewModel
{
    public string Query { get; set; } = string.Empty;
    public string Sort { get; set; } = "updated";
    public string Direction { get; set; } = "desc";
    public int TotalCount { get; set; }
    public List<ProjectListItemViewModel> Projects { get; set; } = [];
}

public sealed class ProjectListItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int NoteCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

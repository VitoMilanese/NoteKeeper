namespace NoteKeeper.ViewModels;

public sealed class NoteListViewModel
{
    public string Query { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Sort { get; set; } = "updated";
    public string Direction { get; set; } = "desc";
    public List<NoteListItemViewModel> Notes { get; set; } = [];
    public List<TagCountViewModel> Tags { get; set; } = [];
}

public sealed class NoteListItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int BlockCount { get; set; }
    public List<string> Tags { get; set; } = [];
}

public sealed class TagCountViewModel
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

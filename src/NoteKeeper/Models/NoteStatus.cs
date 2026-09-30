namespace NoteKeeper.Models;

public enum NoteStatus
{
    Backlog = 0,
    Active = 1,
    Done = 2,
    Released = 3,
    Selected = 4,
    Test = 5,
    Rejected = 6,
    Suspended = 7
}

public static class NoteStatusTags
{
    private static readonly string[] BacklogTags = ["backlog"];
    private static readonly string[] SelectedTags = ["selected"];
    private static readonly string[] ActiveTags = ["active"];
    private static readonly string[] TestTags = ["test"];
    private static readonly string[] DoneTags = ["done"];
    private static readonly string[] ReleasedTags = ["done", "released"];
    private static readonly string[] SuspendedTags = ["suspended"];

    public static IReadOnlyList<string> GetImplicitTags(NoteStatus status)
    {
        return status switch
        {
            NoteStatus.Backlog => BacklogTags,
            NoteStatus.Selected => SelectedTags,
            NoteStatus.Active => ActiveTags,
            NoteStatus.Test => TestTags,
            NoteStatus.Done => DoneTags,
            NoteStatus.Released => ReleasedTags,
            NoteStatus.Rejected => DoneTags,
            NoteStatus.Suspended => SuspendedTags,
            _ => []
        };
    }
}

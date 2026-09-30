namespace NoteKeeper.Models;

public enum NoteStatus
{
    Backlog = 0,
    Active = 1,
    Done = 2,
    Released = 3
}

public static class NoteStatusTags
{
    private static readonly string[] ActiveTags = ["active"];
    private static readonly string[] DoneTags = ["done"];
    private static readonly string[] ReleasedTags = ["done", "released"];

    public static IReadOnlyList<string> GetImplicitTags(NoteStatus status)
    {
        return status switch
        {
            NoteStatus.Active => ActiveTags,
            NoteStatus.Done => DoneTags,
            NoteStatus.Released => ReleasedTags,
            _ => []
        };
    }
}

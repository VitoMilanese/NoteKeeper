namespace NoteKeeper.Services;

public sealed class PriorityTagService(IWebHostEnvironment environment)
{
    private const string FileName = "priority-tags.txt";

    public IReadOnlyList<string> GetTags()
    {
        var path = Path.Combine(environment.ContentRootPath, FileName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadLines(path))
            {
                var tag = line.Trim().TrimStart('#').Trim().ToLowerInvariant();
                if (tag.Length == 0 || !seen.Add(tag))
                {
                    continue;
                }

                result.Add(tag);
            }

            return result;
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}

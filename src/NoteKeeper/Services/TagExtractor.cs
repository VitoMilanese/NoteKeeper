using System.Text.RegularExpressions;

namespace NoteKeeper.Services;

public static partial class TagExtractor
{
    [GeneratedRegex(@"(?<![\p{L}\p{N}_])#([\p{L}\p{N}_-]{1,50})", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    public static IReadOnlyCollection<string> Extract(IEnumerable<string?> texts)
    {
        return texts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(x => TagRegex().Matches(x!).Select(m => m.Groups[1].Value))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

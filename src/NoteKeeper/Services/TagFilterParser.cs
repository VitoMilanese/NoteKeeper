namespace NoteKeeper.Services;

public sealed record TagFilter(
    IReadOnlyList<string> IncludedTags,
    IReadOnlyList<string> ExcludedTags);

public static class TagFilterParser
{
    public static TagFilter Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new TagFilter([], []);
        }

        var included = new List<string>();
        var excluded = new List<string>();
        var includedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        ParseSequence(
            value,
            ref index,
            inheritedExcluded: false,
            included,
            excluded,
            includedSet,
            excludedSet);

        return new TagFilter(included, excluded);
    }

    private static void ParseSequence(
        string value,
        ref int index,
        bool inheritedExcluded,
        List<string> included,
        List<string> excluded,
        HashSet<string> includedSet,
        HashSet<string> excludedSet)
    {
        while (index < value.Length)
        {
            SkipSeparators(value, ref index);

            if (index >= value.Length)
            {
                return;
            }

            if (value[index] == ')')
            {
                index++;
                return;
            }

            var isExcluded = inheritedExcluded;
            if (value[index] == '-')
            {
                isExcluded = true;
                index++;
                SkipWhitespace(value, ref index);
            }

            if (index < value.Length && value[index] == '(')
            {
                index++;
                ParseSequence(
                    value,
                    ref index,
                    isExcluded,
                    included,
                    excluded,
                    includedSet,
                    excludedSet);
                continue;
            }

            var start = index;
            while (index < value.Length &&
                   value[index] != ',' &&
                   value[index] != '(' &&
                   value[index] != ')')
            {
                index++;
            }

            AddTag(
                value[start..index],
                isExcluded,
                included,
                excluded,
                includedSet,
                excludedSet);
        }
    }

    private static void AddTag(
        string rawValue,
        bool isExcluded,
        List<string> included,
        List<string> excluded,
        HashSet<string> includedSet,
        HashSet<string> excludedSet)
    {
        var tag = rawValue.Trim().TrimStart('#').Trim().ToLowerInvariant();
        if (tag.Length is 0 or > 50)
        {
            return;
        }

        if (isExcluded)
        {
            if (excludedSet.Add(tag))
            {
                excluded.Add(tag);
            }

            return;
        }

        if (includedSet.Add(tag))
        {
            included.Add(tag);
        }
    }

    private static void SkipSeparators(string value, ref int index)
    {
        while (index < value.Length &&
               (char.IsWhiteSpace(value[index]) || value[index] == ','))
        {
            index++;
        }
    }

    private static void SkipWhitespace(string value, ref int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }
    }
}

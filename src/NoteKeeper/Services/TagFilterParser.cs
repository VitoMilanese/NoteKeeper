namespace NoteKeeper.Services;

public abstract record TagFilterExpression;

public sealed record TagFilterTag(
    string Name,
    bool Negated) : TagFilterExpression;

public sealed record TagFilterGroup(
    bool MatchAny,
    IReadOnlyList<TagFilterExpression> Items) : TagFilterExpression;

public sealed record TagFilter(
    TagFilterExpression? Expression,
    IReadOnlyList<string> IncludedTags);

public static class TagFilterParser
{
    public static TagFilter Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new TagFilter(null, []);
        }

        var expression = ParseExpression(
            value.Trim(),
            matchAny: false,
            inheritedNegated: false);

        var includedTags = EnumerateTags(expression)
            .Where(x => !x.Negated)
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new TagFilter(expression, includedTags);
    }

    private static TagFilterExpression? ParseExpression(
        string value,
        bool matchAny,
        bool inheritedNegated)
    {
        var parts = SplitTopLevel(value);
        var items = parts
            .Select(part => ParseTerm(part, inheritedNegated))
            .Where(x => x is not null)
            .Cast<TagFilterExpression>()
            .ToArray();

        return items.Length switch
        {
            0 => null,
            1 => items[0],
            _ => new TagFilterGroup(matchAny, items)
        };
    }

    private static TagFilterExpression? ParseTerm(
        string rawValue,
        bool inheritedNegated)
    {
        var value = rawValue.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        var negated = inheritedNegated;

        while (value.StartsWith('-', StringComparison.Ordinal))
        {
            negated = true;
            value = value[1..].TrimStart();
        }

        if (value.Length == 0)
        {
            return null;
        }

        var wrapperCount = 0;
        while (TryStripFullParentheses(value, out var inner))
        {
            wrapperCount++;
            value = inner.Trim();
        }

        if (wrapperCount > 0)
        {
            return ParseExpression(
                value,
                matchAny: wrapperCount >= 2,
                inheritedNegated: negated);
        }

        var tag = NormalizeTag(value);
        return tag.Length is > 0 and <= 50
            ? new TagFilterTag(tag, negated)
            : null;
    }

    private static IReadOnlyList<string> SplitTopLevel(string value)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;

        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '(':
                    depth++;
                    break;

                case ')':
                    depth = Math.Max(0, depth - 1);
                    break;

                case ',' when depth == 0:
                    result.Add(value[start..index]);
                    start = index + 1;
                    break;
            }
        }

        result.Add(value[start..]);
        return result;
    }

    private static bool TryStripFullParentheses(
        string value,
        out string inner)
    {
        inner = string.Empty;

        if (value.Length < 2 ||
            value[0] != '(' ||
            value[^1] != ')')
        {
            return false;
        }

        var depth = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '(')
            {
                depth++;
            }
            else if (value[index] == ')')
            {
                depth--;
                if (depth < 0)
                {
                    return false;
                }

                if (depth == 0 && index != value.Length - 1)
                {
                    return false;
                }
            }
        }

        if (depth != 0)
        {
            return false;
        }

        inner = value[1..^1];
        return true;
    }

    private static string NormalizeTag(string value)
    {
        return value
            .Trim()
            .TrimStart('#')
            .Trim()
            .ToLowerInvariant();
    }

    private static IEnumerable<TagFilterTag> EnumerateTags(
        TagFilterExpression? expression)
    {
        switch (expression)
        {
            case TagFilterTag tag:
                yield return tag;
                yield break;

            case TagFilterGroup group:
                foreach (var item in group.Items)
                {
                    foreach (var tag in EnumerateTags(item))
                    {
                        yield return tag;
                    }
                }

                yield break;
        }
    }
}

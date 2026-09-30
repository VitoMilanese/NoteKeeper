namespace NoteKeeper.Services;

public abstract record TagFilterExpression;

public sealed record TagFilterTag(
    string Name,
    bool Negated) : TagFilterExpression;

public sealed record TagFilterGroup(
    bool MatchAny,
    IReadOnlyList<TagFilterExpression> Items) : TagFilterExpression;

public sealed record TagFilterNot(
    TagFilterExpression Operand) : TagFilterExpression;

public enum TagFilterError
{
    None,
    LogicalCommaNotAllowed,
    LogicalSyntax
}

public sealed record TagFilter(
    TagFilterExpression? Expression,
    IReadOnlyList<string> IncludedTags,
    TagFilterError Error = TagFilterError.None);

public static class TagFilterParser
{
    public static TagFilter Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new TagFilter(null, []);
        }

        var normalizedValue = value.Trim();
        if (normalizedValue.Contains('&') || normalizedValue.Contains('|'))
        {
            return ParseLogical(normalizedValue);
        }

        return ParseLegacy(normalizedValue);
    }

    private static TagFilter ParseLogical(string value)
    {
        if (value.Contains(','))
        {
            return new TagFilter(
                null,
                [],
                TagFilterError.LogicalCommaNotAllowed);
        }

        var parser = new LogicalExpressionParser(value);
        var expression = parser.Parse();

        if (expression is null || parser.HasError)
        {
            return new TagFilter(
                null,
                [],
                TagFilterError.LogicalSyntax);
        }

        return new TagFilter(
            expression,
            EnumeratePositiveTags(expression)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static TagFilter ParseLegacy(string value)
    {
        var expression = ParseLegacyExpression(
            value,
            matchAny: false,
            inheritedNegated: false);

        return new TagFilter(
            expression,
            EnumeratePositiveTags(expression)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static TagFilterExpression? ParseLegacyExpression(
        string value,
        bool matchAny,
        bool inheritedNegated)
    {
        var parts = SplitTopLevel(value);
        var items = parts
            .Select(part => ParseLegacyTerm(part, inheritedNegated))
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

    private static TagFilterExpression? ParseLegacyTerm(
        string rawValue,
        bool inheritedNegated)
    {
        var value = rawValue.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        var negated = inheritedNegated;

        while (value.Length > 0 && value[0] == '-')
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
            return ParseLegacyExpression(
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

    private static IEnumerable<string> EnumeratePositiveTags(
        TagFilterExpression? expression,
        bool logicalNegated = false)
    {
        switch (expression)
        {
            case TagFilterTag tag:
                if (!(logicalNegated ^ tag.Negated))
                {
                    yield return tag.Name;
                }

                yield break;

            case TagFilterNot not:
                foreach (var name in EnumeratePositiveTags(
                             not.Operand,
                             !logicalNegated))
                {
                    yield return name;
                }

                yield break;

            case TagFilterGroup group:
                foreach (var item in group.Items)
                {
                    foreach (var name in EnumeratePositiveTags(
                                 item,
                                 logicalNegated))
                    {
                        yield return name;
                    }
                }

                yield break;
        }
    }

    private sealed class LogicalExpressionParser(string value)
    {
        private int index;

        public bool HasError { get; private set; }

        public TagFilterExpression? Parse()
        {
            SkipWhitespace();

            var expression = ParseOr();
            SkipWhitespace();

            if (expression is null || index != value.Length)
            {
                HasError = true;
                return null;
            }

            return expression;
        }

        private TagFilterExpression? ParseOr()
        {
            var left = ParseAnd();
            if (left is null)
            {
                return null;
            }

            while (!HasError)
            {
                SkipWhitespace();

                if (!TryConsume('|'))
                {
                    break;
                }

                var right = ParseAnd();
                if (right is null)
                {
                    HasError = true;
                    return null;
                }

                left = Combine(left, right, matchAny: true);
            }

            return left;
        }

        private TagFilterExpression? ParseAnd()
        {
            var left = ParseUnary();
            if (left is null)
            {
                return null;
            }

            while (!HasError)
            {
                SkipWhitespace();

                if (!TryConsume('&'))
                {
                    break;
                }

                var right = ParseUnary();
                if (right is null)
                {
                    HasError = true;
                    return null;
                }

                left = Combine(left, right, matchAny: false);
            }

            return left;
        }

        private TagFilterExpression? ParseUnary()
        {
            SkipWhitespace();

            if (TryConsume('!'))
            {
                var operand = ParseUnary();
                if (operand is null)
                {
                    HasError = true;
                    return null;
                }

                return new TagFilterNot(operand);
            }

            return ParsePrimary();
        }

        private TagFilterExpression? ParsePrimary()
        {
            SkipWhitespace();

            if (TryConsume('('))
            {
                var expression = ParseOr();
                SkipWhitespace();

                if (expression is null || !TryConsume(')'))
                {
                    HasError = true;
                    return null;
                }

                return expression;
            }

            if (index >= value.Length ||
                value[index] is ')' or '&' or '|' or '!' or ',')
            {
                HasError = true;
                return null;
            }

            var start = index;

            while (index < value.Length &&
                   !char.IsWhiteSpace(value[index]) &&
                   value[index] is not '(' and not ')' and
                   not '&' and not '|' and not '!' and not ',')
            {
                index++;
            }

            var tag = NormalizeTag(value[start..index]);

            if (tag.Length is 0 or > 50)
            {
                HasError = true;
                return null;
            }

            return new TagFilterTag(tag, Negated: false);
        }

        private static TagFilterExpression Combine(
            TagFilterExpression left,
            TagFilterExpression right,
            bool matchAny)
        {
            if (left is TagFilterGroup existing &&
                existing.MatchAny == matchAny)
            {
                return existing with
                {
                    Items = existing.Items
                        .Append(right)
                        .ToArray()
                };
            }

            return new TagFilterGroup(
                matchAny,
                [left, right]);
        }

        private bool TryConsume(char character)
        {
            if (index >= value.Length ||
                value[index] != character)
            {
                return false;
            }

            index++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (index < value.Length &&
                   char.IsWhiteSpace(value[index]))
            {
                index++;
            }
        }
    }
}

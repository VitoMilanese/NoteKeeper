using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NoteKeeper.Services;

public static partial class RichTextContent
{
    public const string Prefix = "<!--NKHTML1-->";

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"</(div|p|blockquote|details|summary|ol|ul)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex(@"<li(?:\s[^>]*)?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<hr(?:\s[^>]*)?/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"<(?<closing>/)?(?<name>[a-z][a-z0-9]*)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagTokenRegex();

    [GeneratedRegex(@"[ \t]+\n", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExcessNewLinesRegex();

    public static string? ToTagSearchText(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        var html = value[Prefix.Length..];
        var builder = new StringBuilder(html.Length);
        var ignoredDepth = 0;
        var position = 0;

        foreach (Match match in HtmlTagTokenRegex().Matches(html))
        {
            if (ignoredDepth == 0 && match.Index > position)
            {
                builder.Append(html, position, match.Index - position);
            }

            var tagName = match.Groups["name"].Value;
            var isIgnoredElement =
                tagName.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("blockquote", StringComparison.OrdinalIgnoreCase);

            if (isIgnoredElement)
            {
                var isClosing = match.Groups["closing"].Success;
                ignoredDepth = isClosing
                    ? Math.Max(0, ignoredDepth - 1)
                    : ignoredDepth + 1;
            }
            else if (ignoredDepth == 0)
            {
                builder.Append(match.Value);
            }

            position = match.Index + match.Length;
        }

        if (ignoredDepth == 0 && position < html.Length)
        {
            builder.Append(html, position, html.Length - position);
        }

        return ToPlainText(Prefix + builder);
    }

    public static string? ToPlainText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        var html = value[Prefix.Length..];
        html = BreakRegex().Replace(html, "\n");
        html = HorizontalRuleRegex().Replace(html, "\n");
        html = ListItemRegex().Replace(html, "• ");
        html = BlockEndRegex().Replace(html, "\n");
        html = TagRegex().Replace(html, string.Empty);

        var text = WebUtility.HtmlDecode(html)
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        text = TrailingWhitespaceRegex().Replace(text, "\n");
        text = ExcessNewLinesRegex().Replace(text, "\n\n");
        return text.Trim();
    }
}

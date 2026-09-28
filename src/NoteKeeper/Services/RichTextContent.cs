using System.Net;
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

    [GeneratedRegex(@"[ \t]+\n", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExcessNewLinesRegex();

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

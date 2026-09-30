using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NoteKeeper.Services;

public static partial class JiraDuration
{
    public const int MinutesPerHour = 60;
    public const int HoursPerDay = 8;
    public const int DaysPerWeek = 5;
    public const int MinutesPerDay = HoursPerDay * MinutesPerHour;
    public const int MinutesPerWeek = DaysPerWeek * MinutesPerDay;

    public static bool TryParse(
        string? value,
        out int? totalMinutes)
    {
        totalMinutes = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var text = value.Trim();
        var matches = DurationTokenRegex().Matches(text);

        if (matches.Count == 0)
        {
            return false;
        }

        long total = 0;
        var consumedLength = 0;

        foreach (Match match in matches)
        {
            if (match.Index != consumedLength)
            {
                return false;
            }

            if (!long.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var amount))
            {
                return false;
            }

            var multiplier = char.ToLowerInvariant(
                match.Groups[2].Value[0]) switch
            {
                'w' => MinutesPerWeek,
                'd' => MinutesPerDay,
                'h' => MinutesPerHour,
                'm' => 1,
                _ => 0
            };

            if (multiplier == 0)
            {
                return false;
            }

            total += amount * multiplier;
            if (total > int.MaxValue)
            {
                return false;
            }

            consumedLength = match.Index + match.Length;
        }

        if (consumedLength != text.Length)
        {
            return false;
        }

        totalMinutes = (int)total;
        return true;
    }

    public static string Format(int? totalMinutes)
    {
        if (!totalMinutes.HasValue)
        {
            return string.Empty;
        }

        var minutes = Math.Max(0, totalMinutes.Value);
        if (minutes == 0)
        {
            return "0h";
        }

        var result = new StringBuilder();

        AppendUnit(
            result,
            ref minutes,
            MinutesPerWeek,
            "w");
        AppendUnit(
            result,
            ref minutes,
            MinutesPerDay,
            "d");
        AppendUnit(
            result,
            ref minutes,
            MinutesPerHour,
            "h");

        if (minutes > 0)
        {
            AppendPart(result, minutes, "m");
        }

        return result.ToString();
    }

    public static int? NormalizeMinutes(int? value)
    {
        return value is >= 0 ? value : null;
    }

    private static void AppendUnit(
        StringBuilder result,
        ref int remainingMinutes,
        int unitMinutes,
        string suffix)
    {
        if (remainingMinutes < unitMinutes)
        {
            return;
        }

        var amount = remainingMinutes / unitMinutes;
        remainingMinutes %= unitMinutes;
        AppendPart(result, amount, suffix);
    }

    private static void AppendPart(
        StringBuilder result,
        int amount,
        string suffix)
    {
        if (result.Length > 0)
        {
            result.Append(' ');
        }

        result.Append(amount);
        result.Append(suffix);
    }

    [GeneratedRegex(
        @"\G\s*(\d+)\s*([wdhm])",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant)]
    private static partial Regex DurationTokenRegex();
}

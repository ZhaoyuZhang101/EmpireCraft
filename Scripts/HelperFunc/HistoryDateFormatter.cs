using System;
using System.Linq;
using NeoModLoader.General;
using System.Text.RegularExpressions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.HelperFunc;

// Keeps timeline headings and event rows consistent in every supported game language.
public static class HistoryDateFormatter
{
    private static readonly Regex ChineseDate = new Regex(@"^\s*(?<year>.*?年)(?<rest>.*)$",
        RegexOptions.Compiled);
    private static readonly Regex EnglishYearFirst = new Regex(
        @"^\s*(?<year>years?\s*[:#]?\s*\d+)(?<rest>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EnglishNumberFirst = new Regex(
        @"^\s*(?<year>\d+\s*years?)(?<rest>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumericDate = new Regex(@"^\s*(?<year>\d+)(?<rest>\s*[/,.-]\s*.*)$",
        RegexOptions.Compiled);

    public static string GetYear(double timestamp, string fallbackDate = "")
    {
        return Split(timestamp, fallbackDate).year;
    }

    public static string GetMonthDay(double timestamp, string fallbackDate = "")
    {
        return Split(timestamp, fallbackDate).month_day;
    }

    public static HistoryDateParts Split(double timestamp, string fallbackDate = "")
    {
        string date = timestamp >= 0 ? Date.getDate(timestamp) : fallbackDate;
        return SplitText(date, LM.Get("Year"));
    }

    internal static HistoryDateParts SplitText(string date, string localizedYearLabel)
    {
        date = date?.Trim() ?? "";
        if (date.Length == 0) return default;

        Match match = ChineseDate.Match(date);
        if (!match.Success) match = EnglishYearFirst.Match(date);
        if (!match.Success) match = EnglishNumberFirst.Match(date);
        if (!match.Success) match = NumericDate.Match(date);
        if (match.Success)
        {
            string year = match.Groups["year"].Value.Trim();
            string monthDay = match.Groups["rest"].Value.Trim(' ', ',', ';', ':', '-', '/', '.');
            if (year.Length > 0 && year.All(char.IsDigit) && !string.IsNullOrWhiteSpace(localizedYearLabel))
                year = $"{localizedYearLabel} {year}";
            return new HistoryDateParts(year, monthDay);
        }

        // Preserve unknown formats in the heading rather than showing the full date on every event row.
        return new HistoryDateParts(date, "");
    }
}

public readonly struct HistoryDateParts
{
    public readonly string year;
    public readonly string month_day;

    public HistoryDateParts(string year, string monthDay)
    {
        this.year = year ?? "";
        month_day = monthDay ?? "";
    }
}

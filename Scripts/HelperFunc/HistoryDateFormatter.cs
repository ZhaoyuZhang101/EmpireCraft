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

    private static readonly Regex ChineseFullDate = new Regex(@"([\d,]+)\s*年\s*(\d+)\s*月\s*(\d+)\s*日", RegexOptions.Compiled);
    private static readonly Regex EnglishFullDate = new Regex(@"(\d+)\D*?\s+of\s+(.+?),\s*([\d,]+)", RegexOptions.Compiled);

    // 原版 Date.getDate 写出的日期("8,682年6月30日"、"30th of June, 8,682")换回世界时间(一年 60 秒、一月 5 秒、
    // 一月 30 天)；别的语言的格式认不出返回 false
    public static bool TryParseTime(string date, out double time)
    {
        time = 0d;
        if (string.IsNullOrWhiteSpace(date)) return false;
        int year, month, day;
        Match match = ChineseFullDate.Match(date);
        if (match.Success)
        {
            if (!int.TryParse(match.Groups[1].Value.Replace(",", ""), out year) ||
                !int.TryParse(match.Groups[2].Value, out month) || !int.TryParse(match.Groups[3].Value, out day)) return false;
        }
        else
        {
            match = EnglishFullDate.Match(date);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out day) ||
                !int.TryParse(match.Groups[3].Value.Replace(",", ""), out year)) return false;
            string name = match.Groups[2].Value.Trim();
            month = 0;
            for (int candidate = 1; candidate <= 12 && month == 0; candidate++)
                if (string.Equals(LocalizedTextManager.getText("month_" + candidate), name, StringComparison.OrdinalIgnoreCase))
                    month = candidate;
            if (month == 0) return false;
        }
        if (month < 1 || month > 12 || day < 1 || day > 31) return false;
        time = (year - 1) * 60d + (month - 1) * 5d + (day - 0.5d) * 5d / 30d;
        return true;
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

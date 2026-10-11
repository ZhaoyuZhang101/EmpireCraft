using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EmpireCraft.Scripts.HelperFunc;

public static class EmpireCraftVersionRules
{
    public static int Compare(string left, string right)
    {
        List<string> leftParts = Tokenize(left);
        List<string> rightParts = Tokenize(right);
        int commonCount = Math.Min(leftParts.Count, rightParts.Count);

        for (int i = 0; i < commonCount; i++)
        {
            int comparison = ComparePart(leftParts[i], rightParts[i]);
            if (comparison != 0) return comparison;
        }

        if (leftParts.Count == rightParts.Count) return 0;

        // EmpireCraft publishes suffixes as later iterations of the same numeric
        // base (0.4.2 -> 0.4.2Beta1 -> 0.4.2Stable), rather than SemVer prereleases.
        int remaining = leftParts.Count > commonCount
            ? CompareRemaining(leftParts, commonCount)
            : -CompareRemaining(rightParts, commonCount);
        return remaining;
    }

    private static int ComparePart(string left, string right)
    {
        bool leftNumber = long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out long leftValue);
        bool rightNumber = long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out long rightValue);

        if (leftNumber && rightNumber) return leftValue.CompareTo(rightValue);
        if (leftNumber != rightNumber) return leftNumber ? 1 : -1;

        int leftRank = VersionLabelRank(left);
        int rightRank = VersionLabelRank(right);
        return leftRank != rightRank
            ? leftRank.CompareTo(rightRank)
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareRemaining(List<string> parts, int start)
    {
        for (int i = start; i < parts.Count; i++)
        {
            if (long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out long value) && value == 0)
                continue;
            return 1;
        }
        return 0;
    }

    private static List<string> Tokenize(string version)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(version)) return parts;

        var token = new StringBuilder();
        bool? numeric = null;
        foreach (char character in version.Trim().ToLowerInvariant())
        {
            if (!char.IsLetterOrDigit(character))
            {
                FlushToken(parts, token);
                numeric = null;
                continue;
            }

            bool isNumeric = char.IsDigit(character);
            if (numeric.HasValue && numeric.Value != isNumeric) FlushToken(parts, token);
            token.Append(character);
            numeric = isNumeric;
        }
        FlushToken(parts, token);
        return parts;
    }

    private static void FlushToken(List<string> parts, StringBuilder token)
    {
        if (token.Length == 0) return;
        parts.Add(token.ToString());
        token.Clear();
    }

    private static int VersionLabelRank(string label)
    {
        return label switch
        {
            "dev" => 0,
            "alpha" or "a" => 1,
            "beta" or "b" => 2,
            "preview" or "pre" => 3,
            "rc" => 4,
            "stable" or "release" => 5,
            "final" => 6,
            _ => 2
        };
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EmpireCraft.Scripts.GeneralSystems;

// Expected providers of configured unlocks. This describes content, not installation status.
public sealed class TechContentSourceConfig
{
    public string id = "";
    public string name_key = "";
    public List<string> items = new();
    public List<string> buildings = new();
    public List<string> units = new();
    public List<string> materials = new();
}

public static class TechnologyContentRules
{
    public static string FindSource(string id, string kind, IEnumerable<TechContentSourceConfig> sources)
    {
        if (string.IsNullOrEmpty(id) || sources == null) return "unknown";
        // Canonical IDs distinguish WarBox's musket/rpg from ModernBox's Musket/RPG.
        foreach (TechContentSourceConfig source in sources)
            foreach (string pattern in Patterns(source, kind))
                if (string.Equals(pattern, id, StringComparison.Ordinal)) return source.id;
        string matched = null;
        foreach (TechContentSourceConfig source in sources)
            foreach (string pattern in Patterns(source, kind))
            {
                if (string.IsNullOrEmpty(pattern)) continue;
                bool matches = pattern.IndexOf('*') >= 0 ? BuildingPattern(pattern).IsMatch(id)
                    : string.Equals(pattern, id, StringComparison.OrdinalIgnoreCase);
                if (!matches) continue;
                if (matched != null && matched != source.id) return "unknown";
                matched = source.id;
            }
        return matched ?? "unknown";
    }

    private static IEnumerable<string> Patterns(TechContentSourceConfig source, string kind)
    {
        if (source == null || string.IsNullOrEmpty(source.id)) return Array.Empty<string>();
        return (kind switch
        {
            "item" => source.items,
            "building" => source.buildings,
            "unit" => source.units,
            "material" => source.materials,
            _ => null
        }) ?? (IEnumerable<string>)Array.Empty<string>();
    }

    // Exact IDs win. Only unambiguous changes of letter case are aliases;
    // similar weapon names never substitute for equipment that is not present.
    public static T FindAsset<T>(string id, Func<string, T> exactLookup,
        IEnumerable<T> registered, Func<T, string> assetId) where T : class
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        T exact = exactLookup?.Invoke(id);
        if (exact != null) return exact;
        if (registered == null) return null;
        T match = null;
        foreach (T asset in registered)
        {
            if (asset == null || !string.Equals(assetId(asset), id, StringComparison.OrdinalIgnoreCase)) continue;
            if (match != null && !ReferenceEquals(match, asset)) return null;
            match = asset;
        }
        return match;
    }

    public static Regex BuildingPattern(string pattern) => new(
        "^" + Regex.Escape(pattern ?? "").Replace("\\*", ".*") + "$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
}

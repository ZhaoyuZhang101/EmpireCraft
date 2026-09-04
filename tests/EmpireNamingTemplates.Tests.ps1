# Exercise the actual EC culture-template handoff, independent of Unity rendering.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Scripts/GamePatches/CulturePatch.cs') -Raw
$start = $source.IndexOf('private sealed class NamingTemplateState')
$end = $source.IndexOf('public static (string groupName', $start)
if ($start -lt 0 -or $end -lt $start) { throw 'Template handoff source not found' }
$actual = $source.Substring($start, $end - $start)
$fixture = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
public static class AncientWarfareCompatibility { public static bool Loaded; }
public static class PlayerConfig { public static string language = "ch"; public static string detectLanguage() => language; }
public static class OverallHelperFunc {
    public static Dictionary<string,string> mapping = new Dictionary<string,string> { { "Xia", "Huaxia" }, { "human", "Western" } };
    public static string GetCultureFromSpecies(string species) => mapping[species];
}
public enum MetaType { Kingdom, Clan, Family, City, Unit }
public class CultureData { public string creator_species_id = "Xia"; public string name = "saved-culture"; }
public class OnomasticsData { public int writes; public string configuredCulture; }
public class Culture {
    public CultureData data = new CultureData();
    public HashSet<string> traits = new HashSet<string>();
    public Dictionary<MetaType,OnomasticsData> names = Enum.GetValues(typeof(MetaType)).Cast<MetaType>().ToDictionary(t => t, t => new OnomasticsData());
    public OnomasticsData getOnomasticData(MetaType type) => names[type];
    public bool hasTrait(string trait) => traits.Contains(trait);
    public void addTrait(string trait) => traits.Add(trait);
}
public class TemplateSetting { public string rule = "EC"; public Dictionary<string,string> groups = new Dictionary<string,string>(); }
public class FamilySetting : TemplateSetting { }
public class UnitSetting : TemplateSetting { }
public class KingdomSetting : TemplateSetting { }
public class ClanSetting : TemplateSetting { }
public class CitySetting : TemplateSetting { }
public class Setting {
    public FamilySetting Family = new FamilySetting();
    public UnitSetting Unit = new UnitSetting();
    public KingdomSetting Kingdom = new KingdomSetting();
    public ClanSetting Clan = new ClanSetting();
    public CitySetting City = new CitySetting();
    public List<string> traits = new List<string> { "EC-political-trait" };
}
public static class OnomasticsRule { public static Dictionary<string,Setting> ALL_CULTURE_RULE = new Dictionary<string,Setting> { { "Huaxia", new Setting() }, { "Western", new Setting() } }; }
public static class OnomasticsHelper {
    public static void Configure(OnomasticsData data, string culture, string rule, object groups) { data.writes++; data.configuredCulture = culture; }
}
public static partial class TemplateTests {
    private static object setGroup(Dictionary<string,string> groups, string culture) => groups;
    private static int count;
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); count++; }
    public static int Run() {
        var culture = new Culture();
        EnsureEmpireNaming(culture);
        Check(culture.names.Values.All(n => n.writes == 0), "Standalone naming untouched");
        AncientWarfareCompatibility.Loaded = true;
        EnsureEmpireNaming(null);
        EnsureEmpireNaming(culture);
        Check(culture.names.Values.All(n => n.writes == 1 && n.configuredCulture == "Huaxia"), "All five Xia name templates use EC Huaxia rules");
        Check(culture.traits.Count == 0, "Naming handoff does not add political traits");
        Check(culture.data.name == "saved-culture", "Handoff does not randomly rename saved entities");
        EnsureEmpireNaming(culture);
        Check(culture.names.Values.All(n => n.writes == 1), "Repeated actor naming does not rebuild templates");
        PlayerConfig.language = "en";
        EnsureEmpireNaming(culture);
        Check(culture.names.Values.All(n => n.writes == 2), "Language change refreshes templates");
        OverallHelperFunc.mapping["Xia"] = "Western";
        EnsureEmpireNaming(culture);
        Check(culture.names.Values.All(n => n.writes == 3 && n.configuredCulture == "Western"), "Player species mapping is respected");
        culture.data = new CultureData();
        EnsureEmpireNaming(culture);
        Check(culture.names.Values.All(n => n.writes == 4), "Reused culture object with new save data refreshes templates");
        var other = new Culture();
        EnsureEmpireNaming(other);
        Check(other.names.Values.All(n => n.writes == 1), "Cache is per object, not cross-world ID");
        insertCultureTemplate(other, "Western");
        Check(other.traits.Contains("EC-political-trait"), "Existing normal EC template installation still adds configured traits");
        OverallHelperFunc.mapping["Xia"] = "unavailable";
        EnsureEmpireNaming(other);
        Check(other.names.Values.All(n => n.writes == 2), "Missing rule leaves current template intact");
        return count;
    }
}
'@
Add-Type -TypeDefinition ($fixture + "`npublic static partial class TemplateTests {`n" + $actual + "`n}")
Write-Output "$([TemplateTests]::Run()) culture naming handoff assertions passed."

# Only naming callbacks opt out of political isolation.
foreach ($entry in @(
    @('ActorPatch', 'set_actor_culture'), @('ActorPatch', 'set_actor_clan_name'),
    @('ActorPatch', 'set_actor_family_name'), @('ClanPatch', 'set_clan_name'),
    @('FamilyPatch', 'set_family_name'), @('LaunguagePatch', 'set_Language_name'),
    @('ReligionPatch', 'set_religion_name')
)) {
    $code = Get-Content (Join-Path $root "Scripts/GamePatches/$($entry[0]).cs") -Raw
    $head = [regex]::Match($code, '(?s)\b' + $entry[1] + '\([^{}]+\)\s*\{\s*([^\r\n]+)').Groups[1].Value
    if (-not $head -or $head.Contains('OwnsObject')) { throw "Naming still blocked by political guard: $($entry[1])" }
}
Write-Output '7 naming-only entry-point checks passed.'

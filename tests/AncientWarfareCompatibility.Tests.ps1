# Run in PowerShell 7. Compile the actual bridge/rules and actual dispatcher method bodies.
param([switch]$WithoutAw)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read-Source($path) { Get-Content (Join-Path $root $path) -Raw }
$isolation = Read-Source 'Scripts/Compatibility/AncientWarfareIsolation.cs'
function Extract-Method([string]$source, [string]$name) {
    $match = [regex]::Match($source, '(?m)^[ ]{4,8}private static [^\r\n]+\b' + $name + '\(')
    if (-not $match.Success) { throw "Missing method $name" }
    $start = $source.IndexOf('{', $match.Index)
    $depth = 1; $end = $start + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($match.Index, $end - $match.Index)
}
$stubs = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GamePatches;
public class ActorAsset { public string id, banner_id; }
public class Data {
    public string original_actor_asset;
    public Dictionary<string, object> values = new Dictionary<string, object>();
    public void get(string key, out int value, int fallback) { value = values.TryGetValue(key, out var v) ? (int)v : fallback; }
    public void get(string key, out long value, long fallback) { value = values.TryGetValue(key, out var v) ? (long)v : fallback; }
}
public class Kingdom {
    public Data data = new Data(); public ActorAsset asset, species;
    public List<City> cities = new List<City>();
    public int restoredLevel; public bool dead, nativeOther;
    public ActorAsset getActorAsset() { return species; }
    public bool isRekt() { return dead; }
}
public class Actor { public ActorAsset asset; public Kingdom kingdom; }
public class City { public Kingdom kingdom; }
public class CityWindow { public City meta_object; }
public class WorldTile { public City zone_city; }
public class War { public Kingdom main_attacker, main_defender; }
public class KingdomManager {
    public Dictionary<long, Kingdom> items = new Dictionary<long, Kingdom>();
    public Kingdom get(long id) { return items.TryGetValue(id, out var k) ? k : null; }
}
public class World { public static World world = new World(); public KingdomManager kingdoms = new KingdomManager(); }
public class ActorLibrary {
    public ActorAsset xia;
    public ActorAsset get(string id) { return xia; }
}
public static class AssetManager { public static ActorLibrary actor_library = new ActorLibrary(); }
public static class SelectedMetas { public static Kingdom selected_kingdom; public static City selected_city; }
public static class SelectedUnit { public static Actor _unit_main; }
namespace UnityEngine { public static class Time { public static float realtimeSinceStartup; } }
namespace NeoModLoader.services { public static class LogService {
    public static int warnings; public static void LogInfo(string s) { }
    public static void LogWarning(string s) { warnings++; }
} }
namespace EmpireCraft.Scripts.Data { public static class ConfigData {
    public static Dictionary<string,string> speciesCulturePair = new Dictionary<string,string>();
} }
namespace EmpireCraft.Scripts.Layer {
    public class Empire {
        public Kingdom CoreKingdom; public object data = new object(); public List<City> cities_list = new List<City>();
        public bool IsArchived() { return false; }
    }
    public class KingdomTitle { public City title_capital; }
}
namespace EmpireCraft.Scripts.GamePatches {
    public static class KingdomWindowPatch { public static void Test() { } }
    public static class UnitWindowPatch { public static void Test() { } }
    public static class CityWindowPatch { public static void Test() { } }
}
namespace ai.behaviours { public enum BehResult { Continue, Stop } }
namespace AncientWarfare3.core.lineage {
    public static class XiaizationService {
        public static int calls; public static bool fail;
        public static int GetLevel(Kingdom k) { calls++; if (fail) throw new Exception("restoring"); return k.restoredLevel; }
        public static bool IsNativePolicyKingdom(Kingdom k) { return k.nativeOther; }
    }
}
namespace EmpireCraft.Scripts.Compatibility {
    public static class AncientWarfareIsolation { public static void InstallWhenReady() { } }
}
public static partial class DispatchTests {
    private static readonly Dictionary<MethodBase, Func<object, BehResult>> OriginalBehaviours = new Dictionary<MethodBase, Func<object, BehResult>>();
    public static int passed;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed++; }
    private static Kingdom Human(int level = 0) {
        var k = new Kingdom { species = new ActorAsset { id = "human" } };
        k.data.values[AncientWarfareRules.XiaizationLevelKey] = level;
        return k;
    }
    public static int Run() {
        var aw = Human(5); var human = Human();
        UnityEngine.Time.realtimeSinceStartup = 1;
        AncientWarfareCompatibility.Refresh();
        Check(!AncientWarfareCompatibility.Loaded && !AncientWarfareCompatibility.Owns(aw), "No registered mod: EC unchanged");
        Check(!AncientWarfareCompatibility.BlocksEmpireFormation(aw), "No AW: no formation restrictions");
        AssetManager.actor_library.xia = new ActorAsset { id = "Xia" };
        UnityEngine.Time.realtimeSinceStartup = 3;
        AncientWarfareCompatibility.Refresh();
        Check(AncientWarfareCompatibility.Loaded, "Detect loaded service and registered race");
        Check(EmpireCraft.Scripts.Data.ConfigData.speciesCulturePair["Xia"] == "Huaxia", "Recognize Xia culture");
        EmpireCraft.Scripts.Data.ConfigData.speciesCulturePair["Xia"] = "Custom";
        UnityEngine.Time.realtimeSinceStartup = 5;
        AncientWarfareCompatibility.Refresh();
        Check(EmpireCraft.Scripts.Data.ConfigData.speciesCulturePair["Xia"] == "Custom", "Preserve player mapping");
        Check(!AncientWarfareCompatibility.Owns(human), "Ordinary country remains EC");
        human.cities.Add(new City { kingdom = human }); aw.cities.Add(new City { kingdom = aw });
        Check(IsRenderableKingdom(human), "AW loaded: normal kingdom keeps EC territory nameplate");
        Check(IsRenderableEmpire(new Empire { CoreKingdom = human, cities_list = human.cities }), "AW loaded: normal empire keeps EC territory nameplate");
        Check(!IsRenderableKingdom(aw) && !IsRenderableEmpire(new Empire { CoreKingdom = aw, cities_list = aw.cities }), "Only AW realm excluded from EC territory labels");
        var monkey = Human(-1); monkey.nativeOther = true; monkey.restoredLevel = 5;
        Check(!AncientWarfareCompatibility.Owns(monkey), "Other native-policy race is not automatically Xiaized");
        monkey.data.values[AncientWarfareRules.XiaizationLevelKey] = 3;
        Check(AncientWarfareCompatibility.Owns(monkey), "Explicit Xiaization still excludes other races");
        for (int i = 1; i <= 5; i++) Check(AncientWarfareCompatibility.Owns(Human(i)), "All Xiaization levels excluded");
        foreach (int field in new[] { 0, 1, 2, 3 }) {
            var k = Human();
            if (field == 0) k.data.original_actor_asset = "Xia";
            if (field == 1) k.asset = new ActorAsset { id = "Xia" };
            if (field == 2) k.species.id = "Xia";
            if (field == 3) k.species.banner_id = "Xia";
            Check(AncientWarfareCompatibility.Owns(k), "All native Xia indicators supported");
        }
        human.data.values[AncientWarfareRules.XiaizationLevelKey] = 1;
        Check(AncientWarfareCompatibility.Owns(human), "Mid-game Xiaization immediate");
        human.data.values[AncientWarfareRules.XiaizationLevelKey] = 0;
        Check(!AncientWarfareCompatibility.Owns(human), "Marker change not hidden by cache");
        var legacy = Human(-1); legacy.restoredLevel = 3;
        int calls = AncientWarfare3.core.lineage.XiaizationService.calls;
        Check(AncientWarfareCompatibility.Owns(legacy) && AncientWarfareCompatibility.Owns(legacy), "Old save API fallback");
        Check(AncientWarfare3.core.lineage.XiaizationService.calls == calls + 1, "Missing-marker reads throttled");
        var other = Human(-1);
        Check(!AncientWarfareCompatibility.Owns(other), "Different worlds/objects do not share ID cache");
        AncientWarfare3.core.lineage.XiaizationService.fail = true;
        UnityEngine.Time.realtimeSinceStartup = 7;
        Check(AncientWarfareCompatibility.Owns(legacy), "Temporary restore failure keeps known owner");
        legacy.data = new Data();
        Check(!AncientWarfareCompatibility.Owns(legacy), "Replaced data cannot retain old world's cached owner");
        Check(NeoModLoader.services.LogService.warnings == 1, "Failures do not spam logs");
        AncientWarfare3.core.lineage.XiaizationService.fail = false;
        World.world.kingdoms.items[1] = aw;
        var subject = Human(); subject.data.values[AncientWarfareRules.SuzerainIdKey] = 1L;
        Check(AncientWarfareCompatibility.BlocksEmpireFormation(subject), "Unconverted AW vassal cannot form empire");
        Check(!AncientWarfareCompatibility.Owns(subject), "Subject restriction does not disable unrelated EC logic");
        World.world.kingdoms.items[2] = subject;
        var indirect = Human(); indirect.data.values[AncientWarfareRules.SuzerainIdKey] = 2L;
        Check(AncientWarfareCompatibility.BlocksEmpireFormation(indirect), "Nested subjects blocked");
        subject.data.values[AncientWarfareRules.SuzerainIdKey] = -1L;
        Check(!AncientWarfareCompatibility.BlocksEmpireFormation(subject), "Independence immediately lifts restriction");
        var tributary = Human(); tributary.data.values["aw_tributary_suzerain_id"] = 1L;
        Check(!AncientWarfareCompatibility.BlocksEmpireFormation(tributary), "Loose tribute is not vassalage");
        subject.data.values[AncientWarfareRules.SuzerainIdKey] = 123L;
        Check(!AncientWarfareCompatibility.BlocksEmpireFormation(subject), "Missing overlord does not permanently block formation");
        World.world.kingdoms.items[3] = human;
        subject.data.values[AncientWarfareRules.SuzerainIdKey] = 3L;
        Check(!AncientWarfareCompatibility.BlocksEmpireFormation(subject), "Non-Xia overlord not newly restricted");
        human.data.values[AncientWarfareRules.SuzerainIdKey] = 2L;
        Check(AncientWarfareCompatibility.BlocksEmpireFormation(subject), "Malformed relation cycle terminates safely");
        human.data.values.Remove(AncientWarfareRules.SuzerainIdKey);
        var actor = new Actor { kingdom = aw };
        Check(AncientWarfareCompatibility.Owns(actor), "Actors in Xiaized country excluded");
        Check(AncientWarfareCompatibility.Owns(new Actor { kingdom = human, asset = new ActorAsset { id = "Xia" } }), "Native Xia character names protected");
        Check(AncientWarfareCompatibility.OwnsObject(new CityWindow { meta_object = new City { kingdom = aw } }), "City UI detected");
        Check(AncientWarfareCompatibility.OwnsObject(new WorldTile { zone_city = new City { kingdom = aw } }), "God-power tile detected");
        Check(!AncientWarfareCompatibility.OwnsObject(new War { main_attacker = human, main_defender = aw }), "Mixed war retains normal empire processing");
        Check(AncientWarfareCompatibility.OwnsObject(new War { main_attacker = aw, main_defender = aw }), "AW-only war excluded");
        int ecCalls = 0, nativeCalls = 0;
        Func<Actor, bool> ec = a => { ecCalls++; return true; };
        Func<Actor, bool> native = a => { nativeCalls++; return false; };
        var callback = (Func<Actor,bool>)WrapCallback(ec, native);
        Check(!callback(actor) && nativeCalls == 1 && ecCalls == 0, "AW callback uses original");
        Check(callback(new Actor { kingdom = human }) && ecCalls == 1, "Normal callback unchanged");
        Check(!((Func<Actor,bool>)WrapCallback(ec, null))(actor), "EC-only plot unavailable to AW");
        Action<Actor> action = a => ecCalls++;
        ((Action<Actor>)WrapCallback(action, null))(actor);
        Check(ecCalls == 1, "EC-only void callbacks skipped");
        Func<Kingdom,int> opinion = k => 100;
        Check(((Func<Kingdom,int>)WrapCallback(opinion, null))(aw) == 0, "EC-only opinion has neutral fallback");
        MethodInfo method = typeof(DispatchTests).GetMethod("Run");
        int result = 7;
        Check(DirectCityPrefix(new City { kingdom = aw }, ref result) && result == 7, "AW prefix allows game method without touching ref result");
        Check(!DirectCityPrefix(new City { kingdom = human }, ref result) && result == 42, "Normal prefix executes EC logic");
        int beforeVoid = voidCalls;
        DirectActorPostfix(actor);
        Check(voidCalls == beforeVoid, "AW actor postfix skipped");
        DirectActorPostfix(new Actor { kingdom = human });
        Check(voidCalls == beforeVoid + 1, "Normal actor postfix executes");
        BehResult behaviour = BehResult.Stop;
        Check(!BehaviourPrefix(method, actor, ref behaviour) && behaviour == BehResult.Continue, "New EC behaviour skipped");
        OriginalBehaviours[method] = target => BehResult.Stop;
        Check(!BehaviourPrefix(method, actor, ref behaviour) && behaviour == BehResult.Stop, "Replaced behaviour restores original");
        Check(BehaviourPrefix(method, human, ref behaviour), "Non-AW behaviour unchanged");
        return passed;
    }
    public static int RunWithoutAw() {
        var normal = Human(); var xia = Human(5); xia.species.id = "Xia";
        AssetManager.actor_library.xia = xia.species;
        UnityEngine.Time.realtimeSinceStartup = 1;
        AncientWarfareCompatibility.Refresh();
        Check(!AncientWarfareCompatibility.Loaded, "No AW assembly: bridge inactive even with a Xia asset");
        Check(!AncientWarfareCompatibility.Owns(normal) && !AncientWarfareCompatibility.Owns(xia), "No AW: neither normal nor formerly Xiaized countries excluded");
        normal.cities.Add(new City { kingdom = normal }); xia.cities.Add(new City { kingdom = xia });
        Check(IsRenderableKingdom(normal) && IsRenderableKingdom(xia), "No AW: all kingdom nameplates retained");
        Check(IsRenderableEmpire(new Empire { CoreKingdom = normal, cities_list = normal.cities }) && IsRenderableEmpire(new Empire { CoreKingdom = xia, cities_list = xia.cities }), "No AW: all empire nameplates retained");
        Check(!AncientWarfareCompatibility.BlocksEmpireFormation(xia), "No AW: formation not blocked by saved markers");
        int calls = 0;
        Func<Actor,bool> originalEC = actor => { calls++; return true; };
        var wrapped = (Func<Actor,bool>)WrapCallback(originalEC, null);
        Check(wrapped(new Actor { kingdom = xia }) && calls == 1, "No AW: existing callbacks still execute");
        int result = 0;
        Check(!DirectCityPrefix(new City { kingdom = xia }, ref result) && result == 42, "No AW: game patch uses EC body");
        int before = voidCalls; DirectActorPostfix(new Actor { kingdom = xia });
        Check(voidCalls == before + 1, "No AW: actor patch executes");
        Check(!EmpireCraft.Scripts.Data.ConfigData.speciesCulturePair.ContainsKey("Xia"), "No AW: user mapping unchanged");
        return passed;
    }
}
'@
$methods = foreach ($name in 'WrapCallback','BehaviourPrefix') {
    Extract-Method $isolation $name
}
$plates = Read-Source 'Scripts/GameLibrary/EmpireCraftNamePlateLibrary.cs'
$methods += Extract-Method $plates 'IsRenderableKingdom'
$methods += Extract-Method $plates 'IsRenderableEmpire'
function Read-EntryGuard($path, $method) {
    $source = Read-Source $path
    $match = [regex]::Match($source, '(?s)\b' + $method + '\([^{}]+\)\s*\{\s*(if \(EmpireCraft\.Scripts\.Compatibility\.AncientWarfareCompatibility\.OwnsObject\([^;]+;)')
    if (-not $match.Success) { throw "Missing direct guard: $method" }
    $match.Groups[1].Value
}
$cityGuard = Read-EntryGuard 'Scripts/GamePatches/CityPatch.cs' 'GetHouseLimit'
$actorGuard = Read-EntryGuard 'Scripts/GamePatches/ActorPatch.cs' 'UpdateStats'
$dispatcher = @"
public static partial class DispatchTests {
$($methods -join "`n")
private static int voidCalls;
private static bool DirectCityPrefix(City __instance, ref int __result) { $cityGuard __result = 42; return false; }
private static void DirectActorPostfix(Actor __instance) { $actorGuard voidCalls++; }
}
"@
if ($WithoutAw) { $stubs = $stubs.Replace('AncientWarfare3.core.lineage', 'NotLoadedAncientWarfare.core.lineage') }
$rules = Read-Source 'Scripts/Compatibility/AncientWarfareRules.cs'
$bridge = Read-Source 'Scripts/Compatibility/AncientWarfareCompatibility.cs'
Add-Type -TypeDefinition ($stubs + $dispatcher + "`nnamespace ActualSources { }`n" +
    ([regex]::Replace($rules, '(?m)^using [^;]+;\s*', '')) +
    ([regex]::Replace($bridge, '(?m)^using [^;]+;\s*', ''))) -ErrorAction Stop
if ($WithoutAw) { Write-Output "$([DispatchTests]::RunWithoutAw()) standalone-without-AW assertions passed." }
else { Write-Output "$([DispatchTests]::Run()) bridge and callback assertions passed." }
$mod = Read-Source 'Scripts/ModClass.cs'
$plots = Read-Source 'Scripts/AI/EmpireCraftPlotsAddition.cs'
$checks = @{
    'Snapshot native callbacks before EC initializes' = $mod.IndexOf('AncientWarfareIsolation.CaptureOriginalCallbacks();') -lt $mod.IndexOf('LoadUI();')
    'No AW: no Harmony guards installed' = $isolation.Contains('if (!_ready || !AncientWarfareCompatibility.Loaded) return;')
    'Restore removed native basic plots' = $isolation.Contains('AssetManager.plots_library.basic_plots.Add(plot)')
    'Guard player powers' = $isolation.Contains('"powers"')
    'No patch-on-patch installation' = -not $isolation.Contains('InstallPatchGuards') -and -not $isolation.Contains('Guard.Patch(method,')
    'Formation manager guards force paths' = (Read-Source 'Scripts/Layer/EmpireManager.cs').Contains('BlocksEmpireFormation(pKingdom)')
    'Formation eligibility guards special AI path' = (Read-Source 'Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckEmpire.cs').Contains('BlocksEmpireFormation(pKingdom)')
    'Formation plot rechecks force/start/continuation' = ([regex]::Matches($plots, 'BlocksEmpireFormation\(pActor\?\.kingdom\)').Count -eq 3)
    'Keep native Xia nameplates' = (Read-Source 'Scripts/GameLibrary/EmpireCraftNamePlateLibrary.cs').Contains('plate.showTextKingdom(kingdom, kingdom.capital.city_center)')
}
foreach ($check in $checks.GetEnumerator()) { if (-not $check.Value) { throw $check.Key } }
Write-Output "$($checks.Count) integration wiring checks passed."

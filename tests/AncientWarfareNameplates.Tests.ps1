# Compile the actual compatibility callbacks and EC's original mode selector.
param([switch]$FixtureOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Scripts/Compatibility/AncientWarfareNameplates.cs') -Raw
$zonesSource = Get-Content (Join-Path $root 'Scripts/GamePatches/ZonesPatch.cs') -Raw
$start = $zonesSource.IndexOf('public static bool GetCurrentMapBorderMode(')
$end = $zonesSource.IndexOf('{', $start) + 1
$depth = 1
while ($depth -gt 0) {
    if ($zonesSource[$end] -eq '{') { $depth++ }
    if ($zonesSource[$end] -eq '}') { $depth-- }
    $end++
}
$selector = $zonesSource.Substring($start, $end - $start)
$zoneMethods = foreach ($entry in @{
    Culture = 'Culture'; Kingdom = 'Kingdom'; Clan = 'Clan'; Alliance = 'Alliance'; City = 'City';
    Species = 'Subspecies'; Families = 'Family'; Languages = 'Language'; Religion = 'Religion'; Army = 'Army'
}.GetEnumerator()) {
    "public static bool show$($entry.Key)Zones(bool optionsOnly) { return vanilla == MetaType.$($entry.Value); }"
}
$stubs = @'
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.services;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Compatibility;
public enum MetaType { None, City, Kingdom, Culture, Clan, Alliance, Subspecies, Family, Language, Religion, Army }
public class MetaTypeAsset {
    public MetaType map_mode; public bool option, forced;
    public bool isActive(bool onlyOptions) { return option || (!onlyOptions && forced); }
}
public class World {
    public static World world = new World(); public MetaTypeAsset displayed;
    public MetaTypeAsset getCachedMapMetaAsset() { return displayed; }
}
public static partial class Zones {
    public static MetaType vanilla;
    public static bool names = true;
    public static bool showMapNames() { return names; }
    public static MetaType getCurrentMapBorderMode(bool pCheckOnlyOption) { return MetaType.None; }
}
public class NameplateManager { public MetaType getCurrentMode() { return MetaType.City; } }
namespace EmpireCraft.Scripts.GameLibrary {
    public static class MetaTypeExtension {
        public const MetaType Empire = (MetaType)100, KingdomTitle = (MetaType)101;
    }
    public static class EmpireCraftMetaTypeLibrary {
        public static MetaTypeAsset empire = new MetaTypeAsset(), kingdomTitle = new MetaTypeAsset();
    }
}
namespace EmpireCraft.Scripts.Compatibility {
    public static class AncientWarfareCompatibility { public static bool Loaded; }
}
namespace AncientWarfare3.core.policy {
    public static class HierarchicalVassalMapModeService { public static bool IsActive() { return true; } }
}
namespace NeoModLoader.services { public static class LogService {
    public static List<string> warnings = new List<string>();
    public static void LogWarning(string text) { warnings.Add(text); }
} }
namespace HarmonyLib {
    public static class Priority { public const int Last = 0; }
    public class HarmonyMethod {
        public int priority; public MethodInfo method;
        public HarmonyMethod(Type type, string name) { method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static); }
    }
    public static class AccessTools {
        public static MethodInfo Method(Type type, string name) { return type.GetMethod(name); }
    }
    public class Harmony {
        public static List<MethodInfo> patched = new List<MethodInfo>();
        public Harmony(string id) { }
        public void Patch(MethodInfo method, HarmonyMethod postfix) { patched.Add(method); }
    }
}
public static class NameplateTests {
    private static int count;
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); count++; }
    public static T Invoke<T>(string method, T value, params object[] before) {
        object[] args = before.Concat(new object[] { value }).ToArray();
        typeof(AncientWarfareNameplates).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        return (T)args[args.Length - 1];
    }
    public static int Run() {
        AncientWarfareNameplates.Install();
        Check(Harmony.patched.Count == 0, "No AW: no hooks");
        AncientWarfareCompatibility.Loaded = true;
        AncientWarfareNameplates.Install(); AncientWarfareNameplates.Install();
        Check(Harmony.patched.Count == 3 && LogService.warnings.Count == 0, "Three hooks, installed once");
        foreach (MetaType target in new[] { MetaTypeExtension.Empire, MetaTypeExtension.KingdomTitle }) {
            EmpireCraftMetaTypeLibrary.empire.option = target == MetaTypeExtension.Empire;
            EmpireCraftMetaTypeLibrary.kingdomTitle.option = target == MetaTypeExtension.KingdomTitle;
            Check(Invoke("BorderModePostfix", MetaType.None, false) == target, "AW None fallback cannot erase EC mode");
            Check(Invoke("BorderModePostfix", MetaType.City, false) == target, "AW city fallback cannot erase EC mode");
            Check(Invoke("BorderModePostfix", (MetaType)219, false) == (MetaType)219, "Real AW selected mode preserved");
            World.world.displayed = new MetaTypeAsset { map_mode = target };
            Check(Invoke("NameplateModePostfix", MetaType.City) == target, "Nameplates follow displayed EC layer");
            Check(!Invoke("HierarchicalActivePostfix", true), "Stale AW option cannot suppress shared canvas");
        }
        EmpireCraftMetaTypeLibrary.empire.option = false;
        EmpireCraftMetaTypeLibrary.kingdomTitle.option = false;
        EmpireCraftMetaTypeLibrary.kingdomTitle.forced = true;
        Check(Invoke("BorderModePostfix", MetaType.None, true) == MetaType.None, "Options-only query ignores forced view");
        Check(Invoke("BorderModePostfix", MetaType.None, false) == MetaTypeExtension.KingdomTitle, "Forced legal mode restored");
        EmpireCraftMetaTypeLibrary.kingdomTitle.forced = false;
        Check(Invoke("BorderModePostfix", MetaType.None, false) == MetaType.None, "No EC view: leave fallback alone");
        EmpireCraftMetaTypeLibrary.empire.option = true;
        Zones.vanilla = MetaType.Kingdom;
        Check(Invoke("BorderModePostfix", MetaType.None, false) == MetaType.None, "EC vanilla precedence unchanged");
        Zones.vanilla = MetaType.None;
        World.world.displayed.map_mode = MetaType.Kingdom;
        Check(Invoke("NameplateModePostfix", MetaType.City) == MetaType.Kingdom, "Kingdom labels follow kingdom layer");
        Check(!Invoke("HierarchicalActivePostfix", true), "Kingdom canvas not hidden by stale AW flag");
        foreach (MetaType other in new[] { MetaType.City, MetaType.Culture, (MetaType)219, MetaType.None }) {
            World.world.displayed.map_mode = other;
            Check(Invoke("NameplateModePostfix", other) == other, "Other mode names unchanged");
            Check(Invoke("HierarchicalActivePostfix", true), "Other mode AW activation unchanged");
        }
        World.world.displayed.map_mode = MetaTypeExtension.Empire;
        Zones.names = false;
        Check(Invoke("NameplateModePostfix", MetaType.None) == MetaType.None, "Respect hide-nameplates toggle");
        Zones.names = true;
        World.world.displayed = null;
        Check(Invoke("NameplateModePostfix", MetaType.City) == MetaType.City, "Missing displayed mode safe");
        Check(Invoke("HierarchicalActivePostfix", true), "Do not suppress AW while world cache uninitialized");
        World.world = null;
        Check(Invoke("NameplateModePostfix", MetaType.None) == MetaType.None, "No world safe");
        AncientWarfareCompatibility.Loaded = false;
        Check(Invoke("BorderModePostfix", MetaType.None, false) == MetaType.None, "Standalone EC path not intercepted");
        Check(Invoke("HierarchicalActivePostfix", true), "No loaded AW: no suppression");
        return count;
    }
}
'@
$definitions = "`npublic static partial class Zones { $($zoneMethods -join "`n") }`nnamespace EmpireCraft.Scripts.GameClassExtensions { public class ZonesPatch { $selector } }`n"
if ($FixtureOnly) { return @{ Stubs = $stubs; Definitions = $definitions; Source = $source } }
Add-Type -TypeDefinition ($stubs + $definitions + [regex]::Replace($source, '(?m)^using [^;]+;\s*', ''))
Write-Output "$([NameplateTests]::Run()) nameplate mode/ownership assertions passed."
$isolation = Get-Content (Join-Path $root 'Scripts/Compatibility/AncientWarfareIsolation.cs') -Raw
if (-not $isolation.Contains('AncientWarfareNameplates.Install();')) { throw 'Nameplate compatibility not wired' }

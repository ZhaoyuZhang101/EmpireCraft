# Compile the actual routing/installation source with fake game objects and a patch recorder.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Scripts/Compatibility/AncientWarfareNaming.cs') -Raw
$rules = Get-Content (Join-Path $root 'Scripts/Compatibility/AncientWarfareRules.cs') -Raw
$stubs = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NeoModLoader.services;
using EmpireCraft.Scripts.Compatibility;
public class BaseSystemData { public long id; public string name; }
public class KingdomData : BaseSystemData {
    public string original_actor_asset;
    public int level;
    public void get(string key, out int value, int fallback) { value = level; }
}
public class NanoObject {
    public int writes;
    public void setName(string name, bool track) { writes++; }
}
public class Kingdom : NanoObject { public KingdomData data = new KingdomData(); public Actor king; public bool xia; }
public class City : NanoObject { }
public class Actor { public BaseSystemData data = new BaseSystemData(); public Kingdom kingdom; public Culture culture; public bool xia; }
public class Clan { }
public class Family { }
public class Religion { }
public class Language { }
public class Subspecies { }
public class Culture {
    public OnomasticsData naming = new OnomasticsData();
    public OnomasticsData getOnomasticData(MetaType type) { return naming; }
}
public class OnomasticsData { public string next = "EC-name"; public string generateName() { return next; } }
public enum MetaType { Kingdom, Unit, City }
public static class NameGenerator { public static string generateName(Actor pActor, MetaType pType, long pSeed) { return "native"; } }
public class KingdomManager {
    public Dictionary<long, Kingdom> items = new Dictionary<long, Kingdom>();
    public Kingdom get(long id) { return items.TryGetValue(id, out var k) ? k : null; }
}
public class World { public static World world = new World(); public KingdomManager kingdoms = new KingdomManager(); }
namespace NeoModLoader.services { public static class LogService {
    public static List<string> warnings = new List<string>();
    public static void LogWarning(string message) { warnings.Add(message); }
} }
namespace HarmonyLib {
    public static class Priority { public const int First = 800, Normal = 400, Last = 0; }
    public class HarmonyMethod {
        public int priority; public MethodInfo method;
        public HarmonyMethod(Type type, string name) { method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic); }
    }
    public class CodeInstruction {
        public OpCode opcode; public object operand;
        public bool Calls(MethodInfo method) { return (opcode == OpCodes.Call || opcode == OpCodes.Callvirt) && Equals(operand, method); }
    }
    public static class AccessTools {
        public static MethodInfo Method(Type type, string name, Type[] parameters = null) {
            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).FirstOrDefault(m => m.Name == name);
        }
    }
    public class Harmony {
        public static List<MethodInfo> originals = new List<MethodInfo>();
        public static Dictionary<string, HarmonyMethod> patches = new Dictionary<string, HarmonyMethod>();
        public Harmony(string id) { }
        public static IEnumerable<MethodBase> GetAllPatchedMethods() { return Array.Empty<MethodBase>(); }
        public static PatchInfo GetPatchInfo(MethodBase method) { return null; }
        public void Unpatch(MethodBase original, MethodInfo callback) { }
        public void Patch(MethodInfo original, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null) {
            originals.Add(original);
            patches[original.DeclaringType.Name + "." + original.Name + (postfix != null ? ".postfix" : "")] = prefix ?? postfix ?? transpiler;
        }
    }
    public class PatchInfo {
        public List<Patch> Prefixes = new List<Patch>(), Postfixes = new List<Patch>(), Transpilers = new List<Patch>(), Finalizers = new List<Patch>();
    }
    public class Patch { public MethodInfo PatchMethod; }
}
namespace EmpireCraft.Scripts.GamePatches {
    public static class CulturePatch { public static void EnsureEmpireNaming(Culture culture) { } }
}
namespace EmpireCraft.Scripts.Compatibility {
    public static class AncientWarfareCompatibility {
        public static bool Loaded;
        public static bool Owns(Kingdom k) {
            return Loaded && k?.data != null && (k.xia || k.data.original_actor_asset == "Xia" || k.data.level > 0);
        }
        public static bool Owns(Actor a) { return Loaded && a != null && (a.xia || Owns(a.kingdom)); }
    }
}
namespace AncientWarfare3.core.naming {
    public static class AWLocalizedNameService {
        public static string ProjectActor(Actor actor) { actor.data.name = "AW-actor"; return actor.data.name; }
        public static void CaptureNative(BaseSystemData d) { }
        public static string EnsureIdentity(BaseSystemData d) { return "AW"; }
        public static bool CommitChineseName(BaseSystemData d) { return true; }
        public static string ProjectStored(BaseSystemData d) { return "AW"; }
        public static void ApplyKingdom(Kingdom k, Actor a) { }
    }
    public static class AWLocalizedNamePersistence { public static bool Apply(BaseSystemData d) { return true; } }
    public static class ActorManualRenameService {
        public static void ApplyInheritedFamily(Actor actor, string family) { actor.data.name = "AW-inherited"; }
        public static void CommitExplicitDisplay(Actor actor, string given, string display) { actor.data.name = display; }
        public static void CommitActor(Actor actor, object draft, bool custom) { actor.data.name = "AW-manual"; }
    }
    public static class AWLocalizedKingdomNameService {
        public static void BeginEdit(Kingdom k) { }
        public static void CommitEdit(Kingdom k) { }
        public static bool CommitCanonicalStateName(Kingdom k) { return true; }
        public static string ProjectStored(Kingdom k) { return "AW"; }
        public static int SynchronizeSharedIdentity(Kingdom k, IReadOnlyList<Kingdom> members) { return 1; }
    }
}
namespace AncientWarfare3.core.lineage {
    public static class WesternLineageAdmissionService {
        public static int royalBindings;
        public static bool SynchronizeOriginalClan(Actor actor) {
            if (!string.Equals(actor.data.name, "AW-heading", StringComparison.Ordinal)) actor.data.name = "AW-heading";
            if (!string.Equals(actor.data.name, "AW-heading", StringComparison.Ordinal)) return false;
            royalBindings++;
            return true;
        }
    }
    public static class LineageService {
        public static void ApplyDisplayName(Actor actor) { actor.data.name = "AW-lineage"; }
        public static void RenameClanByLeader(Clan clan, Actor actor) { }
    }
    public static class FamilyIdentitySyncService {
        public static void SyncFamilyName(Family family, Actor actor) { }
        public static void SyncClanName(Actor actor) { }
    }
    public static class KingdomIdentityContinuityService { public static void ApplyIdentity(Kingdom k) { } }
    public static class MilitaryGovernorateCreationService { public static bool TryCreateFromCandidateBatch(City c) { return true; } }
    public struct StateNameCommitResult { public bool Success; }
    public static class StateNameService {
        public static StateNameCommitResult EnsureBoundStateName(Kingdom k) { return default; }
        public static string GetBoundOrCurrentName(Kingdom k) { return "AW"; }
        public static bool ProjectCommittedStateName(Kingdom k) { return true; }
        public static bool ProjectExistingStateName(Kingdom k) { return true; }
        public static void ApplyCommittedProjection(Kingdom k) { }
        public static bool ReconcileLocalizedIdentityBeforeRestore(Kingdom k) { return true; }
    }
    public static class PeasantRebelRouteService { public static bool TryApplyRouteName(Kingdom k) { return true; } }
    public static class RulerAppellationService {
        public static string ResolveProjectedStateName(Kingdom k, bool hidden) { return "AW"; }
        public static string GetFullLivingAppellation(Kingdom k) { return "AW-title"; }
        public static string GetCompactLivingAppellation(Kingdom k) { return "AW-title"; }
    }
    public static class SuccessionDisputeService { public static Kingdom[] GetSharedNameMembers(Kingdom k) { return new[] { k }; } }
}
namespace AncientWarfare3.content {
    public static class XiaNamingRepair {
        public static bool TryApplyFullyXiaizedStateName(Kingdom k) { return true; }
        public static bool TryApplyFullyXiaizedKingdomName(Kingdom k) { return true; }
        public static bool TryRenameKingdom(Kingdom k, Actor a, bool force) { return true; }
        public static bool TryRenameCulture(Culture c, Actor a, bool force) { return true; }
        public static bool TryRenameReligion(Religion r, Actor a, bool force) { return true; }
        public static bool TryRenameLanguage(Language l, Actor a, bool force) { return true; }
        public static bool TryRenameSubspecies(Subspecies s, object asset, bool force) { return true; }
    }
}
namespace AncientWarfare3.patch {
    public static class AW_CivMonkeyNamingPatch {
        public static bool Prefix(ref string __result) { __result = "AW-hook"; return false; }
    }
    public static class AW_BabyNamePatch {
        public static int births;
        public static void Postfix(Actor pActor) { births++; AncientWarfare3.core.lineage.LineageService.ApplyDisplayName(pActor); }
    }
    public static class AW_XiaNamingPatch {
        public static void WorldLog_LogAllianceCreated_Prefix() { }
        public static void Culture_CreateCulture_Postfix() { }
    }
}
namespace AncientWarfare3.patch.naming {
    public static class AW_ActorLocalizedNamePatch {
        public static void Postfix(ref string __result) { __result = "AW-projection"; }
    }
}
public static class NamingTests {
    private static int count;
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); count++; }
    private static object Call(string name, object[] args) {
        return typeof(AncientWarfareNaming).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    }
    public static int Run() {
        var normal = new Kingdom { data = new KingdomData { id = 1, name = "East EC Empire", original_actor_asset = "human" } };
        var xia = new Kingdom { data = new KingdomData { id = 2, name = "AW-Xia", original_actor_asset = "Xia" } };
        var converted = new Kingdom { data = new KingdomData { id = 3, name = "AW-converted", level = 2 } };
        World.world.kingdoms.items[1] = normal;
        World.world.kingdoms.items[2] = xia;
        World.world.kingdoms.items[3] = converted;
        AncientWarfareNaming.Install();
        Check(Harmony.originals.Count == 0, "No AW: install is inert");
        Check(!AncientWarfareNaming.UsesEmpireNames(normal), "No AW: no interception");
        Check((bool)Call("WritePrefix", new object[] { xia }), "No AW: old Xia markers do not trigger patches");
        AncientWarfareCompatibility.Loaded = true;
        AncientWarfareNaming.Install();
        Check(LogService.warnings.Count == 0, "Every inspected naming service found");
        Check(Harmony.originals.Count == 42, "All service and generator guards installed");
        AncientWarfareNaming.Install();
        Check(Harmony.originals.Count == 42, "Install is idempotent");
        Check(Harmony.originals.All(m => !m.DeclaringType.FullName.Contains(".patch.")), "Never patch AW Harmony callbacks");
        Check(Harmony.patches["NameGenerator.generateName"].priority == Priority.First, "EC generation before AW generators");
        foreach (var realm in new[] { normal, xia, converted }) {
            bool ec = true;
            Check(AncientWarfareNaming.UsesEmpireNames(realm) == ec, "Country ownership");
            Check(AncientWarfareNaming.UsesEmpireNames(realm.data) == ec, "Data ownership");
            Check((bool)Call("WritePrefix", new object[] { realm }) == !ec, "Canonical name writes");
            object[] args = { realm, "existing-result" };
            Check((bool)Call("NamePrefix", args) == !ec, "Projection routing");
            Check((string)args[1] == (ec ? realm.data.name : "existing-result"), "Full saved name preserved; AW result untouched");
            args = new object[] { realm.data, "existing-result" };
            Check((bool)Call("NamePrefix", args) == !ec, "Language/load data projection");
            args = new object[] { realm, "existing-result" };
            Check((bool)Call("DisplayPrefix", args) == !ec, "Nameplate override ownership");
            Check((string)args[1] == (ec ? "" : "existing-result"), "Do not replace EC UI name with AW title");
        }
        Check(AncientWarfareNaming.UsesEmpireNames(new BaseSystemData()), "All localized name metadata protected");
        Check(!AncientWarfareNaming.UsesEmpireNames(null), "Null safe");
        var stale = new KingdomData { id = 2, original_actor_asset = "human" };
        Check(AncientWarfareNaming.UsesEmpireNames(stale), "No cross-world ID ownership leak");
        stale.original_actor_asset = "Xia";
        Check(AncientWarfareNaming.UsesEmpireNames(stale), "Detached native Xia data protected");
        stale.original_actor_asset = "human"; stale.level = 1;
        Check(AncientWarfareNaming.UsesEmpireNames(stale), "Detached Xiaized data protected");
        normal.data.level = 1;
        Check(AncientWarfareNaming.UsesEmpireNames(normal), "Mid-game conversion never restores AW naming");
        normal.data.level = 0;
        Check(AncientWarfareNaming.UsesEmpireNames(normal), "Conversion reversal resumes EC");
        object[] shared = { xia, new[] { xia, normal, converted } };
        Call("SharedNamesPostfix", shared);
        Check(((Kingdom[])shared[1]).Length == 0, "Xia shared names cannot rename any members");
        shared = new object[] { xia, (IReadOnlyList<Kingdom>)new[] { normal, xia } };
        Check(!(bool)Call("SharedIdentityPrefix", shared), "Xia name identity sync blocked");
        Check(!(bool)Call("SharedIdentityPrefix", new object[] { normal, null }), "EC identity sync blocked");
        var actor = new Actor { kingdom = normal, culture = new Culture() };
        object[] generation = { actor, MetaType.Kingdom, "AW", null };
        Check(!(bool)Call("GenerateKingdomPrefix", generation) && (string)generation[2] == "EC-name", "Ordinary country uses EC onomastics");
        object[] generatedResult = { generation[3], "overwritten-by-another-prefix" };
        Call("GenerateKingdomPostfix", generatedResult);
        Check((string)generatedResult[1] == "EC-name", "NML fork result-writing prefixes cannot replace EC result");
        actor.kingdom = xia;
        Check(!(bool)Call("GenerateKingdomPrefix", generation), "Xia uses EC generator");
        generatedResult = new object[] { generation[3], "AW-name" };
        Call("GenerateKingdomPostfix", generatedResult);
        Check((string)generatedResult[1] == "EC-name", "Xia postfix protects EC result");
        actor.kingdom = converted;
        Check(!(bool)Call("GenerateKingdomPrefix", generation), "Xiaized uses EC generator");
        actor.kingdom = normal; actor.xia = true;
        Check(!(bool)Call("GenerateKingdomPrefix", generation), "Native Xia founder uses EC generator");
        actor.xia = false;
        generation[1] = MetaType.Unit;
        Check((bool)Call("GenerateKingdomPrefix", generation), "Not a global actor-name override");
        generation[1] = MetaType.Kingdom;
        actor.culture.naming.next = "";
        Check((bool)Call("GenerateKingdomPrefix", generation), "Empty culture generator falls back");
        actor.culture = null;
        Check((bool)Call("GenerateKingdomPrefix", generation), "Missing culture falls back");
        Call("SetCountryName", new object[] { normal, "AW", false });
        Check(normal.writes == 0, "Mixed political operation cannot rename normal country");
        Call("SetCountryName", new object[] { xia, "AW", false });
        Check(xia.writes == 0, "Mixed political operation cannot rename Xia country");
        var city = new City();
        Call("SetCountryName", new object[] { city, "AW", true });
        Check(city.writes == 1, "Non-country setName unchanged");
        var setter = new CodeInstruction { opcode = OpCodes.Callvirt, operand = AccessTools.Method(typeof(NanoObject), "setName") };
        var other = new CodeInstruction { opcode = OpCodes.Nop };
        var il = ((IEnumerable<CodeInstruction>)Call("NameWriteTranspiler", new object[] { new[] { setter, other } })).ToArray();
        Check(ReferenceEquals(il[0], setter) && il[0].opcode == OpCodes.Call && ((MethodInfo)il[0].operand).Name == "SetCountryName", "Transpiler replaces only setter call without losing instruction metadata");
        Check(ReferenceEquals(il[1], other) && il[1].opcode == OpCodes.Nop, "Other political instructions preserved");
        actor.data.name = "EC-person";
        object[] actorName = { actor, "AW" };
        Check(!(bool)Call("ActorNamePrefix", actorName) && (string)actorName[1] == "EC-person", "Actor projection retains actual personal name");
        normal.king = actor;
        object[] rulerName = { normal, "AW-title" };
        Check(!(bool)Call("RulerNamePrefix", rulerName) && (string)rulerName[1] == "EC-person", "Tooltip uses actual ruler name");
        Check(!(bool)Call("SuppressPrefix", Array.Empty<object>()), "Periodic AW naming repair suppressed");
        Check(AncientWarfareNaming.IsNamingCallback(AccessTools.Method(typeof(AncientWarfare3.patch.AW_CivMonkeyNamingPatch), "Prefix")), "Remove pure naming hook");
        Check(AncientWarfareNaming.IsNamingCallback(AccessTools.Method(typeof(AncientWarfare3.patch.naming.AW_ActorLocalizedNamePatch), "Postfix")), "Remove localized name hook");
        Check(!AncientWarfareNaming.IsNamingCallback(AccessTools.Method(typeof(AncientWarfare3.patch.AW_BabyNamePatch), "Postfix")), "Keep birth affiliation hook");
        Check(!AncientWarfareNaming.IsNamingCallback(AccessTools.Method(typeof(AncientWarfare3.patch.AW_XiaNamingPatch), "Culture_CreateCulture_Postfix")), "Keep mixed culture integration hook");
        Check(AncientWarfareNaming.IsNamingCallback(AccessTools.Method(typeof(AncientWarfare3.patch.AW_XiaNamingPatch), "WorldLog_LogAllianceCreated_Prefix")), "Remove alliance-only naming hook");
        AncientWarfareCompatibility.Loaded = false;
        generation[0] = new Actor { culture = new Culture() };
        Check((bool)Call("GenerateKingdomPrefix", generation), "Disabled compatibility is inert even if previously installed");
        Check((bool)Call("SuppressPrefix", Array.Empty<object>()), "Service suppression inert without AW");
        return count;
    }
}
'@
$actual = [regex]::Replace($source + "`n" + $rules, '(?m)^using [^;]+;\s*', '')
Add-Type -TypeDefinition ($stubs + "`n" + $actual)
Write-Output "$([NamingTests]::Run()) naming routing and installation assertions passed."

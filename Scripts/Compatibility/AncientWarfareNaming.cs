using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Compatibility
{
    // Patch naming services, not AW's Harmony callbacks or its political systems.
    internal static class AncientWarfareNaming
    {
        private static readonly Harmony Guard = new("EmpireCraft.AncientWarfareNaming");
        private static bool _installed;
        private static Assembly _assembly;
        private static DateTime _nextSweep;

        internal static void Install()
        {
            if (!AncientWarfareCompatibility.Loaded) return;
            Assembly assembly = _assembly ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                a.GetType("AncientWarfare3.core.naming.AWLocalizedNameService", false) != null);
            if (assembly == null) return;
            // AW can register its callbacks after EC. Recheck without touching political patches.
            if (DateTime.UtcNow >= _nextSweep)
            {
                RemoveNamingPatches(assembly);
                _nextSweep = DateTime.UtcNow.AddSeconds(3);
            }
            if (_installed) return;
            _installed = true;

            PatchServices(assembly, "core.naming.AWLocalizedNameService", typeof(BaseSystemData),
                "CaptureNative", "EnsureIdentity", "CommitChineseName", "ProjectStored");
            PatchServices(assembly, "core.naming.AWLocalizedNameService", typeof(Kingdom), "ApplyKingdom");
            PatchNamed(assembly, "core.naming.AWLocalizedNameService", "ProjectActor", typeof(Actor), nameof(ActorNamePrefix));
            SuppressServices(assembly, "core.lineage.LineageService", "ApplyDisplayName", "RenameClanByLeader");
            SuppressServices(assembly, "core.lineage.FamilyIdentitySyncService", "SyncFamilyName", "SyncClanName");
            SuppressServices(assembly, "core.naming.ActorManualRenameService", "ApplyInheritedFamily", "CommitExplicitDisplay", "CommitActor");
            SuppressServices(assembly, "content.XiaNamingRepair", "TryRenameKingdom", "TryApplyFullyXiaizedKingdomName",
                "TryRenameCulture", "TryRenameLanguage", "TryRenameReligion", "TryRenameSubspecies");
            PatchServices(assembly, "core.naming.AWLocalizedNamePersistence", typeof(BaseSystemData), "Apply");
            PatchServices(assembly, "core.naming.AWLocalizedKingdomNameService", typeof(Kingdom),
                "BeginEdit", "CommitEdit", "CommitCanonicalStateName", "ProjectStored");
            PatchServices(assembly, "core.lineage.StateNameService", typeof(Kingdom),
                "EnsureBoundStateName", "GetBoundOrCurrentName", "ProjectCommittedStateName",
                "ProjectExistingStateName", "ApplyCommittedProjection", "ReconcileLocalizedIdentityBeforeRestore");
            PatchServices(assembly, "core.lineage.PeasantRebelRouteService", typeof(Kingdom), "TryApplyRouteName");
            PatchServices(assembly, "content.XiaNamingRepair", typeof(Kingdom), "TryApplyFullyXiaizedStateName");

            // These methods also restore history/create vassals. Filter only their name write,
            // rather than skipping the whole political operation or restoring names afterward.
            TryPatch(Find(assembly, "core.lineage.KingdomIdentityContinuityService", "ApplyIdentity", typeof(Kingdom)),
                nameof(NameWriteTranspiler), transpiler: true);
            TryPatch(Find(assembly, "core.lineage.MilitaryGovernorateCreationService", "TryCreateFromCandidateBatch", typeof(City)),
                nameof(NameWriteTranspiler), transpiler: true);
            TryPatch(Find(assembly, "core.lineage.WesternLineageAdmissionService", "SynchronizeOriginalClan", typeof(Actor)),
                nameof(ClanNameTranspiler), transpiler: true);

            PatchNamed(assembly, "core.lineage.RulerAppellationService", "ResolveProjectedStateName",
                typeof(Kingdom), nameof(DisplayPrefix));
            PatchNamed(assembly, "core.lineage.RulerAppellationService", "GetFullLivingAppellation",
                typeof(Kingdom), nameof(RulerNamePrefix));
            PatchNamed(assembly, "core.lineage.RulerAppellationService", "GetCompactLivingAppellation",
                typeof(Kingdom), nameof(RulerNamePrefix));
            PatchNamed(assembly, "core.lineage.SuccessionDisputeService", "GetSharedNameMembers",
                typeof(Kingdom), nameof(SharedNamesPostfix), postfix: true);
            PatchNamed(assembly, "core.naming.AWLocalizedKingdomNameService", "SynchronizeSharedIdentity",
                typeof(Kingdom), nameof(SharedIdentityPrefix));

            // Run before AW's civilization-specific generators; reuse EC's culture onomastics.
            MethodInfo generator = AccessTools.Method(typeof(NameGenerator), nameof(NameGenerator.generateName));
            TryPatch(generator, nameof(GenerateKingdomPrefix), false, Priority.First);
            TryPatch(generator, nameof(GenerateKingdomPostfix), true, Priority.Last);
        }

        internal static bool IsNamingCallback(MethodInfo method)
        {
            string type = method?.DeclaringType?.FullName ?? "";
            return type.StartsWith("AncientWarfare3.patch.naming.", StringComparison.Ordinal) ||
                   type == "AncientWarfare3.patch.AW_CivMonkeyNamingPatch" ||
                   type == "AncientWarfare3.patch.AW_NameProtectPatch" ||
                   type == "AncientWarfare3.patch.AW_NameplateTitlePatch" ||
                   (type == "AncientWarfare3.patch.AW_XiaNamingPatch" &&
                    method.Name == "WorldLog_LogAllianceCreated_Prefix");
        }

        internal static void RemoveNamingPatches(Assembly assembly)
        {
            foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToArray())
            {
                var info = Harmony.GetPatchInfo(original);
                if (info == null) continue;
                var callbacks = info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                    .Select(p => p.PatchMethod).Where(m => m.DeclaringType?.Assembly == assembly && IsNamingCallback(m))
                    .Distinct().ToArray();
                foreach (MethodInfo callback in callbacks)
                {
                    try { Guard.Unpatch(original, callback); }
                    catch (Exception error)
                    {
                        LogService.LogWarning("[EmpireCraft] Cannot remove AW naming callback " + callback.Name + ": " + error.Message);
                    }
                }
            }
        }

        private static void SuppressServices(Assembly assembly, string typeName, params string[] names)
        {
            Type type = assembly.GetType("AncientWarfare3." + typeName, false);
            foreach (string name in names)
            {
                MethodInfo[] methods = type?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == name).ToArray() ?? Array.Empty<MethodInfo>();
                if (methods.Length == 0)
                    LogService.LogWarning("[EmpireCraft] Optional AW naming API unavailable: " + typeName + "." + name);
                foreach (MethodInfo method in methods) TryPatch(method, nameof(SuppressPrefix));
            }
        }

        private static bool SuppressPrefix() => !AncientWarfareCompatibility.Loaded;

        private static void PatchServices(Assembly assembly, string typeName, Type firstParameter, params string[] names)
        {
            foreach (string name in names)
            {
                MethodInfo method = Find(assembly, typeName, name, firstParameter);
                string prefix = method?.ReturnType == typeof(string) ? nameof(NamePrefix) : nameof(WritePrefix);
                TryPatch(method, prefix);
            }
        }

        private static void PatchNamed(Assembly assembly, string typeName, string name, Type firstParameter,
            string callback, bool postfix = false)
        {
            TryPatch(Find(assembly, typeName, name, firstParameter), callback, postfix);
        }

        private static MethodInfo Find(Assembly assembly, string typeName, string name, Type firstParameter)
        {
            Type type = assembly.GetType("AncientWarfare3." + typeName, false);
            MethodInfo method = type?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length > 0 &&
                    m.GetParameters()[0].ParameterType == firstParameter);
            if (method == null)
                LogService.LogWarning("[EmpireCraft] Optional AW naming API unavailable: " + typeName + "." + name);
            return method;
        }

        private static void TryPatch(MethodInfo method, string callback, bool postfix = false,
            int priority = Priority.Normal, bool transpiler = false)
        {
            if (method == null) return;
            try
            {
                var patch = new HarmonyMethod(typeof(AncientWarfareNaming), callback) { priority = priority };
                Guard.Patch(method, prefix: postfix || transpiler ? null : patch,
                    postfix: postfix ? patch : null, transpiler: transpiler ? patch : null);
            }
            catch (Exception error)
            {
                // An optional mod version mismatch must not disable EC's layers and labels.
                LogService.LogWarning("[EmpireCraft] AW naming guard unavailable for " +
                    method.DeclaringType?.Name + "." + method.Name + ": " + error.Message);
            }
        }

        internal static bool UsesEmpireNames(object subject)
        {
            if (!AncientWarfareCompatibility.Loaded) return false;
            if (subject is Kingdom kingdom)
                return kingdom.data != null;
            if (subject is Actor actor) return actor.data != null;
            return subject is BaseSystemData;
        }

        private static bool WritePrefix(object __0)
        {
            return !UsesEmpireNames(__0);
        }

        private static bool NamePrefix(object __0, ref string __result)
        {
            if (!UsesEmpireNames(__0)) return true;
            __result = (__0 is Kingdom kingdom ? kingdom.data.name : (__0 as BaseSystemData)?.name) ?? "";
            return false;
        }

        private static bool ActorNamePrefix(Actor __0, ref string __result)
        {
            if (!AncientWarfareCompatibility.Loaded) return true;
            __result = __0?.data?.name ?? "";
            return false;
        }

        private static bool RulerNamePrefix(Kingdom __0, ref string __result)
        {
            if (!AncientWarfareCompatibility.Loaded) return true;
            __result = __0?.king?.data?.name ?? "";
            return false;
        }

        private static bool DisplayPrefix(Kingdom __0, ref string __result)
        {
            if (!UsesEmpireNames(__0)) return true;
            // Empty means no AW title override; retain the name already supplied by EC/game UI.
            __result = "";
            return false;
        }

        private static void SharedNamesPostfix(Kingdom __0, ref Kingdom[] __result)
        {
            if (!AncientWarfareCompatibility.Owns(__0) || __result == null) return;
            __result = __result.Where(k => !UsesEmpireNames(k)).ToArray();
        }

        private static bool SharedIdentityPrefix(Kingdom __0, ref IReadOnlyList<Kingdom> __1)
        {
            if (UsesEmpireNames(__0)) return false;
            if (AncientWarfareCompatibility.Owns(__0) && __1 != null)
                __1 = __1.Where(k => !UsesEmpireNames(k)).ToArray();
            return true;
        }

        private static bool GenerateKingdomPrefix(Actor pActor, MetaType pType, ref string __result, out string __state)
        {
            __state = null;
            if (!AncientWarfareCompatibility.Loaded || pType != MetaType.Kingdom ||
                pActor?.culture == null) return true;
            EmpireCraft.Scripts.GamePatches.CulturePatch.EnsureEmpireNaming(pActor.culture);
            OnomasticsData naming = pActor.culture.getOnomasticData(MetaType.Kingdom);
            if (naming == null) return true;
            string name = naming.generateName();
            if (string.IsNullOrWhiteSpace(name)) return true;
            __state = name;
            __result = name;
            return false;
        }

        private static void GenerateKingdomPostfix(string __state, ref string __result)
        {
            // NML's Harmony fork may still run another result-writing prefix after ours.
            // Reuse this invocation's generated name, never generate a second random name.
            if (__state != null) __result = __state;
        }

        private static IEnumerable<CodeInstruction> NameWriteTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo setter = AccessTools.Method(typeof(NanoObject), nameof(NanoObject.setName),
                new[] { typeof(string), typeof(bool) });
            MethodInfo guarded = AccessTools.Method(typeof(AncientWarfareNaming), nameof(SetCountryName));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(setter))
                {
                    // Keep the same stack signature, branch labels and exception boundaries.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = guarded;
                }
                yield return instruction;
            }
        }

        private static IEnumerable<CodeInstruction> ClanNameTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo equals = AccessTools.Method(typeof(string), nameof(string.Equals),
                new[] { typeof(string), typeof(string), typeof(StringComparison) });
            MethodInfo guard = AccessTools.Method(typeof(AncientWarfareNaming), nameof(ClanNameMatches));
            foreach (CodeInstruction instruction in instructions)
            {
                // This service compares only the clan heading. Treat it as satisfied so both
                // the rewrite AND its failure check are bypassed, retaining royal-clan binding.
                if (instruction.Calls(equals)) instruction.operand = guard;
                yield return instruction;
            }
        }

        private static bool ClanNameMatches(string current, string requested, StringComparison comparison)
        {
            return AncientWarfareCompatibility.Loaded || string.Equals(current, requested, comparison);
        }

        private static void SetCountryName(NanoObject subject, string name, bool track)
        {
            if (!UsesEmpireNames(subject)) subject.setName(name, track);
        }
    }
}

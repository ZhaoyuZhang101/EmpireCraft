using HarmonyLib;
using NeoModLoader.api;
using System.Reflection;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches
{
    public class OptimizationPatch : GamePatch
    {
        private const int CivilianAiThreshold = 5000;
        private const int ExtremePopulationThreshold = 20000;
        private const int VisibleActorRefreshThreshold = 10000;

        public ModDeclare declare { get; set; }

        private static AccessTools.FieldRef<Actor, bool> is_visible_ref;
        private static bool _throttleCivilianAi;
        private static bool _throttleVisibleCivilianAi;
        private static int _civilianAiMask;
        private static int _civilianAiStep;

        public void Initialize()
        {
            is_visible_ref = AccessTools.FieldRefAccess<Actor, bool>("is_visible");
            var harmony = new Harmony("EmpireCraft.OptimizationPatch");

            MethodInfo[] methodsToPatch = new MethodInfo[]
            {
                AccessTools.Method(typeof(Actor), "updateRotations"),
                AccessTools.Method(typeof(Actor), "updateChangeScale"),
                AccessTools.Method(typeof(Actor), "updateWalkJump"),
                AccessTools.Method(typeof(Actor), "updateFlipRotation")
            };

            foreach (var method in methodsToPatch)
            {
                if (method != null)
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(OptimizationPatch), nameof(Prefix_VisualUpdate))));
                }
                else
                {
                    Debug.LogWarning($"[EmpireCraft] OptimizationPatch: Could not find method to patch.");
                }
            }

            PatchPrefix(harmony, AccessTools.Method(typeof(BatchActors), "b6_0_updateDecision"),
                nameof(Prefix_BeginCivilianAiTick));
            PatchPrefix(harmony, AccessTools.Method(typeof(Actor), "b6_0_updateDecision"),
                nameof(Prefix_CivilianAi));
            PatchPrefix(harmony, AccessTools.Method(typeof(Actor), "b6_updateAI"),
                nameof(Prefix_CivilianAi));
            PatchPrefix(harmony, AccessTools.Method(typeof(Actor), "b3_findEnemyTarget"),
                nameof(Prefix_CivilianAi));
            PatchPrefix(harmony, AccessTools.Method(typeof(ActorManager), "calculateVisibleActors"),
                nameof(Prefix_CalculateVisibleActors));

            Debug.Log("[EmpireCraft] OptimizationPatch Initialized");
        }

        private void PatchPrefix(Harmony harmony, MethodInfo original, string prefix)
        {
            if (original == null)
            {
                Debug.LogWarning($"[EmpireCraft] OptimizationPatch: Could not find {prefix} target.");
                return;
            }

            harmony.Patch(original, prefix: new HarmonyMethod(GetType(), prefix));
        }

        public static bool Prefix_VisualUpdate(Actor __instance)
        {
            if (__instance == null || !ModClass.PERFORMANCE_SKIP_HIDDEN_VISUALS) return true;
            return is_visible_ref(__instance);
        }

        public static void Prefix_BeginCivilianAiTick()
        {
            if (!ModClass.PERFORMANCE_HIGH_POPULATION_MODE || World.world == null || World.world.units == null)
            {
                _throttleCivilianAi = false;
                _throttleVisibleCivilianAi = false;
                return;
            }

            int unitCount = World.world.units.Count;
            _throttleCivilianAi = unitCount >= CivilianAiThreshold;
            if (!_throttleCivilianAi)
            {
                _throttleVisibleCivilianAi = false;
                return;
            }

            _throttleVisibleCivilianAi = unitCount >= ExtremePopulationThreshold;
            _civilianAiMask = _throttleVisibleCivilianAi ? 7 : 3;
            unchecked
            {
                _civilianAiStep++;
            }
        }

        public static bool Prefix_CivilianAi(Actor __instance)
        {
            if (!_throttleCivilianAi || __instance == null)
                return true;

            // Keep combatants, rulers, city leaders, and player-focused actors fully responsive.
            if ((!_throttleVisibleCivilianAi && is_visible_ref(__instance)) || !__instance.hasCity() || __instance.hasArmy() ||
                __instance.isWarrior() || __instance.isKing() || __instance.isCityLeader() ||
                __instance.isFavorite() || __instance.isCameraFollowingUnit())
            {
                return true;
            }

            return (__instance.getID() & _civilianAiMask) ==
                   ((long)_civilianAiStep & _civilianAiMask);
        }

        public static bool Prefix_CalculateVisibleActors()
        {
            if (!ModClass.PERFORMANCE_HIGH_POPULATION_MODE || World.world == null || World.world.units == null ||
                World.world.units.Count < VisibleActorRefreshThreshold)
            {
                return true;
            }

            // Rendering data is visual-only, so retaining it for one frame is safe.
            return (Time.frameCount & 1) == 0;
        }
    }
}

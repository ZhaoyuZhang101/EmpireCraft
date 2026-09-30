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
            // Do not throttle Actor.updateDecision/updateAI as a whole. Reproduction,
            // relationships, housing and other simulation-critical decisions share that
            // pipeline, so skipping it makes the performance threshold behave like a
            // population cap. Only throttle the expensive optional target search below.
            PatchPrefix(harmony, AccessTools.Method(typeof(Actor), "b3_findEnemyTarget"),
                nameof(Prefix_CivilianAi));
            PatchPrefix(harmony, AccessTools.Method(typeof(ActorManager), "calculateVisibleActors"),
                nameof(Prefix_CalculateVisibleActors));
            PatchPrefix(harmony, AccessTools.Method(typeof(Actor), "b6_0_updateDecision"),
                nameof(Prefix_CivilianDecision));

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

        // 远离前线的平民降低决策频率，不影响生育：
        //   · 只在人口 ≥ DecisionThrottleThreshold 时生效(模组设置里的高人口性能开关)；
        //   · 能生育、到了生育年龄的成年人一律照常决策(生育、找伴侣、投奔伴侣所在城市都走决策)；
        //   · 所在国正在打仗的(前线)、士兵、军队成员、国王、城主、关注/跟随中的单位照常；
        //   · 其余平民(孩童、老人、不能生育的成年人)的部分决策改成原版的"wait"任务(0.5~1.3 秒，
        //     本身还能被生育、社交打断)，等完再重新决策——只是想得慢一点，不会卡住没任务。
        private const int DecisionThrottleThreshold = 3000;

        public static bool Prefix_CivilianDecision(Actor __instance)
        {
            if (!ModClass.PERFORMANCE_HIGH_POPULATION_MODE || __instance == null || World.world?.units == null)
                return true;
            int units = World.world.units.Count;
            if (units < DecisionThrottleThreshold) return true;
            // 原版这一帧本来就不决策的情况照原版处理
            if (__instance._update_done || __instance._beh_skip || __instance.is_unconscious ||
                __instance._has_status_possessed || !__instance.asset.has_ai_system) return true;
            if (!__instance.hasCity() || __instance.hasArmy() || __instance.isWarrior() || __instance.isKing() ||
                __instance.isCityLeader() || __instance.isFavorite() || __instance.isCameraFollowingUnit())
                return true;
            if (__instance.isAdult() && (__instance.canBreed() || __instance.isBreedingAge())) return true;
            if (__instance.kingdom != null && __instance.kingdom.hasEnemies()) return true;
            float skip = units >= ExtremePopulationThreshold ? 0.75f : 0.5f;
            if (Random.value >= skip) return true;
            __instance.setTask("wait");
            return false;
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

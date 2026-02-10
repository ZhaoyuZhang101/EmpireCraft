using HarmonyLib;
using NeoModLoader.api;
using System.Reflection;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches
{
    public class OptimizationPatch : GamePatch
    {
        public ModDeclare declare { get; set; }

        private static AccessTools.FieldRef<Actor, bool> is_visible_ref;

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
            Debug.Log("[EmpireCraft] OptimizationPatch Initialized");
        }

        public static bool Prefix_VisualUpdate(Actor __instance)
        {
            if (__instance == null) return true;
            return is_visible_ref(__instance);
        }
    }
}

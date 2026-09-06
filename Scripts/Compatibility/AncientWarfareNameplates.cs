using System;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using HarmonyLib;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Compatibility
{
    internal static class AncientWarfareNameplates
    {
        private static readonly Harmony Guard = new("EmpireCraft.AncientWarfareNameplates");
        private static bool _installed;

        internal static void Install()
        {
            if (_installed || !AncientWarfareCompatibility.Loaded) return;
            Type hierarchical = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("AncientWarfare3.core.policy.HierarchicalVassalMapModeService", false))
                .FirstOrDefault(t => t != null);
            if (hierarchical == null) return;
            _installed = true;
            Patch(AccessTools.Method(typeof(Zones), nameof(Zones.getCurrentMapBorderMode)), nameof(BorderModePostfix));
            Patch(AccessTools.Method(typeof(NameplateManager), "getCurrentMode"), nameof(NameplateModePostfix));
            // Patch the service, not AW's Harmony callback on NameplateManager.update.
            Patch(AccessTools.Method(hierarchical, "IsActive"), nameof(HierarchicalActivePostfix));
        }

        private static void Patch(MethodInfo method, string callback)
        {
            try
            {
                if (method == null) throw new MissingMethodException(callback);
                Guard.Patch(method, postfix: new HarmonyMethod(typeof(AncientWarfareNameplates), callback)
                {
                    priority = Priority.Last
                });
            }
            catch (Exception error)
            {
                LogService.LogWarning("[EmpireCraft] Optional nameplate compatibility patch failed: " +
                    callback + ": " + error.Message);
            }
        }

        private static bool IsCustomMode(MetaType mode)
        {
            return mode == MetaTypeExtension.Empire || mode == MetaTypeExtension.KingdomTitle;
        }

        private static bool IsTerritoryMode(MetaType mode)
        {
            return IsCustomMode(mode) || mode == MetaType.Kingdom;
        }

        private static void BorderModePostfix(bool pCheckOnlyOption, ref MetaType __result)
        {
            if (!AncientWarfareCompatibility.Loaded ||
                (__result != MetaType.None && __result != MetaType.City)) return;
            // AW's late prefix falls back to None because it does not know EC's meta types.
            // Re-evaluate EC's original precedence; never overwrite a real AW/other map mode.
            if (EmpireCraftMetaTypeLibrary.empire == null || EmpireCraftMetaTypeLibrary.kingdomTitle == null) return;
            MetaType intended = MetaType.None;
            ZonesPatch.GetCurrentMapBorderMode(pCheckOnlyOption, ref intended);
            if (IsCustomMode(intended)) __result = intended;
        }

        private static void NameplateModePostfix(ref MetaType __result)
        {
            if (!AncientWarfareCompatibility.Loaded || !Zones.showMapNames()) return;
            MetaTypeAsset displayed = World.world?.getCachedMapMetaAsset();
            // Match the layer actually being drawn instead of independently choosing city labels.
            if (displayed != null && IsTerritoryMode(displayed.map_mode))
                __result = displayed.map_mode;
        }

        private static void HierarchicalActivePostfix(ref bool __result)
        {
            if (!__result || !AncientWarfareCompatibility.Loaded) return;
            MetaTypeAsset displayed = World.world?.getCachedMapMetaAsset();
            // A stale AW option must not suppress the shared canvas on an EC/kingdom map.
            // Returning false also lets AW restore the canvas it previously disabled.
            if (displayed != null && IsTerritoryMode(displayed.map_mode)) __result = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Compatibility
{
    // Optional runtime bridge: never load the other mod's DLL or access its database ourselves.
    public static class AncientWarfareCompatibility
    {
        private sealed class LevelCache { public float nextRead; public int level; public object data; }
        private static readonly ConditionalWeakTable<Kingdom, LevelCache> Levels = new();
        private static Func<Kingdom, int> _getLevel;
        private static Func<Kingdom, bool> _isNativePolicy;
        private static float _nextProbe;
        private static float _nextWarning;
        public static bool Loaded { get; private set; }

        public static void Refresh()
        {
            if (Time.realtimeSinceStartup < _nextProbe) return;
            _nextProbe = Time.realtimeSinceStartup + 1f;
            if (_getLevel == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type service = assembly.GetType("AncientWarfare3.core.lineage.XiaizationService", false);
                    MethodInfo method = service?.GetMethod("GetLevel", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(Kingdom) }, null);
                    if (method == null) continue;
                    _getLevel = (Func<Kingdom, int>)Delegate.CreateDelegate(typeof(Func<Kingdom, int>), method);
                    MethodInfo nativePolicy = service.GetMethod("IsNativePolicyKingdom", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(Kingdom) }, null);
                    if (nativePolicy != null)
                        _isNativePolicy = (Func<Kingdom, bool>)Delegate.CreateDelegate(typeof(Func<Kingdom, bool>), nativePolicy);
                    break;
                }
            }
            bool loaded = _getLevel != null && AssetManager.actor_library?.get(AncientWarfareRules.XiaSpecies) != null;
            if (loaded && !Loaded) LogService.LogInfo("[EmpireCraft] Ancient Warfare 3 compatibility enabled: Xia and Xiaized kingdoms use AW systems.");
            Loaded = loaded;
            if (Loaded && ConfigData.speciesCulturePair != null &&
                !ConfigData.speciesCulturePair.ContainsKey(AncientWarfareRules.XiaSpecies))
                ConfigData.speciesCulturePair[AncientWarfareRules.XiaSpecies] = "Huaxia";
            if (Loaded) AncientWarfareIsolation.InstallWhenReady();
        }

        public static bool Owns(Kingdom kingdom)
        {
            if (!Loaded || kingdom?.data == null) return false;
            kingdom.data.get(AncientWarfareRules.XiaizationLevelKey, out int level, -1);
            if (AncientWarfareRules.OwnsKingdom(true, kingdom.data.original_actor_asset,
                kingdom.asset?.id, null, null, level)) return true;
            ActorAsset species = kingdom.getActorAsset();
            if (AncientWarfareRules.OwnsKingdom(true, kingdom.data.original_actor_asset,
                kingdom.asset?.id, species?.id, species?.banner_id, level)) return true;
            if (level >= 0) return false;
            // Old AW saves may restore their level lazily. Cache only missing markers, not live changes.
            LevelCache cache = Levels.GetValue(kingdom, _ => new LevelCache());
            if (cache.data != kingdom.data || Time.realtimeSinceStartup >= cache.nextRead)
            {
                if (cache.data != kingdom.data) cache.level = 0;
                cache.data = kingdom.data;
                cache.nextRead = Time.realtimeSinceStartup + 1f;
                // AW also reports native monkey-policy realms as level 5. That is not Xiaization.
                try { cache.level = _isNativePolicy?.Invoke(kingdom) == true ? 0 : _getLevel(kingdom); }
                catch (Exception error)
                {
                    // Keep the last known owner while AW restores a save; never reset its data.
                    if (Time.realtimeSinceStartup >= _nextWarning)
                    {
                        _nextWarning = Time.realtimeSinceStartup + 60f;
                        LogService.LogWarning("[EmpireCraft] AW Xiaization state is not ready: " + error.Message);
                    }
                }
            }
            return cache.level > 0;
        }

        // AW stores inner/outer subjects here; loose tributaries use a different key.
        public static bool BlocksEmpireFormation(Kingdom kingdom)
        {
            if (!Loaded || kingdom?.data == null) return false;
            if (Owns(kingdom)) return true;
            var visited = new HashSet<Kingdom> { kingdom };
            Kingdom current = kingdom;
            while (current?.data != null)
            {
                current.data.get(AncientWarfareRules.SuzerainIdKey, out long id, -1L);
                if (id < 0) return false;
                current = World.world?.kingdoms?.get(id);
                if (current == null || current.data == null || current.isRekt()) return false;
                if (!visited.Add(current)) return true;
                if (Owns(current)) return true;
            }
            return false;
        }

        public static bool Owns(Actor actor)
        {
            return Loaded && actor != null && (AncientWarfareRules.IsXia(actor.asset?.id) || Owns(actor.kingdom));
        }

        public static bool OwnsObject(object value)
        {
            if (!Loaded || value == null) return false;
            if (value is Actor actor) return Owns(actor);
            if (value is Kingdom kingdom) return Owns(kingdom);
            if (value is City city) return Owns(city.kingdom);
            if (value is CityWindow window) return OwnsObject(window.meta_object);
            if (value is WorldTile tile) return Owns(tile.zone_city?.kingdom);
            if (value is Empire empire) return Owns(empire.CoreKingdom);
            if (value is KingdomTitle title) return Owns(title.title_capital?.kingdom);
            if (value is War war) return Owns(war.main_attacker) && Owns(war.main_defender);
            if (value is ActorAsset asset) return AncientWarfareRules.IsXia(asset.id);
            return false;
        }

        public static bool OwnsAny(object[] values)
        {
            if (!Loaded) return false;
            foreach (object value in values) if (OwnsObject(value)) return true;
            return false;
        }
    }
}

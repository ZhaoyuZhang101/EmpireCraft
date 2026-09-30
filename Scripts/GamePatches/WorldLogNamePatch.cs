using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 世界提示里的国名与地图铭牌统一：原版和部分模组提示直接写 kingdom.name / empire.data.name(如"楚帝国""南越")，
// 而铭牌显示的是模组国号(如"中华苏维埃共和国""楚联合临时政府")。提示加入列表前，把与某国原始名称
// 完全相同的 special 换成该国铭牌上的名称；同名城市、多国重名时不替换，以免误改。
// 另外，现代政体的国家不再有国王、王室：原版的国王/王国提示改用现代措辞(领导人、政权)，王室家族提示不再出现。
public class WorldLogNamePatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(WorldLogNamePatch)).Patch(
            AccessTools.Method(typeof(WorldLogMessageExtensions), nameof(WorldLogMessageExtensions.add)),
            prefix: new HarmonyMethod(GetType(), nameof(unify_names)));
    }

    // 原版提示 → 现代措辞(沿用原版的图标、分组和占位符，文本键为 modern_ + 原 id)
    private static readonly HashSet<string> ModernVariants = new()
    {
        "king_new", "king_left", "king_fled_capital", "king_fled_city", "king_dead", "king_killed",
        "kingdom_new", "kingdom_destroyed", "kingdom_shattered", "kingdom_fractured"
    };

    // 现代国家不再有王室，王室家族的更替不再提示
    private static readonly HashSet<string> MonarchyOnly = new()
    {
        "kingdom_royal_clan_new", "kingdom_royal_clan_changed", "kingdom_royal_clan_dead"
    };

    private static bool unify_names(WorldLogMessage pMessage)
    {
        if (pMessage == null || World.world?.kingdoms == null) return true;
        try
        {
            string assetId = pMessage.asset_id;
            if (assetId != null && (ModernVariants.Contains(assetId) || MonarchyOnly.Contains(assetId)) &&
                IsModern(pMessage.kingdom_id))
            {
                if (MonarchyOnly.Contains(assetId)) return false;
                pMessage.asset_id = ModernAsset(assetId);
            }
            Dictionary<string, string> names = BuildNameMap(pMessage.kingdom_id);
            if (names.Count == 0) return true;
            pMessage.special1 = Replace(pMessage.special1, names);
            pMessage.special2 = Replace(pMessage.special2, names);
            pMessage.special3 = Replace(pMessage.special3, names);
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft] 世界提示国名统一失败: {exception.Message}");
        }
        return true;
    }

    private static bool IsModern(long kingdomId)
    {
        if (kingdomId <= 0) return false;
        Kingdom kingdom = World.world.kingdoms.get(kingdomId);
        return kingdom?.data != null && kingdom.GetRegime()?.type == RegimeType.Modern;
    }

    private static string ModernAsset(string id)
    {
        string modernId = "modern_" + id;
        WorldLogLibrary library = AssetManager.world_log_library;
        if (library.get(modernId) != null) return modernId;
        WorldLogAsset original = library.get(id);
        if (original == null) return id;
        library.add(new WorldLogAsset
        {
            id = modernId,
            group = original.group,
            path_icon = original.path_icon,
            color = original.color,
            text_replacer = original.text_replacer
        });
        return modernId;
    }

    private static string Replace(string special, Dictionary<string, string> names) =>
        !string.IsNullOrEmpty(special) && names.TryGetValue(special, out string display) ? display : special;

    // 原始名称 → 铭牌名称。提示本身指向的国家优先；其余国家若原始名称撞名(不同国家、或与城市同名)就不替换
    private static Dictionary<string, string> BuildNameMap(long messageKingdomId)
    {
        var map = new Dictionary<string, string>();
        var ambiguous = new HashSet<string>();
        Kingdom own = null;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            if (kingdom.id == messageKingdomId) { own = kingdom; continue; }
            string display = EmpireCraftNamePlateLibrary.GetDisplayName(kingdom);
            if (string.IsNullOrWhiteSpace(display)) continue;
            foreach (string raw in RawNames(kingdom))
            {
                if (ambiguous.Contains(raw)) continue;
                if (map.TryGetValue(raw, out string existing) && existing != display)
                {
                    map.Remove(raw);
                    ambiguous.Add(raw);
                    continue;
                }
                map[raw] = display;
            }
        }
        if (World.world.cities != null)
            foreach (City city in World.world.cities)
                if (city?.data != null && !string.IsNullOrEmpty(city.data.name))
                    map.Remove(city.data.name);
        // 提示所属国家：即便与其首都同名也按国家处理(原版国家提示的第一个 special 就是国名)
        if (own != null)
        {
            string display = EmpireCraftNamePlateLibrary.GetDisplayName(own);
            if (!string.IsNullOrWhiteSpace(display))
                foreach (string raw in RawNames(own))
                    map[raw] = display;
        }
        foreach (string key in new List<string>(map.Keys))
            if (map[key] == key) map.Remove(key);
        return map;
    }

    private static IEnumerable<string> RawNames(Kingdom kingdom)
    {
        var names = new HashSet<string>();
        void Add(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
        }
        Add(kingdom.data.name);
        Add(kingdom.GetKingdomName());
        Add(kingdom.GetKingdomFullName());
        Empire empire = kingdom.IsEmpire() ? kingdom.GetEmpire() : null;
        if (empire?.data != null && empire.CoreKingdom == kingdom)
        {
            Add(empire.data.name);
            Add(empire.GetEmpireName());
            Add(empire.GetBaseEmpireFullName());
            Add(empire.GetEmpireFullName());
        }
        return names;
    }
}

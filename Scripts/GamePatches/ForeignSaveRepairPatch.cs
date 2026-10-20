using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 别的模组的旧存档(用户 2026-10-19：有人读 0.22.9 带种族模组的存档，Kingdom.loadData 空引用进不去)：
// 存档里国家、城市、宗族、文化等记的开国物种是那个模组加的、现在没有，原版读国家时取物种资产不判空就崩了。
// 原版转换完旧存档之后，把找不到的物种换成现有的：同名(不分大小写) > 名字里含现有的文明物种(xia_human→human) > 人类；
// 开过国、建过城、有宗族文化的物种，它的单位一并换过来，别的找不到的单位照原版丢掉。
// 战争类型同理(2026-10-21 有人读别的模组用过的图，读联盟时 War.isTotalWar 取不到战争类型空引用)：
// 原版谋划读档会丢掉找不到类型的，战争不判，这里把找不到的改成普通战争
public class ForeignSaveRepairPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(ForeignSaveRepairPatch)).Patch(
            AccessTools.Method(typeof(SaveConverter), nameof(SaveConverter.convert)),
            postfix: new HarmonyMethod(typeof(ForeignSaveRepairPatch), nameof(AfterConvert)));
    }

    public static void AfterConvert(SavedMap pData)
    {
        try
        {
            Repair(pData);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 旧存档物种修复失败: {exception.Message}");
        }
        try
        {
            RepairWars(pData);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 旧存档战争类型修复失败: {exception.Message}");
        }
    }

    private static void RepairWars(SavedMap data)
    {
        Dictionary<string, WarTypeAsset> types = AssetManager.war_types_library?.dict;
        if (data?.wars == null || types == null || !types.ContainsKey("normal")) return;
        var unknown = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (WarData war in data.wars)
        {
            if (war == null || !string.IsNullOrEmpty(war.war_type) && types.ContainsKey(war.war_type)) continue;
            string key = string.IsNullOrEmpty(war.war_type) ? "(空)" : war.war_type;
            unknown[key] = unknown.TryGetValue(key, out int count) ? count + 1 : 1;
            war.war_type = "normal";
        }
        if (unknown.Count == 0) return;
        LogService.LogWarning($"[EmpireCraft][外来存档] 找不到的战争类型已改为普通战争：" +
                              string.Join("，", unknown.Select(pair => $"{pair.Key} {pair.Value} 场")));
    }

    private static bool Known(string id) =>
        !string.IsNullOrEmpty(id) && AssetManager.actor_library?.dict != null && AssetManager.actor_library.dict.ContainsKey(id);

    private static void Repair(SavedMap data)
    {
        if (data == null || AssetManager.actor_library?.dict == null) return;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        // 有国家、城市、宗族、文化、语言、宗教的物种：它的单位也要换过来
        var sapient = new HashSet<string>(StringComparer.Ordinal);

        string Fix(string id, string kind, bool fixEmpty, bool isSapient)
        {
            if (Known(id) || string.IsNullOrEmpty(id) && !fixEmpty) return id;
            string key = id ?? "";
            if (!map.TryGetValue(key, out string target)) map[key] = target = Substitute(key);
            if (target == null) return id;
            if (isSapient && key.Length > 0) sapient.Add(key);
            counts[kind] = counts.TryGetValue(kind, out int count) ? count + 1 : 1;
            return target;
        }

        foreach (KingdomData kingdom in data.kingdoms ?? new List<KingdomData>())
            if (kingdom != null) kingdom.original_actor_asset = Fix(kingdom.original_actor_asset, "国家", true, true);
        foreach (CityData city in data.cities ?? new List<CityData>())
            if (city != null) city.original_actor_asset = Fix(city.original_actor_asset, "城市", true, true);
        foreach (ClanData clan in data.clans ?? new List<ClanData>())
            if (clan != null) clan.original_actor_asset = Fix(clan.original_actor_asset, "宗族", true, true);
        foreach (CultureData culture in data.cultures ?? new List<CultureData>())
            if (culture != null) culture.original_actor_asset = Fix(culture.original_actor_asset, "文化", true, true);
        foreach (LanguageData language in data.languages ?? new List<LanguageData>())
            if (language != null) language.creator_species_id = Fix(language.creator_species_id, "语言", false, true);
        foreach (ReligionData religion in data.religions ?? new List<ReligionData>())
            if (religion != null) religion.creator_species_id = Fix(religion.creator_species_id, "宗教", false, true);
        foreach (SubspeciesData subspecies in data.subspecies ?? new List<SubspeciesData>())
            if (subspecies != null) subspecies.species_id = Fix(subspecies.species_id, "亚种", true, false);
        foreach (FamilyData family in data.families ?? new List<FamilyData>())
            if (family != null) family.species_id = Fix(family.species_id, "家族", false, false);
        foreach (ActorData actor in data.actors_data ?? new List<ActorData>())
        {
            if (actor == null || string.IsNullOrEmpty(actor.asset_id) || !sapient.Contains(actor.asset_id) ||
                !map.TryGetValue(actor.asset_id, out string target) || target == null) continue;
            actor.asset_id = target;
            counts["单位"] = counts.TryGetValue("单位", out int count) ? count + 1 : 1;
        }

        if (map.Count == 0) return;
        string pairs = string.Join("，", map.Select(pair => $"{(pair.Key.Length > 0 ? pair.Key : "(空)")}→{pair.Value ?? "无"}"));
        string tally = string.Join("，", counts.Select(pair => $"{pair.Key} {pair.Value}"));
        LogService.LogWarning($"[EmpireCraft] 旧存档里有找不到的物种(多半是别的模组加的)，已替换：{pairs}；{tally}");
    }

    // 同名不分大小写、去掉旧版 unit_ 前缀 > 名字里含现有的文明物种 > 人类
    private static string Substitute(string id)
    {
        Dictionary<string, ActorAsset> dict = AssetManager.actor_library.dict;
        string bare = id.StartsWith("unit_", StringComparison.OrdinalIgnoreCase) ? id.Substring(5) : id;
        if (bare.Length > 0)
        {
            string same = dict.Keys.FirstOrDefault(key => string.Equals(key, bare, StringComparison.OrdinalIgnoreCase));
            if (same != null) return same;
            string lower = bare.ToLowerInvariant();
            ActorAsset civ = dict.Values
                .Where(asset => asset != null && asset.civ && !string.IsNullOrEmpty(asset.id) &&
                                lower.Contains(asset.id.ToLowerInvariant()))
                .OrderByDescending(asset => asset.id.Length)
                .FirstOrDefault();
            if (civ != null) return civ.id;
        }
        if (dict.ContainsKey("human")) return "human";
        return dict.Values.FirstOrDefault(asset => asset != null && asset.civ)?.id;
    }
}

using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

// 人口、矿藏笔刷(玩家："可以自定义人口、资源分布吗"，用户 2026-10-16 选神力笔刷)：
//   · 增加人口 / 减少人口：按住在城市领地上刷，笔刷盖到的每座城每 0.12 秒增减现有人口的 4%(至少 50 人)，
//     按原有的阶层、文化、种族、理念比例增减；空城按本城主文化加农民；
//   · 矿藏笔刷(铜、煤、硝石、硫磺、石油、铝土、稀土、铀各一个)：刷到的城有了这种矿藏、储量回满，固定下来
//     (同读档后的矿藏一样不再随地形变)；"清除矿藏"刷到的城去掉全部战略矿藏。
public static class CityResourcePowers
{
    public const string PopulationAdd = "population_add", PopulationRemove = "population_remove", DepositClear = "deposit_clear";
    private const string DepositPrefix = "deposit_";
    private const float StepShare = 0.04f, MinStep = 50f, Interval = 0.12f;
    private static readonly Dictionary<long, float> LastStroke = new();
    private static readonly HashSet<City> Brushed = new();
    private static readonly Dictionary<string, string> Minerals = new();

    public static string DepositId(string mineral) => DepositPrefix + mineral;

    // 登记各笔刷，返回 (神力 id, 按钮图标路径)
    public static List<(string power, string icon)> Init()
    {
        var result = new List<(string, string)>
        {
            (PopulationAdd, "ui/icons/godpowers/" + PopulationAdd),
            (PopulationRemove, "ui/icons/godpowers/" + PopulationRemove)
        };
        foreach ((string mineral, _, _, _) in MineralResourceSystem.Minerals)
        {
            string id = DepositId(mineral);
            Minerals[id] = mineral;
            // 名称、说明随矿名拼出来
            string name = LM.Get(mineral);
            LM.AddToCurrentLocale(id, string.Format(LM.Get("deposit_power_name"), name));
            LM.AddToCurrentLocale(id + "_description", string.Format(LM.Get("deposit_power_description"), name));
            result.Add((id, "ui/icons/resources/iconRes_" + mineral));
        }
        result.Add((DepositClear, "ui/icons/godpowers/" + DepositClear));
        foreach ((string id, _) in result)
        {
            if (AssetManager.powers.get(id) != null) continue;
            AssetManager.powers.add(new GodPower
            {
                id = id,
                name = id,
                hold_action = true,
                show_tool_sizes = true,
                unselect_when_window = true,
                click_power_action = Collect,
                click_power_brush_action = Stroke
            });
        }
        return result;
    }

    // 笔刷盖到的格子：记下所在的城
    private static bool Collect(WorldTile tile, GodPower power)
    {
        City city = tile?.zone_city;
        if (city?.data != null && !city.isRekt()) Brushed.Add(city);
        return true;
    }

    // 按住时每帧调一次：笔刷盖到的城各结算一次(同一座城隔 Interval 秒才再算)
    private static bool Stroke(WorldTile center, GodPower power)
    {
        if (center == null || power == null || World.world == null) return false;
        Brushed.Clear();
        World.world.loopWithBrush(center, Brush.get(Config.current_brush), Collect, power);
        float now = Time.unscaledTime;
        bool changed = false;
        foreach (City city in Brushed.ToList())
        {
            long key = city.data.id * 31L + power.id.GetHashCode();
            if (LastStroke.TryGetValue(key, out float last) && now - last < Interval) continue;
            LastStroke[key] = now;
            changed |= Apply(city, power.id);
        }
        if (LastStroke.Count > 512) LastStroke.Clear();
        return changed;
    }

    private static bool Apply(City city, string power)
    {
        switch (power)
        {
            case PopulationAdd: return ChangePopulation(city, true);
            case PopulationRemove: return ChangePopulation(city, false);
            case DepositClear:
            {
                CityPopulationData data = CityPopulationSystem.Get(city);
                if (data == null) return false;
                data.deposits_fixed ??= new Dictionary<string, bool>();
                bool changed = false;
                foreach ((string mineral, _, _, float chance) in MineralResourceSystem.Minerals)
                {
                    if (!MineralResourceSystem.HasDeposit(city, mineral, chance)) continue;
                    data.deposits_fixed[mineral] = false;
                    changed = true;
                }
                if (changed) Tip(city, LM.Get("deposit_clear_done"));
                return changed;
            }
        }
        if (!Minerals.TryGetValue(power, out string id)) return false;
        CityPopulationData state = CityPopulationSystem.Get(city);
        if (state == null) return false;
        state.deposits_fixed ??= new Dictionary<string, bool>();
        bool had = state.deposits_fixed.TryGetValue(id, out bool known) && known;
        bool exhausted = state.deposit_depleted_at != null && state.deposit_depleted_at.ContainsKey(id) ||
                         state.deposit_mined != null && state.deposit_mined.TryGetValue(id, out float mined) && mined > 0f;
        if (had && !exhausted) return false;
        state.deposits_fixed[id] = true;
        state.deposit_depleted_at?.Remove(id);
        state.deposit_mined?.Remove(id);
        Tip(city, string.Format(LM.Get("deposit_power_done"), LM.Get(id)));
        return true;
    }

    // 按原有的阶层、文化、种族、理念比例增减；空城按本城主文化加农民
    private static bool ChangePopulation(City city, bool add)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null) return false;
        float total = CityPopulationSystem.GetBackgroundTotal(city);
        float amount = Mathf.Max(MinStep, total * StepShare);
        if (add)
        {
            if (total < 1f)
            {
                string culture = CultureService.GetMainCulture(city);
                string species = city.kingdom?.getSpecies() ?? "";
                CityPopulationSystem.AddBackground(city, SocialClass.Peasant, culture, species, default, amount);
            }
            else
                foreach (PopGroup group in data.groups.ToList())
                    if (group != null && group.Background > 0f) group.size += group.Background / total * amount;
        }
        else
        {
            if (total < 1f) return false;
            amount = Mathf.Min(amount, total);
            foreach (PopGroup group in data.groups.ToList())
                if (group != null && group.Background > 0f) CityPopulationSystem.RemoveBackground(group, group.Background / total * amount);
        }
        LayerCityCache.Invalidate(city);
        Tip(city, string.Format(LM.Get("population_power_done"), Mathf.RoundToInt(CityPopulationSystem.GetBackgroundTotal(city))));
        return true;
    }

    private static void Tip(City city, string text) =>
        WorldTip.showNow($"{city.GetCityName()}：{text}", false, "top", 1.5f);
}

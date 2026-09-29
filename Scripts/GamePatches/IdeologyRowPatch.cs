using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 理念与宗教分开：原版宗教(国教、圣地、圣战等都依赖它)保持原样，理念作为独立的一层——
//   · 原版窗口里"宗教"一行之后，另加一行"理念"(人物 → 个人理念；城市/国家 → 主理念)；
//   · "理念"一行的悬浮提示按悬停对象显示个人/城市/国家的理念构成；
//   · 理念有自己的地图图层(MetaTypeExtension.Ideology)和理念窗口。
public class IdeologyRowPatch : GamePatch
{
    public const string TooltipId = "empirecraft_ideology";
    private const string RowTitle = "ideology_population_title";
    private const int TopEntries = 5;

    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(IdeologyRowPatch));
        TryPatch(harmony, typeof(StatsWindow), nameof(StatsWindow.tryToShowMetaReligion), nameof(WindowRowPostfix));
        TryPatch(harmony, typeof(StatsMetaRowsContainer), nameof(StatsMetaRowsContainer.tryToShowMetaReligion),
            nameof(ContainerRowPostfix));
        if (AssetManager.tooltips != null && !AssetManager.tooltips.dict.ContainsKey(TooltipId))
            AssetManager.tooltips.add(new TooltipAsset
            {
                id = TooltipId,
                prefab_id = "tooltips/tooltip_normal",
                callback = ShowIdeologyTooltip
            });
    }

    private static void TryPatch(Harmony harmony, Type type, string method, string prefix)
    {
        try
        {
            harmony.Patch(AccessTools.Method(type, method),
                postfix: new HarmonyMethod(typeof(IdeologyRowPatch), prefix));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 理念行挂载失败 {type.Name}.{method}: {exception.Message}");
        }
    }

    #region 悬浮提示

    // 悬停对象：人物 → 个人理念/所属政党/阶层 + 所在城市构成；城市 → 城市构成；国家 → 执政党与全国构成；
    // 只有宗教对象 → 该宗教信众的理念构成
    public static void ShowIdeologyTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        if (tooltip == null || data == null) return;
        tooltip.clear();
        Actor actor = data.actor != null && !data.actor.isRekt() ? data.actor : null;
        City city = data.city != null && !data.city.isRekt() ? data.city : actor?.city;
        Kingdom kingdom = data.kingdom != null && !data.kingdom.isRekt() ? data.kingdom : null;

        if (actor != null)
        {
            PartyIdeology ideology = IdeologyPopulationSystem.Get(actor);
            tooltip.setTitle(PartySystem.GetIdeologyName(ideology), RowTitle, Hex(ideology));
            tooltip.addLineText("empirecraft_actor_full_name", actor.getName(), "#FFFFFF", pLocalize: true);
            FixedFaction faction = actor.GetFaction();
            if (faction != null && faction.IsParty)
                tooltip.addLineText("ideology_tooltip_party", faction.Name, Hex(faction.Ideology));
            tooltip.addLineText("ideology_social_class_title", LM.Get($"class_{actor.GetOrCreate().socialClass}"),
                "#E0C783");
            if (city != null)
            {
                tooltip.addLineBreak();
                AddCounts(tooltip, city.GetCityFullName(), IdeologyPopulationSystem.GetCityCounts(city));
            }
            return;
        }
        if (city != null && data.kingdom == null)
        {
            PartyIdeology dominant = IdeologyPopulationSystem.GetDominant(city);
            tooltip.setTitle(PartySystem.GetIdeologyName(dominant), RowTitle, Hex(dominant));
            AddCounts(tooltip, city.GetCityFullName(), IdeologyPopulationSystem.GetCityCounts(city));
            return;
        }
        if (kingdom != null)
        {
            Dictionary<PartyIdeology, int> counts = KingdomCounts(kingdom);
            PartyIdeology dominant = Dominant(counts);
            tooltip.setTitle(PartySystem.GetIdeologyName(dominant), RowTitle, Hex(dominant));
            Empire empire = kingdom.GetEmpire();
            FixedFaction governing = empire == null ? null
                : ParliamentSystem.GetGoverningFaction(empire) ?? kingdom.GetRegime()?.GetDominateFaction();
            if (governing != null && governing.IsParty)
                tooltip.addLineText("ideology_tooltip_governing",
                    $"{governing.Name} · {PartySystem.GetIdeologyName(governing.Ideology)}", Hex(governing.Ideology));
            AddCounts(tooltip, kingdom.GetKingdomFullName(), counts);
            return;
        }
        Religion religion = data.religion;
        Dictionary<PartyIdeology, int> followers = Count(religion?.units);
        PartyIdeology main = Dominant(followers);
        tooltip.setTitle(PartySystem.GetIdeologyName(main), RowTitle, Hex(main));
        AddCounts(tooltip, null, followers);
    }

    private static void AddCounts(Tooltip tooltip, string scope, Dictionary<PartyIdeology, int> counts)
    {
        int total = counts.Values.Sum();
        if (!string.IsNullOrWhiteSpace(scope))
            tooltip.addLineText("ideology_tooltip_scope", scope, "#8FA0A8");
        if (total <= 0)
        {
            tooltip.addLineText("ideology_tooltip_none", "-", "#8FA0A8");
            return;
        }
        foreach (KeyValuePair<PartyIdeology, int> pair in counts.Where(pair => pair.Value > 0)
                     .OrderByDescending(pair => pair.Value).Take(TopEntries))
            tooltip.addLineText(PartySystem.GetIdeologyName(pair.Key), $"{100f * pair.Value / total:0.#}%",
                Hex(pair.Key), pPercent: false, pLocalize: false);
    }

    #endregion

    #region 窗口里宗教一行之后的"理念"一行

    // 原版行的图标只认 ui/Icons 下的图标名；理念没有自己的图标，跟宗教行用同一个
    private const string RowIcon = "iconReligion";

    // 理念不是原版的 MetaType，行本身不会跳转；on_click_value 在行被复用时由原版清空，直接赋值不会串到别的行
    private static void MakeClickable(KeyValueField field, PartyIdeology ideology)
    {
        if (field == null) return;
        field.on_click_value = () => EmpireCraft.Scripts.UI.Windows.IdeologyInfoWindow.Open(ideology);
    }

    public static void WindowRowPostfix(StatsWindow __instance, Religion pObject)
    {
        try
        {
            (PartyIdeology ideology, TooltipDataGetter getter) = ResolveRow(__instance, pObject);
            MakeClickable(__instance.showStatRow(RowTitle, PartySystem.GetIdeologyName(ideology), Hex(ideology),
                MetaType.None, -1L, true, RowIcon, TooltipId, getter, true), ideology);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 理念行显示失败: {exception.Message}");
        }
    }

    public static void ContainerRowPostfix(StatsMetaRowsContainer __instance, Religion pObject)
    {
        try
        {
            (PartyIdeology ideology, TooltipDataGetter getter) = ResolveRow(__instance, pObject);
            // showStatRowMeta 就是 showStatRow(pColorText: true) 的包装，只是不返回这一行；直接调用好拿到行
            MakeClickable(__instance.showStatRow(RowTitle, PartySystem.GetIdeologyName(ideology), Hex(ideology),
                MetaType.None, -1L, true, RowIcon, TooltipId, getter, true), ideology);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 理念行显示失败: {exception.Message}");
        }
    }

    // 按宿主窗口判断是谁的理念：人物窗口 → 该人物；城市窗口 → 城市主理念；国家窗口 → 全国主理念；
    // 其他窗口(氏族、家族等)→ 原宗教信众的主理念
    private static (PartyIdeology, TooltipDataGetter) ResolveRow(object host, Religion religion)
    {
        Component component = host as Component;
        if (component != null && component.GetComponentInParent<UnitWindow>(true) != null)
        {
            Actor actor = SelectedUnit.unit;
            if (actor != null && !actor.isRekt())
                return (IdeologyPopulationSystem.Get(actor), () => new TooltipData { actor = actor });
        }
        if (component != null && component.GetComponentInParent<CityWindow>(true) != null)
        {
            City city = SelectedMetas.selected_city;
            if (city != null && !city.isRekt())
                return (IdeologyPopulationSystem.GetDominant(city), () => new TooltipData { city = city });
        }
        if (component != null && component.GetComponentInParent<KingdomWindow>(true) != null)
        {
            Kingdom kingdom = SelectedMetas.selected_kingdom;
            if (kingdom != null && !kingdom.isRekt())
                return (Dominant(KingdomCounts(kingdom)), () => new TooltipData { kingdom = kingdom });
        }
        return (Dominant(Count(religion?.units)), () => new TooltipData { religion = religion });
    }

    #endregion

    #region 工具

    private static Dictionary<PartyIdeology, int> KingdomCounts(Kingdom kingdom)
    {
        var result = new Dictionary<PartyIdeology, int>();
        foreach (City city in kingdom.cities.Where(city => city != null && !city.isRekt()))
        foreach (KeyValuePair<PartyIdeology, int> pair in IdeologyPopulationSystem.GetCityCounts(city))
            result[pair.Key] = (result.TryGetValue(pair.Key, out int value) ? value : 0) + pair.Value;
        return result;
    }

    private static Dictionary<PartyIdeology, int> Count(IEnumerable<Actor> actors)
    {
        var result = new Dictionary<PartyIdeology, int>();
        foreach (Actor actor in actors ?? Enumerable.Empty<Actor>())
        {
            if (actor == null || actor.isRekt() || !actor.isAlive()) continue;
            PartyIdeology ideology = IdeologyPopulationSystem.Get(actor);
            result[ideology] = (result.TryGetValue(ideology, out int value) ? value : 0) + 1;
        }
        return result;
    }

    private static PartyIdeology Dominant(Dictionary<PartyIdeology, int> counts) =>
        counts.Count == 0 ? PartyIdeology.Centrism : counts.OrderByDescending(pair => pair.Value).First().Key;

    private static string Hex(PartyIdeology ideology)
    {
        ColorAsset color = EmpireCraftNamePlateLibrary.GetIdeologyColorAsset(ideology);
        return color == null ? "#7FD8EA" : "#" + ColorUtility.ToHtmlStringRGB(color.getColorBanner());
    }

    #endregion
}

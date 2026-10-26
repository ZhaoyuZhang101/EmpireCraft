using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;

// 宗教图层(用户 2026-10-27 定稿示意图 Tasks/ui-mockups/religion-wide.png ⑦⑧)。原版宗教图层的地块配色照旧(每教一色)：
//   · 铭牌：原版方框铭牌换成模组文字铭牌(铭牌资源在 EmpireCraftNamePlateLibrary 里登记)——按城所奉的宗教分组，每教一条顺着
//     所占城轮廓弯曲的文字，上淡金下本教色、深色描边；悬停的城所奉之教不透明高亮；
//   · 悬停城市：模组提示(本城信何教、圣地、国教与宗教政策、异教之城的稳定扣减与起事风险、遭迫害、民间教门风险)；
//   · 原版宗教提示框(各窗口里的宗教链接、铭牌)换成模组的(类型、天下、地位、圣地、教首与正统、教权、信众、圣战、分出的教会)；
//   · 点地块、点宗教链接：原版本就开宗教窗口，经 ReligionWindowRedirectPatch 改开模组宗教窗口。
public static class ReligionMap
{
    public const string TooltipType = "empirecraft_religion_city";
    private static readonly Dictionary<long, TerritoryLabelStyle> Styles = new();
    private static readonly Dictionary<Religion, List<City>> Groups = new();

    public static void ResetWorldState() => Styles.Clear();

    // 接管原版宗教图层的悬停提示(EmpireCraftNamePlateLibrary 登记铭牌时一并调用)
    public static void Configure()
    {
        MetaTypeAsset asset = MetaType.Religion.getAsset();
        if (asset == null) return;
        asset.check_cursor_tooltip = (zone, meta, option) =>
        {
            City city = zone?.city;
            if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city)) return false;
            Tooltip.hideTooltip(city, true, TooltipType);
            Tooltip.show(city, TooltipType, new TooltipData
            {
                city = city, kingdom = city.kingdom, tooltip_scale = 0.7f, is_sim_tooltip = true
            });
            return true;
        };
    }

    // 文字铭牌：每教一条
    public static void SubmitLabels()
    {
        Groups.Clear();
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt() || !city.hasReligion() ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city)) continue;
            if (!Groups.TryGetValue(city.religion, out List<City> cities)) Groups[city.religion] = cities = new List<City>();
            cities.Add(city);
        }
        Religion hovered = World.world?.getMouseTilePosCachedFrame()?.zone_city?.religion;
        foreach (KeyValuePair<Religion, List<City>> pair in Groups)
        {
            if (!ReligionDisplay.Alive(pair.Key)) continue;
            TerritoryLabelRenderer.SubmitCities($"religion:{pair.Key.data.id}", pair.Key.name, pair.Value, StyleOf(pair.Key),
                pair.Key == hovered);
        }
    }

    private static TerritoryLabelStyle StyleOf(Religion religion)
    {
        if (Styles.TryGetValue(religion.data.id, out TerritoryLabelStyle style)) return style;
        Color main = ReligionDisplay.MainColor(religion);
        style = new TerritoryLabelStyle
        {
            text_color = new Color(1f, 1f, 1f, 0.85f),
            outline_color = new Color(main.r * 0.25f, main.g * 0.25f, main.b * 0.25f, 0.86f),
            size_multiplier = 0.9f,
            territory_padding = 0.95f,
            min_font_size = 6,
            font_style = FontStyle.Normal,
            use_gold_gradient = true,
            gold_top = new Color32(255, 246, 214, 255),
            gold_bottom = Color.Lerp(main, Color.white, 0.15f),
            bridge_internal_gaps = true,
            arc_curvature = 0.1f
        };
        Styles[religion.data.id] = style;
        return style;
    }

    private const string Plain = "#D8D2B8", Bad = "#E07A5F", Good = "#7CD27C";

    private static void Line(Tooltip tooltip, string labelKey, string value, string hex = Plain) =>
        tooltip.addLineText(LM.Get(labelKey), value, hex, pPercent: false, pLocalize: false);

    // 宗教图层悬停城市
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        City city = data?.city;
        if (city?.data == null || city.isRekt()) return;
        Kingdom kingdom = city.kingdom;
        Religion faith = city.hasReligion() ? city.religion : null;
        tooltip.clear();
        tooltip.setTitle(city.GetCityName(), "religion_city_tooltip", "#FFFFFF");
        if (kingdom?.data != null) Line(tooltip, "religion_tip_kingdom", kingdom.GetKingdomName(), kingdom.getColor()?.color_text ?? Plain);
        if (faith == null)
        {
            Line(tooltip, "religion_tip_faith", LM.Get("religion_tip_no_faith"), "#8FA0A8");
        }
        else
        {
            bool holy = PapacySystem.HolyCity(faith) == city;
            Line(tooltip, "religion_tip_faith", holy ? string.Format(LM.Get("religion_tip_holy"), faith.name) : faith.name,
                ReligionDisplay.TextHex(faith));
            if (holy && PapacySystem.Pope(faith) is Actor head)
                Line(tooltip, "religion_tip_head", $"{ReligionDisplay.HeadTitle(faith)} {head.getName()}", "#F3C34A");
        }
        if (kingdom?.data != null && !kingdom.wild)
        {
            ReligiousTension.Policy policy = ReligiousTension.PolicyOf(kingdom);
            string policyHex = policy == ReligiousTension.Policy.Tolerant ? Good : policy == ReligiousTension.Policy.PollTax ? "#E9A85B" : Bad;
            Line(tooltip, "religion_tip_state", string.Format(LM.Get("religion_tip_state_value"),
                kingdom.hasReligion() ? kingdom.religion.name : LM.Get("label_none"), LM.Get("religion_policy_" + policy)), policyHex);
            if (ReligiousTension.IsHeterodox(city))
            {
                Line(tooltip, "religion_tip_heterodox", string.Format(LM.Get("religion_tip_stability"), ReligiousTension.StabilityLoss(city)), Bad);
                float persecuted = ReligiousPersecution.StabilityLoss(city);
                if (persecuted > 0f)
                    Line(tooltip, "stability_persecution_detail", string.Format(LM.Get("religion_tip_stability"), persecuted), Bad);
                if (policy != ReligiousTension.Policy.Tolerant && ReligiousTension.CanRevolt(city))
                    Line(tooltip, "religion_tip_risk", LM.Get("religion_tip_revolt"), Bad);
            }
            if (SectUprisingSystem.AtRisk(city)) Line(tooltip, "religion_tip_folk", LM.Get("religion_tip_sect"), "#E9A85B");
        }
        if (faith != null) tooltip.addBottomDescription(string.Format(LM.Get("religion_tip_click"), faith.name));
    }

    // 原版宗教提示框换成模组的(EmpireCraftTooltipLibrary 以 "religion" 登记)
    public static void ShowReligionTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        Religion religion = data?.religion;
        if (!ReligionDisplay.Alive(religion)) return;
        tooltip.clear();
        tooltip.setTitle(religion.name, "religion", ReligionDisplay.TextHex(religion));
        (string standing, string standingHex) = ReligionDisplay.Standing(religion);
        Line(tooltip, "religion_tip_type", $"{ReligionDisplay.FaithName(religion)} · {ReligionDisplay.SphereName(religion)}");
        Line(tooltip, "religion_tip_standing", standing, standingHex);
        City holy = PapacySystem.HolyCity(religion);
        Line(tooltip, "papacy_holy_city", holy != null
            ? $"{holy.GetCityName()}({holy.kingdom?.GetKingdomName() ?? ""})"
            : LM.Get("papacy_see_lost"), holy != null ? Plain : Bad);
        if (ReligionFaith.HasHierarchy(religion))
        {
            Actor head = PapacySystem.Pope(religion);
            string headText = head != null ? head.getName() : LM.Get("label_none");
            if (head != null && PopeLegitimacy.Applies(religion))
                headText += " · " + string.Format(LM.Get("religion_pope_legitimacy_short"), PopeLegitimacy.Get(religion));
            Line(tooltip, "religion_tip_head", $"{ReligionDisplay.HeadTitle(religion)} {headText}", "#F3C34A");
            float authority = PapacySystem.Authority(religion);
            Line(tooltip, "papacy_authority", $"{authority:0}", authority >= 60f ? Good : authority >= 35f ? Plain : Bad);
        }
        List<City> cities = religion.cities?.Where(city => city?.data != null && !city.isRekt()).ToList() ?? new List<City>();
        float people = cities.Sum(city => (float)city.CountLivingPopulation());
        int states = World.world.kingdoms.list.Count(kingdom => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild &&
                                                                 kingdom.religion == religion);
        Line(tooltip, "religion_tip_followers", string.Format(LM.Get("religion_tip_followers_value"), UIHelper.ShortCount(people),
            cities.Count, states));
        if (PapacySystem.ActiveCrusade(religion) is War crusade) Line(tooltip, "religion_row_crusade", crusade.data.name, Bad);
        List<Religion> children = ReligionDisplay.Children(religion);
        if (children.Count > 0)
            Line(tooltip, "religion_tip_children", string.Join("、", children.Select(child => child.name)), ReligionDisplay.Purple);
        tooltip.addBottomDescription(string.Format(LM.Get("religion_tip_footer"), ReligionDisplay.ExclusiveName(religion),
            ReligionDisplay.MissionName(religion), religion.getAge()));
    }
}

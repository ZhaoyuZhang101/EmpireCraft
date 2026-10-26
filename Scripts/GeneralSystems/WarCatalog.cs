using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战争类型总表(示意图 Tasks/ui-mockups/war-wide.png ⑥~⑧)：战争窗口、提示框、战争名都从这里取——
//   · 类型：有开战目标的按目标(夺城、侵略、争法理、争夺资源、吞并、收复、臣服、羁縻、继承、扶立、改宗、通商、赔款)，
//     其余按战争类型细分(圣战分收复圣地/援救同教/讨伐异端/东征，革命分共和/现代，叛乱里的变法之乱、阶层起事、农民起义单列)；
//   · 标签：开战缘由(攘夷、帝国扩张、游牧扩张)、统一天下、世界大战、诸王之乱，只作标签，不改目标；
//   · 战争名：没被别的系统起过专名的(原版名、"某某帝国扩张战争"这类拼出来的)，按目标或类型重起，如"大梁夺洛阳之战"。
public static class WarCatalog
{
    public enum Category { Territory, Demand, Religion, Steppe, Throne, Rebellion, Revolution, Other }

    // 类型键：war_kind_<键> 是名称，war_outcome_<键> 是拿到目标后的结局
    public static string KindKey(War war)
    {
        if (war?.data == null) return "none";
        WarExtension.WarExtraData data = war.GetOrCreate();
        EmpireWarType type = war.GetEmpireWarType();
        if (type == EmpireWarType.神圣)
            return data.crusade_kind switch
            {
                PapacySystem.CrusadeHolyLand => "crusade_holy", PapacySystem.CrusadeRescue => "crusade_rescue",
                PapacySystem.CrusadeHeretic => "crusade_heretic", PapacySystem.CrusadeInfidel => "crusade_infidel",
                _ => "holy"
            };
        if (type == EmpireWarType.劫掠) return "raid";
        if (RepublicSystem.IsRevolutionWar(war)) return data.republic_modern_revolution ? "modern_revolution" : "republic_revolution";
        if (data.institution_reform_empire_id > 0) return "reform_revolt";
        if (data.institution_social_rebellion_empire_id > 0) return "class_revolt";
        if (data.peasant_land_rebellion) return "peasant_revolt";
        WarGoal goal = WarGoalSystem.Get(war);
        if (goal != WarGoal.None && !TypeNamed(type)) return "goal_" + goal;
        return type == EmpireWarType.None ? "plain" : "type_" + type;
    }

    // 这几种按类型显示(目标只是战后怎么分)；其余有目标的按目标
    private static bool TypeNamed(EmpireWarType type) => type is EmpireWarType.伐不臣 or EmpireWarType.迫使朝贡 or
        EmpireWarType.游牧扩张 or EmpireWarType.逼冕 or EmpireWarType.去帝号 or EmpireWarType.征伐不服;

    public static string Kind(War war) => LM.Get("war_kind_" + KindKey(war));

    public static Category CategoryOf(War war)
    {
        string key = KindKey(war);
        if (key.StartsWith("crusade") || key == "holy") return Category.Religion;
        if (key is "raid" or "type_草场之争" or "type_游牧扩张") return Category.Steppe;
        if (key is "republic_revolution" or "modern_revolution" or "reform_revolt" or "class_revolt") return Category.Revolution;
        if (key is "peasant_revolt" or "type_派系叛乱" or "type_地方叛乱" or "type_地方独立" or "type_民族叛乱" or "type_宗教叛乱" or
            "type_清君侧") return Category.Rebellion;
        if (key is "goal_夺城" or "goal_侵略" or "goal_争法理" or "goal_争夺资源" or "goal_吞并" or "goal_收复" or "goal_割地" or
            "type_索取法理") return Category.Territory;
        if (key.StartsWith("goal_")) return Category.Demand;
        if (key.StartsWith("type_")) return Category.Throne;
        return Category.Other;
    }

    // 类型的颜色(与示意图一致)
    public static string Color(War war) => KindKey(war) switch
    {
        "goal_夺城" or "goal_争夺资源" or "raid" or "type_草场之争" or "peasant_revolt" => "#E9A85B",
        "goal_侵略" or "goal_吞并" or "type_去帝号" or "type_藩王索取皇位" => "#FF6666",
        "goal_争法理" or "goal_通商" or "type_附庸独立" or "type_地方独立" => "#7FD8EA",
        "goal_收复" => "#65D66E",
        _ => CategoryOf(war) switch
        {
            Category.Religion => "#F3C34A",
            Category.Rebellion => "#FF6666",
            Category.Revolution or Category.Demand or Category.Throne => "#C9A7E8",
            _ => "#C8C2B4"
        }
    };

    // 标签：开战缘由、统一天下、世界大战、诸王之乱
    public static List<string> Tags(War war)
    {
        var tags = new List<string>();
        if (war?.data == null) return tags;
        WarExtension.WarExtraData data = war.GetOrCreate();
        EmpireWarType type = war.GetEmpireWarType();
        if (type is EmpireWarType.攘夷 or EmpireWarType.帝国扩张) tags.Add(LM.Get("war_tag_" + type));
        else if (!string.IsNullOrEmpty(data.war_cause)) tags.Add(LM.Get("war_tag_" + data.war_cause));
        if (type == EmpireWarType.统一 || data.war_label == "unify") tags.Add(LM.Get("war_tag_unify"));
        if (data.war_label == "princes") tags.Add(LM.Get("war_tag_princes"));
        if (data.world_war) tags.Add(LM.Get("war_tag_world_war"));
        if (WarGoalSystem.Get(war) == WarGoal.侵略) tags.Add(LM.Get("war_tag_invasion"));
        return tags;
    }

    #region 目标与结局

    // 目标说一句话：夺取洛阳、偃师；争河南法理里北燕的 4 城；吞并北燕全境 6 城……
    public static string GoalText(War war)
    {
        if (war?.data == null) return "";
        WarExtension.WarExtraData data = war.GetOrCreate();
        WarGoal goal = WarGoalSystem.Get(war);
        string defender = war.main_defender?.GetKingdomName() ?? "";
        List<City> targets = Cities(data);
        string names = string.Join(LM.Get("list_separator"), targets.Take(3).Select(city => city.GetCityName())) +
                       (targets.Count > 3 ? LM.Get("list_etc") : "");
        string main = WarObjective.Main(data)?.GetCityName() ?? "";
        switch (goal)
        {
            case WarGoal.夺城:
            case WarGoal.侵略:
                return string.Format(LM.Get("war_goal_text_cities"), names);
            case WarGoal.争法理:
                return string.Format(LM.Get("war_goal_text_title"), TitleName(data), defender, targets.Count);
            case WarGoal.争夺资源:
                return string.Format(LM.Get("war_goal_text_resource"), main, ResourceName(data.goal_resource));
            case WarGoal.吞并:
                return string.Format(LM.Get("war_goal_text_annex"), defender, targets.Count);
            case WarGoal.收复:
                return string.Format(LM.Get("war_goal_text_reclaim"), names);
            case WarGoal.继承:
                return string.Format(LM.Get("war_goal_text_succession"), defender);
            case WarGoal.None:
                return Kind(war);
            default:
                return string.Format(LM.Get("war_goal_text_" + goal), defender, main);
        }
    }

    // 拿到目标会怎样(提示框、窗口的"达成"一行)
    public static string Outcome(War war)
    {
        string key = KindKey(war);
        string text = LM.Get("war_outcome_" + key);
        return text == "war_outcome_" + key ? "" : text;
    }

    public static List<City> Cities(WarExtension.WarExtraData data) =>
        (data?.goal_city_ids ?? new List<long>()).Select(id => World.world?.cities?.get(id))
        .Where(city => city?.data != null && !city.isRekt()).ToList();

    public static string TitleName(WarExtension.WarExtraData data)
    {
        KingdomTitle title = data?.goal_title_id >= 0 ? ModClass.KINGDOM_TITLE_MANAGER?.get(data.goal_title_id) : null;
        return title?.data?.name ?? "";
    }

    public static string ResourceName(string resource) =>
        string.IsNullOrEmpty(resource) ? "" :
        resource == WarObjective.PastureResource ? LM.Get("war_resource_pasture") :
        string.Format(LM.Get("war_resource_mine"), MineralResourceSystem.NameOf(resource));

    #endregion

    #region 战争名

    // 按目标或类型重起战争名；别的系统起过专名的不动(force：窗口里换了目标时照样重起按目标起的名)
    public static void ApplyName(War war, bool force = false)
    {
        if (war?.data == null || war.hasEnded()) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        if (data.world_war) return;
        EmpireWarType type = war.GetEmpireWarType();
        if (!force && !DefaultName(war, type)) return;
        string name = Compose(war);
        if (string.IsNullOrWhiteSpace(name)) return;
        war.data.name = name;
        data.goal_named = true;
    }

    // 原版起的名、或 SetEmpireWarType 拼出来的"某某帝国扩张战争"
    private static bool DefaultName(War war, EmpireWarType type)
    {
        string name = war.data.name ?? "";
        if (type == EmpireWarType.None) return true;
        string raw = type.ToString();
        return name.EndsWith(raw + "战争", StringComparison.Ordinal) || name.EndsWith(raw, StringComparison.Ordinal);
    }

    private static string Compose(War war)
    {
        WarExtension.WarExtraData data = war.GetOrCreate();
        string attacker = Display(war.main_attacker), defender = Display(war.main_defender);
        string main = WarObjective.Main(data)?.GetCityName() ?? war.main_defender?.capital?.GetCityName() ?? "";
        string key = "war_name_" + KindKey(war);
        string template = LM.Get(key);
        if (template == key) return null;
        return string.Format(template, attacker, defender, main, TitleName(data), ResourceName(data.goal_resource));
    }

    private static string Display(Kingdom kingdom)
    {
        if (kingdom?.data == null) return "";
        string name = EmpireCraftNamePlateLibrary.GetDisplayName(kingdom);
        return string.IsNullOrWhiteSpace(name) ? kingdom.GetKingdomName() : name;
    }

    #endregion
}

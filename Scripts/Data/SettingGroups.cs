using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 模组设置、世界法则各归哪一类(用户 2026-11-16 看着服务器核心的窗口："模组设置和世界法则我希望你分类")。
// 游戏里的设置窗口自己有分类(ModSettingsWindow.Categories)，可那是界面代码，服务器核心不编它；核心的窗口和手机管理页
// 要分类就照这里的。模组设置的类名沿用设置窗口那几个(mod_settings_cat_*)，各项归类与它一致——那边挪了项，这里跟着挪；
// 世界法则游戏里只有一个大组，这里另分(law_group_*)。没列到的归"其他"，排在最后
public static class SettingGroups
{
    public const string ModOther = "mod_settings_cat_other", LawOther = "law_group_other";

    private static readonly (string key, string[] ids)[] ModGroups =
    {
        ("mod_settings_cat_display", new[]
        {
            "pure_map_toggle", "smooth_borders_toggle", "performance_stable_framerate_toggle", "performance_high_population_toggle",
            "performance_adaptive_throughput_toggle", "performance_hidden_visuals_toggle", "performance_nameplate_overlap_toggle",
            "performance_diagnostics_toggle", "save_freeze_toggle_Text"
        }),
        ("mod_settings_cat_empire", new[]
        {
            "realm_name_rules_toggle", "multi_capital_toggle", "kingdom_institution_reforms_toggle", "hre_dissolution_toggle",
            "Feudal_Union_Realm_Limit_Text", "Same_Culture_Empire_Limit_Text"
        }),
        ("mod_settings_cat_title", new[]
        {
            "Title_Freeze_Toggle", "Title_Been_Destroy_Text", "Title_Max_Cities_Text", "Title_Share_Limit_Text", "Core_Share_Limit_Text",
            "Tianxia_Share_Limit_Text"
        }),
        ("mod_settings_cat_war", new[] { "war_end_year_Text", "Warlord_Era_Years_Text" }),
        ("mod_settings_cat_city", new[]
        {
            "City_Max_Zones_Text", "wilderness_toggle", "wilderness_jungle_developed_toggle", "Settlement_Base_Gold_Text",
            "Settlement_Per_City_Gold_Text", "Settlement_Reserve_Gold_Text", "Settlement_Wood_Text", "Settlement_Stone_Text",
            "Settlement_Min_Households_Text", "Settlement_Target_Households_Text", "Settlement_Retain_Households_Text",
            "Settlement_Migration_Percent_Text", "Settlement_Food_Years_Text", "Settlement_Preparation_Months_Text",
            "Settlement_Cooldown_Months_Text"
        }),
        ("mod_settings_cat_terminal", new[]
        {
            "terminal_mode_toggle", "Terminal_Speed_Text", "Terminal_Port_Text", "Terminal_Max_Seats_Text", "terminal_lobby_toggle",
            "terminal_room_open_toggle", "Terminal_Room_Name_Text", "Terminal_Lobby_Line_Text", "Terminal_Lobby_Url_Text",
            "Terminal_Public_Url_Text", "terminal_lightweight_host_toggle", "terminal_core_host_toggle"
        })
    };

    private static readonly (string key, string[] ids)[] LawGroups =
    {
        ("law_group_diplomacy", new[]
        {
            "world_law_diplomacy", "empirecraft_law_ban_diplomacy", "empirecraft_law_ban_ai_war", "empirecraft_law_ban_ai_alliance",
            "empirecraft_law_ban_ai_vassal", "empirecraft_law_ban_ai_tribute", "empirecraft_law_ban_ai_marriage", "empirecraft_law_ban_ai_pact",
            "empirecraft_law_ban_vanilla_alliance", "empirecraft_law_switch_occupy_mode", "empirecraft_law_corps_general"
        }),
        ("law_group_realm", new[]
        {
            "world_law_rebellions", "empirecraft_law_ban_rebellion", "empirecraft_law_ban_empire", "empirecraft_law_dejure_first",
            "empirecraft_law_fixed_de_jure_culture", "empirecraft_law_enforce_laws", "empirecraft_law_surname_dynasty",
            "empirecraft_law_combine_houses"
        }),
        ("law_group_city", new[]
        {
            "empirecraft_law_prevent_city_destroy", "empirecraft_law_prevent_building_destroy", "empirecraft_law_fixed_settlements",
            "empirecraft_law_fixed_city_borders"
        }),
        ("law_group_people", new[]
        {
            "empirecraft_law_realistic_age", "empirecraft_law_allow_harem", "empirecraft_law_allow_social", "empirecraft_law_no_commoners",
            "empirecraft_law_allow_skeleton", "world_law_civ_limit_population_100", "world_law_civ_limit_population_50",
            "world_law_civ_limit_population_20"
        }),
        ("law_group_tech", new[]
        {
            "empirecraft_law_tech_tree", "empirecraft_law_premodern", "empirecraft_law_tech_calendar", "empirecraft_law_transmigrant_arrival"
        }),
        ("law_group_display", new[] { "empirecraft_law_simplify_nameplates" })
    };

    private static readonly Dictionary<string, (string key, int order)> ModIndex = Index(ModGroups), LawIndex = Index(LawGroups);

    private static Dictionary<string, (string key, int order)> Index((string key, string[] ids)[] groups)
    {
        var index = new Dictionary<string, (string key, int order)>(StringComparer.Ordinal);
        for (int i = 0; i < groups.Length; i++)
            foreach (string id in groups[i].ids) index[id] = (groups[i].key, i);
        return index;
    }

    // 这项模组设置归哪一类：类名的语言键，order 是类的先后(小的在前)
    public static string OfSetting(string id, out int order)
    {
        if (id != null && ModIndex.TryGetValue(id, out (string key, int order) found))
        {
            order = found.order;
            return found.key;
        }
        order = ModGroups.Length;
        return ModOther;
    }

    public static string OfLaw(string id, out int order)
    {
        if (id != null && LawIndex.TryGetValue(id, out (string key, int order) found))
        {
            order = found.order;
            return found.key;
        }
        order = LawGroups.Length;
        return LawOther;
    }
}

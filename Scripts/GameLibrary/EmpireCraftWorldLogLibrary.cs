using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftWorldLogLibrary
{
    public static WorldLogAsset history_new_emperor;
    public static WorldLogAsset history_new_emperor_west;
    public static WorldLogAsset empire_powerful_minister_aquire_title;
    public static WorldLogAsset powerful_minister_aquire_empire_war;
    public static WorldLogAsset restore_historcial_empire;
    public static WorldLogAsset empire_pianan;
    public static WorldLogAsset empire_crashed;
    public static WorldLogAsset empire_new_clan;
    public static WorldLogAsset empire_war;
    public static WorldLogAsset empire_enfeoff_log;
    public static WorldLogAsset become_new_empire_log;
    public static WorldLogAsset become_new_empire_west_log;
    public static WorldLogAsset minister_try_aqcuire_empire_log;
    public static WorldLogAsset minister_aqcuire_empire_log;
    public static WorldLogAsset king_take_title_log;
    public static WorldLogAsset kingdom_change_main_title_log;
    public static WorldLogAsset change_city_name_log;
    public static WorldLogAsset change_kingdom_name_log;
    public static WorldLogAsset king_create_title_log;
    public static WorldLogAsset city_add_to_title_log;
    public static WorldLogAsset religion_war_transfer_log;
    public static WorldLogAsset become_kingdom_log;
    public static WorldLogAsset royal_king_become_emperor_log;
    public static WorldLogAsset empire_take_back_title_log;
    public static WorldLogAsset combine_kingdom_log;
    public static WorldLogAsset destroy_title_log;
    public static WorldLogAsset history_kingdom_attack_for_title;
    public static WorldLogAsset history_kingdom_change_capital_to_title;
    public static WorldLogAsset history_empire_get_back_land;
    public static WorldLogAsset history_kingdom_join_empire;
    public static WorldLogAsset emperor_posthumous_name;
    public static WorldLogAsset province_change_to_kingdom_log;
    public static WorldLogAsset minister_select_emperor_log;
    public static WorldLogAsset new_jingshi_log;
    public static WorldLogAsset cotrolled_country_log;
    public static WorldLogAsset become_greater_general;
    public static WorldLogAsset join_empire_war_log;
    public static WorldLogAsset join_religion_war_log;
    public static WorldLogAsset join_rebellion_war_log;
    public static WorldLogAsset officer_build_specific_clan;
    public static WorldLogAsset king_choose_heir_log;
    public static WorldLogAsset 官员品级调动;
    public static WorldLogAsset 成为朝贡国;
    public static WorldLogAsset 邀请入派系;
    public static WorldLogAsset 追加罪行;
    public static WorldLogAsset officer_join_faction;
    public static WorldLogAsset officer_become_faction_leader;
    public static WorldLogAsset empire_law_arrest_log;
    public static WorldLogAsset empire_law_enforced_log;
    public static WorldLogAsset temporary_faction_prepare_log;
    public static WorldLogAsset temporary_faction_prepare_no_target_log;
    public static WorldLogAsset temporary_faction_prepare_crime_log;
    public static WorldLogAsset temporary_faction_success_log;
    public static WorldLogAsset temporary_faction_success_no_target_log;
    public static WorldLogAsset temporary_faction_success_crime_log;
    public static WorldLogAsset temporary_faction_failed_log;
    public static WorldLogAsset temporary_faction_failed_no_target_log;
    public static WorldLogAsset temporary_faction_failed_crime_log;
    public static WorldLogAsset temporary_faction_official_fall_log;
    public static WorldLogAsset temporary_faction_reduce_feudatory_log;
    public static WorldLogAsset temporary_faction_revoke_war_right_log;
    public static WorldLogAsset temporary_faction_revoke_military_region_log;
    public static WorldLogAsset temporary_faction_raise_tax_log;
    public static WorldLogAsset occupation_capture_event_log;

    public static void init()
    {
        WorldLogLibrary wl = AssetManager.world_log_library;
        history_new_emperor = wl.add(new WorldLogAsset
        {
            id = nameof(history_new_emperor),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$place$", 2);
                wl.updateText(ref pText, pMessage, "$year_name$", 3);
            }
        });
        empire_take_back_title_log = wl.add(new WorldLogAsset
        {
            id = nameof(empire_take_back_title_log),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$crime$", 2);
                wl.updateText(ref pText, pMessage, "$title$", 3);
            }
        });
        combine_kingdom_log = wl.add(new WorldLogAsset
        {
            id = nameof(combine_kingdom_log),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
            }
        });
        history_new_emperor_west = wl.add(new WorldLogAsset
        {
            id = nameof(history_new_emperor_west),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$place$", 2);
            }
        });
        royal_king_become_emperor_log = wl.add(new WorldLogAsset
        {
            id = nameof(royal_king_become_emperor_log),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$empire$", 2);
            }
        });
        join_religion_war_log = wl.add(new WorldLogAsset
        {
            id = nameof(join_religion_war_log),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$religion$", 2);
            }
        });
        join_religion_war_log = wl.add(new WorldLogAsset
        {
            id = nameof(join_rebellion_war_log),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$joiner$", 1);
                wl.updateText(ref pText, pMessage, "$beginner$", 2);
            }
        });
        religion_war_transfer_log = wl.add(new WorldLogAsset
        {
            id = nameof(religion_war_transfer_log),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$city$", 1);
                wl.updateText(ref pText, pMessage, "$religion$", 2);
            }
        });
        官员品级调动 = wl.add(new WorldLogAsset
        {
            id = nameof(官员品级调动),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$officeName$", 2);
                wl.updateText(ref pText, pMessage, "$level$", 3);
            }
        });
        成为朝贡国 = wl.add(new WorldLogAsset
        {
            id = nameof(成为朝贡国),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$empire$", 2);
            }
        });
        邀请入派系 = wl.add(new WorldLogAsset
        {
            id = nameof(邀请入派系),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$invitor$", 1);
                wl.updateText(ref pText, pMessage, "$target$", 2);
                wl.updateText(ref pText, pMessage, "$faction$", 3);
            }
        });
        追加罪行 = wl.add(new WorldLogAsset
        {
            id = nameof(追加罪行),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$victim$", 2);
                wl.updateText(ref pText, pMessage, "$crime$", 3);
            }
        });
        officer_build_specific_clan = wl.add(new WorldLogAsset
        {
            id = nameof(officer_build_specific_clan),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$clan_name$", 2);
            }
        });
        temporary_faction_prepare_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_prepare_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
                wl.updateText(ref pText, pMessage, "$claim$", 2);
                wl.updateText(ref pText, pMessage, "$target$", 3);
            }
        });
        temporary_faction_prepare_no_target_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_prepare_no_target_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
                wl.updateText(ref pText, pMessage, "$claim$", 2);
            }
        });
        temporary_faction_prepare_crime_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_prepare_crime_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$target$", 1);
                wl.updateText(ref pText, pMessage, "$crime$", 2);
                wl.updateText(ref pText, pMessage, "$claim$", 3);
            }
        });
        temporary_faction_success_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_success_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
                wl.updateText(ref pText, pMessage, "$claim$", 2);
                wl.updateText(ref pText, pMessage, "$target$", 3);
            }
        });
        temporary_faction_success_no_target_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_success_no_target_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
                wl.updateText(ref pText, pMessage, "$claim$", 2);
            }
        });
        temporary_faction_success_crime_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_success_crime_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$target$", 1);
                wl.updateText(ref pText, pMessage, "$crime$", 2);
                wl.updateText(ref pText, pMessage, "$claim$", 3);
            }
        });
        temporary_faction_failed_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_failed_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
                wl.updateText(ref pText, pMessage, "$claim$", 2);
                wl.updateText(ref pText, pMessage, "$target$", 3);
            }
        });
        temporary_faction_failed_no_target_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_failed_no_target_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
                wl.updateText(ref pText, pMessage, "$claim$", 2);
            }
        });
        temporary_faction_failed_crime_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_failed_crime_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$target$", 1);
                wl.updateText(ref pText, pMessage, "$crime$", 2);
                wl.updateText(ref pText, pMessage, "$claim$", 3);
            }
        });
        temporary_faction_official_fall_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_official_fall_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$crime$", 2);
            }
        });
        temporary_faction_reduce_feudatory_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_reduce_feudatory_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
            }
        });
        temporary_faction_revoke_war_right_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_revoke_war_right_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
            }
        });
        temporary_faction_revoke_military_region_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_revoke_military_region_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
            }
        });
        temporary_faction_raise_tax_log = wl.add(new WorldLogAsset
        {
            id = nameof(temporary_faction_raise_tax_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
            }
        });
        occupation_capture_event_log = wl.add(new WorldLogAsset
        {
            id = nameof(occupation_capture_event_log),
            group = "wars",
            path_icon = "iconWar",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$capturer$", 1);
                wl.updateText(ref pText, pMessage, "$victim$", 2);
                wl.updateText(ref pText, pMessage, "$result$", 3);
            }
        });
        become_greater_general = wl.add(new WorldLogAsset
        {
            id = nameof(become_greater_general),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$empire$", 2);
            }
        });
        king_choose_heir_log = wl.add(new WorldLogAsset
        {
            id = nameof(king_choose_heir_log),
            group = "kings",
            path_icon = "ui/Icons/iconKings",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$relation$", 2);
                wl.updateText(ref pText, pMessage, "$actor$", 3);
            }
        });
        change_kingdom_name_log = wl.add(new WorldLogAsset
        {
            id = nameof(change_kingdom_name_log),
            group = "kings",
            path_icon = "ui/Icons/iconKings",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$pre$", 2);
                wl.updateText(ref pText, pMessage, "$after$", 3);
            }
        });
        change_city_name_log = wl.add(new WorldLogAsset
        {
            id = nameof(change_city_name_log),
            group = "kings",
            path_icon = "ui/Icons/iconKings",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$pre$", 2);
                wl.updateText(ref pText, pMessage, "$after$", 3);
            }
        });
        join_empire_war_log = wl.add(new WorldLogAsset
        {
            id = nameof(join_empire_war_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$empire$", 2);
            }
        });
        history_empire_get_back_land = wl.add(new WorldLogAsset
        {
            id = nameof(history_empire_get_back_land),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$place$", 2);
            }
        });
        cotrolled_country_log = wl.add(new WorldLogAsset
        {
            id = nameof(cotrolled_country_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$empire$", 2);
            }
        });
        province_change_to_kingdom_log = wl.add(new WorldLogAsset
        {
            id = nameof(province_change_to_kingdom_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$province$", 1);
                wl.updateText(ref pText, pMessage, "$province_level$", 2);
            }
        });
        minister_select_emperor_log = wl.add(new WorldLogAsset
        {
            id = nameof(minister_select_emperor_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
                wl.updateText(ref pText, pMessage, "$actor$", 2);
            }
        });
        officer_join_faction = wl.add(new WorldLogAsset
        {
            id = nameof(officer_join_faction),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$office$", 1);
                wl.updateText(ref pText, pMessage, "$actor$", 2);
                wl.updateText(ref pText, pMessage, "$faction$", 3);
            }
        });
        officer_become_faction_leader = wl.add(new WorldLogAsset
        {
            id = nameof(officer_become_faction_leader),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$faction$", 2);
            }
        });
        empire_law_enforced_log = wl.add(new WorldLogAsset
        {
            id = nameof(empire_law_enforced_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$crime$", 2);
                wl.updateText(ref pText, pMessage, "$punishments$", 3);
            }
        });
        empire_law_arrest_log = wl.add(new WorldLogAsset
        {
            id = nameof(empire_law_arrest_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$date$", 2);
                wl.updateText(ref pText, pMessage, "$crime$", 3);
            }
        });
        new_jingshi_log = wl.add(new WorldLogAsset
        {
            id = nameof(new_jingshi_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
                wl.updateText(ref pText, pMessage, "$actor$", 2);
            }
        });
        emperor_posthumous_name = wl.add(new WorldLogAsset
        {
            id = nameof(emperor_posthumous_name),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$actor2$", 2);
            }
        });
        become_kingdom_log = wl.add(new WorldLogAsset
        {
            id = nameof(become_kingdom_log),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$title$", 2);
                wl.updateText(ref pText, pMessage, "$kingdom$", 3);
            }
        });
        empire_war = wl.add(new WorldLogAsset
        {
            id = nameof(empire_war),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
                wl.updateText(ref pText, pMessage, "$defencer$", 2);
            }
        });
        destroy_title_log = wl.add(new WorldLogAsset
        {
            id = nameof(destroy_title_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$king$", 1);
                wl.updateText(ref pText, pMessage, "$title$", 2);
            }
        });
        history_kingdom_change_capital_to_title = wl.add(new WorldLogAsset
        {
            id = nameof(history_kingdom_change_capital_to_title),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$title$", 2);
                wl.updateText(ref pText, pMessage, "$city$", 3);
            }
        });
        history_kingdom_join_empire = wl.add(new WorldLogAsset
        {
            id = nameof(history_kingdom_join_empire),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$empire$", 2);
            }
        });
        king_take_title_log = wl.add(new WorldLogAsset
        {
            id = nameof(king_take_title_log),
            group = "emperors",
            path_icon = "ministerAcquireEmpire",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$king$", 2);
                wl.updateText(ref pText, pMessage, "$title_name$", 3);
            }
        });
        kingdom_change_main_title_log = wl.add(new WorldLogAsset
        {
            id = nameof(kingdom_change_main_title_log),
            group = "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$new_title$", 2);
            }
        });
        king_create_title_log = wl.add(new WorldLogAsset
        {
            id = nameof(king_create_title_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$king$", 2);
                wl.updateText(ref pText, pMessage, "$title_name$", 3);
            }
        });
        city_add_to_title_log = wl.add(new WorldLogAsset
        {
            id = nameof(city_add_to_title_log),
            group = "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$city$", 1);
                wl.updateText(ref pText, pMessage, "$title_name$", 2);
            }
        });
        empire_enfeoff_log = wl.add(new WorldLogAsset
        {
            id = nameof(empire_enfeoff_log),
            group = "emperors",
            path_icon = "SplitAllUnderHeaven.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
            }
        });
        history_kingdom_attack_for_title = wl.add(new WorldLogAsset
        {
            id = nameof(history_kingdom_attack_for_title),
            group = "emperors",
            path_icon = "TitleAcquire.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$attacker$", 1);
                wl.updateText(ref pText, pMessage, "$defender$", 2);
                wl.updateText(ref pText, pMessage, "$title$", 3);
            }
        });
        become_new_empire_log = wl.add(new WorldLogAsset
        {
            id = nameof(become_new_empire_log),
            group = "emperors",
            path_icon = "ChineseCrown.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$emperor$", 1);
                wl.updateText(ref pText, pMessage, "$kingdom_name$", 2);
            }
        });
        become_new_empire_west_log = wl.add(new WorldLogAsset
        {
            id = nameof(become_new_empire_west_log),
            group = "emperors",
            path_icon = "ChineseCrown.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$emperor$", 1);
                wl.updateText(ref pText, pMessage, "$kingdom_name$", 2);
            }
        });
        minister_try_aqcuire_empire_log = wl.add(new WorldLogAsset
        {
            id = nameof(minister_try_aqcuire_empire_log),
            group = "emperors",
            path_icon = "ministerAcquireEmpire.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$title$", 1);
                wl.updateText(ref pText, pMessage, "$minister$", 2);
                wl.updateText(ref pText, pMessage, "$empire_name$", 3);
            }
        });
        powerful_minister_aquire_empire_war = wl.add(new WorldLogAsset
        {
            id = nameof(powerful_minister_aquire_empire_war),
            group = "emperors",
            path_icon = "ministerAcquireEmpire.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$title$", 1);
                wl.updateText(ref pText, pMessage, "$minister$", 2);
                wl.updateText(ref pText, pMessage, "$kingdom_name$", 3);
            }
        });
        minister_aqcuire_empire_log = wl.add(new WorldLogAsset
        {
            id = nameof(minister_aqcuire_empire_log),
            group = "emperors",
            path_icon = "ChineseCrown.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$title$", 1);
                wl.updateText(ref pText, pMessage, "$minister$", 2);
                wl.updateText(ref pText, pMessage, "$new_empire_name$", 3);
            }
        });
        empire_powerful_minister_aquire_title = wl.add(new WorldLogAsset
        {
            id = nameof(empire_powerful_minister_aquire_title),
            group = "emperors",
            path_icon = "ministerAcquireTitle.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$minister$", 1);
                wl.updateText(ref pText, pMessage, "$empire$", 2);
                wl.updateText(ref pText, pMessage, "$title$", 3);
            }
        });
        restore_historcial_empire = wl.add(new WorldLogAsset
        {
            id = nameof(restore_historcial_empire),
            group = "emperors",
            path_icon = "ChineseCrown.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$clan$", 1);
                wl.updateText(ref pText, pMessage, "$empire$", 2);
            }
        });
    }

}

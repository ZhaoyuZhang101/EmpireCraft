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
    public static WorldLogAsset empire_powerful_minister_aquire_title;
    public static WorldLogAsset powerful_minister_aquire_empire_war;
    public static WorldLogAsset restore_historcial_empire;
    public static WorldLogAsset empire_pianan;
    public static WorldLogAsset empire_crashed;
    public static WorldLogAsset empire_new_clan;
    public static WorldLogAsset empire_war;
    public static WorldLogAsset empire_enfeoff_log;
    public static WorldLogAsset become_new_empire_log;
    public static WorldLogAsset minister_try_aqcuire_empire_log;
    public static WorldLogAsset minister_aqcuire_empire_log;
    public static WorldLogAsset king_take_title_log;
    public static WorldLogAsset king_create_title_log;
    public static WorldLogAsset city_add_to_title_log;
    public static WorldLogAsset become_kingdom_log;
    public static WorldLogAsset destroy_title_log;
    public static WorldLogAsset history_kingdom_attack_for_title;
    public static WorldLogAsset history_kingdom_change_capital_to_title;
    public static WorldLogAsset history_empire_get_back_land;
    public static WorldLogAsset history_kingdom_join_empire;
    public static WorldLogAsset emperor_posthumous_name;
    public static WorldLogAsset province_change_to_kingdom_log;
    public static WorldLogAsset new_jingshi_log;
    public static WorldLogAsset cotrolled_country_log;
    public static WorldLogAsset become_greater_general;
    public static WorldLogAsset join_empire_war_log;
    public static WorldLogAsset officer_build_specific_clan;
    public static WorldLogAsset king_choose_heir_log;

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

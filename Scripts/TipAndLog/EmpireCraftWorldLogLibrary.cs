using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.TipAndLog;
public class EmpireCraftWorldLogLibrary
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
    public static WorldLogAsset ministor_try_aqcuire_empire_log;
    public static WorldLogAsset ministor_aqcuire_empire_log;
    public static WorldLogAsset king_take_title_log;
    public static WorldLogAsset king_create_title_log;
    public static WorldLogAsset city_add_to_title_log;
    public static WorldLogAsset become_kingdom_log;
    public static WorldLogAsset destroy_title_log;

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
        become_kingdom_log = wl.add(new WorldLogAsset
        {
            id = nameof(become_kingdom_log),
            group = "EmperorQuest",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$title$", 2);
                wl.updateText(ref pText, pMessage, "$kingdom$", 3);
            }
        });
        empire_war  = wl.add(new WorldLogAsset
        {
            id = nameof(empire_war),
            group= "emperors",
            path_icon = "crown2",
            color = Toolbox.color_log_warning,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
                wl.updateText(ref pText, pMessage, "$defencer$", 2);
            }
        });
        destroy_title_log = wl.add(new WorldLogAsset
        {
            id = nameof(destroy_title_log),
            group= "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$king$", 1);
                wl.updateText(ref pText, pMessage, "$title$", 2);
            }
        });
        king_take_title_log = wl.add(new WorldLogAsset
        {
            id = nameof(king_take_title_log),
            group= "emperors",
            path_icon = "MinistorAcquireEmpire",
            color = Toolbox.color_log_warning,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$king$", 2);
                wl.updateText(ref pText, pMessage, "$title_name$", 3);
            } 
        });
        king_create_title_log = wl.add(new WorldLogAsset
        {
            id = nameof(king_create_title_log),
            group= "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$kingdom$", 1);
                wl.updateText(ref pText, pMessage, "$king$", 2);
                wl.updateText(ref pText, pMessage, "$title_name$", 3);
            }
        });
        city_add_to_title_log = wl.add(new WorldLogAsset
        {
            id = nameof(city_add_to_title_log),
            group= "emperors",
            path_icon = "EmperorQuest",
            color = Toolbox.color_log_warning,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$city$", 1);
                wl.updateText(ref pText, pMessage, "$title_name$", 2);
            }
        });
        empire_enfeoff_log = wl.add(new WorldLogAsset
        {
            id = nameof(empire_enfeoff_log),
            group= "emperors",
            path_icon = "SplitAllUnderHeaven.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$empire$", 1);
            }
        });
        become_new_empire_log = wl.add(new WorldLogAsset
        {
            id = nameof(become_new_empire_log),
            group= "emperors",
            path_icon = "ChineseCrown.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$emperor$", 1);
                wl.updateText(ref pText, pMessage, "$kingdom_name$", 2);
            }
        });
        ministor_try_aqcuire_empire_log = wl.add(new WorldLogAsset
        {
            id = nameof(ministor_try_aqcuire_empire_log),
            group= "emperors",
            path_icon = "MinistorAcquireEmpire.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$title$", 1);
                wl.updateText(ref pText, pMessage, "$ministor$", 2);
                wl.updateText(ref pText, pMessage, "$empire_name$", 3);
            }
        });
        powerful_minister_aquire_empire_war = wl.add(new WorldLogAsset
        {
            id = nameof(powerful_minister_aquire_empire_war),
            group= "emperors",
            path_icon = "MinistorAcquireEmpire.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$title$", 1);
                wl.updateText(ref pText, pMessage, "$ministor$", 2);
                wl.updateText(ref pText, pMessage, "$kingdom_name$", 3);
            }
        });
        ministor_aqcuire_empire_log = wl.add(new WorldLogAsset
        {
            id = nameof(ministor_aqcuire_empire_log),
            group= "emperors",
            path_icon = "ChineseCrown.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$title$", 1);
                wl.updateText(ref pText, pMessage, "$ministor$", 2);
                wl.updateText(ref pText, pMessage, "$new_empire_name$", 3);
            }
        });
        empire_powerful_minister_aquire_title = wl.add(new WorldLogAsset
        {
            id = nameof(empire_powerful_minister_aquire_title),
            group= "emperors",
            path_icon = "MinistorAcquireTitle.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$ministor$", 1);
                wl.updateText(ref pText, pMessage, "$empire$", 2);
                wl.updateText(ref pText, pMessage, "$title$", 3);
            }
        });
        restore_historcial_empire = wl.add(new WorldLogAsset
        {
            id = nameof(restore_historcial_empire),
            group= "emperors",
            path_icon = "ChineseCrown.png",
            color = Toolbox.color_log_good,
            text_replacer = delegate(WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$clan$", 1);
                wl.updateText(ref pText, pMessage, "$empire$", 2);
            }
        });
    }

}

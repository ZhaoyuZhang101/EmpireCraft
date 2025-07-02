using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

internal static class CreateEmpireButton
{
    
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "create_empire",
            name = "create_empire",
            force_map_mode = MetaType.None,
            path_icon = "iconUnity",
            can_drag_map = true
        });
        GodPower power = powerLib.t;
        power.select_button_action = (PowerButtonClickAction)Delegate.Combine(power.select_button_action, new PowerButtonClickAction(selectKingdom));
        power.click_special_action = (PowerActionWithID)Delegate.Combine(power.click_special_action, new PowerActionWithID(clickKingdom));
    }

    public static bool clickKingdom(WorldTile pTile, string pPowerID)
    {
        City city = pTile.zone.city;
        if (city.isRekt())
        {
            return false;
        }
        Kingdom kingdom = city.kingdom;
        if (kingdom.isRekt())
        {
            return false;
        }
        if (kingdom.isNeutral())
        {
            return false;
        }
        if (Config.unity_A == null)
        {
            Config.unity_A = kingdom;
            ActionLibrary.showWhisperTip("kingdom_selected_first");
            return false;
        }
        if (Config.whisper_B == null && Config.unity_A == kingdom)
        {
            ActionLibrary.showWhisperTip("kingdom_cancelled");
            Config.unity_A = null;
            Config.unity_B = null;
            return false;
        }
        if (Config.unity_A.isInEmpire() && kingdom.isInEmpire() && Config.unity_A.GetEmpire() == kingdom.GetEmpire())
        {
            ActionLibrary.showWhisperTip("kingdom_cancelled");
            Config.unity_A = null;
            Config.unity_B = null;
            return false;
        }
        if (Config.unity_B == null)
        {
            Config.unity_B = kingdom;
        }
        if (Config.unity_B == Config.unity_A)
        {
            return false;
        }
        if (Config.unity_A.isInEmpire())
        {
            if (Config.unity_A.GetEmpire() == Config.unity_B.GetEmpire())
            {
                ActionLibrary.showWhisperTip("kingdom_cancelled");
                Config.unity_B = null;
                return false;
            }
            if (Config.unity_B.isInEmpire())
            {
                Config.unity_A.GetEmpire().leave(Config.unity_A, true);
            }
        }
        if (ModClass.EMPIRE_MANAGER.forceEmpire(Config.unity_A, Config.unity_B))
        {
            ActionLibrary.showWhisperTip("unity_new_empire");
        }
        else
        {
            ActionLibrary.showWhisperTip("unity_joined_empire");
        }
        Config.unity_A.affectKingByPowers();
        Config.unity_A = null;
        Config.unity_B = null;
        World.world.zone_calculator.dirtyAndClear();
        return true;
    }
 
    public static bool selectKingdom(string pPowerID)
    {
        WorldTip.showNow("empire_selected", true, "top", 3f, "#F3961F");
        Config.unity_A = null;
        Config.unity_B = null;
        return false;
    }

    public static void CreateEmpire(Kingdom kingdom, Kingdom JoinKingdom)
    {
        if (kingdom == null || JoinKingdom == null)
        {
            Debug.LogError("CreateEmpire: Kingdom is null");
            return;
        }
        Empire empire = ModClass.EMPIRE_MANAGER.newEmpire(kingdom);
        empire.join(JoinKingdom);
        
    }
}

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
            force_map_mode = MetaTypeExtension.Empire,
            path_icon = "iconUnity",
            can_drag_map = true
        });
        GodPower power = powerLib.t;
        power.select_button_action = (PowerButtonClickAction)Delegate.Combine(power.select_button_action, new PowerButtonClickAction(selectKingdom));
        power.click_special_action = (PowerActionWithID)Delegate.Combine(power.click_special_action, new PowerActionWithID(clickKingdom));
    }

    public static bool clickKingdom(WorldTile pTile, string pPowerID)
    {
        City city = pTile?.zone?.city;
        if (city == null || city.isRekt())
        {
            return false;
        }
        Kingdom kingdom = city.kingdom;
        if (kingdom == null || kingdom.isRekt())
        {
            return false;
        }
        if (kingdom.isNeutral())
        {
            return false;
        }
        if (Config.unity_A != null && Config.unity_A.isRekt())
        {
            Config.unity_A = null;
            Config.unity_B = null;
            ActionLibrary.showWhisperTip("kingdom_cancelled");
            return false;
        }
        if (Config.unity_A == null)
        {
            Config.unity_A = kingdom;
            ActionLibrary.showWhisperTip("kingdom_selected_first");
            return false;
        }
        if (Config.unity_A == kingdom)
        {
            ActionLibrary.showWhisperTip("kingdom_cancelled");
            Config.unity_A = null;
            Config.unity_B = null;
            return false;
        }
        if (Config.unity_A.IsInEmpire() && kingdom.IsInEmpire() && Config.unity_A.GetEmpire() == kingdom.GetEmpire())
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
        // Let forceEmpire choose and transfer the actual members. Selecting two existing
        // empires must not first detach (and recolor) the first selection before validation.
        Kingdom first = Config.unity_A, second = Config.unity_B;
        bool created = ModClass.EMPIRE_MANAGER.forceEmpire(first, second);
        bool joined = first.IsInEmpire() && first.GetEmpire() == second.GetEmpire();
        if (created)
        {
            ActionLibrary.showWhisperTip("unity_new_empire");
        }
        else if (joined)
        {
            ActionLibrary.showWhisperTip("unity_joined_empire");
        }
        else
        {
            // 按实际结果提示：加入没有成功就不能说"已加入"
            ActionLibrary.showWhisperTip("unity_join_failed");
        }
        if (created || joined) first.affectKingByPowers();
        Config.unity_A = null;
        Config.unity_B = null;
        if (created || joined) World.world.zone_calculator.dirtyAndClear();
        return created || joined;
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
        Empire empire = ModClass.EMPIRE_MANAGER.NewEmpire(kingdom);
        if (empire == null)
        {
            return;
        }
        empire.join(JoinKingdom, pForce: true);
        
    }
}

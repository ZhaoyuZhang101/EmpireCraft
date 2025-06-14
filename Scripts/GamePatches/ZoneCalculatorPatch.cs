using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

public class ZoneCalculatorPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(draw_kingdom_patch)).Patch(
            AccessTools.Method(typeof(ZoneCalculator), nameof(ZoneCalculator.drawZoneKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(draw_kingdom_patch))
        );
    }

    public static bool draw_kingdom_patch(ZoneCalculator __instance, TileZone pZone)
    {
        if (!PlayerConfig.dict["map_empire_layer"].boolVal)
        {
            return true;
        }
        Empire empire = pZone.city.kingdom.GetEmpire();
        if (empire == null) return true;

        bool pUp = ZoneCalculatorPatch.isBorderColor_Empire(pZone.zone_up, empire, true);
        bool pDown = ZoneCalculatorPatch.isBorderColor_Empire(pZone.zone_down, empire, false);
        bool pLeft = ZoneCalculatorPatch.isBorderColor_Empire(pZone.zone_left, empire, false);
        bool pRight = ZoneCalculatorPatch.isBorderColor_Empire(pZone.zone_right, empire, true);
        int num = -1;
        if (empire != null)
        {
            num = empire.GetHashCode();
        }
        int num2 = __instance.generateIdForDraw(__instance._mode_asset, num, pUp, pDown, pLeft, pRight);
        if (pZone.last_drawn_id == num2 && pZone.last_drawn_hashcode == num)
        {
            return false;
        }
        pZone.last_drawn_id = num2;
        pZone.last_drawn_hashcode = num;
        Color32 colorBorderInsideAlpha = Toolbox.color_clear;
        Color32 colorMain = Toolbox.color_clear;
        if (empire != null)
        {
            ColorAsset color = empire.empire.getColor();
            colorBorderInsideAlpha = color.getColorBorderInsideAlpha();
            colorMain = color.getColorMain2();
            if (__instance.shouldBeClearColor())
            {
                colorBorderInsideAlpha = __instance.color_clear;
            }
        }
        __instance.applyMetaColorsToZone(pZone, ref colorBorderInsideAlpha, ref colorMain, pUp, pDown, pLeft, pRight);
        return false;
    }

    public static bool isBorderColor_Empire(TileZone pZone, Empire pEmpire, bool pCheckFriendly = false)
    {
        if (pZone == null)
        {
            return true;
        }
        if (pZone.city == null) return true;
        if (pZone.city.kingdom == null) return true;
        Empire empireOnZone = pZone.city.kingdom.GetEmpire();
        return empireOnZone == null || empireOnZone != pEmpire;
    }
}

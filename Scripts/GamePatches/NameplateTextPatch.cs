using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;

public class NameplateTextPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(EmpireNamePlateTextShow)).Patch(AccessTools.Method(typeof(NameplateText), nameof(NameplateText.showTextKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(EmpireNamePlateTextShow)));
    }

    public static bool EmpireNamePlateTextShow(NameplateText __instance, Kingdom pMetaObject)
    {
        if (!PlayerConfig.dict["map_empire_layer"].boolVal)
        {
            return true;
        }
        if (!pMetaObject.isInEmpire())
        {
            return true;
        }
        if (pMetaObject.isEmpire())
        {
            Clan kingClan = pMetaObject.getKingClan();
            string text = pMetaObject.GetEmpire().name + "  " + pMetaObject.GetEmpire().countPopulation();
            __instance.setupMeta(pMetaObject.data, pMetaObject.kingdomColor);
            __instance.setText(text, pMetaObject.GetEmpire().GetEmpireCenter());
            __instance.showSpecies(pMetaObject.getSpriteIcon());
            __instance._show_banner_kingdom = true;
            __instance._banner_kingdoms.load(pMetaObject);

            if (kingClan != null)
            {
                __instance._show_banner_clan = true;
                __instance._banner_clan.load(kingClan);
            }

            __instance.nano_object = pMetaObject;
            return false;
        } else
        {
            __instance.setShowing(false);
            return false;
        }
    }
}

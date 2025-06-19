using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

public class NameplateTextPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(EmpireNamePlateTextShow)).Patch(AccessTools.Method(typeof(NameplateText), nameof(NameplateText.showTextKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(EmpireNamePlateTextShow)));
    }

    public static Transform AddIconToNamePlate(NameplateText plate, bool activate)
    {
        Transform exist = plate.gameObject.transform.Find("EmpireIcon");
        if (!exist) 
        {
            Sprite sprite = SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/crown2.png");
            GameObject CrownIcon = new GameObject("EmpireIcon", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var uiImage = CrownIcon.GetComponent<UnityEngine.UI.Image>();
            uiImage.sprite = sprite;
            uiImage.preserveAspect = true;
            uiImage.rectTransform.localScale = new Vector3(0.4f,0.4f,1);
            CrownIcon.transform.SetParent(plate.transform, false);
            uiImage.rectTransform.localPosition = new Vector3(0, 30, 0);
            CrownIcon.SetActive(activate);
            return CrownIcon.transform;
        }
        exist.gameObject.SetActive(activate);
        return exist;
    }

    public static bool EmpireNamePlateTextShow(NameplateText __instance, Kingdom pMetaObject)
    {
        AddIconToNamePlate(__instance, false);

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
            if (!__instance.showing)
                __instance.setShowing(true);
            AddIconToNamePlate(__instance, true);
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

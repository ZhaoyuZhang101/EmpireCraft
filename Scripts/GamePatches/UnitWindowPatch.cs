using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;
public class UnitWindowPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public Actor actor { get; set; }
    public void Initialize()
    {
        // UnitWindow类的补丁
        new Harmony(nameof(set_stats_rows)).Patch(
            AccessTools.Method(typeof(UnitWindow), nameof(UnitWindow.showStatsRows)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(set_stats_rows))
        );
        // UnitWindow类的补丁
        new Harmony(nameof(OnEnable)).Patch(
            AccessTools.Method(typeof(UnitWindow), nameof(UnitWindow.OnEnable)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(OnEnable))
        );
        // UnitWindow类的补丁
        new Harmony(nameof(applyInputName)).Patch(
            AccessTools.Method(typeof(UnitWindow), nameof(UnitWindow.applyInputName)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(applyInputName))
        );
        LogService.LogInfo("角色窗口补丁加载成功");
    }

    public static bool applyInputName(UnitWindow __instance, string pInput)
    {
        if (!string.IsNullOrEmpty(pInput) && __instance.actor != null && __instance.actor.data != null)
        {
            LogService.LogInfo("角色改名触发");
            __instance.actor.initializeActorName();
            Name name = __instance.actor.GetModName();
            bool invert = name.is_invert;
            string[] namePart;
            if (pInput.Contains("\u200A"))
            {
                namePart = pInput.Split('\u200A');
            } else
            {
                namePart = pInput.Split(' ');
            }
            string firstName;
            string familyName;
            if (namePart.Length <= 1)
            {
                familyName = "";
                if (namePart.Length == 1)
                {
                    firstName = namePart[0];
                } else
                {
                    firstName = "";
                }
            } else
            {
                if (invert)
                {
                    firstName = namePart[0].Split(' ').Last();
                    familyName = namePart[1].Split(' ').First();
                }
                else
                {
                    firstName = namePart[1].Split(' ').First();
                    familyName = namePart[0].Split(' ').Last();
                }
            }
            if (familyName != "")
            {
                LogService.LogInfo($"设置姓{familyName}");
                __instance.actor.SetFamilyName(familyName);
            }
            if (firstName != "")
            {
                LogService.LogInfo($"设置名{firstName}");
                __instance.actor.SetFirstName(firstName);
            }
            name.SetName(__instance.actor);
        }
        return false;
    }


    public static void OnEnable(UnitWindow __instance)
    { 
        //if (__instance.transform.Find("historyRecordButton")==null)
        //{
        //    SimpleButton simpleButton = GameObject.Instantiate(SimpleButton.Prefab, __instance.transform);
        //    simpleButton.Setup(OpenHistoryRecordWindow, SpriteTextureLoader.getSprite("EmperorQuest"));
        //}
    }

    public static void OpenHistoryRecordWindow()
    {
        LogService.LogInfo($"OpenHistoryRecordWindow");
    }

    private static void set_stats_rows(UnitWindow __instance)
    {
        Actor actor = __instance.actor;
        PeeragesLevel peeragesLevel = __instance.actor.GetPeeragesLevel();
        __instance.showStatRow("Peerages", LM.Get("default_" + peeragesLevel.ToString()), MetaType.Unit, -1L);
        if (__instance.actor.HasTitle()&&__instance.actor.isKing())
        {
            string value = __instance.actor.kingdom.HasMainTitle() ? __instance.actor.kingdom.GetMainTitle().data.name: __instance.actor.GetTitle();
            __instance.showStatRow("EmpireTitle", value, MetaType.None, -1L, pTooltipId: "all_titles",  pTooltipData: getTooltipAllTitles);
        }
        if (__instance.actor.isOfficer())
        {
            if(actor.city.kingdom.isInEmpire())
            {
                Empire empire = actor.city.kingdom.GetEmpire();
                OfficeIdentity identity = __instance.actor.GetIdentity(empire);
                string culture = "Huaxia";
                //if (ConfigData.speciesCulturePair.TryGetValue(actor.data.asset_id, out string val))
                //{
                //    culture = val;
                //}
                string EmpireMeritString = String.Join("_", culture, "meritlevel", identity.peerageType, identity.meritLevel);
                string EmpireHonoraryOfficialString = String.Join("_", culture, "honoraryofficial", identity.peerageType.ToString(), identity.honoraryOfficial);
                string EmpireOfficialLevelString = String.Join("_", culture,identity.officialLevel.ToString());
                if (identity.officialLevel==OfficialLevel.officiallevel_9)
                {
                    EmpireOfficialLevelString = actor.city.data.name + LM.Get(EmpireOfficialLevelString);
                }
                if (identity.officialLevel == OfficialLevel.officiallevel_8)
                {
                    long province_id = actor.GetProvinceID();
                    Province province = ModClass.PROVINCE_MANAGER.get(province_id);
                    if (province != null)
                    {
                        EmpireOfficialLevelString = province.data.name + LM.Get(EmpireOfficialLevelString);
                    }
                }
                if (identity.officialLevel == OfficialLevel.officiallevel_7)
                {
                    OfficeObject officeObject = empire.data.centerOffice.Divisions.Values.ToList().Find(a => a.actor_id == actor.getID());
                    if (officeObject != null)
                    {
                        EmpireOfficialLevelString = LM.Get(officeObject.pre) + LM.Get(EmpireOfficialLevelString);
                    }
                }
                __instance.showStatRow("EmpireMerit", LM.Get(EmpireMeritString));
                __instance.showStatRow("EmpireHonoraryOfficial", LM.Get(EmpireHonoraryOfficialString));
                __instance.showStatRow("OfficialLevel", LM.Get(EmpireOfficialLevelString));
            }

        }
    }

    public static TooltipData getTooltipAllTitles()
    {
        Actor actor = SelectedUnit.unit;
        return new TooltipData
        {
            tip_name = "all_titles",
            tip_description = "all_titles_description",
            actor = actor
        };
    }
}

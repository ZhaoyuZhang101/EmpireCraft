using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using EmpireCraft.Scripts.UI.Windows;
using HarmonyLib;
using NCMS.Utils;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
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
                __instance.actor.SetFamilyName(familyName);
            }
            if (firstName != "")
            {
                __instance.actor.SetFirstName(firstName);
            }
            name.SetName(__instance.actor);
        }
        return false;
    }


    public static void OnEnable(UnitWindow __instance)
    {
        Transform space = __instance.tabs.transform.Find("space (1)");
        if (space != null)
        {
            GameObject.Destroy(space.gameObject);
        }
        if (__instance.tabs._tabs.All(p => p.name != "specific_clan"))
        {
            SimpleWindowTab simpleWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            simpleWindowTab.Setup("specific_clan", __instance.scroll_window, action:(_) => ShowSpecificClan(__instance.actor), sprite:SpriteTextureLoader.getSprite("ui/specificClanIcon"));
        }
    }

    private static void ShowSpecificClan(Actor actor)
    {
        ScrollWindow.showWindow(nameof(SpecificClanWindow));
        LogService.LogInfo($"开启SpecificClanWindow");
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
                OfficeIdentity identity = __instance.actor.GetIdentity();
                if (empire.CoreKingdom.GetRegime().type == RegimeType.LvLing)
                {
                    string empireMeritString = String.Join("_", "Huaxia", "meritlevel", identity.peerageType, identity.meritLevel);
                    string empireHonoraryOfficialString = String.Join("_", "Huaxia", "honoraryofficial", identity.peerageType.ToString(), identity.honoraryOfficial);
                    __instance.showStatRow("EmpireMerit", LM.Get(empireMeritString));
                    __instance.showStatRow("EmpireHonoraryOfficial", LM.Get(empireHonoraryOfficialString));
                }
                string empireOfficialLevelString = String.Join("_",empire.CoreKingdom.GetRegime().type.ToString(), "empire", identity.officialLevel.ToString());
                if (actor.isCityLeader())
                {
                    empireOfficialLevelString = actor.city.data.name + LM.Get(empireOfficialLevelString);
                } else if (actor.isKing())
                {
                    empireOfficialLevelString = actor.kingdom.GetKingdomName() + LM.Get(empireOfficialLevelString);
                }
                else
                {
                    OfficeObject officeObject = empire.data.centerOffice.Divisions.ToList().Find(a => a.actor_id == actor.getID());
                    if (officeObject != null)
                    {
                        empireOfficialLevelString = LM.Get(officeObject.pre) + LM.Get(empireOfficialLevelString);
                    }
                }
                __instance.showStatRow("OfficialLevel", LM.Get(empireOfficialLevelString));
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

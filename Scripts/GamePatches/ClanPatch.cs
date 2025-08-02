using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;

namespace EmpireCraft.Scripts.GamePatches;
public class ClanPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(set_clan_name)).Patch(
            AccessTools.Method(typeof(Clan), nameof(Clan.newClan)),
            postfix: new HarmonyMethod(GetType(), nameof(set_clan_name))
        );
        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(Clan), nameof(Clan.Dispose)),
            postfix: new HarmonyMethod(GetType(), nameof(removeData))
        );
        LogService.LogInfo("氏族命名补丁加载成功");
    }
    public static void removeData(Clan __instance)
    {
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (empire.empire_clan == __instance)
            {
                empire.empire_clan = null;
                empire.data.original_royal_been_changed = true;
                empire.data.original_royal_been_changed_timestamp = World.world.getCurWorldTime();
            }
        }
        __instance.RemoveExtraData<Clan, ClanExtraData>();
    }

    public static void set_clan_name(Clan __instance, Actor pFounder, bool pAddDefaultTraits)
    {
        if (pFounder.GetModName().hasFamilyName(pFounder))
        {
            __instance.data.name = pFounder.GetModName().familyName+ "\u200A" + LM.Get("Clan");
            if (pFounder.hasFamily())
            {
                string clanName = __instance.GetClanName();
                string familyEnd = LM.Get("Family");
                if (pFounder.city != null)
                {
                    string cityName = pFounder.city.GetCityName();
                    pFounder.family.data.name = string.Join("\u200A", cityName, clanName, familyEnd);
                    OverallHelperFunc.SetFamilyCityPre(pFounder.family);
                }
                else
                {
                    if (!pFounder.family.HasBeenSetBefored())
                    {
                        pFounder.family.data.name = string.Join("\u200A", clanName, familyEnd);
                        OverallHelperFunc.SetFamilyCityPre(pFounder.family, false);
                    }
                }
            }
        } else
        {
            if (pFounder.hasCulture())
            {
                __instance.data.name = pFounder.culture.getOnomasticData(MetaType.Clan).generateName();
                pFounder.SetFamilyName(__instance.GetClanName());
                if (pFounder.hasFamily())
                {
                    string clanName = __instance.GetClanName();
                    string familyEnd = LM.Get("Family");
                    if (pFounder.city != null)
                    {
                        string cityName = pFounder.city.GetCityName();
                        pFounder.family.data.name = string.Join("\u200A", cityName, clanName, familyEnd);
                        OverallHelperFunc.SetFamilyCityPre(pFounder.family);
                    }
                    else
                    {
                        if (!pFounder.family.HasBeenSetBefored())
                        {
                            pFounder.family.data.name = string.Join("\u200A", clanName, familyEnd);
                            OverallHelperFunc.SetFamilyCityPre(pFounder.family, false);
                        }
                    }
                }
            }
        }
        if (pFounder.GetModName().has_whole_name(pFounder))
        {
            pFounder.GetModName().SetName(pFounder);
        }
    }
}

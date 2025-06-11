using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        LogService.LogInfo("氏族命名补丁加载成功");
    }

    public static void set_clan_name(Clan __instance, Actor pFounder, bool pAddDefaultTraits)
    {
        if (pFounder.data.name.Split(' ').Length > 1)
        {
            string familyName = pFounder.data.name.Split(' ')[0];
            __instance.data.name = familyName + " " + LM.Get("Clan");
        }
    }
}

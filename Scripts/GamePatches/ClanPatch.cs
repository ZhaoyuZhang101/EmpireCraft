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
        new Harmony(nameof(NewClan)).Patch(
            AccessTools.Method(typeof(Clan), nameof(Clan.newClan)),
            postfix: new HarmonyMethod(GetType(), nameof(NewClan))
        );
        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(Clan), nameof(Clan.Dispose)),
            postfix: new HarmonyMethod(GetType(), nameof(removeData))
        );
        LogService.LogInfo("氏族命名补丁加载成功");
    }
    public static void removeData(Clan __instance)
    {
        __instance.RemoveExtraData<Clan, ClanExtraData>();
    }

    public static void NewClan(Clan __instance, Actor pFounder, bool pAddDefaultTraits)
    {
        // todo: 新氏族创建时触发
    }
}

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
public class FamilyPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(NewFamily)).Patch(
            AccessTools.Method(typeof(Family), nameof(Family.newFamily)),
            postfix: new HarmonyMethod(GetType(), nameof(NewFamily))
        );
        LogService.LogInfo("家族命名补丁加载成功");
    }

    public static void NewFamily(Family __instance, Actor pActor1, Actor pActor2, WorldTile pTile)
    {
        // todo: 组建新家庭时触发
    }
}

using HarmonyLib;
using System;
using NeoModLoader.services;
using NeoModLoader.General;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;
public class LaunguagePatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(set_Language_name)).Patch(AccessTools.Method(typeof(Language), nameof(Language.newLanguage)),
        postfix: new HarmonyMethod(GetType(), nameof(set_Language_name)));
        LogService.LogInfo("语言命名模板加载成功");
    }

    private static void set_Language_name(Language __instance, Actor pActor)
    {
        __instance.data.name = pActor.kingdom.name.Split(' ')[0]+ LM.Get("Language") + pActor.city.name.Split(' ')[0] + LM.Get("Dialect");
    }
}

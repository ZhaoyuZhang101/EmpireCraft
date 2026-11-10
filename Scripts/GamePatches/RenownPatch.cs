using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 原版的名望来源压到 RenownSystem.VanillaShare(用户 2026-11-10 选"身份年入，原版压低")：杀人(newKillAction 按对方经验值)、
// 打仗胜负与议和(warWon / warLost / madePeace)、城主逐年 +1(City.updateAge)。记下方法前后的名望差，只留两成
// (见 RenownSystem.ScaleVanilla)；减名望的不管。名望改由身份年入(RenownSystem.Yearly)
public class RenownPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(RenownPatch));
        // newKillAction、City.updateAge 是按名字找的内部方法：游戏改版找不到就跳过这一处，不让整个模组初始化失败
        foreach (string name in new[] { nameof(Actor.warWon), nameof(Actor.warLost), nameof(Actor.madePeace), "newKillAction" })
        {
            var method = AccessTools.Method(typeof(Actor), name);
            if (method == null)
            {
                LogService.LogWarning($"[EmpireCraft][名望] 找不到 Actor.{name}，原版这一处的名望不压低");
                continue;
            }
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(RenownPatch), nameof(Before)),
                postfix: new HarmonyMethod(typeof(RenownPatch), nameof(After)));
        }
        var cityAge = AccessTools.Method(typeof(City), "updateAge");
        if (cityAge == null)
            LogService.LogWarning("[EmpireCraft][名望] 找不到 City.updateAge，城主逐年的名望不压低");
        else
            harmony.Patch(cityAge, prefix: new HarmonyMethod(typeof(RenownPatch), nameof(CityBefore)),
                postfix: new HarmonyMethod(typeof(RenownPatch), nameof(CityAfter)));
    }

    public static void Before(Actor __instance, out int __state) => __state = __instance?.data?.renown ?? 0;

    public static void After(Actor __instance, int __state) => RenownSystem.ScaleVanilla(__instance, __state);

    public static void CityBefore(City __instance, out (Actor leader, int renown) __state)
    {
        Actor leader = __instance != null && __instance.hasLeader() ? __instance.leader : null;
        __state = (leader, leader?.data?.renown ?? 0);
    }

    public static void CityAfter((Actor leader, int renown) __state)
    {
        if (__state.leader != null) RenownSystem.ScaleVanilla(__state.leader, __state.renown);
    }
}

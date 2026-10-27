using System;
using EmpireCraft.Scripts.UI.Components;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 返回时停在原来那页的挂点(见 WindowBackState)
public class WindowBackPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(WindowBackPatch));
        try
        {
            harmony.Patch(AccessTools.Method(typeof(WindowHistory), nameof(WindowHistory.clickBack)),
                prefix: new HarmonyMethod(typeof(WindowBackPatch), nameof(BackPrefix)),
                finalizer: new HarmonyMethod(typeof(WindowBackPatch), nameof(BackFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(WindowHistory), nameof(WindowHistory.addIntoHistory)),
                postfix: new HarmonyMethod(typeof(WindowBackPatch), nameof(AddedPostfix)));
            harmony.Patch(AccessTools.Method(typeof(WindowHistory), nameof(WindowHistory.clear)),
                postfix: new HarmonyMethod(typeof(WindowBackPatch), nameof(ClearPostfix)));
            harmony.Patch(AccessTools.Method(typeof(ScrollWindow), nameof(ScrollWindow.show)),
                prefix: new HarmonyMethod(typeof(WindowBackPatch), nameof(ShowPrefix)));
            harmony.Patch(AccessTools.Method(typeof(ScrollWindow), nameof(ScrollWindow.clickShowLeft)),
                prefix: new HarmonyMethod(typeof(WindowBackPatch), nameof(ShowLeftPrefix)),
                postfix: new HarmonyMethod(typeof(WindowBackPatch), nameof(ShowLeftPostfix)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 返回保持页面的补丁未生效: {exception.Message}");
        }
    }

    public static void BackPrefix() => WindowBackState.Returning = true;

    public static Exception BackFinalizer(Exception __exception)
    {
        WindowBackState.Returning = false;
        return __exception;
    }

    public static void AddedPostfix() => Guard(WindowBackState.OnAdded);

    public static void ClearPostfix() => WindowBackState.Clear();

    public static void ShowPrefix(ScrollWindow __instance) => Guard(() =>
    {
        WindowBackState.OnLeaving();
        SimpleWindowTab.Forget(__instance);
    });

    public static void ShowLeftPrefix(ScrollWindow __instance) => Guard(() => WindowBackState.BeforeReturn(__instance));

    public static void ShowLeftPostfix(ScrollWindow __instance) => Guard(() => WindowBackState.AfterReturn(__instance));

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 返回保持页面出错: {exception.Message}");
        }
    }
}

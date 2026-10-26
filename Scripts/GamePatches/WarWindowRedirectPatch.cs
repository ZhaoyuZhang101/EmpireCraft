using System;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.UI.Windows;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 把原版"战争"窗口改道到模组的战争窗口(同 ReligionWindowRedirectPatch)：战争列表、战争徽标、各窗口里的战争链接
// 最终都走 ScrollWindow.showWindow("war")，在这里改开 WarInfoWindow；战争窗口左栏的"原版战争窗口"按钮临时放行一次
public class WarWindowRedirectPatch : GamePatch
{
    public const string VanillaWarWindowId = "war";
    private static bool _allowVanillaOnce;

    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        MethodInfo prefix = AccessTools.Method(typeof(WarWindowRedirectPatch), nameof(RedirectPrefix));
        var harmony = new Harmony(nameof(WarWindowRedirectPatch));
        int patched = 0;
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ScrollWindow))
                     .Where(method => method.Name == nameof(ScrollWindow.showWindow) && method.IsStatic &&
                                      method.GetParameters().FirstOrDefault()?.ParameterType == typeof(string)))
        {
            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            patched++;
        }
        if (patched == 0) LogService.LogWarning("[EmpireCraft] 未找到 ScrollWindow.showWindow，战争窗口改道未生效。");
    }

    public static void OpenVanilla(War war)
    {
        if (war?.data != null) SelectedMetas.selected_war = war;
        _allowVanillaOnce = true;
        try
        {
            ScrollWindow.showWindow(VanillaWarWindowId);
        }
        finally
        {
            _allowVanillaOnce = false;
        }
    }

    public static bool RedirectPrefix(string __0)
    {
        if (!string.Equals(__0, VanillaWarWindowId, StringComparison.Ordinal) || _allowVanillaOnce) return true;
        try
        {
            War war = SelectedMetas.selected_war;
            if (war?.data == null || EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(war)) return true;
            WarInfoWindow.Open(war);
            return false;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 打开模组战争窗口失败，改用原版窗口: {exception.Message}");
            return true;
        }
    }
}

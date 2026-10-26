using System;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.UI.Windows;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 把原版"宗教"窗口统一改道到模组的宗教窗口(同 CultureWindowRedirectPatch)：图层地块、铭牌、人物/王国窗口里的宗教链接
// 最终都走 ScrollWindow.showWindow("religion")，在这里改开 ReligionInfoWindow；宗教窗口左栏的"原版宗教窗口"按钮临时放行一次
public class ReligionWindowRedirectPatch : GamePatch
{
    public const string VanillaReligionWindowId = "religion";
    private static bool _allowVanillaOnce;

    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        MethodInfo prefix = AccessTools.Method(typeof(ReligionWindowRedirectPatch), nameof(RedirectPrefix));
        var harmony = new Harmony(nameof(ReligionWindowRedirectPatch));
        int patched = 0;
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ScrollWindow))
                     .Where(method => method.Name == nameof(ScrollWindow.showWindow) && method.IsStatic &&
                                      method.GetParameters().FirstOrDefault()?.ParameterType == typeof(string)))
        {
            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            patched++;
        }
        if (patched == 0) LogService.LogWarning("[EmpireCraft] 未找到 ScrollWindow.showWindow，宗教窗口改道未生效。");
    }

    public static void OpenVanilla(Religion religion)
    {
        if (religion?.data != null) SelectedMetas.selected_religion = religion;
        _allowVanillaOnce = true;
        try
        {
            ScrollWindow.showWindow(VanillaReligionWindowId);
        }
        finally
        {
            _allowVanillaOnce = false;
        }
    }

    public static bool RedirectPrefix(string __0)
    {
        if (!string.Equals(__0, VanillaReligionWindowId, StringComparison.Ordinal) || _allowVanillaOnce) return true;
        try
        {
            Religion religion = SelectedMetas.selected_religion;
            if (religion?.data == null || religion.isRekt() ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(religion)) return true;
            ReligionInfoWindow.Open(religion);
            return false;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 打开模组宗教窗口失败，改用原版窗口: {exception.Message}");
            return true;
        }
    }
}

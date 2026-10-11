using System;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Windows;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 把原版"文化"窗口统一改道到模组的文化窗口。
//
// 不去逐个接管铭牌点击、地块点击、人物/王国窗口里的文化链接等各种入口，而是拦截它们最终都会走到的
// ScrollWindow.showWindow("culture")：只要当前选中的原版文化能对应到一个模组文化，就改为打开
// CultureInfoWindow。模组文化窗口里的"原版文化窗口"按钮会临时放行一次，走回原版窗口。
public class CultureWindowRedirectPatch : GamePatch
{
    public const string VanillaCultureWindowId = "culture";
    private static bool _allowVanillaOnce;

    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        // showWindow 可能有多个重载：凡是第一个参数是窗口 id 字符串的都挂上同一个前缀
        MethodInfo prefix = AccessTools.Method(typeof(CultureWindowRedirectPatch), nameof(RedirectPrefix));
        var harmony = new Harmony(nameof(CultureWindowRedirectPatch));
        int patched = 0;
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ScrollWindow))
                     .Where(method => method.Name == nameof(ScrollWindow.showWindow) && method.IsStatic &&
                                      method.GetParameters().FirstOrDefault()?.ParameterType == typeof(string)))
        {
            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            patched++;
        }
        if (patched == 0) LogService.LogWarning("[EmpireCraft] 未找到 ScrollWindow.showWindow，文化窗口改道未生效。");
    }

    // 模组文化窗口里的"原版文化窗口"按钮：选中对应的原版文化对象并放行一次
    public static void OpenVanilla(string culture)
    {
        Culture native = CultureService.GetNativeCultureObject(culture);
        if (native != null) SetSelectedCulture(native);
        _allowVanillaOnce = true;
        try
        {
            ScrollWindow.showWindow(VanillaCultureWindowId);
        }
        finally
        {
            _allowVanillaOnce = false;
        }
    }

    // 原版"当前选中的文化"。用反射读写，避免游戏版本字段名不同时整个模组编译失败
    private static Culture GetSelectedCulture()
    {
        Traverse selected = Traverse.Create(typeof(SelectedMetas));
        if (selected.Field("selected_culture").FieldExists()) return selected.Field("selected_culture").GetValue<Culture>();
        if (selected.Property("selected_culture").PropertyExists())
            return selected.Property("selected_culture").GetValue<Culture>();
        return null;
    }

    private static void SetSelectedCulture(Culture culture)
    {
        Traverse selected = Traverse.Create(typeof(SelectedMetas));
        if (selected.Field("selected_culture").FieldExists()) selected.Field("selected_culture").SetValue(culture);
        else if (selected.Property("selected_culture").PropertyExists())
            selected.Property("selected_culture").SetValue(culture);
    }

    public static bool RedirectPrefix(string __0)
    {
        if (!string.Equals(__0, VanillaCultureWindowId, StringComparison.Ordinal)) return true;
        if (_allowVanillaOnce) return true;
        try
        {
            string culture = CulturePatch.GetInjectedCultureName(GetSelectedCulture());
            if (!CultureService.IsValidCulture(culture)) return true;
            CultureInfoWindow.Open(culture);
            return false;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 打开模组文化窗口失败，改用原版窗口: {exception.Message}");
            return true;
        }
    }
}

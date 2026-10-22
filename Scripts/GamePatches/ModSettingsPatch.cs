using System;
using EmpireCraft.Scripts.UI.Windows;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 加载器模组列表里点本模组的「设置」时，改开按用途分类的模组设置窗口(ModSettingsWindow)；别的模组照旧用加载器的窗口。
// 加载器的窗口类按名字找：手机版加载器没有这个类或改了名字时什么都不改，仍用加载器自己的窗口
public class ModSettingsPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        Type window = AccessTools.TypeByName("NeoModLoader.ui.ModConfigureWindow");
        var show = window == null ? null : AccessTools.Method(window, "ShowWindow", new[] { typeof(ModConfig) });
        if (show == null) return;
        new Harmony(nameof(ModSettingsPatch)).Patch(show,
            prefix: new HarmonyMethod(typeof(ModSettingsPatch), nameof(BeforeShowWindow)));
    }

    // 参数按位置取(各版加载器的参数名不一定一样)
    public static bool BeforeShowWindow(object[] __args)
    {
        if (__args == null || __args.Length == 0 || __args[0] == null || !ReferenceEquals(__args[0], ModClass.modConfig))
            return true;
        try
        {
            return !ModSettingsWindow.Open();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][设置] 打开设置窗口失败，改用加载器的窗口: {exception.Message}");
            return true;
        }
    }
}

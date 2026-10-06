using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 原版 string.Localize() 查词前先把键转成"小写+下划线"(Underscore)，比如"发现的知识"提示里
// trait_empire_ideology_ReligiousDemocracy 会被查成 trait_empire_ideology_religious_democracy。
// 模组的特质等资源用驼峰 id(empire_ideology_ReligiousDemocracy、founderRuler……)，语言文件也按原样写键，
// 转换后的键查不到时退回原始键。
public class LocalizeKeyPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(LocalizeKeyPatch)).Patch(
            AccessTools.Method(typeof(StringExtension), nameof(StringExtension.Localize)),
            postfix: new HarmonyMethod(typeof(LocalizeKeyPatch), nameof(FallbackToRawKey)));
    }

    private static void FallbackToRawKey(string pString, ref string __result)
    {
        if (string.IsNullOrEmpty(pString)) return;
        string underscored = pString.Underscore();
        if (underscored == pString || LocalizedTextManager.stringExists(underscored) ||
            !LocalizedTextManager.stringExists(pString)) return;
        __result = LocalizedTextManager.getText(pString);
    }
}

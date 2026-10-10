using System;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 原版 StatsRowsContainer 用 ui/Icons/；NML 注册的模组图片用 ui/icons/。
// 原版 SpriteTextureLoader 的缓存区分大小写，不能靠 Resources 的宽松匹配找到模组图片。
public class SpriteResourcePathPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize() => new Harmony(nameof(SpriteResourcePathPatch)).Patch(
        AccessTools.Method(typeof(SpriteTextureLoader), nameof(SpriteTextureLoader.getSprite)),
        postfix: new HarmonyMethod(typeof(SpriteResourcePathPatch), nameof(ResolveIconAlias)));

    private static void ResolveIconAlias(string pPath, ref Sprite __result)
    {
        if (__result != null || string.IsNullOrEmpty(pPath) ||
            !pPath.StartsWith("ui/Icons/", StringComparison.Ordinal)) return;
        __result = SpriteTextureLoader.getSprite("ui/icons/" + pPath.Substring("ui/Icons/".Length));
    }
}

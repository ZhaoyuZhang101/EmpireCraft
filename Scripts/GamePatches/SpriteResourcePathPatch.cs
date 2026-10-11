using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 原版 StatsRowsContainer 用 ui/Icons/；NML 注册的模组图片用 ui/icons/。
// 原版 SpriteTextureLoader 的缓存区分大小写，不能靠 Resources 的宽松匹配找到模组图片。
public class SpriteResourcePathPatch : GamePatch
{
    private static Dictionary<string, Sprite> _traitSprites;
    private static readonly Dictionary<string, Sprite> Restored = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ReportedMissing = new(StringComparer.Ordinal);
    public ModDeclare declare { get; set; }
    public void Initialize() => new Harmony(nameof(SpriteResourcePathPatch)).Patch(
        AccessTools.Method(typeof(SpriteTextureLoader), nameof(SpriteTextureLoader.getSprite)),
        postfix: new HarmonyMethod(typeof(SpriteResourcePathPatch), nameof(ResolveIconAlias)));

    private static void ResolveIconAlias(string pPath, ref Sprite __result)
    {
        if (__result != null || string.IsNullOrEmpty(pPath) ||
            !pPath.StartsWith("ui/icons/", StringComparison.OrdinalIgnoreCase)) return;
        if (Restored.TryGetValue(pPath, out Sprite cached) && cached != null)
        {
            __result = cached;
            return;
        }
        // 直接重读 Resources，避免提前预载的 null 永久留在 SpriteTextureLoader 缓存中。
        string tail = pPath.Substring("ui/icons/".Length);
        __result = Resources.Load<Sprite>("ui/Icons/" + tail) ?? Resources.Load<Sprite>("ui/icons/" + tail);
        if (__result != null) { Restored[pPath] = __result; return; }
        if (!tail.StartsWith("actor_traits/", StringComparison.OrdinalIgnoreCase)) return;

        // 部分版本的原版图标已加载在图集内，旧资源路径无法独立 Load。
        // 按原名找同一张图，一次建索引，不能每个特质都遍历所有 Unity 对象。
        if (_traitSprites == null)
        {
            _traitSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            foreach (Sprite sprite in Resources.LoadAll<Sprite>("ui/Icons/actor_traits"))
                if (sprite != null) _traitSprites[sprite.name] = sprite;
            foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite != null && sprite.name.StartsWith("icon", StringComparison.OrdinalIgnoreCase) &&
                    !_traitSprites.ContainsKey(sprite.name)) _traitSprites[sprite.name] = sprite;
        }
        _traitSprites.TryGetValue(Path.GetFileNameWithoutExtension(tail), out Sprite restored);
        __result = restored;
        if (__result != null) Restored[pPath] = __result;
        if (__result == null && ReportedMissing.Add(pPath))
            NeoModLoader.services.LogService.LogWarning($"特质图标缺失，未找到同名原版资源: {pPath}");
    }
}

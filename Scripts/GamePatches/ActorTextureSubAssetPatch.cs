using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;
public class ActorTextureSubAssetPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    private static readonly HashSet<string> ReportedAnimationRepairs = new HashSet<string>();

    public void Initialize()
    {
        new Harmony(nameof(getUnitTexturePath)).Patch(AccessTools.Method(typeof(ActorTextureSubAsset), nameof(ActorTextureSubAsset.getUnitTexturePath)),
            prefix: new HarmonyMethod(GetType(), nameof(getUnitTexturePath)));
        new Harmony(nameof(RepairAnimationContainer)).Patch(
            AccessTools.Method(typeof(Actor), "checkAnimationContainer"),
            postfix: new HarmonyMethod(GetType(), nameof(RepairAnimationContainer)));
        new Harmony(nameof(CalculateMainSpriteFinalizer)).Patch(
            AccessTools.Method(typeof(Actor), nameof(Actor.calculateMainSprite)),
            finalizer: new HarmonyMethod(GetType(), nameof(CalculateMainSpriteFinalizer)));
    }

    public static bool getUnitTexturePath(ActorTextureSubAsset __instance, Actor pActor, ref string __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pActor)) return true;
        if (__instance == null || pActor == null) return true;
        Subspecies subspecies = pActor.subspecies;
        if (pActor.isEgg())
        {
            // 部分（尤其是旧存档里的）Actor 在这一步 subspecies 还没有被赋值——
            // ActorExtension.cs 里判断"是否骷髅"时也专门对 a.subspecies == null
            // 做了兼容，说明这确实是会真实出现的情况，不是理论上的边界条件。
            // 原版这里大概率有自己的空值兜底，但我们的补丁是 prefix 完全接管
            // （return false），一旦替上来就得自己保证不空引用，否则会在
            // MapBox.preloadRenderedSprites 批量预计算贴图时直接崩溃，导致
            // 旧存档整个进不去地图。没有 subspecies 就退回主贴图，等下一次
            // 该 Actor 的 subspecies 被正常赋值后贴图会自然刷新回正确的蛋贴图。
            __result = subspecies != null ? subspecies.egg_sprite_path : __instance.texture_path_main;
            return false;
        }
        if (pActor.isBaby())
        {
            __result = __instance.texture_path_baby;
            return false;
        }
        string result = __instance.texture_path_main;
        ProfessionAsset profession_asset = pActor.profession_asset;
        if (profession_asset == null || profession_asset.profession_id == UnitProfession.Nothing)
        {
            __result = result;
            return false;
        }
        if (!__instance.has_advanced_textures)
        {
            __result = result;
            return false;
        }
        switch (profession_asset.profession_id)
        {
            case UnitProfession.Warrior:
                {
                    string text = __instance.texture_path_warrior;
                    if (pActor.hasSubspecies())
                    {
                        text = pActor.subspecies.getSkinWarrior();
                    }
                    // 同上：subspecies 可能为 null（旧存档等情况），这里原来没有
                    // 判空就直接访问 has_mutation_reskin，是另一处会导致
                    // preloadRenderedSprites 崩溃的空引用。
                    if (subspecies != null && subspecies.has_mutation_reskin && pActor.asset?.skin_warrior != null)
                    {
                        List<string> skin_warrior = subspecies.mutation_skin_asset.skin_warrior;
                        int index = Toolbox.loopIndex(pActor.asset.skin_warrior.IndexOf(text), skin_warrior.Count);
                        text = skin_warrior[index];
                    }
                    __result = __instance.texture_path_base + text;
                    return false;
                }
            case UnitProfession.King:
                __result = __instance.texture_path_king;
                return false;
            case UnitProfession.Leader:
                __result = __instance.texture_path_leader;
                return false;
            case UnitProfessionExtension.minister:
                // 模组没有独立 minister/officer 动画资源，使用真实存在的长官贴图。
                __result = FirstPath(__instance.texture_path_leader, __instance.texture_path_main);
                return false;
            case UnitProfessionExtension.Emperor:
                __result = FirstPath(__instance.texture_path_king, __instance.texture_path_main);
                return false;
            case UnitProfessionExtension.General:
                __result = FirstPath(__instance.texture_path_warrior, __instance.texture_path_main);
                return false;
            case UnitProfessionExtension.Officer:
                __result = FirstPath(__instance.texture_path_leader, __instance.texture_path_main);
                return false;
            default:
                __result = __instance.getTextureSkinBasedOnSex(pActor);
                return false;
        }
    }

    /// <summary>
    /// 旧档可能已经缓存了由失效贴图路径生成的空动画容器。原版随后直接读取
    /// idle/walking/swimming.frames，会在加载末尾的 preloadRenderedSprites 崩溃。
    /// 这里在原版创建容器后，改用现存的标准职业路径重建并补齐缺失动画。
    /// </summary>
    public static void RepairAnimationContainer(Actor __instance)
    {
        if (__instance?.asset == null || HasRenderableAnimations(__instance.animation_container)) return;
        ActorTextureSubAsset texture = __instance.asset.texture_asset;
        if (texture == null) return;

        List<string> fallbackPaths = new List<string>();
        AddPath(fallbackPaths, GetProfessionFallbackPath(texture, __instance));
        try
        {
            AddPath(fallbackPaths, texture.getTextureSkinBasedOnSex(__instance));
        }
        catch
        {
            // Some old actors do not yet have all sex/subspecies data during preload.
        }
        AddPath(fallbackPaths, texture.texture_path_main);
        AddPath(fallbackPaths, texture.texture_path_leader);
        AddPath(fallbackPaths, texture.texture_path_king);
        AddPath(fallbackPaths, texture.texture_path_warrior);
        AddPath(fallbackPaths, texture.texture_path_baby);

        foreach (string path in fallbackPaths)
        {
            AnimationContainerUnit container;
            try
            {
                container = ActorAnimationLoader.getAnimationContainer(path, __instance.asset,
                    __instance.subspecies?.egg_asset, __instance.subspecies?.mutation_skin_asset);
            }
            catch
            {
                continue;
            }
            if (!NormalizeAnimationContainer(container)) continue;
            __instance.animation_container = container;
            ReportRepair(__instance, path);
            return;
        }

        // Last chance for malformed third-party containers that loaded individual sprites but did
        // not build ActorAnimation objects.
        if (NormalizeAnimationContainer(__instance.animation_container))
            ReportRepair(__instance, "cached sprite container");
    }

    public static Exception CalculateMainSpriteFinalizer(Actor __instance, ref Sprite __result,
        Exception __exception)
    {
        if (__exception == null) return null;
        if (!(__exception is NullReferenceException) || __instance?.asset == null) return __exception;

        RepairAnimationContainer(__instance);
        ActorAnimation fallback = GetFirstAnimation(__instance.animation_container);
        Sprite sprite = fallback?.frames?.FirstOrDefault(candidate => candidate != null);
        if (sprite == null)
        {
            sprite = ActorTextureSubAsset.all_preloaded_sprites_units?
                .FirstOrDefault(candidate => candidate != null);
        }
        if (sprite == null) return __exception;

        __result = sprite;
        ReportRepair(__instance, "preloaded sprite fallback");
        return null;
    }

    private static string GetProfessionFallbackPath(ActorTextureSubAsset texture, Actor actor)
    {
        UnitProfession profession = actor.profession_asset?.profession_id ?? UnitProfession.Nothing;
        switch (profession)
        {
            case UnitProfession.King:
            case UnitProfessionExtension.Emperor:
                return FirstPath(texture.texture_path_king, texture.texture_path_main);
            case UnitProfession.Leader:
            case UnitProfessionExtension.minister:
            case UnitProfessionExtension.Officer:
                return FirstPath(texture.texture_path_leader, texture.texture_path_main);
            case UnitProfession.Warrior:
            case UnitProfessionExtension.General:
                return FirstPath(texture.texture_path_warrior, texture.texture_path_main);
            default:
                return texture.texture_path_main;
        }
    }

    private static bool HasRenderableAnimations(AnimationContainerUnit container)
    {
        return HasFrames(container?.idle) && HasFrames(container.walking) &&
               (!container.has_swimming || HasFrames(container.swimming));
    }

    private static bool NormalizeAnimationContainer(AnimationContainerUnit container)
    {
        if (container == null) return false;
        CompactFrames(container.idle);
        CompactFrames(container.walking);
        CompactFrames(container.swimming);
        ActorAnimation fallback = GetFirstAnimation(container);
        if (fallback == null && container.sprites != null)
        {
            Sprite[] sprites = container.sprites.Values.Where(sprite => sprite != null).Distinct().ToArray();
            if (sprites.Length > 0) fallback = new ActorAnimation { frames = sprites };
        }
        if (fallback == null) return false;

        if (!HasFrames(container.idle)) container.idle = fallback;
        if (!HasFrames(container.walking)) container.walking = container.idle;
        if (!HasFrames(container.swimming))
        {
            container.swimming = container.walking;
            container.has_swimming = false;
        }
        container.has_idle = true;
        container.has_walking = true;
        return true;
    }

    private static ActorAnimation GetFirstAnimation(AnimationContainerUnit container)
    {
        if (container == null) return null;
        if (HasFrames(container.idle)) return container.idle;
        if (HasFrames(container.walking)) return container.walking;
        return HasFrames(container.swimming) ? container.swimming : null;
    }

    private static bool HasFrames(ActorAnimation animation)
    {
        Sprite[] frames = animation?.frames;
        if (frames == null) return false;
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null) return true;
        }
        return false;
    }

    private static void CompactFrames(ActorAnimation animation)
    {
        Sprite[] frames = animation?.frames;
        if (frames == null || frames.Length == 0) return;
        int validCount = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null) validCount++;
        }
        if (validCount == 0 || validCount == frames.Length) return;
        Sprite[] compacted = new Sprite[validCount];
        int targetIndex = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null) compacted[targetIndex++] = frames[i];
        }
        animation.frames = compacted;
    }

    private static string FirstPath(string preferred, string fallback)
    {
        return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
    }

    private static void AddPath(List<string> paths, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !paths.Contains(path)) paths.Add(path);
    }

    private static void ReportRepair(Actor actor, string fallback)
    {
        string profession = actor?.profession_asset?.profession_id.ToString() ?? "none";
        string key = $"{actor?.asset?.id}|{profession}|{fallback}";
        if (!ReportedAnimationRepairs.Add(key)) return;
        LogService.LogWarning($"[EmpireCraft] Repaired actor animation: asset={actor?.asset?.id}, " +
                              $"profession={profession}, fallback={fallback}");
    }
}

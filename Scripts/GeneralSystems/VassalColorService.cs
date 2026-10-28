using System.Collections.Generic;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 外部附属国(封建附庸、殖民地；朝贡国见下)：底色往宗主色靠一半(用户 2026-10-27 嫌"朝贡没换色"，选"底色混入宗主色"：
// 以前只把边框换成宗主色，平滑国界下边框只有一格宽，远看看不出)，边框用宗主色，旗帜照旧是自己的：
//   原版地图用 color_main 填色、color_main_2 描外边框(见 ZoneCalculator.drawZoneMeta)，
//   所以给附属国一个"自己与宗主各半的 color_main + 宗主的 color_main_2 + 自己的旗帜"的合成颜色。
//   合成颜色只放在运行时缓存 _cached_color 里，存档里的 color_id 仍是附属国自己的颜色；
//   读档或被原版改色后由月度同步(FeudalVassalService.Sync)重新套上，独立时 KingdomColorService.Restore 清掉。
// 朝贡国底色整个用帝国的颜色(用户 2026-10-29)，退出朝贡照样由 Restore 换回自己的颜色。
public static class VassalColorService
{
    private static readonly Dictionary<string, ColorAsset> Composites = new();

    public static void Apply(Kingdom subject, Kingdom lord)
    {
        if (subject?.data == null || subject.isRekt() || lord?.data == null || lord.isRekt() || subject == lord ||
            AncientWarfareCompatibility.Owns(subject)) return;
        var colors = subject.getColorLibrary()?.list;
        if (colors == null || colors.Count == 0) return;
        // 旧版把附庸整个染成宗主色：先换回自己原来的颜色
        int own = subject.data.color_id;
        int original = subject.GetOrCreate().feudal_independent_color_id;
        ColorAsset lordColor = lord.getColor();
        if (lordColor == null) return;
        if ((own < 0 || own >= colors.Count || colors[own] == lordColor) && original >= 0 && original < colors.Count &&
            colors[original] != null && colors[original] != lordColor)
        {
            subject._cached_color = null;
            subject.updateColor(colors[original]);
            own = subject.data.color_id;
        }
        if (own < 0 || own >= colors.Count || colors[own] == null) return;
        // 朝贡国底色整个换成帝国的颜色(用户 2026-10-29："朝贡还是直接改为和帝国同色吧，退出朝贡才恢复")；封建附庸、殖民地仍各半
        bool tributary = subject.HasTakenAlliance() && subject.GetTakenAllianceEmpire()?.CoreKingdom == lord;
        ColorAsset composite = Composite(colors[own], lordColor, tributary ? 1f : LordBlend);
        if (subject._cached_color == composite) return;
        subject._cached_color = composite;
        World.world?.zone_calculator?.dirtyAndClear();
    }

    // 不再从属：去掉合成颜色，回到自己存档里的颜色
    public static void Clear(Kingdom subject)
    {
        if (subject?.data == null || subject._cached_color == null || !IsComposite(subject._cached_color)) return;
        subject._cached_color = null;
        World.world?.zone_calculator?.dirtyAndClear();
    }

    public static bool IsComposite(ColorAsset color) => color != null && Composites.ContainsValue(color);

    // 原版不少入口会重置国家颜色缓存(generateColor、读档、改色)，附庸描边会悄悄掉回原色，
    // 等每月同步才恢复。这里每两秒轻量重申一次(国家只有几十个，挂在世界扫描轮转上)
    private static float _nextReassert;

    public static void TickReassert()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextReassert || World.world?.kingdoms == null) return;
        _nextReassert = now + 2f;
        foreach (Kingdom subject in World.world.kingdoms)
        {
            if (subject?.data == null || subject.isRekt()) continue;
            Kingdom lord = FeudalVassalService.GetOverlord(subject);
            if (lord == null && subject.HasTakenAlliance()) lord = subject.GetTakenAllianceEmpire()?.CoreKingdom;
            lord ??= ColonySystem.MetropoleOf(subject);
            if (lord == null || lord.isRekt()) continue;
            // 已套着合成色的也再套一遍(Apply 里同一合成色直接返回)：附庸改为朝贡、朝贡改为附庸时深浅要跟着换
            Apply(subject, lord);
        }
    }

    // 边框直接用宗主的主色(次色对比常常不明显)；宗主若本身也是合成颜色，主色仍是它自己的底色
    // 底色往宗主色靠多少
    private const float LordBlend = 0.5f;

    private static ColorAsset Composite(ColorAsset own, ColorAsset lord, float blend)
    {
        string key = own.color_main + "|" + lord.color_main + "|" + blend.ToString("0.##");
        if (Composites.TryGetValue(key, out ColorAsset cached)) return cached;
        var color = new ColorAsset
        {
            id = "vassal_" + key,
            color_main = Blend(own.color_main, lord.color_main, blend),
            color_main_2 = lord.color_main,
            color_banner = own.color_banner,
            color_text = own.color_text,
            index_id = ColorAsset._create_last_index_id++
        };
        color.initColor();
        Composites[key] = color;
        return color;
    }

    // 两种色值(#RRGGBB，可不带 #)按比例混合；读不出就用自己的
    private static string Blend(string own, string lord, float t)
    {
        if (!TryParse(own, out UnityEngine.Color a) || !TryParse(lord, out UnityEngine.Color b)) return own;
        string mixed = UnityEngine.ColorUtility.ToHtmlStringRGB(UnityEngine.Color.Lerp(a, b, t));
        return own.StartsWith("#") ? "#" + mixed : mixed;
    }

    private static bool TryParse(string hex, out UnityEngine.Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        return UnityEngine.ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out color);
    }
}

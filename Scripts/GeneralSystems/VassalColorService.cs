using System.Collections.Generic;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 外部附属国(封建附庸、朝贡国)保留自己的底色与旗帜，只把边框换成宗主的颜色：
//   原版地图用 color_main 填色、color_main_2 描外边框(见 ZoneCalculator.drawZoneMeta)，
//   所以给附属国一个"自己的 color_main/旗帜 + 宗主的 color_main_2"的合成颜色。
//   合成颜色只放在运行时缓存 _cached_color 里，存档里的 color_id 仍是附属国自己的颜色；
//   读档或被原版改色后由月度同步(FeudalVassalService.Sync)重新套上，独立时 KingdomColorService.Restore 清掉。
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
        ColorAsset composite = Composite(colors[own], lordColor);
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

    // 宗主若本身也是合成颜色，边框沿用宗主的边框(即最上层宗主的颜色)
    private static ColorAsset Composite(ColorAsset own, ColorAsset lord)
    {
        string key = own.color_main + "|" + lord.color_main_2;
        if (Composites.TryGetValue(key, out ColorAsset cached)) return cached;
        var color = new ColorAsset
        {
            id = "vassal_" + key,
            color_main = own.color_main,
            color_main_2 = lord.color_main_2,
            color_banner = own.color_banner,
            color_text = own.color_text,
            index_id = ColorAsset._create_last_index_id++
        };
        color.initColor();
        Composites[key] = color;
        return color;
    }
}

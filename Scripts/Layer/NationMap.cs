using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.Layer;

// 民族情绪地图图层(MetaTypeExtension.Nation)：按城市的民族情绪(见 NationalSentimentSystem)由冷到热着色。
// 三种显示模式(图层按钮切换)：
//   0 情绪热度：同一档的城市连成一片，只描档位交界；
//   1 城市边界：每座城单独描边；
//   2 异族统治地区：只显示主流文化与统治者不同的城市(独立运动的温床)，其余不上色。
public sealed class NationMapColor : MetaObject<IdeologyMapColorData>
{
    private ColorAsset _color;

    public override MetaType meta_type => MetaTypeExtension.Nation;
    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;
    public override ColorAsset getColor() => _color;

    public static NationMapColor Create(int bucket, string hex)
    {
        // 原版 tryMakeNewColorAsset 新建的颜色不会 initColor()，不手动初始化就全是透明色，图层什么也画不出来
        ColorAsset asset = ColorAsset.tryMakeNewColorAsset(hex);
        asset.initColor();
        var color = new NationMapColor { _color = asset };
        color.loadData(new IdeologyMapColorData { name = "nation_" + bucket });
        return color;
    }
}

public static class NationMap
{
    public const string TooltipType = "empirecraft_nation_sentiment";

    // 0~20 平静、20~40 不满、40~60 躁动、60~80 高涨、80~100 沸腾：由绿到红
    private static readonly string[] BucketHex = { "#3fb24f", "#a6cf3a", "#f0cf33", "#f08a2c", "#de3328" };
    private static readonly string[] BucketKeys =
        { "nation_level_0", "nation_level_1", "nation_level_2", "nation_level_3", "nation_level_4" };
    private static NationMapColor[] _colors;

    public static int Bucket(float sentiment) => sentiment >= 80f ? 4 : sentiment >= 60f ? 3 : sentiment >= 40f ? 2 :
        sentiment >= 20f ? 1 : 0;

    private static NationMapColor ColorOf(int bucket)
    {
        _colors ??= BucketHex.Select((hex, i) => NationMapColor.Create(i, hex)).ToArray();
        return _colors[bucket];
    }

    private static bool Shown(City city) =>
        city != null && !city.isRekt() && !AncientWarfareCompatibility.OwnsObject(city) &&
        city.units != null && city.units.Count > 0;

    public static void Configure(MetaTypeAsset asset)
    {
        asset.draw_zones = (MetaZoneDrawAction)(meta =>
        {
            int mode = meta.getZoneOptionState();
            foreach (City city in World.world.cities)
            {
                if (!Shown(city)) continue;
                if (mode == 2 && !NationalSentimentSystem.IsForeignRuled(city)) continue;
                int bucket = Bucket(NationalSentimentSystem.GetCity(city));
                NationMapColor color = ColorOf(bucket);
                foreach (TileZone zone in city.zones)
                {
                    EmpireCraftMetaTypeLibrary.zone_manager.drawBegin();
                    EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(color, zone,
                        IsBorder(mode, zone.zone_up, city, bucket), IsBorder(mode, zone.zone_down, city, bucket),
                        IsBorder(mode, zone.zone_left, city, bucket), IsBorder(mode, zone.zone_right, city, bucket),
                        color.data, meta);
                    EmpireCraftMetaTypeLibrary.zone_manager.drawEnd(zone);
                }
            }
        });
        asset.check_cursor_highlight = (MetaZoneHighlightAction)((meta, tile, highlight) =>
        {
            City hovered = tile?.zone?.city;
            if (!Shown(hovered)) return;
            QuantumSpriteLibrary.colorZones(highlight, hovered.zones, highlight.color);
        });
        // 原版 MouseCursor/CursorTooltipHelper 每帧无条件调用这两个委托，不设会每帧抛空引用
        asset.check_tile_has_meta = (MetaZoneTooltipAction)((zone, meta, option) => Shown(zone?.city));
        asset.check_cursor_tooltip = (zone, meta, option) =>
        {
            City city = zone?.city;
            if (!Shown(city)) return false;
            Tooltip.hideTooltip(city, true, TooltipType);
            Tooltip.show(city, TooltipType, new TooltipData
            {
                city = city, kingdom = city.kingdom, tooltip_scale = 0.7f, is_sim_tooltip = true
            });
            return true;
        };
        asset.click_action_zone = new MetaZoneClickAction((tile, power) => false);
    }

    private static bool IsBorder(int mode, TileZone neighbour, City city, int bucket)
    {
        City other = neighbour?.city;
        if (mode != 0) return other != city;
        return !Shown(other) || Bucket(NationalSentimentSystem.GetCity(other)) != bucket;
    }

    // 悬停提示：民族情绪、档位、城市文化与统治者文化、当前处境
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        City city = data?.city;
        if (city == null || city.isRekt()) return;
        tooltip.clear();
        float sentiment = NationalSentimentSystem.GetCity(city);
        int bucket = Bucket(sentiment);
        tooltip.setTitle(city.GetCityFullName(), "national_sentiment", "#FFFFFF");
        tooltip.addLineText(LM.Get("national_sentiment"), $"{sentiment:0}% · {LM.Get(BucketKeys[bucket])}",
            BucketHex[bucket], pPercent: false, pLocalize: false);
        string cityCulture = CultureService.GetMainCulture(city);
        string rulerCulture = NationalSentimentSystem.RulerCulture(city);
        if (CultureService.IsValidCulture(cityCulture))
            tooltip.addLineText(LM.Get("nation_tooltip_city_culture"), cityCulture.GetCultureTranslate(), "#FFFFFF",
                pPercent: false, pLocalize: false);
        if (CultureService.IsValidCulture(rulerCulture))
            tooltip.addLineText(LM.Get("nation_tooltip_ruler_culture"), rulerCulture.GetCultureTranslate(), "#FFFFFF",
                pPercent: false, pLocalize: false);
        List<string> states = new();
        if (NationalSentimentSystem.IsForeignRuled(city)) states.Add(LM.Get("nation_state_foreign_rule"));
        if (NationalSentimentSystem.IsInvadedCity(city)) states.Add(LM.Get("nation_state_invaded"));
        tooltip.addBottomDescription(states.Count > 0 ? string.Join(" · ", states) : LM.Get("nation_state_calm"));
    }
}

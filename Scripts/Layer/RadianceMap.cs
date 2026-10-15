using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;

// 教化图层、政令图层(用户 2026-10-16 要："加热力图层")的色块
public sealed class RadianceMapColor : MetaObject<IdeologyMapColorData>
{
    private ColorAsset _color;
    private MetaType _type;

    public override MetaType meta_type => _type;
    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;
    public override ColorAsset getColor() => _color;

    public static RadianceMapColor Create(MetaType type, string name, string hex)
    {
        // 原版 tryMakeNewColorAsset 新建的颜色要手动 initColor()，不然全是透明色
        ColorAsset asset = ColorAsset.tryMakeNewColorAsset(hex);
        asset.initColor();
        var color = new RadianceMapColor { _color = asset, _type = type };
        color.loadData(new IdeologyMapColorData { name = name });
        return color;
    }
}

// 两个图层共用的画法：每座城一个档(同档连成一片只描交界，或按城市边界描)，悬停显示本城明细
internal static class RadianceLayers
{
    public static bool Shown(City city) =>
        city != null && !city.isRekt() && city.data != null && !AncientWarfareCompatibility.OwnsObject(city) &&
        city.kingdom != null && !city.kingdom.wild;

    public static void Configure(MetaTypeAsset asset, global::System.Func<City, (string key, RadianceMapColor color)> bucketOf,
        string tooltipType)
    {
        asset.draw_zones = (MetaZoneDrawAction)(meta =>
        {
            int mode = meta.getZoneOptionState();
            var buckets = new Dictionary<City, string>();
            string Key(City city)
            {
                if (!Shown(city)) return null;
                if (!buckets.TryGetValue(city, out string key)) buckets[city] = key = bucketOf(city).key;
                return key;
            }
            bool Border(TileZone neighbour, City city, string key)
            {
                City other = neighbour?.city;
                return mode != 0 ? other != city : Key(other) != key;
            }
            foreach (City city in World.world.cities)
            {
                if (!Shown(city)) continue;
                (string key, RadianceMapColor color) = bucketOf(city);
                if (color == null) continue;
                buckets[city] = key;
                foreach (TileZone zone in city.zones)
                {
                    EmpireCraftMetaTypeLibrary.zone_manager.drawBegin();
                    EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(color, zone,
                        Border(zone.zone_up, city, key), Border(zone.zone_down, city, key),
                        Border(zone.zone_left, city, key), Border(zone.zone_right, city, key), color.data, meta);
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
        asset.check_tile_has_meta = (MetaZoneTooltipAction)((zone, meta, option) => Shown(zone?.city));
        asset.check_cursor_tooltip = (zone, meta, option) =>
        {
            City city = zone?.city;
            if (!Shown(city)) return false;
            Tooltip.hideTooltip(city, true, tooltipType);
            Tooltip.show(city, tooltipType, new TooltipData
            {
                city = city, kingdom = city.kingdom, tooltip_scale = 0.7f, is_sim_tooltip = true
            });
            return true;
        };
        asset.click_action_zone = new MetaZoneClickAction((tile, power) => false);
    }
}

// 教化图层(MetaTypeExtension.Edification)：每座城涂上教化最盛的文化的颜色(见 CultureRadiation)，
// 那个文化占得越少颜色越淡——腹地浓、边地淡，山后、长城外、两种文化交界处一眼看得出。
// 两种显示模式：0 按教化(同文化同浓淡的连成一片)；1 按城市边界。还没算出教化的城不上色
public static class EdificationMap
{
    public const string TooltipType = "empirecraft_city_edification";
    private static readonly float[] Levels = { 0.85f, 0.7f, 0.55f, 0.4f };
    private static readonly float[] Strength = { 1f, 0.8f, 0.62f, 0.46f, 0.32f };
    private static readonly Color Faint = new(0.17f, 0.17f, 0.17f);
    private static readonly Dictionary<(string, int), RadianceMapColor> Colors = new();

    private static int Level(float share)
    {
        for (int i = 0; i < Levels.Length; i++)
            if (share >= Levels[i]) return i;
        return Levels.Length;
    }

    private static (string key, RadianceMapColor color) BucketOf(City city)
    {
        (string culture, float share) = CultureRadiation.Dominant(city);
        if (culture == null) return (null, null);
        int level = Level(share);
        if (!Colors.TryGetValue((culture, level), out RadianceMapColor color))
        {
            Color shade = Color.Lerp(Faint, culture.GetCultureColor(), Strength[level]);
            Colors[(culture, level)] = color = RadianceMapColor.Create(MetaTypeExtension.Edification,
                $"edification_{culture}_{level}", "#" + ColorUtility.ToHtmlStringRGB(shade));
        }
        return ($"{culture}:{level}", color);
    }

    public static void Configure(MetaTypeAsset asset) => RadianceLayers.Configure(asset, BucketOf, TooltipType);

    // 悬停：本城主流文化、教化到本城的前几种文化
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        City city = data?.city;
        if (city == null || city.isRekt()) return;
        tooltip.clear();
        tooltip.setTitle(city.GetCityFullName(), "edification_layer_tooltip", "#FFFFFF");
        string main = CultureService.GetMainCulture(city, false);
        if (CultureService.IsValidCulture(main))
            tooltip.addLineText(LM.Get("edification_main_culture"), main.GetCultureTranslate(),
                "#" + ColorUtility.ToHtmlStringRGB(main.GetCultureColor()), pPercent: false, pLocalize: false);
        IReadOnlyDictionary<string, float> shares = CultureRadiation.SharesOf(city);
        if (shares == null)
        {
            tooltip.addDescription(LM.Get("edification_pending"));
            return;
        }
        foreach (KeyValuePair<string, float> pair in shares.OrderByDescending(pair => pair.Value).Take(4))
            tooltip.addLineText(pair.Key.GetCultureTranslate(), $"{pair.Value:P0}",
                "#" + ColorUtility.ToHtmlStringRGB(pair.Key.GetCultureColor()), pPercent: false, pLocalize: false);
        if (CultureService.IsValidCulture(main))
            tooltip.addLineText(LM.Get("edification_assimilation"),
                $"×{CultureRadiation.AssimilationFactor(city, main):0.00}", "#D8D2B8", pPercent: false, pLocalize: false);
    }
}

// 政令图层(MetaTypeExtension.Governance)：按各城的行政控制力(见 AdministrativeControlSystem)由红(鞭长莫及)到绿(如臂使指)分五档。
// 两种显示模式：0 按档位(同档连成一片)；1 按城市边界。还没算出控制力的城不上色
public static class GovernanceMap
{
    public const string TooltipType = "empirecraft_city_governance";
    private static readonly string[] BucketHex = { "#de3328", "#f08a2c", "#f0cf33", "#a6cf3a", "#3fb24f" };
    private static readonly string[] BucketKeys =
        { "governance_level_0", "governance_level_1", "governance_level_2", "governance_level_3", "governance_level_4" };
    private static RadianceMapColor[] _colors;

    public static int Bucket(float control) =>
        control >= 0.9f ? 4 : control >= 0.7f ? 3 : control >= 0.5f ? 2 : control >= 0.3f ? 1 : 0;

    private static (string key, RadianceMapColor color) BucketOf(City city)
    {
        if (!AdministrativeControlSystem.Known(city)) return (null, null);
        _colors ??= BucketHex.Select((hex, i) => RadianceMapColor.Create(MetaTypeExtension.Governance, "governance_" + i, hex)).ToArray();
        int bucket = Bucket(AdministrativeControlSystem.Get(city));
        return (bucket.ToString(), _colors[bucket]);
    }

    public static void Configure(MetaTypeAsset asset) => RadianceLayers.Configure(asset, BucketOf, TooltipType);

    // 悬停：控制力与档位、离治所的路程(或飞地、隔海)、赋税征兵收得上几成
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        City city = data?.city;
        if (city == null || city.isRekt()) return;
        tooltip.clear();
        tooltip.setTitle(city.GetCityFullName(), "governance_layer_tooltip", "#FFFFFF");
        if (!AdministrativeControlSystem.Known(city))
        {
            tooltip.addDescription(LM.Get("governance_pending"));
            return;
        }
        float control = AdministrativeControlSystem.Get(city);
        int bucket = Bucket(control);
        tooltip.addLineText(LM.Get("admin_control"), $"{control:P0} · {LM.Get(BucketKeys[bucket])}", BucketHex[bucket],
            pPercent: false, pLocalize: false);
        string where = AdministrativeControlSystem.Describe(city);
        if (!string.IsNullOrEmpty(where))
            tooltip.addLineText(LM.Get("governance_reach"), where.Substring(where.IndexOf('·') + 1).Trim(), "#D8D2B8",
                pPercent: false, pLocalize: false);
        tooltip.addLineText(LM.Get("governance_yield"), $"{AdministrativeControlSystem.Yield(city):P0}", "#F3C34A",
            pPercent: false, pLocalize: false);
    }
}

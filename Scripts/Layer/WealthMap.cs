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

// 财富地图图层(MetaTypeExtension.Wealth)：按城市富庶度(人均产出、人口为主，乘地利，见 CityConstructionSystem.Wealth)
// 由红(赤贫)到绿(富庶)着色。三种显示模式(图层按钮切换)：
//   0 贫富热度：按全图排名分五档(最穷的两成赤贫、最富的两成富庶)，各城都穷或都富时也一眼看得出哪里最富、哪里最穷；
//     同一档的城市连成一片，只描档位交界；
//   1 城市边界：同样按排名分档，每座城单独描边；
//   2 固定档位：按富庶度本身分档(见 AbsoluteBucket)，不排名——普遍富足的天下不会硬凑出两成赤贫；同一档连成一片。
// 经济圈选主城、城市格局的档位(见 EconomicCircleSystem)照旧按排名(Bucket)，不随图层显示模式变
public sealed class WealthMapColor : MetaObject<IdeologyMapColorData>
{
    private ColorAsset _color;

    public override MetaType meta_type => MetaTypeExtension.Wealth;
    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;
    public override ColorAsset getColor() => _color;

    public static WealthMapColor Create(int bucket, string hex)
    {
        ColorAsset asset = ColorAsset.tryMakeNewColorAsset(hex);
        asset.initColor();
        var color = new WealthMapColor { _color = asset };
        color.loadData(new IdeologyMapColorData { name = "wealth_" + bucket });
        return color;
    }
}

public static class WealthMap
{
    public const string TooltipType = "empirecraft_city_wealth";

    // 赤贫、清贫、小康、殷实、富庶：由红到绿
    private static readonly string[] BucketHex = { "#de3328", "#f08a2c", "#f0cf33", "#a6cf3a", "#3fb24f" };
    private static readonly string[] BucketKeys =
        { "wealth_level_0", "wealth_level_1", "wealth_level_2", "wealth_level_3", "wealth_level_4" };
    private static WealthMapColor[] _colors;
    // 富庶度要算经济繁荣，图层每帧重画：每城缓存半秒
    private static readonly Dictionary<long, (float score, float at)> _scores = new();

    public static float Score(City city)
    {
        if (city?.data == null) return 0f;
        float now = Time.unscaledTime;
        if (_scores.TryGetValue(city.data.id, out var cached) && now - cached.at < 0.5f) return cached.score;
        float score = Mathf.Clamp01(CityConstructionSystem.Wealth(city));
        if (_scores.Count > 2000) _scores.Clear();
        _scores[city.data.id] = (score, now);
        return score;
    }

    // 富庶档位的颜色(按图层眼下的显示模式)
    public static string Hex(City city) => BucketHex[DisplayBucket(Score(city))];

    // 固定档位的分界：富庶度 0.25、0.4、0.55、0.7。稠密的农耕腹地约 0.6~0.75(殷实、富庶)，
    // 边地小城约 0.4~0.5(小康)，游牧营地约 0.25~0.4(清贫)
    private static readonly float[] AbsoluteEdges = { 0.25f, 0.4f, 0.55f, 0.7f };
    public const int AbsoluteMode = 2;

    public static int AbsoluteBucket(float score)
    {
        int bucket = 0;
        while (bucket < AbsoluteEdges.Length && score >= AbsoluteEdges[bucket]) bucket++;
        return bucket;
    }

    // 图层眼下是不是固定档位模式
    public static bool Absolute => EmpireCraftMetaTypeLibrary.wealth?.getZoneOptionState() == AbsoluteMode;

    // 图层显示用的档位：固定档位模式按富庶度本身，其余按全图排名
    public static int DisplayBucket(float score) => Absolute ? AbsoluteBucket(score) : Bucket(score);

    // 按全图排名分档：富庶度在全图各城里排第几(两秒重排一次)
    private static float[] _ranked = new float[0];
    private static float _rankedAt = -10f;

    public static int Bucket(float score)
    {
        float now = Time.unscaledTime;
        if (now - _rankedAt >= 2f || now < _rankedAt)
        {
            _rankedAt = now;
            _ranked = World.world?.cities?.list?.Where(Shown).Select(Score).OrderBy(value => value).ToArray() ?? new float[0];
        }
        if (_ranked.Length < 5) return AbsoluteBucket(score);
        int below = global::System.Array.BinarySearch(_ranked, score);
        if (below < 0) below = ~below;
        float percentile = below / (float)_ranked.Length;
        return Mathf.Clamp(Mathf.FloorToInt(percentile * 5f), 0, 4);
    }

    private static WealthMapColor ColorOf(int bucket)
    {
        _colors ??= BucketHex.Select((hex, i) => WealthMapColor.Create(i, hex)).ToArray();
        return _colors[bucket];
    }

    private static bool Shown(City city) =>
        city != null && !city.isRekt() && !AncientWarfareCompatibility.OwnsObject(city) &&
        city.kingdom != null && !city.kingdom.wild;

    public static void Configure(MetaTypeAsset asset)
    {
        asset.draw_zones = (MetaZoneDrawAction)(meta =>
        {
            int mode = meta.getZoneOptionState();
            foreach (City city in World.world.cities)
            {
                if (!Shown(city)) continue;
                int bucket = BucketFor(mode, Score(city));
                WealthMapColor color = ColorOf(bucket);
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

    private static int BucketFor(int mode, float score) => mode == AbsoluteMode ? AbsoluteBucket(score) : Bucket(score);

    // 城市边界模式每座城描边；贫富热度、固定档位只描档位交界
    private static bool IsBorder(int mode, TileZone neighbour, City city, int bucket)
    {
        City other = neighbour?.city;
        if (mode == 1) return other != city;
        return !Shown(other) || BucketFor(mode, Score(other)) != bucket;
    }

    private static string Percent(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";

    // 悬停提示：富庶度与档位(注明是按排名还是固定档位)、构成、城市国库、民间资产(无小人模式再列现金与投资池)
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        City city = data?.city;
        if (city == null || city.isRekt()) return;
        tooltip.clear();
        float score = Score(city);
        bool absolute = Absolute;
        int bucket = absolute ? AbsoluteBucket(score) : Bucket(score);
        tooltip.setTitle(city.GetCityFullName(), "wealth_layer_tooltip", "#FFFFFF");
        tooltip.addLineText(LM.Get("wealth_layer_tooltip"), $"{Percent(score)} · {LM.Get(BucketKeys[bucket])}",
            BucketHex[bucket], pPercent: false, pLocalize: false);
        tooltip.addLineText(LM.Get("wealth_mode_label"), LM.Get(absolute ? "wealth_mode_absolute" : "wealth_mode_rank"),
            "#D8D2B8", pPercent: false, pLocalize: false);
        CityConstructionSystem.WealthParts parts = CityConstructionSystem.WealthBreakdown(city);
        tooltip.addLineText(LM.Get("wealth_parts_label"),
            string.Format(LM.Get("wealth_parts_value"), Percent(parts.Output), Percent(parts.Population), Percent(parts.Commerce),
                Percent(parts.Assets)), "#D8D2B8", pPercent: false, pLocalize: false);
        float geography = CityGeographySystem.Factor(city);
        tooltip.addLineText(LM.Get("city_geography_label"),
            string.Format(LM.Get("city_geography_value"), geography, CityGeographySystem.Commerce(city), CityGeographySystem.Industry(city)),
            geography >= 1.1f ? "#7CD27C" : geography <= 0.8f ? "#E07A5F" : "#D8D2B8", pPercent: false, pLocalize: false);
        tooltip.addLineText(LM.Get("fiscal_cash_balance"),
            HelperFunc.MoneyDisplay.Format(city.GetTreasuryBalance()), "#F3C34A", pPercent: false, pLocalize: false);
        City core = EconomicCircleSystem.CoreOf(city);
        if (core != null)
            tooltip.addLineText(LM.Get("economic_circle_label"), core == city
                    ? string.Format(LM.Get("economic_circle_core"), city.GetCityName(), EconomicCircleSystem.Members(city))
                    : string.Format(LM.Get("economic_circle_member"), core.GetCityName()),
                core == city ? "#F3C34A" : "#D8D2B8", pPercent: false, pLocalize: false);
        CityPopulationData population = CityPopulationSystem.Get(city);
        if (CityPopulationSystem.AbstractPopulationEnabled && population != null)
        {
            tooltip.addLineText(LM.Get("city_pop_savings"),
                HelperFunc.MoneyDisplay.Format(Mathf.Max(0f, population.private_savings)), "#E6D36A",
                pPercent: false, pLocalize: false);
            tooltip.addLineText(LM.Get("city_pop_cash"),
                HelperFunc.MoneyDisplay.Format(EnterpriseSystem.Cash(population)), "#E6D36A",
                pPercent: false, pLocalize: false);
            if (population.investment_pool >= 1f)
                tooltip.addLineText(LM.Get("city_enterprise_pool"),
                    HelperFunc.MoneyDisplay.Format(population.investment_pool), "#D9B36A",
                    pPercent: false, pLocalize: false);
        }
    }
}

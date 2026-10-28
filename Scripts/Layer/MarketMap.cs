using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;

// 商品市场图层(MetaTypeExtension.Market，见 CommodityMarketSystem)：
//   0 市场：同一市场的国家涂领衔国的颜色，市场之间描边；不止一国的市场铭牌写"某某市场"；
//   1 缺口：按市场物资缺口(按需求额加权)由绿(充足)到红(匮乏)分五档。
// 悬停看领衔、大小国市场、成员、外贸、最缺的几样与出口最多的几样；点地块打开这个市场的汇总窗口(见 MarketWindow)。
// 市场每年结算一次：读档后第一轮结算前，市场模式按宗主一系(RootOf)着色，缺口模式不上色
public sealed class MarketMapColor : MetaObject<IdeologyMapColorData>
{
    private ColorAsset _color;

    public override MetaType meta_type => MetaTypeExtension.Market;
    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;
    public override ColorAsset getColor() => _color;

    public static MarketMapColor Create(int index, string hex)
    {
        // 原版 tryMakeNewColorAsset 新建的颜色不 initColor() 就是透明色
        ColorAsset asset = ColorAsset.tryMakeNewColorAsset(hex);
        asset.initColor();
        var color = new MarketMapColor { _color = asset };
        color.loadData(new IdeologyMapColorData { name = "market_" + index, id = 7_200_000_000_000L + index });
        // 原版按哈希码判同一对象：不给的话各档都是 0，平滑国界会把它们当成同一片
        color.setHash(BaseSystemManager._latest_hash++);
        return color;
    }
}

public static class MarketMap
{
    public const string TooltipType = "empirecraft_market";
    public const int ShortageMode = 1;

    // 充足、略缺、紧缺、奇缺、匮乏：由绿到红
    private static readonly string[] BucketHex = { "#3fb24f", "#a6cf3a", "#f0cf33", "#f08a2c", "#de3328" };
    private static readonly string[] BucketKeys =
        { "market_short_level_0", "market_short_level_1", "market_short_level_2", "market_short_level_3", "market_short_level_4" };
    // 加权缺口的分界
    private static readonly float[] Edges = { 0.05f, 0.15f, 0.3f, 0.5f };
    private static MarketMapColor[] _colors;

    private static readonly TerritoryLabelStyle LabelStyle = new TerritoryLabelStyle
    {
        text_color = new Color32(255, 236, 170, 220),
        outline_color = new Color32(54, 34, 8, 210),
        size_multiplier = 0.85f,
        territory_padding = 0.92f,
        min_font_size = 6,
        font_style = FontStyle.Normal,
        wrap_english_name = true,
        bridge_internal_gaps = true,
        arc_curvature = 0.1f
    };

    // 每国归哪个市场领衔：图层每帧按地块取很多次，同一帧只算一次
    private static readonly Dictionary<long, Kingdom> LeaderCache = new();
    private static int _cacheFrame = -1;

    public static Kingdom LeaderOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv()) return null;
        if (_cacheFrame != Time.frameCount)
        {
            _cacheFrame = Time.frameCount;
            LeaderCache.Clear();
        }
        if (LeaderCache.TryGetValue(kingdom.id, out Kingdom cached)) return cached;
        Kingdom leader = CommodityMarketSystem.MarketOf(kingdom)?.leader;
        if (leader?.data == null || leader.isRekt()) leader = CommodityMarketSystem.RootOf(kingdom);
        return LeaderCache[kingdom.id] = leader;
    }

    private static bool Shown(City city) =>
        city != null && !city.isRekt() && !AncientWarfareCompatibility.OwnsObject(city) && LeaderOf(city.kingdom) != null;

    // 加权缺口：各物资缺口按需求额(需求×底价)加权平均。市场对象每年重建，按对象缓存
    private static readonly Dictionary<CommodityMarketSystem.Market, float> ShortageCache = new();

    public static float Shortage(CommodityMarketSystem.Market market)
    {
        if (market == null) return 0f;
        if (ShortageCache.TryGetValue(market, out float cached)) return cached;
        float weighted = 0f, total = 0f;
        foreach (CommodityConfig good in CommodityMarketSystem.AllGoods)
        {
            if (!market.demand.TryGetValue(good.id, out float demand) || demand <= 0f) continue;
            float weight = demand * Mathf.Max(0.1f, good.price);
            market.shortage.TryGetValue(good.id, out float shortage);
            weighted += shortage * weight;
            total += weight;
        }
        float value = total > 0f ? weighted / total : 0f;
        if (ShortageCache.Count > 512) ShortageCache.Clear();
        return ShortageCache[market] = value;
    }

    public static int Bucket(float shortage)
    {
        int bucket = 0;
        while (bucket < Edges.Length && shortage >= Edges[bucket]) bucket++;
        return bucket;
    }

    private static MarketMapColor ColorOf(int bucket)
    {
        _colors ??= BucketHex.Select((hex, i) => MarketMapColor.Create(i, hex)).ToArray();
        return _colors[bucket];
    }

    // 一块地在市场图层上归谁：0 市场领衔国、1 所在市场的缺口档(还没结算不上色)
    private static IMetaObject MetaOf(City city, int mode)
    {
        if (!Shown(city)) return null;
        if (mode != ShortageMode) return LeaderOf(city.kingdom);
        CommodityMarketSystem.Market market = CommodityMarketSystem.MarketOf(city.kingdom);
        return market == null ? null : ColorOf(Bucket(Shortage(market)));
    }

    // 各市场的城(铭牌、悬停高亮用)：一秒重排一次
    private static readonly Dictionary<long, List<City>> CitiesByLeader = new();
    private static float _groupedAt = -10f;

    private static Dictionary<long, List<City>> Grouped()
    {
        float now = Time.unscaledTime;
        if (now - _groupedAt < 1f && now >= _groupedAt) return CitiesByLeader;
        _groupedAt = now;
        CitiesByLeader.Clear();
        if (World.world?.cities == null) return CitiesByLeader;
        foreach (City city in World.world.cities)
        {
            if (!Shown(city)) continue;
            long id = LeaderOf(city.kingdom).id;
            if (!CitiesByLeader.TryGetValue(id, out List<City> list)) CitiesByLeader[id] = list = new List<City>();
            list.Add(city);
        }
        return CitiesByLeader;
    }

    public static void Configure(MetaTypeAsset asset)
    {
        asset.draw_zones = (MetaZoneDrawAction)(meta =>
        {
            int mode = meta.getZoneOptionState();
            foreach (City city in World.world.cities)
            {
                if (MetaOf(city, mode) is not IMetaObject owner) continue;
                Kingdom leader = LeaderOf(city.kingdom);
                MetaObjectData ownerData = owner is Kingdom kingdom ? kingdom.data : ((MarketMapColor)owner).data;
                foreach (TileZone zone in city.zones)
                {
                    EmpireCraftMetaTypeLibrary.zone_manager.drawBegin();
                    EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(owner, zone,
                        Border(zone.zone_up, leader), Border(zone.zone_down, leader),
                        Border(zone.zone_left, leader), Border(zone.zone_right, leader), ownerData, meta);
                    EmpireCraftMetaTypeLibrary.zone_manager.drawEnd(zone);
                }
            }
        });
        asset.check_cursor_highlight = (MetaZoneHighlightAction)((meta, tile, highlight) =>
        {
            City hovered = tile?.zone?.city;
            if (!Shown(hovered)) return;
            if (!Grouped().TryGetValue(LeaderOf(hovered.kingdom).id, out List<City> cities)) return;
            QuantumSpriteLibrary.colorZones(highlight, cities.Where(city => city != null && !city.isRekt())
                .SelectMany(city => city.zones).ToList(), highlight.color);
        });
        // 按地块取归属：平滑国界(整图画布)要它才画得出来(见 SmoothBorders.Smoothable)
        asset.tile_get_metaobject = (zone, option) => MetaOf(zone?.city, option);
        asset.tile_get_metaobject_0 = zone => MetaOf(zone?.city, 0);
        asset.tile_get_metaobject_1 = zone => MetaOf(zone?.city, 1);
        // 原版每帧无条件调用这两个委托，不设会每帧抛空引用
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
        asset.click_action_zone = new MetaZoneClickAction((tile, power) =>
        {
            City city = tile?.zone?.city;
            if (!Shown(city)) return false;
            MarketWindow.Open(city.kingdom);
            return true;
        });
    }

    // 市场之间描边(两种模式都按市场分片，缺口模式同档的相邻市场也分得开)
    private static bool Border(TileZone neighbour, Kingdom leader)
    {
        City other = neighbour?.city;
        return !Shown(other) || LeaderOf(other.kingdom) != leader;
    }

    public static string Name(Kingdom leader) => string.Format(LM.Get("market_layer_name"), leader.GetKingdomName());

    // 铭牌：不止一国的市场写"某某市场"；只有一国的就是那一国，不另写
    public static void SubmitLabels()
    {
        foreach (KeyValuePair<long, List<City>> pair in Grouped())
        {
            Kingdom leader = World.world.kingdoms.get(pair.Key);
            if (leader?.data == null || leader.isRekt()) continue;
            if (pair.Value.Select(city => city.kingdom).Distinct().Count() < 2) continue;
            TerritoryLabelRenderer.SubmitCities($"market:{pair.Key}", Name(leader), pair.Value, LabelStyle);
        }
    }

    private static string Percent(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";

    // 悬停提示：市场名、大小国市场、成员、外贸、加权缺口、最缺与出口最多的三样
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        City city = data?.city;
        Kingdom leader = LeaderOf(city?.kingdom);
        if (leader == null) return;
        tooltip.clear();
        tooltip.setTitle(Name(leader), "market_layer", leader.getColor()?.color_text ?? "#FFFFFF");
        CommodityMarketSystem.Market market = CommodityMarketSystem.MarketOf(city.kingdom);
        if (market == null)
        {
            tooltip.addLineText(LM.Get("diplomacy_market_pending"), LM.Get("diplomacy_market_pending_hint"), "#8FA0A8",
                pPercent: false, pLocalize: false);
        }
        else
        {
            tooltip.addLineText(LM.Get("diplomacy_market_leader"),
                LM.Get(market.great ? "diplomacy_market_great" : "diplomacy_market_minor"), market.great ? "#F3C34A" : "#D8D2B8",
                pPercent: false, pLocalize: false);
            tooltip.addLineText(LM.Get("diplomacy_market_members"),
                string.Format(LM.Get("diplomacy_chip_count"), market.members.Count), "#D8D2B8", pPercent: false, pLocalize: false);
            tooltip.addLineText(LM.Get("diplomacy_market_trade"), UIHelper.ShortCount((long)market.trade_value), "#7CD27C",
                pPercent: false, pLocalize: false);
            float shortage = Shortage(market);
            int bucket = Bucket(shortage);
            tooltip.addLineText(LM.Get("market_layer_shortage"), $"{Percent(shortage)} · {LM.Get(BucketKeys[bucket])}",
                BucketHex[bucket], pPercent: false, pLocalize: false);
            List<CommodityConfig> goods = CommodityMarketSystem.AllGoods.ToList();
            string shortList = string.Join(" · ", goods
                .Where(good => market.shortage.TryGetValue(good.id, out float value) && value >= Edges[0])
                .OrderByDescending(good => market.shortage[good.id]).Take(3)
                .Select(good => $"{DiplomacyLedger.GoodName(good)} {Percent(market.shortage[good.id])}"));
            if (shortList.Length > 0)
                tooltip.addLineText(LM.Get("market_layer_most_short"), shortList, "#E07A5F", pPercent: false, pLocalize: false);
            string exportList = string.Join(" · ", goods
                .Where(good => market.exports.TryGetValue(good.id, out float value) && value > 0f)
                .OrderByDescending(good => market.exports[good.id]).Take(3)
                .Select(DiplomacyLedger.GoodName));
            if (exportList.Length > 0)
                tooltip.addLineText(LM.Get("diplomacy_goods_export"), exportList, "#7CD27C", pPercent: false, pLocalize: false);
        }
        tooltip.addBottomDescription(string.Format(LM.Get("market_layer_tooltip_city"), city.GetCityName(),
            city.kingdom.GetKingdomName()));
    }
}

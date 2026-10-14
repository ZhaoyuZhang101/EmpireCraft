using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 物资账(帝国财政页、国情窗口的"物资产需"卡)：每城按物资记一年的产出、消耗与缺口。
//   产出：人口经济入库(收成连同交国家粮仓的部分、岗位、矿场、伐木、畜牧、冶炼，含自给口粮)；
//         赈灾调粮、市场买卖、迁城运货是调拨，不算产出。
//   消耗：百姓吃粮、用皮革，军粮，民居升级用料，炼钢耗煤耗铁，征兵打造装备。
//   缺口：要吃没吃上的粮、要用没有的皮革(年需 = 消耗 + 缺口)。
// 本年累计满一年就存为"上一年"；界面优先显示上一年，头一年没满按已过的时间折算成一年。
// 只有人口经济(无小人模式的背景人口)在这里记账；原版小人自己采集、吃饭不经过这里。
public static class GoodsLedger
{
    // 各种粮食在界面上合成一行
    public const string FoodKey = "food";
    private const double YearSeconds = 60d;
    // 头一年至少过了一季才折算，免得刚开始记账时一次收成被放大成十几倍
    private const double MinScaleSeconds = 15d;

    public static void Made(City city, string resource, float amount) => Add(city, resource, amount, 0);
    public static void Used(City city, string resource, float amount) => Add(city, resource, amount, 1);
    public static void Short(City city, string resource, float amount) => Add(city, resource, amount, 2);

    private static void Add(City city, string resource, float amount, int kind)
    {
        if (city?.data == null || string.IsNullOrEmpty(resource) || amount <= 0f || float.IsNaN(amount) ||
            float.IsInfinity(amount) || World.world == null) return;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data == null) return;
        if (data.goods_since < 0d) data.goods_since = World.world.getCurWorldTime();
        Dictionary<string, float> acc = kind switch
        {
            0 => data.goods_made_acc ??= new Dictionary<string, float>(),
            1 => data.goods_used_acc ??= new Dictionary<string, float>(),
            _ => data.goods_short_acc ??= new Dictionary<string, float>()
        };
        acc.TryGetValue(resource, out float previous);
        acc[resource] = previous + amount;
    }

    // 满一年：本年累计按整一年折算存为上一年，重新开始累计。只在经济结算时调用(见 PopulationEconomySystem.Settle)，
    // 不在记账时翻页——不然一年里只记到一两次结算，年账时有时无
    public static void Roll(CityPopulationData data)
    {
        if (data == null || World.world == null) return;
        double now = World.world.getCurWorldTime();
        if (data.goods_since < 0d || now < data.goods_since)
        {
            data.goods_since = now;
            data.goods_made_acc?.Clear();
            data.goods_used_acc?.Clear();
            data.goods_short_acc?.Clear();
            return;
        }
        double elapsed = now - data.goods_since;
        if (elapsed < YearSeconds) return;
        float scale = (float)(YearSeconds / elapsed);
        data.goods_made = Scaled(data.goods_made_acc, scale);
        data.goods_used = Scaled(data.goods_used_acc, scale);
        data.goods_short = Scaled(data.goods_short_acc, scale);
        data.goods_made_acc?.Clear();
        data.goods_used_acc?.Clear();
        data.goods_short_acc?.Clear();
        data.goods_since = now;
    }

    private static Dictionary<string, float> Scaled(Dictionary<string, float> source, float scale)
    {
        var result = new Dictionary<string, float>();
        if (source != null)
            foreach (var pair in source)
                if (pair.Value > 0f) result[pair.Key] = pair.Value * scale;
        return result;
    }

    // 一城的年账：有上一年用上一年，否则按本年已过的时间折算
    private static (Dictionary<string, float> made, Dictionary<string, float> used, Dictionary<string, float> shortage, bool estimated)
        Annual(CityPopulationData data)
    {
        if (data.goods_made != null)
            return (data.goods_made, data.goods_used, data.goods_short, false);
        if (data.goods_since < 0d || World.world == null) return (null, null, null, true);
        double elapsed = World.world.getCurWorldTime() - data.goods_since;
        float scale = elapsed >= MinScaleSeconds ? (float)(YearSeconds / elapsed) : 1f;
        return (Scaled(data.goods_made_acc, scale), Scaled(data.goods_used_acc, scale), Scaled(data.goods_short_acc, scale), true);
    }

    public static string KeyOf(string resource) =>
        resource == FoodKey || PopulationEconomySystem.IsFood(resource) ? FoodKey : resource;

    // 一片领土(一国、一个帝国)的物资产需：粮食一行在前，其余按产耗多少排序
    public static GoodsSummary Summarize(IEnumerable<City> cities)
    {
        var summary = new GoodsSummary();
        var rows = new Dictionary<string, GoodsRow>();
        GoodsRow Row(string key)
        {
            if (!rows.TryGetValue(key, out GoodsRow row)) rows[key] = row = new GoodsRow { key = key };
            return row;
        }
        Row(FoodKey);
        foreach (City city in cities ?? Enumerable.Empty<City>())
        {
            if (city?.data == null || city.isRekt()) continue;
            CityPopulationData data = CityPopulationSystem.Get(city);
            if (data != null)
            {
                var (made, used, shortage, estimated) = Annual(data);
                if (made != null)
                {
                    summary.tracked = true;
                    summary.estimated |= estimated;
                    foreach (var pair in made) Row(KeyOf(pair.Key)).made += pair.Value;
                    if (used != null) foreach (var pair in used) Row(KeyOf(pair.Key)).used += pair.Value;
                    if (shortage != null) foreach (var pair in shortage) Row(KeyOf(pair.Key)).shortage += pair.Value;
                }
            }
            foreach (MarketSystem.Good good in MarketSystem.Goods)
            {
                GoodsRow row = Row(GoodKey(good));
                row.reserve += MarketSystem.Reserve(city, good);
                if (good != MarketSystem.Good.Food) continue;
                row.stock += MarketSystem.Stock(city, good);
            }
        }
        // 库存：粮食按 MarketSystem 合计各种粮食；其余逐项读仓库
        foreach (GoodsRow row in rows.Values)
        {
            if (row.key == FoodKey) continue;
            foreach (City city in cities ?? Enumerable.Empty<City>())
                if (city?.data != null && !city.isRekt()) row.stock += Math.Max(0, city.getResourcesAmount(row.key));
        }
        summary.food = rows[FoodKey];
        summary.others = rows.Values.Where(row => row.key != FoodKey && row.made + row.used + row.shortage + row.stock + row.reserve > 0f)
            .OrderByDescending(row => row.made + row.used + row.shortage).ThenByDescending(row => row.stock).ToList();
        return summary;
    }

    private static string GoodKey(MarketSystem.Good good) => good switch
    {
        MarketSystem.Good.Food => FoodKey,
        MarketSystem.Good.Wood => "wood",
        MarketSystem.Good.Stone => "stone",
        _ => "common_metals"
    };
}

public sealed class GoodsRow
{
    public string key;
    public float made, used, shortage, reserve;
    public long stock;
    public float need => used + shortage;
    public float balance => made - need;
}

public sealed class GoodsSummary
{
    public GoodsRow food;
    public List<GoodsRow> others = new();
    // 有没有记过账(只有人口经济记账)；头一年没满、是折算的
    public bool tracked, estimated;
}

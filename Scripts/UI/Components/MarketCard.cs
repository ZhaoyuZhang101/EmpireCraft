using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 帝国财政页、国情窗口的"市场"卡(用户 2026-10-29 "现在的市场系统无法真正影响国家的经济，甚至帝国的经济界面和这些市场完全无关")：
//   · 标题行：所属市场；物价(各物资市价指数按需求加权，1 = 照底价)；
//   · 本国(帝国为全境各成员)一年的出口收入、进口花费、关税收入；
//   · 最缺的三样物资，以及缺货此刻对本国的影响(缺工具产出打折、缺布缺粮民心下降、奢侈品满足)；
//   · 最后一行点开市场汇总(见 MarketWindow)。
public static class MarketCard
{
    public static void Draw(AutoVertLayoutGroup parent, float width, Kingdom focus, IEnumerable<Kingdom> realm)
    {
        if (focus?.data == null || focus.isRekt()) return;
        CommodityMarketSystem.Market market = CommodityMarketSystem.MarketOf(focus);
        var card = parent.BeginCard(width, "economy_market_title", WideKit.Gold,
            market == null ? LM.Get("diplomacy_market_pending") : MarketMap.Name(market.leader), "ui/icons/godpowers/market_layer");
        if (market == null)
        {
            card.AddParagraph(width, LM.Get("diplomacy_market_pending_hint"), 7, WideKit.Muted);
            return;
        }
        List<Kingdom> members = (realm ?? new[] { focus }).Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).ToList();
        float priceLevel = CommodityMarketSystem.PriceLevel(focus);
        (float exports, float imports, float customs) = CommodityMarketSystem.TradeOf(members);
        card.AddMetricRow(width,
            (LM.Get("economy_market_price"), priceLevel.ToString("0.00"),
                priceLevel >= 1.5f ? WideKit.Red : priceLevel >= 1.15f ? WideKit.Orange : WideKit.Green),
            (LM.Get("diplomacy_goods_export"), MoneyDisplay.Format(Mathf.RoundToInt(exports)), WideKit.Green),
            (LM.Get("diplomacy_goods_import"), MoneyDisplay.Format(Mathf.RoundToInt(imports)), WideKit.Cyan),
            (LM.Get("fiscal_source_Customs"), MoneyDisplay.Format(Mathf.RoundToInt(customs)), WideKit.Gold));

        string shortages = string.Join(" · ", CommodityMarketSystem.AllGoods
            .Where(good => market.shortage.TryGetValue(good.id, out float value) && value >= 0.05f)
            .OrderByDescending(good => market.shortage[good.id]).Take(3)
            .Select(good => $"{DiplomacyLedger.GoodName(good)} {Mathf.RoundToInt(market.shortage[good.id] * 100f)}%"));
        card.AddParagraph(width, string.Format(LM.Get("economy_market_short"),
            shortages.Length == 0 ? LM.Get("market_no_short") : shortages), 7, shortages.Length == 0 ? WideKit.Green : WideKit.Orange);

        City sample = focus.capital ?? focus.cities.FirstOrDefault();
        if (sample != null)
        {
            float output = 1f - CommodityMarketSystem.OutputFactor(sample);
            float necessity = -CommodityMarketSystem.NecessityStability(sample);
            float luxury = CommodityMarketSystem.LuxurySatisfaction(focus);
            card.AddParagraph(width, string.Format(LM.Get("economy_market_effects"), Mathf.RoundToInt(output * 100f),
                Mathf.RoundToInt(necessity), Mathf.RoundToInt(luxury * 100f)), 6, WideKit.Muted);
        }
        Kingdom target = focus;
        card.AddListRow(width, 0, null, LM.Get("economy_market_open"), "", () => MarketWindow.Open(target), WideKit.Gold, width - 20f);
    }
}

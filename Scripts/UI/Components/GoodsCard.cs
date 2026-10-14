using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 「物资产需」卡(帝国财政页、国情窗口共用，数据见 GoodsLedger)：
//   标题右侧：余粮 / 缺粮多少(一年)；
//   指标行：粮食年产、年需(吃掉的 + 没吃上的)、存粮(各城仓库 + 国家粮仓)、吃饱率；
//   表格：粮食一行在前，其余物资按产耗多少排，列出年产、年需、盈亏、库存(库存低于应有储备标橙)。
// 原版小人自己采集、吃饭不经过物资账，没开无小人模式时只列库存
public static class GoodsCard
{
    private const int MaxRows = 10;
    private const float NameWidth = 64f;

    public static void Draw(AutoVertLayoutGroup page, float width, IEnumerable<City> cities, float granary)
    {
        List<City> list = (cities ?? Enumerable.Empty<City>()).Where(city => city?.data != null && !city.isRekt()).ToList();
        GoodsSummary summary = GoodsLedger.Summarize(list);
        GoodsRow food = summary.food;
        string status;
        string statusHex;
        if (!summary.tracked)
        {
            status = LM.Get("goods_untracked_chip");
            statusHex = WideKit.Muted;
        }
        else if (food.balance >= 0f)
        {
            status = string.Format(LM.Get("goods_food_surplus"), Amount(food.balance));
            statusHex = WideKit.Green;
        }
        else
        {
            status = string.Format(LM.Get("goods_food_deficit"), Amount(-food.balance));
            statusHex = WideKit.Red;
        }
        if (summary.tracked && summary.estimated) status += LM.Get("goods_estimated");
        var card = page.BeginCard(width, "goods_title", WideKit.Green, status.ColorString(statusHex), "ui/icons/iconResWheat");

        float fed = food.need <= 0f ? 1f : Mathf.Clamp01(food.used / food.need);
        card.AddMetricRow(width,
            (LM.Get("goods_food_made"), summary.tracked ? Amount(food.made) : "-", WideKit.Green),
            (LM.Get("goods_food_need"), summary.tracked ? Amount(food.need) : "-", WideKit.Value),
            (LM.Get("goods_food_stock"), Amount(food.stock + granary), food.stock + granary < food.reserve ? WideKit.Orange : WideKit.Value),
            (LM.Get("goods_food_fed"), summary.tracked ? fed.ToString("P0") : "-", WideKit.Grade(fed, 0.95f, 0.8f)));

        card.AddTableRow(width, -1, null, LM.Get("goods_col_name"), NameWidth,
            (LM.Get("goods_col_made"), null), (LM.Get("goods_col_need"), null), (LM.Get("goods_col_balance"), null),
            (LM.Get("goods_col_stock"), null));
        int index = 0;
        Row(card, width, index++, food, summary.tracked, LM.Get("goods_food"), AssetManager.resources?.get("wheat")?.getSpriteIcon());
        foreach (GoodsRow row in summary.others.Take(MaxRows))
        {
            ResourceAsset asset = AssetManager.resources?.get(row.key);
            Row(card, width, index++, row, summary.tracked, asset?.getTranslatedName() ?? row.key, asset?.getSpriteIcon());
        }
        if (!summary.tracked)
        {
            var note = card.AddTextIntoVertLayout(LM.Get("goods_untracked_hint").ColorString(WideKit.Muted), true,
                TextAnchor.UpperLeft, new Vector2(width - 16f, 16f));
            note.UseFixedFontSize(6, HorizontalWrapMode.Wrap);
        }
    }

    private static void Row(AutoVertLayoutGroup card, float width, int index, GoodsRow row, bool tracked, string name, Sprite icon)
    {
        string stockHex = row.reserve > 0f && row.stock < row.reserve ? WideKit.Orange : WideKit.Value;
        if (!tracked)
        {
            card.AddTableRow(width, index, icon, name, NameWidth, ("-", WideKit.Muted), ("-", WideKit.Muted), ("-", WideKit.Muted),
                (Amount(row.stock), stockHex));
            return;
        }
        float balance = row.balance;
        string balanceHex = balance > 0.05f ? WideKit.Green : balance < -0.05f ? WideKit.Red : WideKit.Muted;
        card.AddTableRow(width, index, icon, name, NameWidth,
            (Amount(row.made), row.made > 0f ? WideKit.Value : WideKit.Muted),
            (Amount(row.need), row.shortage > 0.05f ? WideKit.Red : row.need > 0f ? WideKit.Value : WideKit.Muted),
            ((balance > 0.05f ? "+" : balance < -0.05f ? "-" : "") + Amount(Mathf.Abs(balance)), balanceHex),
            (Amount(row.stock), stockHex));
    }

    // 不到 10 留一位小数，大数按万、亿缩写
    private static string Amount(double value)
    {
        double absolute = global::System.Math.Abs(value);
        if (absolute < 0.05d) return "0";
        if (absolute < 10d) return value.ToString("0.#");
        return UIHelper.ShortCount(value);
    }
}

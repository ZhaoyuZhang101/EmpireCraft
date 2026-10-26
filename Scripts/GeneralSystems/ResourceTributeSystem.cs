using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 岁供(争夺资源之战的和约，见 WarObjective.SettleWin)：没打下产地而攻方占优议和的，败方每年从都城仓里送一份这种资源到胜方都城，
// 十年为期；仓里不够有多少送多少(不顺延)。牧场送皮革(马匹不是原版资源)。收方灭亡或两国再开战即作废。
public static class ResourceTributeSystem
{
    private const int Years = 10, Amount = 20;
    private const string PastureGoods = "leather";
    private static double _lastScan = -1d;

    public static void Impose(Kingdom debtor, Kingdom creditor, string resource)
    {
        if (debtor?.data == null || creditor?.data == null || debtor.isRekt() || creditor.isRekt() || debtor == creditor ||
            string.IsNullOrEmpty(resource)) return;
        string goods = resource == WarObjective.PastureResource ? PastureGoods : resource;
        (debtor.GetOrCreate().resource_tributes ??= new List<ResourceTribute>()).Add(new ResourceTribute
        {
            creditor_kingdom_id = creditor.id, resource = goods, amount = Amount, years_left = Years,
            last_paid = World.world?.getCurWorldTime() ?? -1d
        });
        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_resource_tribute"), debtor.GetKingdomName(),
            creditor.GetKingdomName(), WarCatalog.ResourceName(resource), Years), creditor);
    }

    // 每月看一次(挂 WarReparationsSystem 的月度扫描)，各笔满一年送一次
    public static void TryMonthlyScan()
    {
        if (World.world?.kingdoms == null) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getMonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Kingdom debtor in World.world.kingdoms)
        {
            List<ResourceTribute> list = debtor?.data == null || debtor.isRekt() ? null : debtor.GetOrCreate().resource_tributes;
            if (list == null || list.Count == 0) continue;
            for (int i = 0; i < list.Count; i++)
            {
                ResourceTribute item = list[i];
                Kingdom creditor = World.world.kingdoms.get(item.creditor_kingdom_id);
                if (item.years_left <= 0 || creditor?.data == null || creditor.isRekt() || debtor.isInWarWith(creditor))
                {
                    list.RemoveAt(i--);
                    continue;
                }
                if (item.last_paid >= 0d && now >= item.last_paid && Date.getYearsSince(item.last_paid) < 1) continue;
                item.last_paid = now;
                item.years_left--;
                try
                {
                    City from = debtor.capital, to = creditor.capital;
                    if (from?.data == null || to?.data == null) continue;
                    int paid = Math.Min(item.amount, Math.Max(0, from.getResourcesAmount(item.resource)));
                    if (paid <= 0) continue;
                    from.takeResource(item.resource, paid);
                    to.addResourcesToRandomStockpile(item.resource, paid);
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][岁供] {debtor.name} 送 {item.resource} 失败: {exception.Message}");
                }
            }
        }
    }
}

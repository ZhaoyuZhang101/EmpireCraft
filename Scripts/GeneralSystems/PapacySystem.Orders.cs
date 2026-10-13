using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 骑士团(宗教武装组织，存在宗教的 ReligionExtraData.Orders，每年随教权结算)：
//   · 成立：圣战告捷后有一半可能成立(每个宗教最多 3 个)，驻在号召国；名号按游戏里的地名取(某某圣殿骑士团、某某医院骑士团、某某骑士团)；
//   · 大团长：驻在国里兵略最好的成年人(不是君主)，死了另选；
//   · 团库：同教国家每年捐献(政教合一捐年度经常收入的 1%，国教限制 0.5%)，养军花销从团库出；
//   · 军：团库够养几支就养几支(最多 3 支，每支 2 万人)，驻在国都；攻防按 1.4 倍计、缺粮断供只受一半损耗，
//     不随驻在国动员复员(见 CorpsSystem)；驻在国有圣战时响应概率 +40%；
//   · 镇压：驻在国欠着国债、团库超过它两年经常收入，而教权不到 40 或教廷落在世俗强国手里，每年三成可能被镇压——
//     团库充公、在该国的军就地解散；教权不到 30 或教廷之囚时骑士团就此解散，否则迁到教廷另起炉灶；
//   · 骑士团国：在圣战中收复圣地而当时没有教廷，骑士团就在圣地立国，大团长为君(原型条顿骑士团)。
public static partial class PapacySystem
{
    private const int MaxOrders = 3;
    private const int MaxOrderCorps = 3;
    private const float OrderCorpsPeople = 20000f;
    private const float OrderFoundChance = 0.5f;
    private const float OrderStrength = 1.4f;
    private const float SuppressChance = 0.3f;

    public static float OrderFactor(CorpsData corps) => corps != null && corps.order_id >= 0 ? OrderStrength : 1f;

    public static IEnumerable<KnightOrderData> Orders(Religion religion) =>
        religion?.data == null ? Enumerable.Empty<KnightOrderData>()
            : (religion.GetOrCreate().Orders ??= new List<KnightOrderData>()).Where(order => !order.dissolved);

    public static KnightOrderData FindOrder(long id)
    {
        foreach (Religion religion in World.world.religions)
            foreach (KnightOrderData order in Orders(religion))
                if (order.id == id) return order;
        return null;
    }

    // 圣战告捷后成立骑士团
    private static void TryFoundOrder(Religion religion, Kingdom host)
    {
        if (host?.data == null || host.isRekt() || Orders(religion).Count() >= MaxOrders ||
            UnityEngine.Random.value >= OrderFoundChance) return;
        var data = religion.GetOrCreate();
        data.Orders ??= new List<KnightOrderData>();
        int index = data.Orders.Count % 3 + 1;
        string place = index == 1 ? religion.GetCity()?.GetCityName() ?? host.capital?.GetCityName()
            : host.capital?.GetCityName() ?? host.GetKingdomName();
        var order = new KnightOrderData
        {
            id = OverallHelperFunc.IdGenerator.NextId(),
            name = string.Format(LM.Get("knight_order_name_" + index), place),
            host_kingdom_id = host.id,
            founded_at = World.world.getCurWorldTime()
        };
        data.Orders.Add(order);
        PickGrandMaster(order, host);
        RecordReligion(host, string.Format(LM.Get("knight_order_founded_history"), order.name, host.GetKingdomName()));
        TranslateHelper.LogEventMessage(string.Format(LM.Get("knight_order_founded_history"), order.name,
            host.GetKingdomName()), host);
    }

    private static void PickGrandMaster(KnightOrderData order, Kingdom host)
    {
        Actor best = null;
        foreach (Actor actor in host.units)
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult() || actor.isKing() ||
                actor.asset == null || !actor.asset.civ || CityPopulationSystem.IsVehicle(actor)) continue;
            if (best == null || actor.warfare > best.warfare) best = actor;
        }
        order.grand_master_id = best?.id ?? -1L;
        best?.RecordPersonalHistory(string.Format(LM.Get("knight_order_master_history"), order.name));
    }

    public static Actor GrandMaster(KnightOrderData order)
    {
        Actor actor = order?.grand_master_id >= 0 ? World.world.units.get(order.grand_master_id) : null;
        return actor?.data != null && !actor.isRekt() && actor.isAlive() ? actor : null;
    }

    public static Kingdom Host(KnightOrderData order)
    {
        Kingdom host = order?.host_kingdom_id >= 0 ? World.world.kingdoms.get(order.host_kingdom_id) : null;
        return host?.data != null && !host.isRekt() && !host.wild ? host : null;
    }

    public static List<CorpsData> OrderCorps(KnightOrderData order)
    {
        Kingdom host = Host(order);
        return host == null ? new List<CorpsData>() : CorpsSystem.Of(host).Where(corps => corps.order_id == order.id).ToList();
    }

    // 驻在国有骑士团时响应圣战更积极
    private static float OrderHostBonus(Kingdom kingdom, Religion religion) =>
        Orders(religion).Any(order => Host(order) is Kingdom host && Realm(host) == Realm(kingdom)) ? 0.4f : 0f;

    // 每年(随教权结算)：大团长、捐献、养军、镇压
    private static void UpdateOrders(Religion religion)
    {
        foreach (KnightOrderData order in Orders(religion).ToList())
        {
            try
            {
                UpdateOrder(religion, order);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][骑士团] {order.name} 结算失败: {exception.Message}");
            }
        }
    }

    private static void UpdateOrder(Religion religion, KnightOrderData order)
    {
        Kingdom host = Host(order);
        if (host == null || host.religion != religion)
        {
            Kingdom papacy = Papacy(religion);
            if (papacy == null || papacy == host)
            {
                Dissolve(religion, order, LM.Get("knight_order_dissolved_homeless"));
                return;
            }
            order.host_kingdom_id = papacy.id;
            host = papacy;
        }
        if (GrandMaster(order) is not Actor master || master.kingdom != host) PickGrandMaster(order, host);
        // 捐献
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.religion != religion ||
                Realm(kingdom) != kingdom) continue;
            float rate = kingdom.GetRegime()?.GetReligionLevel() switch
            {
                ReligionLevel.High => 0.01f,
                ReligionLevel.Medium => 0.005f,
                _ => 0f
            };
            if (rate <= 0f) continue;
            long gift = Math.Min(Math.Max(0L, (long)kingdom.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(kingdom) * rate));
            if (gift <= 0) continue;
            kingdom.SubMoney((int)gift, TreasuryCategory.Gift);
            order.treasury += gift;
        }
        // 养军：每支军每年按人头付饷(同野战军)，付不起就解散一支
        List<CorpsData> corps = OrderCorps(order);
        bool war = CorpsSystem.IsAtWar(host);
        long upkeep = (long)corps.Sum(item => CityStabilityRules.CorpsAnnual(item.strength, war));
        order.treasury -= upkeep;
        if (order.treasury < 0 && corps.Count > 0)
        {
            CorpsSystem.Disband(host, corps[corps.Count - 1]);
            corps.RemoveAt(corps.Count - 1);
            order.treasury = 0;
        }
        long perCorps = (long)Math.Max(1d, CityStabilityRules.CorpsAnnual(OrderCorpsPeople, false) * 2d);
        int affordable = (int)Mathf.Clamp(order.treasury / perCorps, 0, MaxOrderCorps);
        City seat = host.capital;
        if (seat != null)
            for (int i = corps.Count; i < affordable; i++)
                if (CorpsSystem.RaiseOrderCorps(host, seat, OrderCorpsPeople, order.id) is CorpsData raised) corps.Add(raised);
        foreach (CorpsData item in corps)
            if (item.strength < OrderCorpsPeople) CorpsSystem.ReinforceOrderCorps(host, item, (OrderCorpsPeople - item.strength) * 0.5f);
        TrySuppress(religion, order, host);
    }

    private static void TrySuppress(Religion religion, KnightOrderData order, Kingdom host)
    {
        Kingdom realm = Realm(host);
        if (realm == null || realm == Papacy(religion)) return;
        float authority = Authority(religion);
        bool weakChurch = authority < 40f || IsCaptive(religion);
        long income = StateDebtSystem.AnnualStableIncome(realm);
        if (!weakChurch || StateDebtSystem.Outstanding(realm) <= 0 || order.treasury <= income * 2 ||
            UnityEngine.Random.value >= SuppressChance) return;
        long seized = order.treasury;
        order.treasury = 0;
        if (seized > 0) realm.AddMoney((int)Math.Min(int.MaxValue, seized), TreasuryCategory.RecoveredFunds);
        foreach (CorpsData item in OrderCorps(order)) CorpsSystem.Disband(host, item);
        RecordReligion(realm, string.Format(LM.Get("knight_order_suppressed_history"), realm.GetKingdomName(), order.name,
            MoneyDisplay.Format(seized)));
        Kingdom papacy = Papacy(religion);
        if (authority < 30f || IsCaptive(religion) || papacy == null || Realm(papacy) == realm)
            Dissolve(religion, order, string.Format(LM.Get("knight_order_dissolved_suppressed"), order.name));
        else order.host_kingdom_id = papacy.id;
    }

    private static void Dissolve(Religion religion, KnightOrderData order, string text)
    {
        Kingdom host = Host(order);
        if (host != null)
            foreach (CorpsData item in OrderCorps(order)) CorpsSystem.Disband(host, item);
        order.dissolved = true;
        if (host != null) RecordReligion(host, text.Contains("{0}") ? string.Format(text, order.name) : text);
    }

    // 圣战收复圣地而当时没有教廷：骑士团在圣地立国，大团长为君
    private static bool TryFoundOrderState(Religion religion, City holy)
    {
        KnightOrderData order = Orders(religion).FirstOrDefault(item => item.state_kingdom_id < 0 && Host(item) != null);
        Actor master = order == null ? null : GrandMaster(order);
        if (order == null || master == null || holy?.data == null) return false;
        Kingdom former = Host(order);
        master.joinCity(holy);
        Kingdom state = holy.makeOwnKingdom(master, false, false);
        if (state?.data == null) return false;
        holy.setReligion(religion);
        state.data.name = order.name;
        order.state_kingdom_id = state.id;
        foreach (CorpsData item in OrderCorps(order)) CorpsSystem.Disband(former, item);
        order.host_kingdom_id = state.id;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("knight_order_state_history"), order.name, holy.GetCityName()), state);
        RecordReligion(former, string.Format(LM.Get("knight_order_state_history"), order.name, holy.GetCityName()));
        return true;
    }
}

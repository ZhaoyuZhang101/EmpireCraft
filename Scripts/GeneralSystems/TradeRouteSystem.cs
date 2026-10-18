using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 海上商路(无小人模式)：建成港口的城市与最近的外国港口城结成商路，每月按双方富庶度产生贸易利得——
//   双方民间资产各记一笔贸易利润(与生产估值同口径，之后由商业税基征税)，
//   并按商业税率就地征一笔关税：从民间现金扣，入城市国库(商业税)；没有现金就征不到。
//   两国交战即断航(禁运)，任一港口被敌方舰队封锁也断航；港口被拆、城市易主重新配对。配对每年重算一次，结算每月一次。
//   只与同一片海上的港口配对(码头旁的海面属于同一水域；以前按直线距离，隔着陆地的内湖、两洋也配上)；
//   锁国的(制度特性 closed_country，如日本锁国令)不与外国通商；
//   被迫开港(和约，见 WarGoalSystem 通商)的二十年里锁国也开，港口优先与胜者的港口配对，两边都得利。
public static class TradeRouteSystem
{
    private const float MaxRouteDistance = 160f;
    private const float BaseTrade = 2f;
    private const float WealthTrade = 10f;
    private static double _lastSettle = -1d;
    private static double _lastPairing = -1d;
    // 城市 id → 贸易伙伴城市 id(每年重算)
    private static readonly Dictionary<long, long> Routes = new();
    private static MapBox _world;

    public static long PartnerOf(City city) =>
        city?.data != null && Routes.TryGetValue(city.data.id, out long partner) ? partner : -1L;

    private static float TradeValue(City a, City b) =>
        BaseTrade + WealthTrade * (CityConstructionSystem.Wealth(a) + CityConstructionSystem.Wealth(b)) * 0.5f;

    // 城市悬停提示：有商路时给一行"伙伴城市（+N/月）"，交战中标注禁运
    public static string Describe(City city)
    {
        City partner = World.world?.cities?.get(PartnerOf(city));
        if (partner?.data == null || partner.isRekt() || city?.kingdom == null || partner.kingdom == null) return null;
        if (city.kingdom.isInWarWith(partner.kingdom))
            return string.Format(LM.Get("trade_route_embargo"), partner.GetCityName());
        if (NavySystem.IsBlockaded(city) || NavySystem.IsBlockaded(partner))
            return string.Format(LM.Get("trade_route_blockaded"), partner.GetCityName());
        return string.Format(LM.Get("trade_route_status"), partner.GetCityName(), (long)TradeValue(city, partner));
    }

    public static void TryMonthlyScan()
    {
        if (World.world?.cities == null || ModClass.IS_CLEAR || !CityPopulationSystem.AbstractPopulationEnabled) return;
        if (!ReferenceEquals(_world, World.world)) { Routes.Clear(); _lastPairing = -1d; _world = World.world; }
        double now = World.world.getCurWorldTime();
        if (_lastSettle >= 0d && now >= _lastSettle && Date.getMonthsSince(_lastSettle) < 1) return;
        _lastSettle = now;
        try
        {
            if (_lastPairing < 0d || now < _lastPairing || Date.getYearsSince(_lastPairing) >= 1)
            {
                _lastPairing = now;
                Pair();
            }
            Settle();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][商路] 结算失败: {exception.Message}");
        }
    }

    private static bool Tradeable(City city) =>
        city?.data != null && !city.isRekt() && city.kingdom != null && !city.kingdom.wild &&
        TreasurySystem.Enabled(city) &&
        city.getBuildingOfType("type_docks", pCountOnlyFinished: true, pRandom: false) != null;

    // 每城找最近的外国港口城(直线距离近似海程；每年一次，港口城数量有限)
    private static void Pair()
    {
        Routes.Clear();
        var ports = new List<City>();
        var seas = new Dictionary<City, object>();
        foreach (City city in World.world.cities)
            if (Tradeable(city) && !Closed(city.kingdom))
            {
                ports.Add(city);
                seas[city] = SeaOf(city);
            }
        foreach (City city in ports)
        {
            City best = null;
            float bestDistance = MaxRouteDistance * MaxRouteDistance;
            Vector2 from = city.city_center;
            object sea = seas[city];
            // 被迫开港的先与胜者的港口配对
            Kingdom partner = TreatyPartner(city.kingdom);
            bool partnerReachable = partner != null && ports.Any(other => other.kingdom == partner &&
                                                                         (sea == null || seas[other] == null || seas[other] == sea));
            foreach (City other in ports)
            {
                if (other == city || other.kingdom == city.kingdom) continue;
                if (partnerReachable && other.kingdom != partner) continue;
                if (sea != null && seas[other] != null && seas[other] != sea) continue;
                Vector2 to = other.city_center;
                float distance = (to - from).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = other;
            }
            if (best != null) Routes[city.data.id] = best.data.id;
        }
    }

    // 港口所在的那片海：码头所在或紧挨着的海面所属的水域；找不到返回 null(那就照旧按距离配)
    private static object SeaOf(City city)
    {
        Building docks = city.getBuildingOfType("type_docks", pCountOnlyFinished: true, pRandom: false);
        WorldTile tile = docks?.current_tile;
        if (tile == null) return null;
        if (tile.Type?.ocean == true && tile.region?.island != null) return tile.region.island;
        foreach (WorldTile next in tile.neighboursAll ?? Array.Empty<WorldTile>())
            if (next?.Type?.ocean == true && next.region?.island != null) return next.region.island;
        return null;
    }

    private const int TreatyYears = 20;

    public static bool HasPort(Kingdom kingdom) => kingdom?.cities?.Any(Tradeable) == true;

    // 和约逼开港：二十年里与胜者通商(锁国也开)
    public static bool ImposeTreaty(Kingdom loser, Kingdom winner)
    {
        if (!HasPort(loser) || !HasPort(winner) || loser == winner) return false;
        var data = loser.GetOrCreate();
        data.trade_treaty_partner_id = winner.id;
        data.trade_treaty_at = World.world.getCurWorldTime();
        _lastPairing = -1d;
        return true;
    }

    private static Kingdom TreatyPartner(Kingdom kingdom)
    {
        if (kingdom?.data == null || World.world == null) return null;
        var data = kingdom.GetOrCreate();
        if (data.trade_treaty_partner_id < 0 || data.trade_treaty_at < 0d ||
            Date.getYearsSince(data.trade_treaty_at) >= TreatyYears) return null;
        Kingdom partner = World.world.kingdoms.get(data.trade_treaty_partner_id);
        return partner?.data != null && !partner.isRekt() ? partner : null;
    }

    private static bool Treaty(Kingdom a, Kingdom b) => TreatyPartner(a) == b || TreatyPartner(b) == a;

    // 锁国：不与外国通商(被迫开港的不算)
    public static bool Closed(Kingdom kingdom)
    {
        if (TreatyPartner(kingdom) != null) return false;
        string culture = CultureService.GetRealmCulture(kingdom);
        return CultureService.IsValidCulture(culture) && InstitutionSystem.GetFeature(culture, "closed_country") > 0f;
    }

    private static void Settle()
    {
        foreach (KeyValuePair<long, long> route in Routes)
        {
            City city = World.world.cities.get(route.Key);
            City partner = World.world.cities.get(route.Value);
            bool treaty = Treaty(city.kingdom, partner.kingdom);
            if (!Tradeable(city) || !Tradeable(partner) || city.kingdom == partner.kingdom ||
                !treaty && (Closed(city.kingdom) || Closed(partner.kingdom))) continue;
            // 交战禁运；任一港口被敌方舰队封锁也断航(见 NavySystem)
            if (city.kingdom.isInWarWith(partner.kingdom) || NavySystem.IsBlockaded(city) ||
                NavySystem.IsBlockaded(partner)) continue;
            Earn(city, TradeValue(city, partner));
            // 和约商路：胜者那边也得利
            if (treaty) Earn(partner, TradeValue(partner, city));
        }
    }

    // 贸易利得记进民间资产(与生产估值同口径)；关税按商业税率从民间现金征，入城市国库
    private static void Earn(City city, float trade)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data == null) return;
        PopulationEconomySystem.AddSavings(city, data, trade);
        double rate = SectorTaxRules.Rate(city.kingdom.GetTaxRate(), TreasuryCategory.CommercialTax);
        long tariff = Math.Min((long)Math.Floor(trade * rate), EnterpriseSystem.Cash(data));
        if (tariff <= 0L) return;
        EnterpriseSystem.TakeCash(data, tariff);
        TreasurySystem.CollectResidentTax(city, (int)Math.Min(int.MaxValue, tariff), TreasuryCategory.CommercialTax);
    }
}

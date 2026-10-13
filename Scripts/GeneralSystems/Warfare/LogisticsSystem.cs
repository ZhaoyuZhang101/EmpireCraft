using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 后勤补给(每月，CorpsSystem 月度结算里逐国调用，先于休整与补员)：
//   · 补给程度(0~1)：在本国、友军控制区或野地为满；深入敌境按离最近己方控制区几块地递减(两块内不减，之后每块 -12%，
//     山地、丛林、沼泽、沙漠、雪原再打八折)，12 块内找不到己方控制区(被围)为 0；
//     海外靠海路：回国航线被敌方制海截断为 0，否则最多七成五，超出舰种航程只剩四成；在船上八成；
//     粮草官按治事加成(每点 +1%，最多 +20%，被围的不加)；就地征粮期间在敌境的军 +35%；
//   · 军粮：各军按人头每月吃粮(每户每年 1.25 份，与百姓同一尺度；粮草官按治事省一些)，
//     就地征粮期间在敌城的军先从所在敌城征(那座城每月掉稳定)，其余先从国家粮仓扣，不够再从本国各城口粮线(每户一份)以上的余粮里调；
//     调不够的按缺口比例口粮不足；
//   · 效果：战力 × (0.6 + 0.4×补给)(见 FrontSystem)；补给不到五成组织度不恢复反而下降，也补不上兵(见 CorpsSystem)；
//     交战时补给不到三成或口粮不足就减员，每月 3%~8%(越缺越多)；太平时缺粮只掉组织度；
//   · 就地征粮是统帅的决议(见 PlotAsset war_forage)：交战中缺粮、或三分之一以上的军在敌境时，统帅发起，为期半年。
public static class LogisticsSystem
{
    private const float RationPerHouseholdYear = 1.25f;
    private const int FullSupplyDepth = 2;
    private const float SupplyPerZone = 0.12f;
    private const int MaxSupplyDepth = 12;
    private const float RoughTerrain = 0.8f;
    private const float OverseasCap = 0.75f, OutOfRange = 0.4f, AtSea = 0.8f;
    private const float ForageBonus = 0.35f;
    public const int ForageMonths = 6;
    private const float ForageStability = 4f;
    public const string ForagePlot = "war_forage";
    // 原版一个月是 5 个世界秒(见 Date.getMonthsSince)
    private const double SecondsPerMonth = 5d;

    // 战力：补给满时不打折，断绝时六成
    public static float AttackFactor(CorpsData corps) => 0.6f + 0.4f * Mathf.Clamp01(corps?.supply ?? 1f);

    public static bool Foraging(Kingdom kingdom)
    {
        Kingdom realm = FrontSystem.Realm(kingdom);
        double until = realm?.data == null ? -1d : realm.GetOrCreate().forage_until;
        return until > 0d && World.world != null && World.world.getCurWorldTime() < until;
    }

    public static void MonthTick(Kingdom kingdom)
    {
        List<CorpsData> list = CorpsSystem.Of(kingdom);
        if (list.Count == 0) return;
        bool foraging = Foraging(kingdom);
        float bonus = WarCommandSystem.QuartermasterSupply(kingdom);
        foreach (CorpsData corps in list)
        {
            float supply = Supply(kingdom, corps, foraging);
            corps.supply = supply <= 0f ? 0f : Mathf.Min(1f, supply + bonus);
        }
        Feed(kingdom, list, foraging);
        Attrition(kingdom, list);
        if (FrontSystem.Realm(kingdom) == kingdom) TryForage(kingdom);
    }

    private static float Supply(Kingdom kingdom, CorpsData corps, bool foraging)
    {
        if (corps.embarked) return AtSea;
        TileZone zone = CorpsSystem.Zone(corps);
        if (zone == null) return 1f;
        float supply = 1f;
        Kingdom controller = FrontSystem.Controller(zone);
        if (controller != null && !FrontSystem.Friendly(kingdom, controller))
        {
            int depth = DepthToFriendly(kingdom, zone);
            supply = depth < 0 ? 0f : depth <= FullSupplyDepth ? 1f : Mathf.Max(0f, 1f - SupplyPerZone * (depth - FullSupplyDepth));
            if (TerrainSystem.Of(zone) is FrontTerrain.Mountain or FrontTerrain.Jungle or FrontTerrain.Swamp or
                FrontTerrain.Desert or FrontTerrain.Snow) supply *= RoughTerrain;
            if (foraging && supply > 0f) supply = Mathf.Min(1f, supply + ForageBonus);
        }
        // 海外：补给走海路
        City home = CorpsSystem.Home(corps);
        if (home != null && !CorpsSystem.SameLand(zone, home.getTile()?.zone))
        {
            if (NavySystem.IsCutOff(kingdom, corps)) return 0f;
            List<SeaRegionSystem.Region> route = zone.city != null ? NavySystem.LandingRoute(kingdom, zone.city) : null;
            supply = Mathf.Min(supply, route != null && route.Count > NavySystem.MaxRange(kingdom) ? OutOfRange : OverseasCap);
        }
        return supply;
    }

    // 离最近的己方(含友军)控制区隔几块地；12 块内找不到返回 -1(被围或孤军深入)
    private static int DepthToFriendly(Kingdom kingdom, TileZone start)
    {
        var seen = new HashSet<int> { start.id };
        var frontier = new List<TileZone> { start };
        for (int depth = 1; depth <= MaxSupplyDepth && frontier.Count > 0; depth++)
        {
            var next = new List<TileZone>();
            foreach (TileZone zone in frontier)
                foreach (TileZone neighbour in zone.neighbours_all ?? Array.Empty<TileZone>())
                {
                    if (neighbour == null || !seen.Add(neighbour.id) || SeaRegionSystem.IsSea(neighbour)) continue;
                    Kingdom controller = FrontSystem.Controller(neighbour);
                    if (controller != null && FrontSystem.Friendly(kingdom, controller)) return depth;
                    if (!TerrainSystem.Impassable(neighbour)) next.Add(neighbour);
                }
            frontier = next;
        }
        return -1;
    }

    private static float Ration(CorpsData corps)
    {
        int perSlot = CityPopulationSystem.PeoplePerSlot(CorpsSystem.Home(corps));
        return Mathf.Max(0f, corps.strength) / Mathf.Max(1, perSlot) * RationPerHouseholdYear / 12f;
    }

    private static void Feed(Kingdom kingdom, List<CorpsData> list, bool foraging)
    {
        float saving = WarCommandSystem.QuartermasterSaving(kingdom);
        float total = 0f, remaining = 0f;
        var pending = new List<(CorpsData corps, float need)>();
        foreach (CorpsData corps in list)
        {
            float need = Ration(corps) * (1f - saving);
            total += need;
            corps.rations = 1f;
            if (need <= 0f) continue;
            // 就地征粮：在敌城的军先从所在城征
            City city = foraging && !corps.embarked ? CorpsSystem.Zone(corps)?.city : null;
            if (city?.data != null && !city.isRekt() && FrontSystem.Hostile(kingdom, city.kingdom))
            {
                int taken = PopulationEconomySystem.TakeFood(city, Mathf.CeilToInt(need));
                if (taken > 0)
                {
                    CityStabilityData state = CityStabilitySystem.State(city);
                    if (state != null) state.stability = Mathf.Max(0f, state.stability - ForageStability);
                }
                need = Mathf.Max(0f, need - taken);
            }
            if (need <= 0f) continue;
            pending.Add((corps, need));
            remaining += need;
        }
        float supplied = 0f;
        if (remaining > 0f && GranarySystem.Enabled(kingdom))
        {
            Kingdom realm = GranarySystem.Realm(kingdom);
            var data = realm.GetOrCreate();
            float take = Mathf.Min(data.granary, remaining);
            data.granary -= take;
            supplied += take;
        }
        if (supplied < remaining && kingdom.cities != null)
        {
            foreach (City city in kingdom.cities.Where(city => city?.data != null && !city.isRekt())
                         .OrderByDescending(city => city.getTotalFood() - CityPopulationSystem.Households(city)).ToList())
            {
                float surplus = city.getTotalFood() - CityPopulationSystem.Households(city);
                if (surplus < 1f) break;
                supplied += PopulationEconomySystem.TakeFood(city, Mathf.FloorToInt(Mathf.Min(surplus, remaining - supplied)));
                if (supplied >= remaining) break;
            }
        }
        float ratio = remaining <= 0f ? 1f : Mathf.Clamp01(supplied / remaining);
        foreach ((CorpsData corps, float need) in pending) corps.rations = ratio;
        var kingdomData = kingdom.GetOrCreate();
        kingdomData.war_food_need = total;
        kingdomData.war_food_short = Mathf.Max(0f, remaining - supplied);
    }

    private static void Attrition(Kingdom kingdom, List<CorpsData> list)
    {
        bool war = CorpsSystem.IsAtWar(kingdom);
        int starving = 0;
        foreach (CorpsData corps in list)
        {
            float hunger = 1f - Mathf.Clamp01(corps.rations);
            float cut = corps.supply < 0.3f ? (0.3f - corps.supply) / 0.3f : 0f;
            float severity = Mathf.Max(hunger, cut);
            if (severity <= 0.01f) continue;
            corps.org = Mathf.Max(0f, corps.org - 10f * severity);
            if (!war) continue;
            CorpsSystem.Casualties(corps, corps.strength * (0.03f + 0.05f * severity));
            starving++;
        }
        if (starving > 0) FrontSystem.Log(kingdom, string.Format(LM.Get("corps_log_attrition"), starving));
    }

    // 交战中缺粮、或三分之一以上能上阵的军在敌境时，统帅发起"就地征粮"
    private static void TryForage(Kingdom realm)
    {
        if (Foraging(realm) || !realm.hasEnemies()) return;
        Actor commander = WarCommandSystem.Commander(realm);
        if (commander == null || commander.plot != null) return;
        if (!ForageWanted(realm)) return;
        PlotAsset asset = AssetManager.plots_library.get(ForagePlot);
        if (asset == null || World.world?.plots == null) return;
        World.world.plots.newPlot(commander, asset, false);
    }

    private static bool ForageWanted(Kingdom realm)
    {
        if (realm.GetOrCreate().war_food_short > 0f) return true;
        List<CorpsData> list = CorpsSystem.Of(realm);
        if (list.Count == 0) return false;
        int abroad = list.Count(corps => CorpsSystem.Zone(corps) is TileZone zone &&
                                         FrontSystem.Controller(zone) is Kingdom controller && FrontSystem.Hostile(realm, controller));
        return abroad * 3 >= list.Count;
    }

    // 决议：发起人须是本国统帅，本国在打仗、还没在征粮
    public static bool CanForage(Actor actor, bool needy)
    {
        Kingdom realm = FrontSystem.Realm(actor?.kingdom);
        if (realm?.data == null || realm.isRekt() || WarCommandSystem.Commander(realm) != actor ||
            !realm.hasEnemies() || Foraging(realm)) return false;
        return !needy || ForageWanted(realm);
    }

    public static bool Forage(Actor actor)
    {
        if (!CanForage(actor, needy: false)) return false;
        Kingdom realm = FrontSystem.Realm(actor.kingdom);
        realm.GetOrCreate().forage_until = World.world.getCurWorldTime() + ForageMonths * SecondsPerMonth;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_forage_ordered"), realm.GetKingdomName(),
            WarCommandSystem.TitleOf(realm), actor.getName()), realm);
        return true;
    }
}

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
//   · 补给线(0~1)：从军所在的地块往后方找一条运粮的路，到最近的己方(含友军)城为止，沿途每块地折损一点——
//     有路的地块折损小(路越好越小)，路外大，山地、丛林、沼泽、沙漠、雪原的路外再加重；己方控制区折损最小、无主地次之；
//     敌军控制的地块只有阵地前沿两块能穿过；本国过不去的无人区(见 WildernessPassage，用户 2026-10-29)不经过，有路的、
//     惯走的照走，军困在无人区里的(区域刚划出来时)可以经无人区运出去；
//     找不到这样的路(被围、孤军深入、后路被截断)就是补给线被切断，补给为 0；
//     海外靠海路：补给线可以到这片陆地上本国船到得了的岸边(己方控制、无主地或军脚下的海滩，从海上卸粮，见 SeaLanding)，
//     最多七成五，超出舰种航程只剩四成；岸外海区都被敌方制海截断、或走不到岸边为 0；在船上八成；
//     粮草官按治事加成(每点 +1%，最多 +20%，被围的不加)；就地征粮期间在敌境的军 +35%；
//   · 军粮：各军按人头每月吃粮(每户每年 1.25 份，与百姓同一尺度；粮草官按治事省一些)，
//     就地征粮期间在敌城的军先从所在敌城征(那座城每月掉稳定)；其余就近调粮——先从离这支军最近的几座本国城
//     口粮线(每户一份)以上的余粮里调，再从国家粮仓扣，还不够再从全国各城的余粮里调；
//     调不够的按缺口比例口粮不足；
//   · 效果：战力 × (0.35 + 0.65×补给)(见 FrontSystem)，补给线被切断战力只剩三成五；补给不到五成组织度不恢复反而下降，
//     也补不上兵(见 CorpsSystem)；补给线被切断、恢复时记入纪事；
//     交战时补给不到三成或口粮不足就减员，每月 3%~8%(越缺越多)；太平时缺粮只掉组织度；
//   · 就地征粮是统帅的决议(见 PlotAsset war_forage)：交战中缺粮、或三分之一以上的军在敌境时，统帅发起，为期半年。
public static class LogisticsSystem
{
    private const float RationPerHouseholdYear = 1.25f;
    // 就近调粮的城数
    private const int NearbyGranaries = 3;
    // 补给线：敌占的阵地前沿能穿过几块；最多找多远、展开几块
    private const int FrontlineDepth = 2, MaxSupplyDepth = 30, MaxSupplySearch = 1500;
    // 每块地的折损：己方控制区、无主地、敌占前沿；有路时按路的等级再减，山地等路外再加重
    private const float FriendlyLoss = 0.04f, NeutralLoss = 0.1f, EnemyLoss = 0.25f, RoadShare = 0.3f, RoughLoss = 1.5f;
    private const float OverseasCap = 0.75f, OutOfRange = 0.4f, AtSea = 0.8f;
    private const float ForageBonus = 0.35f;
    public const int ForageMonths = 6;
    private const float ForageStability = 4f;
    public const string ForagePlot = "war_forage";
    // 原版一个月是 5 个世界秒(见 Date.getMonthsSince)
    private const double SecondsPerMonth = 5d;

    // 战力：补给满时不打折，补给线被切断时只剩三成五
    public static float AttackFactor(CorpsData corps) => 0.35f + 0.65f * Mathf.Clamp01(corps?.supply ?? 1f);

    public static bool Foraging(Kingdom kingdom)
    {
        Kingdom realm = FrontSystem.Realm(kingdom);
        double until = realm?.data == null ? -1d : realm.GetOrCreate().forage_until;
        return until > 0d && World.world != null && World.world.getCurWorldTime() < until;
    }

    // 一国一月的补给结算里：各控制方与本国的关系(友、敌、都不是)只判一次；同一块地、同样走不走海路的补给线只找一次
    // (几支军常挤在同一块地上)。都只在这一国这一次结算里有效
    private const sbyte RelationFriendly = 1, RelationHostile = 2, RelationNeither = 3;
    private static readonly Dictionary<long, sbyte> Relations = new();
    private static readonly Dictionary<int, float> SupplyByZone = new();

    private static sbyte Relation(Kingdom kingdom, Kingdom other)
    {
        if (other == null) return RelationNeither;
        if (Relations.TryGetValue(other.id, out sbyte known)) return known;
        sbyte relation = FrontSystem.Friendly(kingdom, other) ? RelationFriendly
            : FrontSystem.Hostile(kingdom, other) ? RelationHostile : RelationNeither;
        Relations[other.id] = relation;
        return relation;
    }

    public static void MonthTick(Kingdom kingdom)
    {
        List<CorpsData> list = CorpsSystem.Of(kingdom);
        if (list.Count == 0) return;
        bool foraging = Foraging(kingdom);
        float bonus = WarCommandSystem.QuartermasterSupply(kingdom);
        Relations.Clear();
        SupplyByZone.Clear();
        foreach (CorpsData corps in list)
        {
            float supply = Supply(kingdom, corps, foraging);
            bool wasCut = corps.supply <= 0f;
            corps.supply = supply <= 0f ? 0f : Mathf.Min(1f, supply + bonus);
            if (wasCut != corps.supply <= 0f && !corps.embarked)
                FrontSystem.Log(kingdom, string.Format(LM.Get(wasCut ? "corps_log_supply_restored" : "corps_log_supply_cut"),
                    CorpsSystem.Name(corps)));
        }
        Relations.Clear();
        SupplyByZone.Clear();
        Feed(kingdom, list, foraging);
        Attrition(kingdom, list);
        if (FrontSystem.Realm(kingdom) == kingdom) TryForage(kingdom);
    }

    private static float Supply(Kingdom kingdom, CorpsData corps, bool foraging)
    {
        if (corps.embarked) return AtSea;
        TileZone zone = CorpsSystem.Zone(corps);
        if (zone == null) return 1f;
        // 海外：补给走海路，从这片陆地上本国船到得了的岸边卸粮(见 SupplyLine、SeaLanding.Reachable)。
        // 以前看军脚下那座城临不临海，海外的军一进内陆城的地块就算断了补给(玩家反馈"殖民地要等陆地连上了才打得动")
        City home = CorpsSystem.Home(corps);
        Dictionary<int, int> sea = null;
        if (home != null && !CorpsSystem.SameLand(zone, home.getTile()?.zone)) sea = SeaLanding.Reachable(kingdom);
        // 补给线只看起点与走不走海路(海路、航程每国一样)
        int key = zone.id * 2 + (sea != null ? 1 : 0);
        if (!SupplyByZone.TryGetValue(key, out float supply))
        {
            supply = SupplyLine(kingdom, zone, sea, NavySystem.MaxRange(kingdom));
            SupplyByZone[key] = supply;
        }
        Kingdom controller = FrontSystem.Controller(zone);
        if (foraging && supply > 0f && controller != null && Relation(kingdom, controller) == RelationHostile)
            supply = Mathf.Min(1f, supply + ForageBonus);
        return supply;
    }

    // 补给线最短路的工作数组(按地块编号，记趟次不清空)与堆：以前每找一次新建三个字典、集合和一个列表
    private static int _pass, _closedCount;
    private static int[] _seen = new int[0], _closed = new int[0], _steps = new int[0];
    private static float[] _cost = new float[0];
    private static readonly List<(float key, TileZone zone)> Open = new();

    private static void BeginPass()
    {
        int size = World.world?.zone_calculator?.zones?.Count ?? 0;
        if (_seen.Length < size) Grow(size);
        _closedCount = 0;
        Open.Clear();
        if (++_pass < int.MaxValue) return;
        Array.Clear(_seen, 0, _seen.Length);
        Array.Clear(_closed, 0, _closed.Length);
        _pass = 1;
    }

    private static void Grow(int size)
    {
        Array.Resize(ref _seen, size);
        Array.Resize(ref _closed, size);
        Array.Resize(ref _steps, size);
        Array.Resize(ref _cost, size);
    }

    private static void Reach(TileZone zone, float cost, int steps)
    {
        if (zone.id >= _seen.Length) Grow(zone.id + 1);
        _seen[zone.id] = _pass;
        _cost[zone.id] = cost;
        _steps[zone.id] = steps;
    }

    private static bool Closed(TileZone zone) => zone.id < _closed.Length && _closed[zone.id] == _pass;

    // 补给线：从军所在的地块往后方找折损最小的运粮路线，到最近的己方(含友军)城为止(海外还可以到沿岸，按海路封顶)；
    // 返回沿途剩下的几成，找不到为 0(被切断)。按每块折损取对数做最短路。
    // 海外(sea 不为空：本国的船能到的海区 → 航线经过几个海区)：卸粮的岸边要是己方控制、无主地，或军脚下的那片海滩，
    // 外面的海区本国的船到得了；航线在舰种航程内七成五，超出只剩四成
    private static float SupplyLine(Kingdom kingdom, TileZone start, Dictionary<int, int> sea, int range)
    {
        float Source(TileZone zone)
        {
            City city = zone.city;
            if (city?.data != null && !city.isRekt() && city.getTile()?.zone == zone &&
                Relation(kingdom, city.kingdom) == RelationFriendly)
                return 1f;
            if (sea == null || sea.Count == 0) return 0f;
            if (zone != start && FrontSystem.Controller(zone) is Kingdom holder && Relation(kingdom, holder) != RelationFriendly)
                return 0f;
            // 岸外的海区(同 SeaLanding.Waters，不另建列表)
            if (SeaRegionSystem.IsSea(zone)) return 0f;
            int hops = int.MaxValue;
            foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
                if (SeaRegionSystem.Of(next) is { } region && sea.TryGetValue(region.Id, out int count) && count < hops)
                    hops = count;
            return hops == int.MaxValue ? 0f : hops <= range ? OverseasCap : OutOfRange;
        }
        float here = Source(start);
        if (here >= 1f) return 1f;
        // 无人区：本国过不去的不进；起点就在里面的可以在无人区里接着走，好运出去
        bool wild = WildernessPassage.Active;
        int way = WildernessPassage.Way(kingdom);
        BeginPass();
        Reach(start, 0f, 0);
        Open.Add((0f, start));
        float best = here;
        while (Open.Count > 0 && _closedCount < MaxSupplySearch)
        {
            (float key, TileZone zone) = (Open[0].key, CorpsSystem.PopMin(Open));
            // 再往外走剩下的不会比已找到的多
            if (Mathf.Exp(-key) <= best) break;
            if (Closed(zone)) continue;
            if (zone.id >= _closed.Length) Grow(zone.id + 1);
            _closed[zone.id] = _pass;
            _closedCount++;
            int depth = _steps[zone.id];
            if (zone != start)
            {
                float source = Source(zone);
                if (source > 0f)
                {
                    best = Mathf.Max(best, Mathf.Exp(-key) * source);
                    continue;
                }
            }
            if (depth >= MaxSupplyDepth) continue;
            foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
            {
                if (next == null || Closed(next) || SeaRegionSystem.IsSea(next) || TerrainSystem.Impassable(next) ||
                    !TerrainSystem.Crossable(zone, next)) continue;
                if (wild && !WildernessPassage.Open(next, way) && WildernessPassage.Open(zone, way)) continue;
                float loss = Loss(kingdom, next, depth + 1);
                if (loss >= 1f) continue;
                float total = key - Mathf.Log(1f - loss);
                if (next.id < _seen.Length && _seen[next.id] == _pass && _cost[next.id] <= total) continue;
                Reach(next, total, depth + 1);
                CorpsSystem.PushHeap(Open, (total, next));
            }
        }
        Open.Clear();
        return best;
    }

    // 运粮经过这块地折损几成；敌占的地块只有阵地前沿几块能穿过(否则返回 1，走不通)
    private static float Loss(Kingdom kingdom, TileZone zone, int depth)
    {
        Kingdom controller = FrontSystem.Controller(zone);
        float loss;
        sbyte relation = Relation(kingdom, controller);
        if (controller == null) loss = NeutralLoss;
        else if (relation == RelationFriendly) loss = FriendlyLoss;
        else if (relation == RelationHostile)
        {
            if (depth > FrontlineDepth) return 1f;
            loss = EnemyLoss;
        }
        else loss = NeutralLoss;
        int grade = RoadNetwork.RoadGrade(zone);
        if (grade > 0) return loss * RoadShare * (1.25f - 0.15f * grade);
        bool rough = TerrainSystem.Of(zone) is FrontTerrain.Mountain or FrontTerrain.Jungle or FrontTerrain.Swamp or
            FrontTerrain.Desert or FrontTerrain.Snow;
        return Mathf.Min(0.9f, rough ? loss * RoughLoss : loss);
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
            // 就近调粮：离这支军最近的几座本国城的余粮
            TileZone at = CorpsSystem.Zone(corps);
            if (at != null && kingdom.cities != null)
                foreach (City near in CorpsSystem.NearestCities(kingdom, at, NearbyGranaries))
                {
                    float surplus = near.getTotalFood() - CityPopulationSystem.Households(near);
                    if (surplus < 1f) continue;
                    need -= PopulationEconomySystem.TakeFood(near, Mathf.FloorToInt(Mathf.Min(surplus, need)));
                    if (need < 1f) break;
                }
            if (need < 1f) continue;
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
            // 骑士团的军宗教狂热，缺粮断供只受一半的损耗
            if (corps.order_id >= 0) severity *= 0.5f;
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

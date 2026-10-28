using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 穿越政权的扩张(用户 2026-10-28 试玩：临高三十年只有一城两千人、兵剩个位数，第三十二年被邻近部落灭了，要"加扩张与流民"；
// 又要"给元老加保护，永远无法被杀死或者说被杀死的几率几乎不可能"、"他们初期就有枪支和现代的部分科技")，每年一次：
//   · 流民投奔：RefugeeReach 地块内的外国城每年有 RefugeeShare 的平民投奔最近的本国城(或殖民地的城)，饥荒、战乱、民心低的城
//     成倍地走；阶段越往后越多。投奔来的进检疫营(住房另给容量，不按住房回落)，之后按检疫营归化(见 Naturalize)；
//   · 开拓：本国人口最多的城平民够 SettleMinPeople、国库够 SettleCost 时，每年 SettleChance 在离本国城 SettleReach 步以内的
//     空地建一座新城，从那座城迁 SettlePeople 人过去(原著的博铺、马袅……)；
//   · 伏波军保编：本国至少有一支线膛枪兵的手动军，满编 ArmyBase + 每城 ArmyPerCity(至多 ArmyMax)，缺了从人口里补；
//   · 用兵：不在打仗、伏波军够 CampaignMinArmy 时，每隔 CampaignYears 年有 CampaignChance 向 CampaignReach 地块以内
//     兵力不及本国 CampaignOdds 倍的邻国开战，目标夺城(原著先取临高县城，再取海南、广东)；
//   · 元老几乎杀不死：落成实体的元老遇到寿终以外的死(兵刃、处决、刺杀、瘟疫、饥饿、火……)只有 KillChance 的机会真死，
//     挡下的当场满血(见 ActorPatch.Die、BiographySystem.Kill)；化回人口(不计死亡)与寿终照常；
//   · 元老不投别国(用户"元老不可能加入其他国家")：约每月查一次，成了别国的人的元老(城被占、被拉去当别国君主城主)带回本国都城。
public static partial class TransmigrantSystem
{
    private const int RefugeeReach = 48;
    private const float RefugeeShare = 0.012f, RefugeeYearShare = 0.25f, RefugeeYearMin = 400f, RefugeeLogMin = 50f;
    private const int SettleReach = 6;
    private const float SettleChance = 0.35f, SettleMinPeople = 2500f, SettlePeople = 400f;
    private const int SettleCost = 300;
    private const float ArmyBase = 500f, ArmyPerCity = 150f, ArmyMax = 3000f;
    private const int CampaignYears = 4, CampaignReach = 40;
    private const float CampaignChance = 0.5f, CampaignMinArmy = 300f, CampaignOdds = 1.5f;
    private const float KillChance = 0.01f;

    // 降临时随身带来的现代技术：线膛枪、炸药、发电、蒸汽印刷与蒸汽铁甲(连同前置的工业化)；其余工业、电气时代的各项先有一半进度
    private static readonly string[] BroughtTechs =
        { "industrialization", "rifling", "explosives", "electricity", "steam_press", "steam_ironclads" };

    private static void Grow(Kingdom kingdom, TransmigrantState state)
    {
        Refugees(kingdom, state);
        Settle(kingdom, state);
        KeepArmy(kingdom);
        Campaign(kingdom, state);
    }

    #region 流民投奔

    private static void Refugees(Kingdom kingdom, TransmigrantState state)
    {
        var homes = new List<City>(kingdom.cities);
        foreach (Kingdom colony in ColonySystem.ColoniesOf(kingdom)) homes.AddRange(colony.cities);
        homes.RemoveAll(city => city?.data == null || city.isRekt());
        if (homes.Count == 0) return;
        float own = homes.Sum(CityPopulationSystem.GetTotal);
        float budget = Mathf.Max(RefugeeYearMin, own * RefugeeYearShare);
        float stage = 1f + 0.25f * Mathf.Max(0, state.stage - 1);
        float moved = 0f;
        var sources = new List<string>();
        foreach (City from in World.world.cities.list.ToList())
        {
            if (budget - moved < 1f) break;
            if (from?.data == null || from.isRekt() || from.kingdom == null || from.kingdom == kingdom ||
                homes.Contains(from)) continue;
            City to = Nearest(homes, from, out int distance);
            if (to == null || distance > RefugeeReach) continue;
            float people = CityPopulationSystem.CivilianBackground(from);
            if (people < 50f) continue;
            float amount = people * RefugeeShare * stage * Distress(from) * (1f - distance / (float)(RefugeeReach + 1));
            amount = Mathf.Min(amount, budget - moved);
            if (amount < 1f) continue;
            float arrived = CityPopulationSystem.TransferCivilianBackground(from, to, amount);
            if (arrived <= 0f) continue;
            Shelter(to, arrived);
            moved += arrived;
            if (arrived >= 10f && sources.Count < 3) sources.Add(from.GetCityName());
        }
        if (moved < 1f) return;
        CityPopulationSystem.InvalidateHouseholdCaches();
        if (moved >= RefugeeLogMin)
            TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_refugees_history"), Mathf.RoundToInt(moved),
                string.Join(LM.Get("list_separator"), sources), kingdom.GetKingdomFullName()), kingdom);
    }

    // 饥荒 ×3、战乱 ×2、民心低于四十 ×1.5
    private static float Distress(City city)
    {
        float factor = 1f;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data != null && data.last_food_shortage > 0f) factor *= 3f;
        if (city.kingdom != null && city.kingdom.hasEnemies()) factor *= 2f;
        if (CityStabilitySystem.Effective(city) < 40f) factor *= 1.5f;
        return factor;
    }

    // 检疫营：投奔来的人另给住房容量(同人口笔刷)，不按住房回落
    private static void Shelter(City city, float people)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data != null) data.brush_capacity = Mathf.Max(0f, data.brush_capacity) + people;
        LayerCityCache.Invalidate(city);
    }

    private static City Nearest(List<City> cities, City from, out int distance)
    {
        distance = int.MaxValue;
        City best = null;
        foreach (City city in cities)
        {
            int d = ZoneDistance(city, from);
            if (d >= distance) continue;
            distance = d;
            best = city;
        }
        return best;
    }

    private static int ZoneDistance(City a, City b)
    {
        TileZone za = a?.getTile()?.zone, zb = b?.getTile()?.zone;
        return za == null || zb == null ? int.MaxValue : Mathf.Abs(za.x - zb.x) + Mathf.Abs(za.y - zb.y);
    }

    #endregion

    #region 开拓

    private static void Settle(Kingdom kingdom, TransmigrantState state)
    {
        if (kingdom.GetMoney() < SettleCost || Random.value >= SettleChance) return;
        City from = kingdom.cities.Where(city => city?.data != null && !city.isRekt())
            .OrderByDescending(CityPopulationSystem.CivilianBackground).FirstOrDefault();
        if (from == null || CityPopulationSystem.CivilianBackground(from) < SettleMinPeople) return;
        TileZone zone = FrontierZone(kingdom);
        Actor founder = from.leader ?? kingdom.king;
        if (zone == null || founder == null) return;
        City city;
        Landing = true;
        try
        {
            city = World.world.cities.buildNewCity(founder, zone);
        }
        finally
        {
            Landing = false;
        }
        if (city == null) return;
        kingdom.SubMoney(SettleCost, TreasuryCategory.Construction);
        float arrived = CityPopulationSystem.TransferCivilianBackground(from, city, SettlePeople);
        Shelter(city, arrived);
        CultureService.SetCityMainCulture(city, Culture);
        CityPopulationData population = CityPopulationSystem.Get(city);
        if (population != null) CityPopulationSystem.Census(city, population, keepBackground: true, preserveSmallGroups: true);
        CityPopulationSystem.EnsureLeader(city);
        CityConstructionSystem.StartSettlement(city);
        state.towns_founded++;
        TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_settled_history"), kingdom.GetKingdomFullName(),
            city.GetCityName(), from.GetCityName()), kingdom);
    }

    // 离本国城 SettleReach 步以内、没有城、能建城的空地，挑最近的
    private static TileZone FrontierZone(Kingdom kingdom)
    {
        var seen = new HashSet<TileZone>();
        var frontier = new List<TileZone>();
        foreach (City city in kingdom.cities)
            foreach (TileZone zone in city.zones)
                if (seen.Add(zone)) frontier.Add(zone);
        for (int step = 0; step < SettleReach && frontier.Count > 0; step++)
        {
            var next = new List<TileZone>();
            foreach (TileZone zone in frontier)
                foreach (TileZone neighbour in zone.neighbours_all)
                {
                    if (neighbour == null || !seen.Add(neighbour)) continue;
                    if (neighbour.city == null && neighbour.tiles_with_ground >= 32 && neighbour.canStartCityHere()) return neighbour;
                    if (neighbour.tiles_with_ground > 0 && (neighbour.city == null || neighbour.city.kingdom == kingdom)) next.Add(neighbour);
                }
            frontier = next;
        }
        return null;
    }

    #endregion

    #region 伏波军与用兵

    private static float ArmyOf(Kingdom kingdom) => CorpsSystem.Of(kingdom).Sum(corps => corps.strength);

    private static void KeepArmy(Kingdom kingdom)
    {
        if (!CorpsSystem.Enabled || kingdom.capital == null) return;
        float wanted = Mathf.Min(ArmyMax, ArmyBase + ArmyPerCity * Mathf.Max(0, kingdom.cities.Count - 1));
        CorpsData fubo = CorpsSystem.Of(kingdom).FirstOrDefault(corps => corps.manual && !corps.militia &&
            corps.arm >= (int)CorpsArmSystem.Arm.Rifle && corps.arm != (int)CorpsArmSystem.Arm.Militia);
        if (fubo == null)
        {
            if (CityPopulationSystem.LevyBackground(kingdom.capital) >= wanted * 0.5f)
                CorpsSystem.RaiseLanding(kingdom, kingdom.capital, wanted, CorpsArmSystem.Arm.Rifle);
            return;
        }
        fubo.full = Mathf.Max(fubo.full, wanted);
    }

    private static void Campaign(Kingdom kingdom, TransmigrantState state)
    {
        double now = World.world.getCurWorldTime();
        if (now < state.next_campaign_at || kingdom.hasEnemies() || Random.value >= CampaignChance) return;
        float army = ArmyOf(kingdom);
        if (army < CampaignMinArmy || CorpsSystem.CannotFundWar(kingdom)) return;
        Kingdom target = null;
        int best = int.MaxValue;
        foreach (City city in World.world.cities.list)
        {
            Kingdom other = city?.kingdom;
            if (other?.data == null || city.isRekt() || other == kingdom || other.isRekt() || other.IsInSameEmpire(kingdom) ||
                ColonySystem.Bound(kingdom, other) || WarGate.Forbidden(kingdom, other) ||
                ArmyOf(other) * CampaignOdds > army) continue;
            City home = Nearest(kingdom.cities.ToList(), city, out int distance);
            if (home == null || distance > CampaignReach || distance >= best) continue;
            if (!WarReach.MayDeclare(kingdom, other, WarTypeLibrary.normal)) continue;
            best = distance;
            target = other;
        }
        // 没有合适的对象就明年再看
        state.next_campaign_at = now + (target == null ? 1 : CampaignYears) * 60d;
        if (target == null) return;
        War war = World.world.diplomacy.startWar(kingdom, target, WarTypeLibrary.normal);
        if (war == null) return;
        WarGoalSystem.Preset(war, WarGoal.夺城);
        TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_campaign_history"), kingdom.GetKingdomFullName(),
            target.GetKingdomFullName()), kingdom);
    }

    #endregion

    #region 元老几乎杀不死、不投别国

    // 落成实体的元老成了别国的人(所在的城被别国占了、被拉去当了别国的君主或城主)：带回本国都城(殖民地的总督不算)
    // 落成实体的元老(读档后、每次查时重记；落成时记上，见 Bind)：每死一个人都要问是不是元老，先查这张表
    private static readonly HashSet<long> SenatorIds = new();

    private static void KeepSenatorsHome()
    {
        SenatorIds.Clear();
        Kingdom kingdom = Active();
        TransmigrantState state = Of(kingdom);
        if (state == null) return;
        foreach (SenatorSeat seat in state.seats)
            if (seat.actor_id >= 0) SenatorIds.Add(seat.actor_id);
        City home = kingdom.capital;
        if (home?.data == null || home.isRekt()) return;
        foreach (SenatorSeat seat in state.seats)
        {
            if (seat.actor_id < 0) continue;
            Actor actor = World.world.units.get(seat.actor_id);
            if (actor?.data == null || !actor.isAlive()) continue;
            Kingdom at = actor.kingdom;
            if (at == kingdom || at != null && ColonySystem.MetropoleOf(at) == kingdom) continue;
            if (at != null && actor.isKing()) at.removeKing();
            if (actor.isCityLeader() && actor.city != null) actor.city.removeLeader();
            actor.joinCity(home);
        }
    }

    // 刚掷到"真死"的元老：接下来那一次死不再掷第二回(处决、刺杀先在 BiographySystem.Kill 掷过)
    private static long _doomed = -1L;

    public static bool IsSenator(Actor actor)
    {
        if (actor?.data == null || !SenatorIds.Contains(actor.id)) return false;
        TransmigrantState state = Of(Active());
        return state != null && SeatOf(state, actor) != null;
    }

    // 挡下这次死亡：寿终、化回人口(不计死亡)不挡；其余只有 KillChance 的机会真死
    public static bool Shield(Actor actor, AttackType type, bool countDeath)
    {
        if (!countDeath || type == AttackType.Age || !IsSenator(actor)) return false;
        if (_doomed == actor.id)
        {
            _doomed = -1L;
            return false;
        }
        if (Random.value < KillChance)
        {
            _doomed = actor.id;
            return false;
        }
        actor.data.health = actor.getMaxHealth();
        return true;
    }

    #endregion
}

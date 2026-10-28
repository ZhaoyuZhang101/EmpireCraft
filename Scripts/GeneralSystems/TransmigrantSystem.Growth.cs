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
//
// 10-29 试玩又改(用户"临高政权死得太快了，有这么大的时代差都没打过大明""一开始是大明的属国，一直在伪装""占领两广后才撕破的脸"
// "收拢流民、吸纳归化民、暴兵这套也没有""临高现代体制能拉的军队更多、战斗力更强")。那一局前十年连下明的九座城，
// 之后流民涌进来没粮吃、连年大饥荒，伏波军按城数涨到三千多却没钱养，军费系数跌到 0.01，好几座城守军为零：
//   · 伪装：降临时对外以所在天下的共主(没有就是最近的华夏帝国)的属国自居。伪装期间朝廷与各省不来开战，
//     临高打朝廷下面的省，朝廷本部和别的省也不援(地方上的事，朝廷蒙在鼓里)；临高也不碰朝廷本部。
//     据有两处省会(王国法理的首府，原著是两广)或宣布大宋国号以后撕破脸，朝廷这才来剿(见 Reveal)；
//   · 收拢流民 → 检疫营归化 → 暴兵：流民只往不闹饥荒的城去(一年至多本国人口一成)；检疫营每年把外族编为归化民(见 Naturalize)；
//     伏波军按归化民扩编(ArmyBase + 归化民 NationalArmyShare，打仗时再扩一半)，钱不够时不扩；
//   · 养得起：每月给缺粮的城补粮(高产作物与外洋米粮，见 Supply)；钱从工业来(用户"主要是工业产量更高，之前的市场系统可以用上")：
//     厂区产量 ×IndustryBonus，归化民每千人每年另出 IndustryGoods 的布匹、工具、军械、钢材、糖(阶段越往后越多)，
//     记进商品市场(见 CommodityMarketSystem.Book)——自家不缺军械钢材，多的卖给邻国市场换钱；
//   · 能打：兵制按现代动员(征兵比例 LevyShare)，交战时战力 ×CombatBonus(新式操典与组织，见 FrontSystem)。
//
// 10-29 再改(用户"在市场系统下，临高政体应当靠着技术差赚着比这个世界任何国家的钱都要多，不可能缺钱，而且也不可能缺军队，
// 因为可以给足军饷，直接吸收其他国家的人作为自己的兵员地")：
//   · 赚钱：商品市场按技术差加价、挤占(见 CommodityMarketSystem.Trade)；船队海路通到 SeaTradeReach 以内各国的市场；
//   · 足饷：扩编不再看国库(只在真欠饷时停)；
//   · 募兵：伏波军不满编，花钱从 RefugeeReach 以内不交战的外国城招丁壮(每城至多 RecruitCityShare)，本国人口不够也补得满。
public static partial class TransmigrantSystem
{
    private const int RefugeeReach = 48;
    private const float RefugeeShare = 0.012f, RefugeeYearShare = 0.1f, RefugeeYearMin = 200f, RefugeeLogMin = 50f;
    private const int SettleReach = 6;
    private const float SettleChance = 0.35f, SettleMinPeople = 2500f, SettlePeople = 400f;
    private const int SettleCost = 300;
    private const float ArmyBase = 500f, NationalArmyShare = 0.1f, WarSurge = 1.5f, ArmyMax = 8000f, ArmyLogStep = 300f;
    private const int CampaignYears = 2, CampaignReach = 40;
    private const float CampaignChance = 0.7f, CampaignMinArmy = 300f, CampaignOdds = 1.5f;
    private const float KillChance = 0.01f;
    // 现代动员的征兵比例(本土户数的几成能征)；交战战力加成(穿越政权与它的殖民地)
    public const float LevyShare = 0.2f;
    private const float CombatBonus = 1.5f;
    // 每月补粮：存粮不到每户 SupplyFloor 的城补到每户 SupplyGrain(至多 SupplyMax)
    private const float SupplyFloor = 1f, SupplyGrain = 0.3f;
    private const int SupplyMax = 300;
    // 工业：厂区产量倍数；归化民每千人每年的工业品(商品市场的物资编号)
    private const float IndustryBonus = 2f;
    private static readonly (string good, float perThousand)[] IndustryGoods =
        { ("cloth", 3f), ("tools", 2f), ("arms", 1.5f), ("steel", 1.5f), ("sugar", 1f) };
    // 募兵：每人 RecruitFee 安家银；每座外国城一年至多招它平民的 RecruitCityShare
    private const float RecruitFee = 0.5f, RecruitCityShare = 0.05f, RecruitLogMin = 50f;
    // 船队海路通到多远以内各国的市场(地块)
    private const int SeaTradeReach = 90;
    // 撕破脸：据有几处省会
    private const int RevealSeats = 2;

    // 降临时随身带来的现代技术：线膛枪、炸药、发电、蒸汽印刷与蒸汽铁甲(连同前置的工业化)；其余工业、电气时代的各项先有一半进度
    private static readonly string[] BroughtTechs =
        { "industrialization", "rifling", "explosives", "electricity", "steam_press", "steam_ironclads" };

    private static void Grow(Kingdom kingdom, TransmigrantState state, float naturalized)
    {
        Disguise(kingdom, state);
        Reveal(kingdom, state);
        float refugees = Refugees(kingdom, state);
        Settle(kingdom, state);
        KeepArmy(kingdom);
        Recruit(kingdom);
        Campaign(kingdom, state);
        CorpsData fubo = Fubo(kingdom);
        NeoModLoader.services.LogService.LogInfo($"[EmpireCraft][穿越] {kingdom.GetKingdomFullName()} 城{kingdom.cities.Count} " +
            $"流民{Mathf.RoundToInt(refugees)} 归化{Mathf.RoundToInt(naturalized)} 归化民共{Mathf.RoundToInt(NaturalizedPopulation(kingdom))} " +
            $"伏波军{Mathf.RoundToInt(fubo?.strength ?? 0f)}/{Mathf.RoundToInt(fubo?.full ?? 0f)} 国库{kingdom.GetMoney()} " +
            $"外贸{Mathf.RoundToInt(CommodityMarketSystem.MarketOf(kingdom)?.trade_value ?? 0f)} " +
            $"{(DisguiseLord(kingdom) is Empire lord ? "伪装为" + lord.GetEmpireFullName() + "属国" : "已公开")}");
    }

    #region 流民投奔

    private static float Refugees(Kingdom kingdom, TransmigrantState state)
    {
        var homes = new List<City>(kingdom.cities);
        foreach (Kingdom colony in ColonySystem.ColoniesOf(kingdom)) homes.AddRange(colony.cities);
        homes.RemoveAll(city => city?.data == null || city.isRekt());
        // 闹饥荒的城不收(以前流民涌进来没粮吃，连年大饥荒)
        homes.RemoveAll(city => CityPopulationSystem.Get(city)?.last_food_shortage > 0f);
        if (homes.Count == 0) return 0f;
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
        if (moved < 1f) return 0f;
        CityPopulationSystem.InvalidateHouseholdCaches();
        if (moved >= RefugeeLogMin)
            TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_refugees_history"), Mathf.RoundToInt(moved),
                string.Join(LM.Get("list_separator"), sources), kingdom.GetKingdomFullName()), kingdom);
        return moved;
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

    private static CorpsData Fubo(Kingdom kingdom) => CorpsSystem.Of(kingdom).FirstOrDefault(corps => corps.manual && !corps.militia &&
        corps.arm >= (int)CorpsArmSystem.Arm.Rifle && corps.arm != (int)CorpsArmSystem.Arm.Militia);

    // 伏波军按归化民扩编(暴兵)：ArmyBase + 归化民的 NationalArmyShare，打仗时再扩 WarSurge；军饷给足，只在真欠饷时不扩编
    private static void KeepArmy(Kingdom kingdom)
    {
        if (!CorpsSystem.Enabled || kingdom.capital == null) return;
        float wanted = Mathf.Min(ArmyMax, (ArmyBase + NaturalizedPopulation(kingdom) * NationalArmyShare) *
                                          (kingdom.hasEnemies() ? WarSurge : 1f));
        CorpsData fubo = Fubo(kingdom);
        if (fubo == null)
        {
            if (CityPopulationSystem.LevyBackground(kingdom.capital) >= Mathf.Min(wanted, ArmyBase) * 0.5f)
                CorpsSystem.RaiseLanding(kingdom, kingdom.capital, Mathf.Min(wanted, ArmyBase), CorpsArmSystem.Arm.Rifle);
            return;
        }
        if (wanted <= fubo.full || CorpsSystem.Unpaid(kingdom)) return;
        float before = fubo.full;
        fubo.full = wanted;
        if (fubo.full - before >= ArmyLogStep)
            TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_army_expanded_history"), kingdom.GetKingdomFullName(),
                Mathf.RoundToInt(fubo.full)), kingdom);
    }

    // 募兵：伏波军不满编时，从 RefugeeReach 以内不交战的外国城招丁壮，近的先招，每人付 RecruitFee
    private static float Recruit(Kingdom kingdom)
    {
        CorpsData fubo = Fubo(kingdom);
        if (fubo == null || CorpsSystem.Unpaid(kingdom)) return 0f;
        float deficit = fubo.full - fubo.strength;
        if (deficit < 1f) return 0f;
        var homes = new List<City>(kingdom.cities);
        foreach (Kingdom colony in ColonySystem.ColoniesOf(kingdom)) homes.AddRange(colony.cities);
        homes.RemoveAll(city => city?.data == null || city.isRekt());
        if (homes.Count == 0) return 0f;
        var sources = new List<(City city, int distance)>();
        foreach (City from in World.world.cities.list)
        {
            if (from?.data == null || from.isRekt() || from.kingdom == null || homes.Contains(from) ||
                from.kingdom.isEnemy(kingdom)) continue;
            if (Nearest(homes, from, out int distance) == null || distance > RefugeeReach) continue;
            sources.Add((from, distance));
        }
        float hired = 0f;
        var names = new List<string>();
        foreach ((City from, int _) in sources.OrderBy(source => source.distance))
        {
            float want = Mathf.Min(deficit - hired, CityPopulationSystem.CivilianBackground(from) * RecruitCityShare);
            if (want < 1f) continue;
            if (kingdom.GetMoney() < Mathf.CeilToInt(want * RecruitFee)) break;
            float got = CorpsSystem.Enlist(kingdom, fubo, from, want);
            if (got <= 0f) continue;
            kingdom.SubMoney(Mathf.CeilToInt(got * RecruitFee), TreasuryCategory.Military);
            hired += got;
            if (got >= 10f && names.Count < 3) names.Add(from.GetCityName());
            if (deficit - hired < 1f) break;
        }
        if (hired >= RecruitLogMin)
            TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_recruit_history"), kingdom.GetKingdomFullName(),
                Mathf.RoundToInt(hired), string.Join(LM.Get("list_separator"), names)), kingdom);
        return hired;
    }

    // 船队海路(见 CommodityMarketSystem.Trade)：本国与殖民地任一城 SeaTradeReach 地块以内有城的外国
    public static IEnumerable<Kingdom> SeaTradePartners(Kingdom kingdom)
    {
        if (kingdom?.cities == null) yield break;
        var homes = new List<City>(kingdom.cities);
        foreach (Kingdom colony in ColonySystem.ColoniesOf(kingdom)) homes.AddRange(colony.cities);
        homes.RemoveAll(city => city?.data == null || city.isRekt());
        if (homes.Count == 0) yield break;
        var seen = new HashSet<long>();
        foreach (City city in World.world.cities.list)
        {
            Kingdom other = city?.kingdom;
            if (other?.data == null || other.isRekt() || other == kingdom || seen.Contains(other.id) || homes.Contains(city)) continue;
            if (Nearest(homes, city, out int distance) == null || distance > SeaTradeReach) continue;
            seen.Add(other.id);
            yield return other;
        }
    }

    // 归化民(本国与殖民地各城里的澳洲人背景人口，元老在内)
    public static float NaturalizedPopulation(Kingdom kingdom)
    {
        if (kingdom?.cities == null) return 0f;
        float total = 0f;
        var realms = new List<Kingdom> { kingdom };
        realms.AddRange(ColonySystem.ColoniesOf(kingdom));
        foreach (Kingdom realm in realms)
            foreach (City city in realm.cities)
            {
                CityPopulationData data = CityPopulationSystem.Get(city);
                if (data?.groups == null) continue;
                foreach (PopGroup group in data.groups)
                    if (group != null && group.culture == Culture) total += group.Background;
            }
        return total;
    }

    // 交战战力(见 FrontSystem)：穿越政权与它的殖民地
    public static float CombatFactor(Kingdom kingdom) =>
        kingdom != null && (IsTransmigrant(kingdom) || IsTransmigrant(ColonySystem.MetropoleOf(kingdom))) ? CombatBonus : 1f;

    // 兵制(见 CityPopulationSystem.DoctrineOf)：穿越政权与它的殖民地按现代动员
    public static bool Mobilizes(Kingdom kingdom) =>
        kingdom != null && (IsTransmigrant(kingdom) || IsTransmigrant(ColonySystem.MetropoleOf(kingdom)));

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

    #region 伪装与撕破脸

    // 伪装期的宗主：所在天下的共主，没有就是离都城最近的华夏帝国
    private static void Disguise(Kingdom kingdom, TransmigrantState state)
    {
        if (state.revealed || DisguiseLord(kingdom) != null) return;
        Empire lord = PickLord(kingdom);
        if (lord == null) return;
        state.disguise_lord_id = lord.id;
        TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_disguise_history"), kingdom.GetKingdomFullName(),
            lord.GetEmpireFullName()), kingdom);
    }

    private static Empire PickLord(Kingdom kingdom)
    {
        City capital = kingdom.capital;
        if (capital == null || ModClass.EMPIRE_MANAGER == null) return null;
        bool Valid(Empire empire) => empire?.data != null && !empire.isRekt() && !empire.IsArchived() &&
                                     empire.CoreKingdom != null && empire.CoreKingdom != kingdom && !IsTransmigrant(empire.CoreKingdom) &&
                                     RealmNamingSystem.IsHuaxia(empire);
        EmpireCore lead = TianxiaSystem.LeadOf(capital);
        Empire holder = lead == null ? null : TianxiaSystem.Holder(lead)?.GetEmpire();
        if (Valid(holder)) return holder;
        return ModClass.EMPIRE_MANAGER.Where(Valid)
            .OrderBy(empire => ZoneDistance(empire.CoreKingdom.capital, capital)).FirstOrDefault();
    }

    // 还在伪装：返回名义上的宗主(撕破脸了、宗主没了返回 null)
    public static Empire DisguiseLord(Kingdom kingdom)
    {
        TransmigrantState state = Of(kingdom);
        if (state == null || state.revealed || state.disguise_lord_id < 0 || ModClass.EMPIRE_MANAGER == null) return null;
        Empire lord = ModClass.EMPIRE_MANAGER.get(state.disguise_lord_id);
        return lord?.data != null && !lord.isRekt() && !lord.IsArchived() && lord.CoreKingdom != null ? lord : null;
    }

    private static bool LordSide(Empire lord, Kingdom kingdom) =>
        kingdom != null && (kingdom == lord.CoreKingdom || kingdom.GetEmpire() == lord);

    // 开战闸门(见 WarGate.Forbidden)：伪装期间朝廷与各省不来开战，临高也不碰朝廷本部
    public static bool DisguiseForbids(Kingdom attacker, Kingdom target)
    {
        Kingdom lingao = Active();
        Empire lord = DisguiseLord(lingao);
        if (lord == null) return false;
        if (target == lingao && LordSide(lord, attacker)) return true;
        return attacker == lingao && target == lord.CoreKingdom;
    }

    // 参战闸门(见 WarPatch.CanJoinWarSide)：伪装期间临高与某省的仗，朝廷本部和别的省都不掺和
    public static bool DisguiseStaysOut(War war, Kingdom kingdom)
    {
        if (war == null || kingdom == null) return false;
        Kingdom lingao = Active();
        Empire lord = DisguiseLord(lingao);
        if (lord == null) return false;
        Kingdom attacker = war.getMainAttacker(), defender = war.getMainDefender();
        if (attacker != lingao && defender != lingao || kingdom == attacker || kingdom == defender) return false;
        return LordSide(lord, kingdom);
    }

    // 撕破脸：据有 RevealSeats 处省会(王国法理的首府)，或宣布了大宋国号，或宗主没了
    private static void Reveal(Kingdom kingdom, TransmigrantState state)
    {
        if (state.revealed || state.disguise_lord_id < 0) return;
        Empire lord = DisguiseLord(kingdom);
        List<string> seats = kingdom.cities.Select(city => city.GetTitle())
            .Where(title => title?.title_capital != null && title.title_capital.kingdom == kingdom)
            .Distinct().Select(title => title.title_capital.GetCityName()).ToList();
        if (lord != null && seats.Count < RevealSeats && state.stage < 4) return;
        state.revealed = true;
        // 打出大宋旗号当年就设大宋澳洲行在、对外情报局(见 Milestones)
        Milestones(kingdom, state, Date.getCurrentYear());
        if (lord == null) return;
        TranslateHelper.LogMajor(string.Format(LM.Get("transmigrant_revealed_history"), kingdom.GetKingdomFullName(),
            string.Join(LM.Get("list_separator"), seats.Take(3)), lord.GetEmpireFullName()), kingdom);
    }

    #endregion

    #region 补粮(每月，见 Tick)与工业

    private static void Supply()
    {
        Kingdom kingdom = Active();
        if (kingdom == null) return;
        var realms = new List<Kingdom> { kingdom };
        realms.AddRange(ColonySystem.ColoniesOf(kingdom));
        foreach (Kingdom realm in realms)
            foreach (City city in realm.cities.ToList())
            {
                if (city?.data == null || city.isRekt()) continue;
                int households = CityPopulationSystem.Households(city);
                if (households <= 0) continue;
                float food;
                try
                {
                    food = city.getTotalFood();
                }
                catch
                {
                    continue;
                }
                if (food >= households * SupplyFloor) continue;
                int grain = Mathf.Clamp(Mathf.CeilToInt(households * SupplyGrain), 5, SupplyMax);
                city.addResourcesToRandomStockpile("bread", grain);
            }
    }

    // 厂区产量与工厂收益的倍数(见 CommodityMarketSystem.Book、FactorySystem.Produce)：穿越政权与它的殖民地的城
    public static float IndustryFactor(City city) =>
        city?.kingdom != null && Mobilizes(city.kingdom) ? IndustryBonus : 1f;

    // 这座城的归化民按人头出的工业品(每年)：阶段越往后越多
    public static IEnumerable<(string good, float amount)> IndustryOutput(City city)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null) yield break;
        float people = 0f;
        foreach (PopGroup group in data.groups)
            if (group != null && group.culture == Culture) people += group.Background;
        if (people <= 0f) yield break;
        Kingdom realm = IsTransmigrant(city.kingdom) ? city.kingdom : ColonySystem.MetropoleOf(city.kingdom);
        float stage = 1f + 0.25f * Mathf.Max(0, StageOf(realm) - 1);
        foreach ((string good, float perThousand) in IndustryGoods)
            yield return (good, people / 1000f * perThousand * stage);
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

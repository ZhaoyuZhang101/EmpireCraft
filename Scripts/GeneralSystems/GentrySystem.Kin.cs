using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 族众(用户 2026-10-30："为什么世家依旧没有什么千人世家，或者那种百人世家""而且依旧很弱，一点影响力都没有"；
// 问答选"谱上要人+族众人数""族众兼并郡望的田"，地方、朝堂、门第、界面四处都要明显，剧本开局立一批世家)。
// 查 237 年的存档：34 家世家谱上在世的最多 23 人、多数 5~17 人(每族在世上限平民 30、世家 50，平民每代只生出约一个儿子)；
// 田产多半半份(纯净地图里城里的田几乎都在背景农民手里)，荫户少收的税、坞堡都不起作用；新开的盛唐一家世家都没有。
//   · 族众：族谱只记有名有姓的要人(族长、近支、做官的)，世家另记一个族众人数(连远支旁系，含谱上在世的)。每年向上限靠——
//     上限 = 时代底数(青铜 100、铁器 200、中世纪 400、火药 500、近代 600) × (门第/50)^1.5 × (1 + 郡望田产/20，至多 ×2)
//     × (1 + 在任官员每人 0.25——在京城的算一人、在外的半人，至多 ×2)，再不超过郡望之城人口的三成(另加 60)；
//     初列世家从上限的一成起，每年长 8%(逻辑斯谛，越近上限越慢)，超了上限每年回落超出的一成；
//     郡望之城告急折 5%、闹饥荒折 3%，抑豪强折三成；不再是世家的，每年散去超出谱上人数的一成；
//   · 兼并：郡望之城开了土地买卖，族众越多占田越多(族众 × 4 / 城里人口，至多 25%，施行均田减半)，
//     每年从背景农民有地的那部分划过来(每年至多目标的两成)，城里无地的农民跟着多(投献、强占，不付地价)；
//   · 势力 = 门第 × 族众系数(族众 50 人为 1，每翻一番 +0.5，不到 50 人按比例、至少 0.5)：
//     补官挑哪家、改革阻力、法统增减、择主、抑豪强抄出的钱都按它算；
//   · 荫户：每个族人荫三口佃客(× 门第/100)，与田产算出来的取大；部曲：每个族人聚一个半；坞堡族众百人以上就能筑，
//     守势、少收赋税、安定随族众加大(千人 守势 ×1.5、赋税征兵八折、稳定 +5)；
//   · 本宗凋尽、族众还在(二十人以上)：族中远支记一人入谱为宗子，不算绝嗣；
//   · 剧本开局：世界地图剧本头十年里还没有过世家时，各华夏帝国按族人官爵挑几家立为世家
//     (青铜 2、铁器 5、中世纪以后 8，不超过城数的五分之一、至少 2)，门第 72 起往下排，族众从上限的一半起。
public static partial class GentrySystem
{
    // 时代底数：下标是科技时代 tier(0 未研究、1 石器、2 铜器、3 青铜、4 铁器、5 中世纪、6 火药、7 以后)；没开科技树按 NoTechKin
    private static readonly float[] KinBase = { 40f, 40f, 60f, 100f, 200f, 400f, 500f, 600f };
    private const float NoTechKin = 250f, KinGrowth = 0.08f, KinStart = 0.1f, KinDisperse = 0.1f, KinOverflow = 0.1f,
        KinLandFull = 20f, KinOfficial = 0.25f, KinOfficialMax = 4f, KinSeatShare = 0.3f, KinSeatFloor = 60f,
        KinThreatLoss = 0.05f, KinFamineLoss = 0.03f, KinCurbLoss = 0.3f, KinCurbMoney = 0.5f;
    private const float AnnexPerKin = 4f, AnnexCap = 25f, AnnexStep = 0.2f, TenantsPerKin = 3f, RetainersPerKin = 1.5f;
    private const float InfluenceKin = 50f, FortressKinFull = 1000f;
    public const float FortressKin = 100f;
    private const int KinRevive = 20;

    #region 族众

    // 族人：族众与谱上在世的取大(没另记族众的就是谱上在世的)
    public static int Members(SpecificClan clan) => clan == null ? 0 : Members(clan, clan.Count);

    public static int Members(SpecificClan clan, int named) => Mathf.Max(named, Mathf.RoundToInt(clan?.gentry_kin ?? 0f));

    // 族众上限(见文件头)
    public static float KinCap(SpecificClan clan, City seat)
    {
        if (clan == null || seat?.data == null) return 0f;
        float cap = EraKin(seat) * Mathf.Pow(Mathf.Max(0f, clan.gentry_pedigree) / 50f, 1.5f) *
                    (1f + Mathf.Min(1f, SeatLand(clan, seat) / KinLandFull)) *
                    (1f + KinOfficial * Mathf.Min(KinOfficialMax, Officials(clan)));
        return Mathf.Min(cap, KinSeatFloor + KinSeatShare * CityPopulationSystem.GetBackgroundTotal(seat));
    }

    private static readonly Dictionary<string, (int year, int tier)> EraCache = new();

    // 郡望之城文化的科技时代(一年算一次)；-1 没开科技树
    private static int EraTier(City city)
    {
        string culture = CultureService.GetMainCulture(city, initialize: false);
        if (!TechnologySystem.IsActiveFor(culture)) return -1;
        int year = Date.getCurrentYear();
        if (EraCache.TryGetValue(culture, out var cached) && cached.year == year) return cached.tier;
        int tier = Mathf.Max(0, TechnologySystem.GetEraTier(culture));
        EraCache[culture] = (year, tier);
        return tier;
    }

    private static float EraKin(City seat)
    {
        int tier = EraTier(seat);
        return tier < 0 ? NoTechKin : KinBase[Mathf.Min(tier, KinBase.Length - 1)];
    }

    // 郡望之城的田产(份数，百分数)
    public static float SeatLand(SpecificClan clan, City seat) =>
        seat?.data != null && seat.GetOrCreate().household_land_shares?.TryGetValue($"s:{clan.id}", out float share) == true
            ? Mathf.Max(0f, share) : 0f;

    // 族人在任官(在任官员、城主、君主)：在京城的算一人、在外的半人；谱上记着官职的虚拟族人算半人
    public static float Officials(SpecificClan clan)
    {
        float count = 0f;
        foreach (PersonalClanIdentity person in clan.LivingPeople)
        {
            if (person.is_virtual)
            {
                if (!string.IsNullOrEmpty(person.officeName)) count += 0.5f;
                continue;
            }
            Actor actor = person._actor;
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() ||
                !(actor.IsOnOffice() || actor.isCityLeader() || actor.isKing())) continue;
            count += IsCapital(actor.city) ? 1f : 0.5f;
        }
        return count;
    }

    // 京城：所在帝国核心的都城(不在帝国里的按本国都城)
    private static bool IsCapital(City city)
    {
        Kingdom kingdom = city?.kingdom;
        if (kingdom == null || kingdom.isRekt()) return false;
        Kingdom court = (kingdom.IsEmpire() || kingdom.IsInEmpire() ? kingdom.GetEmpire()?.CoreKingdom : null) ?? kingdom;
        return court.capital == city;
    }

    // 每年(Assess)：世家的族众向上限靠；不是世家的散回谱上的人数
    private static void UpdateKin(SpecificClan clan, City seat, int named)
    {
        float kin = clan.gentry_kin;
        if (!clan.gentry || seat == null)
        {
            if (kin > named) kin -= Mathf.Max(1f, (kin - named) * KinDisperse);
            clan.gentry_kin = kin > named ? kin : 0f;
            return;
        }
        float cap = Mathf.Max(named, KinCap(clan, seat));
        if (kin <= 0f) kin = Mathf.Max(named, cap * KinStart);
        if (kin < cap) kin = Mathf.Min(cap, kin + Mathf.Max(1f, KinGrowth * kin * (1f - kin / cap)));
        else kin -= (kin - cap) * KinOverflow;
        if (Threatened(seat)) kin *= 1f - KinThreatLoss;
        if (FamineUnrestSystem.InFamine(seat)) kin *= 1f - KinFamineLoss;
        clan.gentry_kin = Mathf.Max(named, kin);
    }

    // 族众系数：50 人为 1，每翻一番 +0.5；不到 50 人按比例、至少 0.5
    public static float KinFactor(float kin) =>
        kin < InfluenceKin ? Mathf.Max(0.5f, kin / InfluenceKin) : 1f + Mathf.Log(kin / InfluenceKin, 2f) / 2f;

    // 势力 = 门第 × 族众系数
    public static float Influence(SpecificClan clan) => clan == null ? 0f : clan.gentry_pedigree * KinFactor(clan.gentry_kin);

    // 一组世家的势力合计
    public static float Influence(IEnumerable<SpecificClan> clans) => clans.Sum(clan => Influence(clan));

    // 谱上的人死尽而族众还在、等着远支入谱的世家：绝户田不放(见 LandEconomySystem.ReleaseExtinctLand)
    public static bool KeepsLand(SpecificClan clan) => clan != null && clan.gentry && clan.gentry_kin >= KinRevive;

    // 本宗凋尽而族众还在：族中远支记一人入谱为宗子(见 VirtualGenealogySystem.RecordKinsman)
    private static bool Revive(SpecificClan clan)
    {
        if (clan.gentry_kin < KinRevive || !CityPopulationSystem.AbstractPopulationEnabled) return false;
        City seat = Seat(clan);
        if (seat == null) return false;
        PersonalClanIdentity head = VirtualGenealogySystem.RecordKinsman(clan, seat);
        if (head == null) return false;
        clan.gentry_kin -= 1f;
        clan.extinct_since = -1d;
        string text = string.Format(LM.Get("gentry_kin_revive_history"), JunWang(clan), head.name);
        NoteClan(clan, "clan_history_gentry_adopt", text);
        LogService.LogInfo("[EmpireCraft][世家] " + text);
        return true;
    }

    #endregion

    #region 兼并、荫户、部曲

    // 兼并：郡望之城开了土地买卖，族众越多占田越多
    private static void Annex(SpecificClan clan, City seat)
    {
        if (!clan.gentry || seat?.kingdom == null || clan.gentry_kin <= 0f || !CityPopulationSystem.AbstractPopulationEnabled ||
            !LandEconomySystem.IsLandMarketOpen(seat.kingdom)) return;
        float people = Mathf.Max(1f, CityPopulationSystem.GetBackgroundTotal(seat));
        float target = Mathf.Min(AnnexCap, 100f * clan.gentry_kin * AnnexPerKin / people);
        string culture = CultureService.GetMainCulture(seat, initialize: false);
        if (CultureService.IsValidCulture(culture) && InstitutionSystem.IsEnacted(culture, EqualFieldNode)) target *= 0.5f;
        float held = SeatLand(clan, seat);
        if (target - held < 0.05f) return;
        LandEconomySystem.AnnexForClan(seat, clan, Mathf.Min(target - held, Mathf.Max(0.5f, target * AnnexStep)));
    }

    // 一家在郡望之城荫庇的份额：田产(份额 × 2 × 门第/100)与族众(每人三口佃客 × 门第/100 ÷ 城里人口)取大
    private static float ShelterOf(SpecificClan clan, City seat, float people)
    {
        float land = SeatLand(clan, seat) / 100f * ShelterPerShare;
        float kin = people <= 0f ? 0f : clan.gentry_kin * TenantsPerKin / people;
        return Mathf.Max(land, kin) * clan.gentry_pedigree / 100f;
    }

    // 荫户(人)：这一家在郡望之城荫庇的人口(界面用；一城合计至多两成五，见 ShelterShare)
    public static int ShelteredBy(SpecificClan clan)
    {
        City seat = Seat(clan);
        if (clan == null || !clan.gentry || seat == null) return 0;
        float people = CityPopulationSystem.GetBackgroundTotal(seat);
        return Mathf.RoundToInt(people * Mathf.Min(ShelterCap, ShelterOf(clan, seat, people)));
    }

    // 部曲：告急时聚的人数(每个族人一个半，不到本城人口的 RetainerMin 按 RetainerMin)
    public static int Retainers(SpecificClan clan)
    {
        City seat = Seat(clan);
        if (clan == null || seat == null) return 0;
        float people = CityPopulationSystem.GetBackgroundTotal(seat);
        return Mathf.RoundToInt(Mathf.Min(CityPopulationSystem.PeoplePerLegion,
            people * CityPopulationSystem.MilitiaTurnout * RetainerShare(clan, people)));
    }

    // 聚部曲抽本城人口的比例(见 Monthly)
    private static float RetainerShare(SpecificClan clan, float people) =>
        people <= 0f ? RetainerMin
            : Mathf.Clamp(clan.gentry_kin * RetainersPerKin / (people * CityPopulationSystem.MilitiaTurnout), RetainerMin, RetainerMax);

    // 坞堡随族众加大：百人 0、千人 1
    private static float FortressScale(SpecificClan clan) =>
        clan == null || clan.gentry_kin <= FortressKin ? 0f
            : Mathf.Clamp01(Mathf.Log10(clan.gentry_kin / FortressKin) / Mathf.Log10(FortressKinFull / FortressKin));

    #endregion

    #region 剧本开局

    private const int SeedYears = 10;
    private const float SeedKin = 0.5f;
    // 每个华夏帝国立几家：按科技时代(下标 tier，3 青铜 2 家、4 铁器 5 家、5 以后 8 家)；没开科技树 5 家
    private static readonly int[] SeedCount = { 0, 0, 0, 2, 5, 8 };
    private const int NoTechSeed = 5, SeedCitiesPer = 5, SeedMin = 2;
    private static readonly float[] SeedPedigree = { 72f, 64f, 58f, 52f, 47f, 43f, 39f, 35f };

    // 世界地图剧本头十年、还没有过世家：各华夏帝国按族人官爵挑几家立为世家(每轮门第结算开始时，见 Tick)
    private static void SeedScenario()
    {
        if (WorldMap.WorldMapScale.ZoneKm <= 0f || ModClass.EMPIRE_MANAGER == null) return;
        double now = World.world.getCurWorldTime();
        if (now < 0d || Date.getYearsSince(0d) > SeedYears) return;
        if (SpecificClanManager._specificClans.Any(clan => clan != null && (clan.gentry || clan.gentry_since >= 0d))) return;
        var candidates = new List<(SpecificClan clan, City seat, Empire realm, float score, int named)>();
        foreach (SpecificClan clan in SpecificClanManager._specificClans.ToList())
        {
            if (clan == null || Ruling.Contains(clan)) continue;
            List<PersonalClanIdentity> living = clan.LivingPeople;
            if (living.Count == 0) continue;
            City seat = FindSeat(clan, living);
            Empire realm = RealmOf(seat);
            if (realm == null || !Huaxia(seat)) continue;
            candidates.Add((clan, seat, realm, Standing(living), living.Count));
        }
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom?.capital == null ||
                !Huaxia(empire.CoreKingdom.capital)) continue;
            int tier = EraTier(empire.CoreKingdom.capital);
            int quota = Mathf.Min(tier < 0 ? NoTechSeed : SeedCount[Mathf.Min(tier, SeedCount.Length - 1)],
                Mathf.Max(SeedMin, empire.countCities() / SeedCitiesPer));
            if (quota <= 0) continue;
            var picks = candidates.Where(entry => entry.realm == empire).OrderByDescending(entry => entry.score)
                .ThenByDescending(entry => entry.named).Take(quota).ToList();
            for (int i = 0; i < picks.Count; i++)
            {
                SpecificClan clan = picks[i].clan;
                clan.gentry = true;
                clan.gentry_assessed = true;
                clan.gentry_pedigree = Mathf.Max(clan.gentry_pedigree, SeedPedigree[Mathf.Min(i, SeedPedigree.Length - 1)]);
                clan.gentry_since = now;
                clan.gentry_seat_city_id = picks[i].seat.data.id;
                clan.gentry_noted_grade = Grade(clan.gentry_pedigree);
                clan.gentry_kin = Mathf.Max(picks[i].named, KinCap(clan, picks[i].seat) * SeedKin);
                NoteTrend(clan);
            }
            if (picks.Count == 0) continue;
            string content = string.Format(LM.Get("gentry_seed_history"), empire.GetEmpireFullName(),
                string.Join("、", picks.Take(3).Select(entry => JunWang(entry.clan))), picks.Count);
            foreach (var entry in picks) NoteClan(entry.clan, "clan_history_gentry_rise", content);
            LogService.LogInfo("[EmpireCraft][世家] 剧本开局 " + content + "：" +
                               string.Join("、", picks.Select(entry => $"{JunWang(entry.clan)} 门第 {entry.clan.gentry_pedigree:0} 族众 {entry.clan.gentry_kin:0}")));
            empire.RecordHistory(directContent: content);
        }
    }

    // 开局挑世家：族人的官爵(同门第里做官一项的算法)、名望
    private static float Standing(List<PersonalClanIdentity> living)
    {
        float score = 0f;
        foreach (PersonalClanIdentity person in living)
        {
            Actor actor = person.is_virtual ? null : person._actor;
            if (actor?.data != null && !actor.isRekt() && actor.isAlive())
            {
                if (actor.isKing()) score += 12f;
                else if (actor.IsOnOffice()) score += 6f;
                else if (actor.isCityLeader()) score += 4f;
                if (actor.HasHonoraryPeerage()) score += 6f;
                score += 5f * (actor.GetOwnedTitle()?.Count ?? 0) + actor.renown / 30f;
            }
            else
            {
                if (!string.IsNullOrEmpty(person.officeName)) score += 3f;
                score += 5f * (person.ownedTitleNames?.Count ?? 0);
            }
            if (!string.IsNullOrEmpty(person.prince_title)) score += 6f;
        }
        return score;
    }

    #endregion
}

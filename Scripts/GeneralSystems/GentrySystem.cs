using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 世家(用户 2026-10-16："我可能还需要一个世家系统"；选了门第郡望、仕途垄断、庄园部曲、兴衰站队四样，只给华夏)。
// 第一步——门第与郡望：
//   · 每年给每个在世的华夏宗族(在位皇帝的宗室除外)算一回"门第"目标(0~100)：
//       族人做官(在任官员每人 6，城主 4，诸侯王 12；虚拟族人记着官职的 3)、爵位与领有的头衔(每个 5，荣誉爵位 6，藩王 6)，
//       这两项合计至多 60；郡望之城的田产(每占一分地 1.5，至多 15)；名望最高的三人(每 30 名望 1，至多 10)；
//       族大(在世族人每人 0.4，至多 10)；年深(立族每 15 年 1，至多 10)；与世家、宗室联姻(每对 2，至多 8)；
//     门第向目标靠拢——升得快(每年一成五)、跌得慢(每年半成)：家道中落要好些年；
//   · 门第到三十列为世家，跌到二十二以下不再是世家；七十以上甲等、五十五乙等、四十丙等，其余丁等；
//   · 郡望：田产最多的城，没有田产按祖籍，再没有按族人最多的城；称"某城某氏"；
//   · 头一次算(旧存档、新开局)直接到位、不记史；此后列为世家、门第衰微记入郡望所在帝国的史册(每国每年至多三条)。
// 第二步——仕途垄断：
//   · 官缺由世家子弟补上的机会：施行九品中正 七成五，施行察举 四成，二者都没有(世卿世禄) 两成五，施行科举以后不再优先；
//     从本帝国郡望在册的世家里按门第挑一家，取那家在本国的成年族人(实体优先，没有就把虚拟族人落成实体)；
//   · 九品中正期间门第固化：门第跌得慢一半。
// 第三步——庄园部曲：
//   · 荫户：世家在城里的田产荫庇佃户，那座城的税少收(每家：占地份额 × 2 × 门第/100)，一城至多少收两成五，
//     施行均田以后减半；荫户也不服兵役(见 CorpsSystem 的兵源)；
//   · 部曲：郡望之城有敌军占地或交战时，世家聚部曲成一支义军守城(按门第抽本城人口的百分之二到八)，三年至多一次，仗打完就散；
//   · 据城自守：甲、乙等世家的郡望不是国都，所在国正在打仗、朝廷衰弱(法统二十以下或皇位空缺)，每年百分之四的机会
//     由族中名望最高的人据城自立；天下裂土时，郡望在京畿的甲等世家各有一半机会据城自守(见 InterregnumSystem)。
// 第四步——兴衰与站队：
//   · 择主：两朝并立、天下裂土时，世家依附郡望所在的那一朝；附的世家门第合计每 40 得法统 1(至多 10)，
//     史册记"某氏、某氏等若干家附某朝"(见 ChooseSides)；
//   · 篡位：篡位者出自世家，诸世家多附之(法统 +5)；起于寒门，甲乙等世家耻之(每家 -2，至多 -8)；
//   · 皇权压门阀：帝党、文官可请"修氏族志"(以当朝官爵重定门第：族中有人在朝的升、没人出仕的降，法统 +3，三十年一次)；
//     法家、帝党可请"抑豪强"(抄没门第最高三家一半田产、家财入国库，这几家门第 -10，贵族地主怨、农民喜，二十年一次)。
// 宗族成千上万，按帧分批算(见 Tick)。
public static class GentrySystem
{
    public const float EnterAt = 30f, LeaveAt = 22f;
    public const string NineRankNode = "huaxia_nine_rank", RecommendationNode = "huaxia_recommendation",
        ExamNode = "huaxia_examination_officialdom";
    private const float NineRankMonopoly = 0.75f, RecommendationMonopoly = 0.4f, HereditaryMonopoly = 0.25f;
    private const int CandidateClans = 12, CandidateTries = 3;
    private const float ShelterPerShare = 2f, ShelterCap = 0.25f, RetainerMin = 0.02f, RetainerMax = 0.08f,
        AutonomyChance = 0.04f, FractureAutonomyChance = 0.5f;
    private const int RetainerCooldownYears = 3, AutonomyMandate = 20, FractureAutonomyMax = 2;
    public const string EqualFieldNode = "huaxia_land_equalization";
    private const float RiseRate = 0.15f, FallRate = 0.05f, OfficeCap = 60f;
    private const int MaxRecordsPerEmpire = 3;

    private static readonly FrameBudgetQueue<long> Queue = new(1.5d, Assess, "世家门第");
    private static double _lastPass = -1d;
    private static readonly Dictionary<long, (float share, long city)> Land = new();
    private static readonly HashSet<SpecificClan> Ruling = new();
    private static readonly Dictionary<long, int> Recorded = new();
    // 各城被世家荫庇的份额(上一轮结算时算)；在册世家(上一轮)
    private static readonly Dictionary<long, float> Sheltered = new();
    private static readonly List<long> GentryIds = new();
    private static double _lastMonth = -1d;

    public static void ResetWorldState()
    {
        Queue.Cancel();
        _lastPass = -1d;
        _lastMonth = -1d;
        Sheltered.Clear();
        GentryIds.Clear();
        Land.Clear();
        Ruling.Clear();
        Recorded.Clear();
        Registry.Clear();
    }

    #region 查询

    public static bool IsGentry(SpecificClan clan) => clan != null && clan.gentry;

    public static bool IsGentry(Actor actor) => IsGentry(actor?.GetSpecificClan());

    public static float Pedigree(SpecificClan clan) => clan?.gentry_pedigree ?? 0f;

    // 门第等第：1 甲、2 乙、3 丙、4 丁
    public static int Grade(float pedigree) => pedigree >= 70f ? 1 : pedigree >= 55f ? 2 : pedigree >= 40f ? 3 : 4;

    public static string GradeName(SpecificClan clan) => LM.Get("gentry_grade_" + Grade(Pedigree(clan)));

    public static City Seat(SpecificClan clan)
    {
        long id = clan?.gentry_seat_city_id ?? -1L;
        City city = id > 0 ? World.world.cities.get(id) : null;
        return city?.data == null || city.isRekt() ? null : city;
    }

    // 郡望："某城某氏"
    public static string JunWang(SpecificClan clan)
    {
        if (clan == null) return "";
        string surname = !string.IsNullOrEmpty(clan.shi) ? clan.shi.TrimEnd('氏') : clan.name ?? "";
        City seat = Seat(clan);
        return string.Format(LM.Get("gentry_junwang"), seat?.GetCityName() ?? "", surname);
    }

    // 郡望在本帝国的世家，门第从高到低
    public static List<SpecificClan> Of(Empire empire)
    {
        if (empire?.data == null) return new List<SpecificClan>();
        return SpecificClanManager._specificClans
            .Where(clan => clan != null && clan.gentry && RealmOf(Seat(clan)) == empire)
            .OrderByDescending(clan => clan.gentry_pedigree).ToList();
    }

    private static Empire RealmOf(City city)
    {
        Kingdom kingdom = city?.kingdom;
        if (kingdom == null || kingdom.isRekt()) return null;
        return kingdom.IsEmpire() || kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
    }

    private static bool Huaxia(City city)
    {
        if (city == null) return false;
        try
        {
            return InstitutionSystem.GetCultureLine(CultureService.GetMainCulture(city, initialize: false)) == "Huaxia";
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region 仕途

    // 官缺由世家子弟补上的机会
    public static float Monopoly(Kingdom kingdom)
    {
        // 选官之制照制度文化(旧制犹存的照旧制，见 LegacyInstitutionSystem)
        string culture = CultureService.GetInstitutionCulture(kingdom);
        if (!CultureService.IsValidCulture(culture)) return 0f;
        try
        {
            if (InstitutionSystem.GetCultureLine(culture) != "Huaxia") return 0f;
        }
        catch
        {
            return 0f;
        }
        if (InstitutionSystem.IsEnacted(culture, ExamNode)) return 0f;
        if (InstitutionSystem.IsEnacted(culture, NineRankNode)) return NineRankMonopoly;
        return InstitutionSystem.IsEnacted(culture, RecommendationNode) ? RecommendationMonopoly : HereditaryMonopoly;
    }

    private static readonly Dictionary<long, (double at, List<long> clans)> Registry = new();

    // 本帝国郡望在册的世家(门第从高到低，一年一更)
    private static List<SpecificClan> Registered(Empire empire)
    {
        double now = World.world.getCurWorldTime();
        if (!Registry.TryGetValue(empire.data.id, out var cached) || now < cached.at || Date.getYearsSince(cached.at) >= 1)
        {
            cached = (now, Of(empire).Take(CandidateClans).Select(clan => clan.id).ToList());
            Registry[empire.data.id] = cached;
        }
        return cached.clans.Select(SpecificClanManager.Get).Where(clan => clan != null && clan.gentry).ToList();
    }

    // 官缺补人(OfficeSelector.Select)：按机会挑一位世家子弟，没挑上返回空
    public static Actor Candidate(Kingdom kingdom, Func<Actor, bool> fit)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire?.data == null || UnityEngine.Random.value >= Monopoly(kingdom)) return null;
        List<SpecificClan> clans = Registered(empire);
        if (clans.Count == 0) return null;
        for (int attempt = 0; attempt < CandidateTries; attempt++)
        {
            SpecificClan clan = Weighted(clans);
            Actor member = Member(clan, kingdom, fit);
            if (member != null) return member;
        }
        return null;
    }

    private static SpecificClan Weighted(List<SpecificClan> clans)
    {
        float total = clans.Sum(clan => clan.gentry_pedigree * clan.gentry_pedigree);
        float roll = UnityEngine.Random.value * total;
        foreach (SpecificClan clan in clans)
            if ((roll -= clan.gentry_pedigree * clan.gentry_pedigree) <= 0f) return clan;
        return clans[clans.Count - 1];
    }

    // 那家在本国的成年族人：实体优先，没有就把虚拟族人落成实体
    private static Actor Member(SpecificClan clan, Kingdom kingdom, Func<Actor, bool> fit)
    {
        PersonalClanIdentity candidate = null;
        foreach (PersonalClanIdentity person in clan.LivingPeople.OrderBy(_ => UnityEngine.Random.value))
        {
            if (!person.is_virtual)
            {
                Actor actor = person._actor;
                if (actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && actor.kingdom == kingdom &&
                    !actor.IsOnOffice() && !actor.isKing() && !actor.isCityLeader() && fit(actor)) return actor;
                continue;
            }
            if (candidate == null && person.age >= 18 && World.world.cities.get(person.virtual_city_id)?.kingdom == kingdom)
                candidate = person;
        }
        Actor realized = candidate?.Realize();
        return realized?.data != null && fit(realized) ? realized : null;
    }

    #endregion

    #region 庄园部曲

    private static float ShelterShare(City city)
    {
        if (city?.data == null || !Sheltered.TryGetValue(city.data.id, out float share) || share <= 0f) return 0f;
        share = Mathf.Min(ShelterCap, share);
        string culture = CultureService.GetMainCulture(city, initialize: false);
        return CultureService.IsValidCulture(culture) && InstitutionSystem.IsEnacted(culture, EqualFieldNode) ? share * 0.5f : share;
    }

    // 荫户少收的税(乘在税率上，见 PopulationEconomySystem.PayTaxes)
    public static float TaxFactor(City city) => 1f - ShelterShare(city);

    // 荫户不服兵役(见 CorpsSystem)
    public static float ShelteredPeople(City city) => CityPopulationSystem.GetBackgroundTotal(city) * ShelterShare(city);

    // 每月：郡望之城告急，世家聚部曲守城
    private static void Monthly()
    {
        double now = World.world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        if (!CorpsSystem.Enabled) return;
        foreach (long id in GentryIds)
        {
            SpecificClan clan = SpecificClanManager.Get(id);
            City seat = Seat(clan);
            if (clan == null || !clan.gentry || seat?.kingdom == null || !Threatened(seat)) continue;
            if (clan.gentry_retainers_at >= 0d && now >= clan.gentry_retainers_at &&
                Date.getYearsSince(clan.gentry_retainers_at) < RetainerCooldownYears) continue;
            float share = Mathf.Clamp(clan.gentry_pedigree / 1000f, RetainerMin, RetainerMax);
            float before = CityPopulationSystem.GetBackgroundTotal(seat);
            if (CorpsSystem.RaiseMilitia(seat, 1, share) <= 0) continue;
            clan.gentry_retainers_at = now;
            int raised = Mathf.Max(0, Mathf.RoundToInt(before - CityPopulationSystem.GetBackgroundTotal(seat)));
            Record(seat, string.Format(LM.Get("gentry_retainers_history"), JunWang(clan), raised, seat.GetCityName()), force: true);
        }
    }

    private static bool Threatened(City city)
    {
        if (FrontSystem.Battles.Values.Any(battle => battle.city_id == city.data.id)) return true;
        Dictionary<int, long> occupied = ScorchedEarthSystem.OccupiedZones(city);
        return occupied != null && occupied.Values.Any(owner => World.world.kingdoms.get(owner) is Kingdom enemy &&
                                                                 enemy.isEnemy(city.kingdom));
    }

    // 据城自守：族中名望最高的人(实体优先，没有把郡望之城的虚拟族人落成实体)据郡望之城自立
    private static bool Autonomy(SpecificClan clan, City seat, string key)
    {
        if (seat?.kingdom == null || seat.kingdom.capital == seat) return false;
        Kingdom former = seat.kingdom;
        List<PersonalClanIdentity> living = clan.LivingPeople;
        Actor head = living.Where(person => !person.is_virtual).Select(person => person._actor)
            .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && actor.city == seat &&
                            !actor.isKing())
            .OrderByDescending(actor => actor.renown).FirstOrDefault()
            ?? living.FirstOrDefault(person => person.is_virtual && person.age >= 18 && person.virtual_city_id == seat.data.id)?.Realize();
        if (head?.data == null || head.isKing()) return false;
        if (head.isCityLeader() && head.city != null && head.city != seat) head.city.removeLeader();
        Kingdom own = seat.makeOwnKingdom(head);
        if (own == null) return false;
        Record(seat, string.Format(LM.Get(key), JunWang(clan), seat.GetCityName(), former.GetKingdomName()), force: true);
        return true;
    }

    // 乱世里的强宗：每年(Assess)
    private static void TryAutonomy(SpecificClan clan, City seat)
    {
        if (Grade(clan.gentry_pedigree) > 2 || seat?.kingdom == null || seat.kingdom.capital == seat || !seat.kingdom.hasEnemies())
            return;
        Empire realm = RealmOf(seat);
        bool weak = realm == null ? seat.kingdom.king == null
            : realm.Mandate <= AutonomyMandate || realm.Emperor == null || realm.Emperor.isRekt();
        if (!weak || UnityEngine.Random.value >= AutonomyChance) return;
        Autonomy(clan, seat, "gentry_autonomy_history");
    }

    // 天下裂土(InterregnumSystem.TryFracture)：郡望在京畿的甲等世家各有一半机会据城自守
    public static void OnFracture(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null) return;
        int count = 0;
        foreach (SpecificClan clan in Of(empire).Where(clan => Grade(clan.gentry_pedigree) == 1).ToList())
        {
            if (count >= FractureAutonomyMax) break;
            City seat = Seat(clan);
            if (seat?.kingdom != core || UnityEngine.Random.value >= FractureAutonomyChance) continue;
            if (Autonomy(clan, seat, "gentry_fracture_autonomy_history")) count++;
        }
    }

    #endregion

    #region 兴衰与站队

    private const int RegisterYears = 30, CurbYears = 20, CurbClans = 3, CurbMoneyPerClan = 150, SidesMandatePer = 40,
        SidesMandateMax = 10, UsurperGentryMandate = 5, UsurperDisdainPer = 2, UsurperDisdainMax = 8;

    // 两朝并立、天下裂土：世家依附郡望所在的那一朝
    public static void ChooseSides(IEnumerable<Empire> realms)
    {
        foreach (Empire realm in realms.Where(realm => realm?.data != null && !realm.isRekt()).Distinct().ToList())
        {
            List<SpecificClan> clans = Of(realm);
            if (clans.Count == 0) continue;
            foreach (SpecificClan clan in clans) clan.gentry_patron_empire_id = realm.data.id;
            int bonus = Mathf.Min(SidesMandateMax, Mathf.RoundToInt(clans.Sum(clan => clan.gentry_pedigree) / SidesMandatePer));
            if (bonus > 0) realm.AddMandate(bonus);
            string names = string.Join("、", clans.Take(2).Select(JunWang));
            string content = string.Format(LM.Get("gentry_sides_history"), names, clans.Count, realm.GetEmpireFullName());
            LogService.LogInfo("[EmpireCraft][世家] " + content);
            realm.RecordHistory(directContent: content);
        }
    }

    // 篡位(Empire.NewEmperor)：出自世家的诸世家多附之，起于寒门的诸世家耻之
    public static void OnUsurpation(Empire empire, Actor usurper)
    {
        if (empire?.data == null || usurper?.data == null) return;
        SpecificClan clan = usurper.GetSpecificClan();
        string content;
        if (clan != null && clan.gentry)
        {
            empire.AddMandate(UsurperGentryMandate);
            content = string.Format(LM.Get("gentry_usurper_history"), JunWang(clan), usurper.getName());
        }
        else
        {
            int proud = Of(empire).Count(other => Grade(other.gentry_pedigree) <= 2);
            if (proud == 0) return;
            empire.AddMandate(-Mathf.Min(UsurperDisdainMax, proud * UsurperDisdainPer));
            content = string.Format(LM.Get("gentry_upstart_history"), usurper.getName());
        }
        LogService.LogInfo("[EmpireCraft][世家] " + content);
        empire.RecordHistory(directContent: content, actorId: usurper.id);
    }

    private static bool Due(double at, int years) =>
        at < 0d || World.world.getCurWorldTime() < at || Date.getYearsSince(at) >= years;

    public static bool CanReviseRegister(Empire empire) =>
        empire?.data != null && Due(empire.data.gentry_register_at, RegisterYears) && Of(empire).Count >= 3;

    // 修氏族志：族中有人在朝的世家门第上升(每人 5，至多 15)，没人出仕的降 15；返回升、降的家数
    public static (int raised, int lowered) ReviseRegister(Empire empire)
    {
        if (empire?.data == null) return (0, 0);
        empire.data.gentry_register_at = World.world.getCurWorldTime();
        int raised = 0, lowered = 0;
        foreach (SpecificClan clan in Of(empire))
        {
            int office = clan.LivingPeople.Count(person => !person.is_virtual && person._actor?.data != null &&
                                                            !person._actor.isRekt() && (person._actor.IsOnOffice() || person._actor.isCityLeader()));
            if (office > 0)
            {
                clan.gentry_pedigree = Mathf.Min(100f, clan.gentry_pedigree + 5f * Mathf.Min(3, office));
                raised++;
            }
            else
            {
                clan.gentry_pedigree = Mathf.Max(0f, clan.gentry_pedigree - 15f);
                lowered++;
            }
        }
        empire.AddMandate(3);
        empire.RecordHistory(directContent: string.Format(LM.Get("gentry_register_history"), empire.GetEmpireFullName(), raised, lowered));
        return (raised, lowered);
    }

    public static bool CanCurbMagnates(Empire empire) =>
        empire?.data != null && Due(empire.data.gentry_curbed_at, CurbYears) &&
        Of(empire).Any(clan => Grade(clan.gentry_pedigree) <= 2);

    // 抑豪强：门第最高三家的田产减半(空出的田归民田)、家财入国库、门第 -10；返回家数、入库钱数
    public static (int clans, int money) CurbMagnates(Empire empire)
    {
        if (empire?.data == null || empire.CoreKingdom == null) return (0, 0);
        empire.data.gentry_curbed_at = World.world.getCurWorldTime();
        List<SpecificClan> targets = Of(empire).Take(CurbClans).ToList();
        foreach (SpecificClan clan in targets)
        {
            string key = $"s:{clan.id}";
            foreach (City city in World.world.cities.list)
            {
                if (city?.data == null || city.isRekt()) continue;
                Dictionary<string, float> shares = city.GetOrCreate().household_land_shares;
                if (shares != null && shares.TryGetValue(key, out float share) && share > 0f) shares[key] = share * 0.5f;
            }
            clan.gentry_pedigree = Mathf.Max(0f, clan.gentry_pedigree - 10f);
        }
        int money = targets.Count * CurbMoneyPerClan;
        if (money > 0) empire.CoreKingdom.AddMoney(money, TreasuryCategory.RecoveredFunds);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Noble, 6f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Landlord, 6f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -4f);
        if (targets.Count > 0)
            empire.RecordHistory(directContent: string.Format(LM.Get("gentry_curb_history"), empire.GetEmpireFullName(),
                string.Join("、", targets.Select(JunWang))));
        return (targets.Count, money);
    }

    #endregion

    #region 每年分批结算

    // 每帧(CityPopulationSystem 里跟着虚拟族谱一起推进)：一年一轮
    public static void Tick()
    {
        if (World.world == null) return;
        Monthly();
        if (Queue.Active)
        {
            Queue.Tick();
            return;
        }
        double now = World.world.getCurWorldTime();
        if (_lastPass >= 0d && now >= _lastPass && Date.getYearsSince(_lastPass) < 1) return;
        if (SpecificClanManager._specificClans.Count == 0) return;
        _lastPass = now;
        GentryIds.Clear();
        GentryIds.AddRange(SpecificClanManager._specificClans.Where(clan => clan != null && clan.gentry).Select(clan => clan.id));
        BuildLand();
        BuildRuling();
        Recorded.Clear();
        Queue.Start(SpecificClanManager._specificClans.Where(clan => clan != null && clan.extinct_since < 0d)
            .Select(clan => clan.id).ToList());
        Queue.Tick();
    }

    // 各宗族的私田：合计占地(按城的分数加总)、占地最多的城
    private static void BuildLand()
    {
        Land.Clear();
        Sheltered.Clear();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt()) continue;
            Dictionary<string, float> shares = city.GetOrCreate().household_land_shares;
            if (shares == null) continue;
            foreach (KeyValuePair<string, float> pair in shares)
            {
                if (pair.Value <= 0f || !pair.Key.StartsWith("s:", StringComparison.Ordinal) ||
                    !long.TryParse(pair.Key.Substring(2), out long clanId)) continue;
                // 荫户：世家在这座城的田产(份额是百分数)
                SpecificClan owner = SpecificClanManager.Get(clanId);
                if (owner != null && owner.gentry)
                {
                    Sheltered.TryGetValue(city.data.id, out float sheltered);
                    Sheltered[city.data.id] = sheltered + pair.Value / 100f * ShelterPerShare * owner.gentry_pedigree / 100f;
                }
                Land.TryGetValue(clanId, out var held);
                float best = held.city > 0 && World.world.cities.get(held.city) is City bestCity &&
                             bestCity.GetOrCreate().household_land_shares.TryGetValue(pair.Key, out float bestShare)
                    ? bestShare : 0f;
                Land[clanId] = (held.share + pair.Value, pair.Value > best ? city.data.id : held.city);
            }
        }
    }

    // 在位皇帝的宗室(连同分出去的各支)：是皇族，不算世家
    private static void BuildRuling()
    {
        Ruling.Clear();
        if (ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.EmpireSpecificClan == null) continue;
            foreach (SpecificClan clan in SpecificClanManager.LineageClans(empire.EmpireSpecificClan)) Ruling.Add(clan);
        }
    }

    private static void Assess(long id)
    {
        SpecificClan clan = SpecificClanManager.Get(id);
        if (clan == null) return;
        List<PersonalClanIdentity> living = clan.LivingPeople;
        City seat = living.Count == 0 ? null : FindSeat(clan, living);
        if (living.Count == 0 || Ruling.Contains(clan) || !Huaxia(seat))
        {
            if (clan.gentry && living.Count == 0) Decline(clan, extinct: true);
            else if (clan.gentry) clan.gentry = false;
            return;
        }
        clan.gentry_seat_city_id = seat.data.id;
        float target = Target(clan, living);
        if (!clan.gentry_assessed)
        {
            // 头一次直接到位、不记史
            clan.gentry_assessed = true;
            clan.gentry_pedigree = target;
            clan.gentry = target >= EnterAt;
            if (clan.gentry) clan.gentry_since = World.world.getCurWorldTime();
            NoteTrend(clan);
            return;
        }
        float pedigree = clan.gentry_pedigree;
        // 九品中正：门第固化，跌得慢一半
        float fall = FallRate * (InstitutionSystem.IsEnacted(CultureService.GetMainCulture(seat, initialize: false), NineRankNode) ? 0.5f : 1f);
        pedigree += (target - pedigree) * (target > pedigree ? RiseRate : fall);
        clan.gentry_pedigree = Mathf.Clamp(pedigree, 0f, 100f);
        NoteTrend(clan);
        if (!clan.gentry && clan.gentry_pedigree >= EnterAt)
        {
            clan.gentry = true;
            clan.gentry_since = World.world.getCurWorldTime();
            string rise = string.Format(LM.Get("gentry_rise_history"), JunWang(clan), GradeName(clan));
            Record(seat, rise);
            NoteClan(clan, "clan_history_gentry_rise", rise);
            clan.gentry_noted_grade = Grade(clan.gentry_pedigree);
        }
        else if (clan.gentry && clan.gentry_pedigree < LeaveAt) Decline(clan, extinct: false);
        else if (clan.gentry) NoteGrade(clan);
        if (clan.gentry) TryAutonomy(clan, seat);
    }

    private static void Decline(SpecificClan clan, bool extinct)
    {
        string name = JunWang(clan);
        City seat = Seat(clan);
        clan.gentry = false;
        string text = string.Format(LM.Get(extinct ? "gentry_extinct_history" : "gentry_decline_history"), name);
        Record(seat, text);
        NoteClan(clan, "clan_history_gentry_decline", text);
    }

    private const int TrendYears = 80, ClanHistoryCap = 40;
    // 门第走势只给世家与够得着世家的(门第 20 以上)记，跌到 20 以下再记一笔就停，免得每个寒门都存八十年
    public const float TrendFloor = 20f;
    private const float GradeMargin = 2f;

    // 门第换了等(离分界 2 分以上才算，回落、被括户抑豪强压下去的也在这里记)
    private static void NoteGrade(SpecificClan clan)
    {
        float pedigree = clan.gentry_pedigree;
        int grade = Grade(pedigree);
        if (Grade(pedigree + GradeMargin) != grade || Grade(pedigree - GradeMargin) != grade) return;
        int noted = clan.gentry_noted_grade;
        clan.gentry_noted_grade = grade;
        if (noted <= 0 || noted == grade) return;
        NoteClan(clan, "clan_history_gentry_grade", string.Format(LM.Get(grade < noted ? "clan_history_grade_up" : "clan_history_grade_down"),
            JunWang(clan), GradeName(clan)));
    }

    // 门第走势记一笔：同一年只留一笔(读档重算、补算时覆盖)
    private static void NoteTrend(SpecificClan clan)
    {
        int year = Date.getCurrentYear();
        int value = Mathf.RoundToInt(clan.gentry_pedigree);
        int last = (clan.pedigree_years?.Count ?? 0) - 1;
        bool recording = last >= 0 && clan.pedigree_years[last] >= year - 1;
        if (!clan.gentry && clan.gentry_pedigree < TrendFloor && !recording) return;
        clan.pedigree_years ??= new List<int>();
        clan.pedigree_values ??= new List<int>();
        if (clan.pedigree_values.Count != clan.pedigree_years.Count)
        {
            clan.pedigree_years.Clear();
            clan.pedigree_values.Clear();
            last = -1;
        }
        if (last >= 0 && clan.pedigree_years[last] == year)
        {
            clan.pedigree_values[last] = value;
            return;
        }
        clan.pedigree_years.Add(year);
        clan.pedigree_values.Add(value);
        while (clan.pedigree_years.Count > TrendYears)
        {
            clan.pedigree_years.RemoveAt(0);
            clan.pedigree_values.RemoveAt(0);
        }
    }

    // 宗族大事记一条(宗族窗口大事记用)
    private static void NoteClan(SpecificClan clan, string key, string content)
    {
        clan.clan_history ??= new List<PersonalHistoryRecord>();
        clan.clan_history.Add(new PersonalHistoryRecord
        {
            timestamp = World.world.getCurWorldTime(), date = Date.getDate(World.world.getCurWorldTime()),
            content = content, event_key = key, major = true
        });
        if (clan.clan_history.Count > ClanHistoryCap) clan.clan_history.RemoveAt(0);
    }

    // 郡望：田产最多的城；没有田产按祖籍；再没有按族人最多的城
    private static City FindSeat(SpecificClan clan, List<PersonalClanIdentity> living)
    {
        if (Land.TryGetValue(clan.id, out var held) && World.world.cities.get(held.city) is City landed && landed.data != null &&
            !landed.isRekt()) return landed;
        City ancestral = clan.ancestral_city_id > 0 ? World.world.cities.get(clan.ancestral_city_id) : null;
        if (ancestral?.data != null && !ancestral.isRekt()) return ancestral;
        long cityId = living.Select(person => person.is_virtual ? person.virtual_city_id : person._actor?.city?.data?.id ?? -1L)
            .Where(cityIdOf => cityIdOf > 0).GroupBy(cityIdOf => cityIdOf).OrderByDescending(group => group.Count())
            .Select(group => group.Key).FirstOrDefault();
        City common = cityId > 0 ? World.world.cities.get(cityId) : null;
        return common?.data != null && !common.isRekt() ? common : null;
    }

    private static float Target(SpecificClan clan, List<PersonalClanIdentity> living)
    {
        float office = 0f, marriage = 0f;
        var renown = new List<int>();
        foreach (PersonalClanIdentity person in living)
        {
            Actor actor = person.is_virtual ? null : person._actor;
            if (actor?.data != null && !actor.isRekt() && actor.isAlive())
            {
                if (actor.isKing()) office += 12f;
                else if (actor.IsOnOffice()) office += 6f;
                else if (actor.isCityLeader()) office += 4f;
                if (actor.HasHonoraryPeerage()) office += 6f;
                office += 5f * (actor.GetOwnedTitle()?.Count ?? 0);
                renown.Add(actor.renown);
            }
            else
            {
                if (!string.IsNullOrEmpty(person.officeName)) office += 3f;
                office += 5f * (person.ownedTitleNames?.Count ?? 0);
            }
            if (!string.IsNullOrEmpty(person.prince_title)) office += 6f;
            SpecificClan spouse = person.lover.identity > 0 ? SpecificClanManager.getPerson(person.lover.identity)?._specificClan : null;
            if (spouse != null && spouse != clan && (spouse.gentry || Ruling.Contains(spouse))) marriage += 2f;
        }
        float score = Mathf.Min(OfficeCap, office);
        if (Land.TryGetValue(clan.id, out var held)) score += Mathf.Min(15f, held.share * 1.5f);
        score += Mathf.Min(10f, renown.OrderByDescending(value => value).Take(3).Sum() / 30f);
        score += Mathf.Min(10f, living.Count * 0.4f);
        if (clan.established_timestamp > 0d) score += Mathf.Min(10f, Date.getYearsSince(clan.established_timestamp) / 15f);
        score += Mathf.Min(8f, marriage);
        return Mathf.Clamp(score, 0f, 100f);
    }

    private static void Record(City seat, string content, bool force = false)
    {
        LogService.LogInfo("[EmpireCraft][世家] " + content);
        Empire empire = RealmOf(seat);
        if (empire == null) return;
        Recorded.TryGetValue(empire.data.id, out int count);
        if (!force && count >= MaxRecordsPerEmpire) return;
        Recorded[empire.data.id] = count + 1;
        empire.RecordHistory(directContent: content);
        if (force) TranslateHelper.LogEventMessage(content, seat?.kingdom);
    }

    #endregion
}

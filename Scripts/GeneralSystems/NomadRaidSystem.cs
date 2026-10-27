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

// 游牧联盟劫掠(用户 2026-10-20 定的游牧基调"税收低，可以劫掠，大汗死后会分裂，高扩张，低控制"；问答定劫掠的规矩)：
// 草原压力大时游牧临时结盟南下，劫掠定居之国，抢够就退、战争结束、联盟解散；盟主屡胜可成雄主，联盟转为汗国。
//   · 草原压力(每年每个有劫掠传统的国家算一次，算式见 SteppeRules)：纪元(寒冬最甚、黑暗次之，只算游牧)、牲畜粮食(白灾)、
//     国库见底(欠饷；得着朝贡赏赐的减半)、互市断绝、岁币停付与朝贡赏赐断绝(只算游牧)；到 40 就要劫掠。维京这类有劫掠传统的非游牧文化只算国库与粮食，只单国劫掠；
//   · 结盟：压力够的游牧政权(独立的国或帝国核心)按国力从强到弱当盟主召集——接壤的游牧政权(本汗国的兀鲁思也算)按自身压力、
//     亲疏(同文化、姻亲、同一汗国)、强弱掷入盟(比盟主强的不跟)，一盟至多 MaxMembers 部；没人来就单独劫掠
//     (每年 SingleRaidChance 的机会，而且要比对方强)；
//   · 选目标：盟里各部接壤的定居之国(连同定居帝国的边地成员)，过开战闸门(停战、同盟、正给盟里送岁币的不打，见 WarGate)，
//     五年内遭劫过的不打；联盟兵力要有对方八成(单国要强过对方)；按财富 × 强弱 × 关隘之固打分，断了岁币、互市的那家加倍；
//   · 劫掠之战(EmpireWarType.劫掠，只抢不占，见 NomadRaidSystem.War)；出过兵的部 RaidCooldownYears 年内不再出兵；
//   · 汗国已有的游牧帝国核心也照此结盟；同在一个帝国里的定居政权、羁縻的部落不劫。
// 进不了游戏核对的数字都在 SteppeRules 与下面的常量里；诊断日志 [EmpireCraft][游牧劫掠]。
public static partial class NomadRaidSystem
{
    private const int RaidCooldownYears = 3, VictimImmunityYears = 5, TributeGrievanceYears = 10, MarketCutYears = 2;
    // 火器时代以后草原骑兵劫掠不成(科技时代 tier：6 = 火药，7 = 工业)
    private const int MaxRaidEraTier = 6;
    private const int MaxMembers = 5;
    private const float SingleRaidChance = 0.5f, LeaguePowerShare = 0.8f, MuchWeaker = 0.5f;
    private const int HostileOpinion = -20;
    public const string LogTag = "[EmpireCraft][游牧劫掠]";

    private static object _world;
    private static double _lastMonth = -1d, _lastYear = -1d;

    // 正在开劫掠之战(startWar 前后)：开战钩子据此不拉姻亲、不扩封建、不升级世界大战(见 HolyRomanSystem.ContainedWar)
    public static bool Declaring;

    public static SteppeState State(Kingdom kingdom) => kingdom.GetOrCreate().steppe ??= new SteppeState();

    // 全世界扫描(CityPatch 每帧轮一个)：每月一次劫掠战的进展，每年一次草原压力与结盟
    public static void TryMonthlyScan()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _lastMonth = _lastYear = -1d;
        }
        double now = World.world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        NoticeEra();
        try
        {
            MonthTick();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 月度结算失败: {exception.Message}");
        }
        if (_lastYear >= 0d && now >= _lastYear && Date.getYearsSince(_lastYear) < 1) return;
        bool first = _lastYear < 0d;
        _lastYear = now;
        // 读档后头一个月只记起点，满一年再结算(不在读档时爆发结盟)
        if (first) return;
        try
        {
            using (FrameProfiler.Measure("游牧劫掠·年度")) YearTick();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 年度结算失败: {exception.Message}");
        }
    }

    // 纪元转入、转出苦寒(寒冬、五谷不生)时提示一句(用户 2026-10-27："冰封纪元……要给提醒……不然玩家不知道什么原因")：
    // 世上有游牧政权才提示；读档后第一次只记下当前纪元
    private static string _lastAge;
    private static bool _lastHard;

    private static void NoticeEra()
    {
        WorldAgeAsset age = World.world_era;
        if (age == null) return;
        bool hard = SteppeRules.HardEra(age.flag_winter, age.flag_crops_grow);
        string previous = _lastAge;
        bool wasHard = _lastHard;
        _lastAge = age.id;
        _lastHard = hard;
        if (previous == null || previous == age.id || hard == wasHard && !hard) return;
        if (!World.world.kingdoms.list.Any(kingdom => Alive(kingdom) && IsNomad(kingdom))) return;
        string name = LocalizedTextManager.getText(age.getLocaleID());
        string key = hard ? age.flag_winter ? "steppe_era_notice_winter" : "steppe_era_notice_dark" : "steppe_era_notice_relief";
        string text = string.Format(LM.Get(key), name);
        TranslateHelper.LogMajor(text);
        LogService.LogInfo($"{LogTag} 纪元转为 {name}({age.id})：{text}");
    }

    #region 身份

    private static bool Alive(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && kingdom.hasCapital() && kingdom.cities?.Count > 0;

    // 一方之主：独立的国或帝国核心
    private static bool IsRealm(Kingdom kingdom) =>
        Alive(kingdom) && (kingdom.GetEmpire() is not Empire empire || empire.CoreKingdom == kingdom);

    private static Kingdom Realm(Kingdom kingdom) => FrontSystem.Realm(kingdom);

    // 游牧：本国文化逐水草而居；帝国看主体文化与统治族群(复合帝国)
    public static bool IsNomad(Kingdom kingdom)
    {
        if (!Alive(kingdom)) return false;
        if (FrontierRelations.IsNomadRealm(kingdom)) return true;
        Empire empire = kingdom.GetEmpire();
        return empire?.CoreKingdom == kingdom && ClaimRules.HasTrait(empire, OnomasticsRule.NomadTrait);
    }

    // 有劫掠传统(草原、维京)
    public static bool IsRaider(Kingdom kingdom)
    {
        if (!Alive(kingdom)) return false;
        if (CultureService.GetRealmCulture(kingdom).HasPoliticalTrait(OnomasticsRule.RaiderTrait)) return true;
        Empire empire = kingdom.GetEmpire();
        return empire?.CoreKingdom == kingdom && ClaimRules.HasTrait(empire, OnomasticsRule.RaiderTrait);
    }

    private static Kingdom LiveKingdom(long id)
    {
        Kingdom kingdom = id >= 0 ? World.world.kingdoms.get(id) : null;
        return kingdom?.data != null && !kingdom.isRekt() ? kingdom : null;
    }

    private static War LiveWar(long id)
    {
        if (id < 0 || World.world?.wars == null) return null;
        War war = World.world.wars.get(id);
        return war?.data != null && war.isAlive() && !war.hasEnded() ? war : null;
    }

    private static float YearsSince(double at) =>
        at < 0d || World.world.getCurWorldTime() < at ? float.MaxValue : Date.getYearsSince(at);

    // 本国(不连帝国成员)的兵：原版士兵 + 各军团
    private static float Troops(Kingdom kingdom) =>
        kingdom.countTotalWarriors() + (CorpsSystem.Enabled ? CorpsSystem.Of(kingdom).Sum(corps => Mathf.Max(0f, corps.strength)) : 0f);

    // 劫掠时拼的国力：帝国核心算整个帝国，帝国的边地成员只算它自己(中央不为劫掠出兵，见 MemberWarAid)
    private static double PowerOf(Kingdom kingdom) =>
        Realm(kingdom) == kingdom ? WarSituation.Power(kingdom) : kingdom.GetNationalPower();

    #endregion

    #region 草原压力

    // 一国的草原压力(四项见 SteppeRules)，写进 SteppeState
    private static void Assess(Kingdom kingdom, double now)
    {
        SteppeState state = State(kingdom);
        if (state.watch_since < 0d) state.watch_since = now;
        bool nomad = IsNomad(kingdom);
        WorldAgeAsset age = World.world_era;
        state.era = SteppeRules.Era(nomad, age?.flag_winter == true, age == null || age.flag_crops_grow,
            World.world.map_stats?.current_age_progress ?? 0f);
        int cities = 0, famine = 0;
        float herd = 0f, capacity = 0f;
        foreach (City city in kingdom.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            cities++;
            if (FamineUnrestSystem.InFamine(city)) famine++;
            if (!nomad) continue;
            herd += CityPopulationSystem.Get(city)?.herd ?? 0f;
            capacity += AnimalHusbandrySystem.HerdCapacity(city);
        }
        state.herd = SteppeRules.Herd(nomad, cities > 0 ? famine / (float)cities : 0f, capacity > 0f ? herd / capacity : 1f);
        if (TreasurySystem.Enabled(kingdom))
        {
            long reserve = TreasurySystem.SafetyReserve(kingdom);
            float ratio = reserve > 0 ? (float)kingdom.GetTreasuryBalance() / reserve : 1f;
            TreasuryReport report = TreasurySystem.Report(kingdom);
            state.treasury = SteppeRules.Treasury(CorpsSystem.Unpaid(kingdom), ratio, report.months >= 3 && report.operating_balance < 0);
            // 朝贡换赏赐：一年半内得过定居宗主的赏赐，国库一项减半(见 SteppeOrderSystem)
            if (SteppeOrderSystem.Gifted(state)) state.treasury *= 0.5f;
        }
        else state.treasury = 0f;
        TrackTribute(kingdom, state, now);
        state.market = SteppeRules.Market(nomad, TributeCut(state) != null, MarketCut(state), SteppeOrderSystem.GiftCut(state) != null);
        state.pressure = SteppeRules.Total(state.era, state.herd, state.treasury, state.market);
        state.pressure_at = now;
    }

    // 岁币：上回看到的出钱帝国这回没了(二十年到期、付不起)，记下停付
    private static void TrackTribute(Kingdom kingdom, SteppeState state, double now)
    {
        long payer = kingdom.GetGivenAllianceEmpire()?.CoreKingdom?.id ?? -1L;
        if (state.tribute_payer_id >= 0 && state.tribute_payer_id != payer)
        {
            state.tribute_cut_from = state.tribute_payer_id;
            state.tribute_cut_at = now;
            Kingdom former = LiveKingdom(state.tribute_payer_id);
            if (former != null)
                LogService.LogInfo($"{LogTag} {kingdom.GetKingdomName()} 的岁币停了(原由{former.GetKingdomName()}送)");
        }
        state.tribute_payer_id = payer;
    }

    // 十年内停了岁币的那国(还在)；没有返回 null
    private static Kingdom TributeCut(SteppeState state) =>
        state.tribute_cut_at >= 0d && YearsSince(state.tribute_cut_at) < TributeGrievanceYears ? LiveKingdom(state.tribute_cut_from) : null;

    // 互市断绝：通过市、两年没再通(买和开的互市还在期内的不算)
    private static bool MarketCut(SteppeState state) =>
        state.market_at >= 0d && YearsSince(state.market_at) >= MarketCutYears && !TreatyActive(state);

    private static bool TreatyActive(SteppeState state) =>
        state.market_treaty_with >= 0 && state.market_treaty_at >= 0d && YearsSince(state.market_treaty_at) < MarketTreatyYears;

    // 互市得利(FrontierRelations.Market 调用)：游牧一方记下时间与对方
    public static void NoteMarket(Kingdom a, Kingdom b)
    {
        if (World.world == null) return;
        foreach ((Kingdom own, Kingdom other) in new[] { (a, b), (b, a) })
        {
            if (own?.data == null || own.isRekt() || !IsNomad(own)) continue;
            SteppeState state = State(own);
            state.market_at = World.world.getCurWorldTime();
            state.market_partner_id = other?.id ?? -1L;
        }
    }

    // 买和开的互市(期内照常通市，见 FrontierRelations.Market)
    public static bool MarketTreaty(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null) return false;
        foreach ((Kingdom own, Kingdom other) in new[] { (a, b), (b, a) })
        {
            SteppeState state = own.GetOrCreate().steppe;
            if (state != null && TreatyActive(state) && state.market_treaty_with == Realm(other)?.id) return true;
        }
        return false;
    }

    #endregion

    #region 结盟与选目标

    private static void YearTick()
    {
        double now = World.world.getCurWorldTime();
        var raiders = new List<Kingdom>();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!IsRaider(kingdom)) continue;
            try
            {
                Assess(kingdom, now);
                raiders.Add(kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"{LogTag} {kingdom.GetKingdomName()} 草原压力结算失败: {exception.Message}");
            }
        }
        foreach (Kingdom kingdom in raiders.Where(IsRealm))
        {
            SteppeState state = State(kingdom);
            LogService.LogInfo($"{LogTag} {kingdom.GetKingdomName()} 草原压力{state.pressure:0}(纪元{state.era:0} 牲畜{state.herd:0} " +
                               $"国库{state.treasury:0} 互市{state.market:0}){(IsNomad(kingdom) ? "" : "[非游牧]")}" +
                               (state.prestige > 0 ? $" 盟主威望{state.prestige}" : ""));
        }
        // 草原常态：争草场等(见 SteppeOrderSystem)
        using (FrameProfiler.Measure("游牧劫掠·草原常态")) SteppeOrderSystem.Yearly(raiders);
        var assigned = new HashSet<Kingdom>();
        foreach (Kingdom leader in raiders.Where(kingdom => IsRealm(kingdom) && State(kingdom).pressure >= SteppeRules.RaidThreshold)
                     .OrderByDescending(PowerOf).ToList())
        {
            if (assigned.Contains(leader) || !CanRaid(leader)) continue;
            try
            {
                Muster(leader, assigned);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"{LogTag} {leader.GetKingdomName()} 结盟劫掠失败: {exception.Message}");
            }
        }
    }

    // 能不能出兵：活着、有君、不在打仗、冷却已过、时代还没到工业
    private static bool CanRaid(Kingdom kingdom)
    {
        if (!Alive(kingdom) || !kingdom.hasKing() || kingdom.hasEnemies() || WarSituation.TooBusyForWar(kingdom)) return false;
        if (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling()) return false;
        if (TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(kingdom)) > MaxRaidEraTier) return false;
        SteppeState state = State(kingdom);
        if (LiveWar(state.raid_war_id) != null) return false;
        return state.last_raid_at < 0d || YearsSince(state.last_raid_at) >= RaidCooldownYears;
    }

    private static void Muster(Kingdom leader, HashSet<Kingdom> assigned)
    {
        List<Kingdom> members = IsNomad(leader) ? Gather(leader, assigned) : new List<Kingdom> { leader };
        (Kingdom target, double score) = PickTarget(leader, members);
        if (target == null)
        {
            LogService.LogInfo($"{LogTag} {leader.GetKingdomName()} 压力{State(leader).pressure:0} 想劫掠，四邻无可劫之国" +
                               (members.Count > 1 ? $"(应召{members.Count - 1}部作罢)" : ""));
            return;
        }
        if (members.Count == 1 && UnityEngine.Random.value >= SingleRaidChance) return;
        foreach (Kingdom member in members) assigned.Add(member);
        Launch(members, target, score);
    }

    // 召集：接壤的游牧政权(连同本汗国的兀鲁思)按自身压力、亲疏、强弱掷入盟；新入盟的接壤之部也一并召集
    private static List<Kingdom> Gather(Kingdom leader, HashSet<Kingdom> assigned)
    {
        var members = new List<Kingdom> { leader };
        Empire own = leader.GetEmpire();
        double leaderPower = PowerOf(leader);
        string culture = CultureService.GetRealmCulture(leader);
        int opinionFloor = HostileOpinion;
        var asked = new HashSet<Kingdom> { leader };
        var queue = new Queue<Kingdom>();
        queue.Enqueue(leader);
        while (queue.Count > 0 && members.Count < MaxMembers)
        {
            Kingdom from = queue.Dequeue();
            var candidates = new List<Kingdom>();
            foreach (City city in from.cities)
                foreach (Kingdom next in city?.neighbours_kingdoms ?? new HashSet<Kingdom>())
                    if (next != null && !candidates.Contains(next)) candidates.Add(next);
            if (from == leader && own?.CoreKingdom == leader)
                foreach (Kingdom member in own.kingdoms_list)
                    if (member != null && !candidates.Contains(member)) candidates.Add(member);
            foreach (Kingdom candidate in candidates.OrderBy(kingdom => kingdom.id))
            {
                if (members.Count >= MaxMembers) break;
                if (!asked.Add(candidate) || assigned.Contains(candidate) || !IsNomad(candidate) || !CanRaid(candidate)) continue;
                bool sameRealm = own != null && candidate.GetEmpire() == own;
                if (sameRealm)
                {
                    // 本汗国的兀鲁思：游牧政体的成员(行省、羁縻不出兵)
                    if (candidate == own.CoreKingdom || candidate.GetKingdomType() == KingdomType.YouMu_province ||
                        JimiSystem.IsJimiAdministration(candidate) || JimiSystem.IsJimiRegion(candidate, own) ||
                        candidate.GetRegime()?.type is not (RegimeType.YouMu or RegimeType.NomadFeudalism)) continue;
                }
                else if (!IsRealm(candidate)) continue;
                double power = PowerOf(candidate);
                if (!sameRealm && power > leaderPower) continue;
                bool sameCulture = CultureService.SameKin(culture, CultureService.GetRealmCulture(candidate));
                bool inLaw = RoyalMarriageSystem.Allied(leader, candidate);
                bool hostile = (World.world.diplomacy?.getOpinion(candidate, leader)?.total ?? 0) < opinionFloor;
                float chance = SteppeRules.JoinChance(State(candidate).pressure, sameCulture, inLaw,
                    sameRealm || SameOverlord(leader, candidate), power < leaderPower * MuchWeaker, hostile);
                if (UnityEngine.Random.value >= chance) continue;
                members.Add(candidate);
                queue.Enqueue(candidate);
            }
        }
        return members;
    }

    // 同奉一主：同一个宗主的朝贡国，或共事同一个部落联盟
    private static bool SameOverlord(Kingdom a, Kingdom b)
    {
        Empire lordA = a.GetTakenAllianceEmpire() ?? TribalFissionService.OverlordOf(a);
        return lordA != null && lordA == (b.GetTakenAllianceEmpire() ?? TribalFissionService.OverlordOf(b));
    }

    // 盟里各部接壤的定居之国里挑一家
    private static (Kingdom target, double score) PickTarget(Kingdom leader, List<Kingdom> members)
    {
        double ours = members.Sum(PowerOf);
        bool league = members.Count > 1;
        var seen = new HashSet<Kingdom>();
        Kingdom best = null;
        double bestScore = 0d;
        foreach (Kingdom member in members)
            foreach (City city in member.cities)
                foreach (Kingdom target in city?.neighbours_kingdoms ?? new HashSet<Kingdom>())
                {
                    if (!seen.Add(target) || !Raidable(leader, members, target)) continue;
                    double theirs = Math.Max(1d, PowerOf(target));
                    double ratio = ours / theirs;
                    if (ratio < (league ? LeaguePowerShare : 1d) || !league && ratio <= 1d) continue;
                    double score = SteppeRules.TargetScore(Wealth(target), ratio, Defense(members, target), Grievance(members, target));
                    if (score <= bestScore) continue;
                    best = target;
                    bestScore = score;
                }
        return (best, bestScore);
    }

    // 可劫：定居之国，不在盟里、不与盟里同一帝国，五年内没遭劫，过得了开战闸门，没给盟里送岁币，没在被别家劫
    private static bool Raidable(Kingdom leader, List<Kingdom> members, Kingdom target)
    {
        if (!Alive(target) || members.Contains(target) || IsNomad(Realm(target))) return false;
        // 劫异族：同一文化的不劫(维京不劫维京)
        if (CultureService.GetRealmCulture(Realm(target)) == CultureService.GetRealmCulture(leader)) return false;
        Empire theirs = target.GetEmpire();
        foreach (Kingdom member in members)
        {
            if (theirs != null && member.GetEmpire() == theirs) return false;
            if (theirs != null && member.GetGivenAllianceEmpire() == theirs) return false;
            if (MarketTreaty(member, target)) return false;
        }
        if (TribalFissionService.IsTribal(target) && TribalFissionService.IsTribal(leader)) return false;
        if (target.GetOrCreate().raided_at >= 0d && YearsSince(target.GetOrCreate().raided_at) < VictimImmunityYears) return false;
        if (target.getWars().Any(war => war?.data != null && !war.hasEnded() && war.GetEmpireWarType() == EmpireWarType.劫掠 &&
                                       war.isDefender(target))) return false;
        // 开战的是盟主：目标只挨着盟里远处的某部、盟主够不着的，开战会被远战闸拦下(见 WarReach)，劫掠白白作罢
        return WarGate.CanDeclare(leader, target) && WarReach.Reachable(leader, target);
    }

    // 财富：国库 + 存粮 + 户数(同一量级的粗算)
    private static double Wealth(Kingdom target)
    {
        double food = target.cities.Where(city => city?.data != null && !city.isRekt()).Sum(city => (double)city.getTotalFood());
        int households = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.Households(target) : target.units?.Count ?? 0;
        return Math.Max(0d, target.GetMoney()) + food + households;
    }

    // 断了盟里哪部的岁币、互市的那家
    private static bool Grievance(List<Kingdom> members, Kingdom target)
    {
        long realm = Realm(target)?.id ?? -1L;
        foreach (Kingdom member in members)
        {
            SteppeState state = State(member);
            if (TributeCut(state)?.id == realm || SteppeOrderSystem.GiftCut(state)?.id == realm) return true;
            if (MarketCut(state) && state.market_partner_id == realm) return true;
        }
        return false;
    }

    #endregion

    #region 开战

    private static void Launch(List<Kingdom> members, Kingdom target, double score)
    {
        Kingdom leader = members[0];
        War war;
        Declaring = true;
        try
        {
            war = World.world.diplomacy.startWar(leader, target, WarTypeLibrary.normal);
            if (war?.data != null)
            {
                war.SetEmpireWarType(EmpireWarType.劫掠);
                foreach (Kingdom member in members.Skip(1)) war.joinAttackers(member);
            }
        }
        finally
        {
            Declaring = false;
        }
        if (war?.data == null) return;
        double now = World.world.getCurWorldTime();
        var joined = members.Where(member => war.isAttacker(member)).ToList();
        var state = new RaidWarState
        {
            leader_id = leader.id, leader_name = leader.GetKingdomName(), target_name = target.GetKingdomName(),
            members = joined.Select(member => member.id).ToList(),
            troops = joined.ToDictionary(member => member.id, member => Mathf.Max(1f, Troops(member))),
            goal = SteppeRules.SackGoal(joined.Count), defense = Defense(joined, target), started_at = now
        };
        war.GetOrCreate().raid = state;
        foreach (Kingdom member in joined)
        {
            SteppeState steppe = State(member);
            steppe.last_raid_at = now;
            steppe.raid_war_id = war.data.id;
        }
        bool league = joined.Count > 1;
        string others = string.Join("、", joined.Skip(1).Select(member => member.GetKingdomName()));
        war.data.name = string.Format(LM.Get(league ? "nomad_raid_league_war_name" : "nomad_raid_war_name"),
            state.leader_name, state.target_name);
        string text = league
            ? string.Format(LM.Get("nomad_raid_league_history"), state.leader_name, leader.king?.getName() ?? "", others,
                state.target_name)
            : string.Format(LM.Get("nomad_raid_single_history"), state.leader_name, state.target_name);
        Chronicle(text, joined.Concat(new[] { target }));
        LogService.LogInfo($"{LogTag} {(league ? "结盟南下" : "单独劫掠")}：盟主{state.leader_name}(压力{State(leader).pressure:0})" +
                           (league ? $"，应召{others}" : "") + $"，目标{state.target_name}(打分{score:0}，关隘之固{state.defense:0.00})，" +
                           $"要抢{state.goal:0.#}城，兵{state.troops.Values.Sum():0}");
    }

    // 记一笔：世界日志一条，涉及各国所在的帝国史书各一条
    private static void Chronicle(string text, IEnumerable<Kingdom> involved)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Kingdom first = null;
        var empires = new HashSet<Empire>();
        foreach (Kingdom kingdom in involved)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            first ??= kingdom;
            Empire empire = kingdom.GetEmpire();
            if (empire?.data != null && !empire.isRekt() && empires.Add(empire))
                empire.RecordHistory(directContent: text, kingdomId: kingdom.id);
        }
        TranslateHelper.LogEventOnly(text, first);
    }

    #endregion
}

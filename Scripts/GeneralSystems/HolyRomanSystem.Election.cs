using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
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

// 选帝侯会选(选帝制、有帝国议会的帝国；没有议会的选帝制仍走 OfficeSelector 的老办法)：
//   · 时机：皇帝死后、中央无君时(EmpireCraftKingdomBehCheckKing 在 office.Select 之前调 TryHoldElection)；选立的储君在世则
//     直接即位(老办法)；
//   · 候选：成员国君主(成年、能任职、非被禁，自由城市与主教领除外)，按基础票势(各在座选帝侯打分之和)取前四；
//   · 打分(每位在座选帝侯给每个候选)：自己 +1.0；同族 +0.5；同派系 +0.3；好感/100 夹 ±0.3；同宗教 +0.1、异宗教 −0.4；
//     国力 0.25 × min(1, 国力/最强候选国力)；候选与当今皇族同族且已连任两届以上 +0.15；皇帝党或教皇党同党 +0.1；
//     收了钱 +0.4 × min(1, 收的钱 / max(100, 选帝侯年收))；随机 ±0.1。计票见 ImperialElectionRules.Tally；
//   · 拉票：皇帝年满 55(没有成年子嗣时 50)、没有储君时，每年票势前两名的候选各拿出 min(国库两成, 年收一成五)，
//     分给还没倒向自己的选帝侯(钱进选帝侯国库)；记在 campaign，每年减半，选完清空；
//   · 选立储君也过票：照旧每年两成发起，皇帝替储君出一成年收拉票，储君与票势最高的诸侯决选，过半才立；
//   · 双重选举(决选平票)：首席选帝侯所投者即位；另一位称对立国王，权威 −10，开"{国}争位之战"——对立国王与投他的
//     选帝侯所在国(各六成)对中央与投皇帝的选帝侯国(各六成)，不拉姻亲、不升级；打下中央都城正统只扣 10；
//     打满五年强制议和。攻方胜——对立国王的领国成为中央(不扣正统)，让步一项、权威再 −5；守方胜或议和——对立国王放弃，权威 +5；
//   · 大空位：在座选帝侯不足三位、没有候选，或权威不到 15 时四成"选帝侯不肯会选"——先帝家族领国暂不立君，由在座世俗选帝侯中
//     国力最强的非候选者代理帝国(没有就最强诸侯；只挂名、主持议会，史书不记为皇帝)；权威开头 −10、每年 −4；
//     每年重开会选，成功几率 35% + 每过一年 15%；满五年由帝国代理定夺；期间议会只议帝国战争与禁令，不选储君、不加冕、
//     不授选帝侯位，私战几率 ×1.5。
public static partial class HolyRomanSystem
{
    private const int MaxCandidates = 4, ElectionLogSize = 8, MinElectors = 3, InterregnumMaxYears = 5, ContestMaxYears = 5;
    private const int RefuseToConveneAuthority = 15, CampaignAge = 55, CampaignAgeNoHeir = 50;
    private const float RefuseToConveneChance = 0.4f, ContestJoinChance = 0.6f, InterregnumFeudFactor = 1.5f;
    public const int AuthDoubleElection = -10, AuthInterregnumStart = -10, AuthInterregnumYear = -4;
    public const int AuthAntiKingWon = -5, AuthAntiKingFailed = 5, ContestCapitalLegitimacy = -10;

    // 正在开争位之战(startWar 前后)
    public static bool DeclaringContest;

    public static bool Interregnum(Empire empire) => State(empire)?.interregnum_since >= 0d;

    public static Actor Vicar(Empire empire)
    {
        HolyRomanState state = State(empire);
        Actor actor = state?.vicar_id >= 0 ? World.world.units.get(state.vicar_id) : null;
        return actor?.data != null && !actor.isRekt() && actor.isAlive() ? actor : null;
    }

    // 在座选帝侯(被禁者停权)
    private static List<Actor> SeatedElectors(Empire empire) =>
        empire.GetCabinetMembers().Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() &&
                                                  actor.kingdom?.GetEmpire() == empire && !IsBanned(actor.kingdom)).ToList();

    private static List<Actor> ElectionCandidates(Empire empire) =>
        Members(empire).Where(realm => realm.hasKing() && Estate(realm) == EstateNone && !IsBanned(realm))
            .Select(realm => realm.king)
            .Where(candidate => candidate?.data != null && !candidate.isRekt() && candidate.isAlive() && candidate.isAdult() &&
                                candidate.isUnitFitToRule() && candidate.CanServeOffice(empire.CoreKingdom) && !candidate.IsSkeleton())
            .ToList();

    private static string CampaignKey(Kingdom candidate, Kingdom elector) => $"{candidate?.id ?? -1L}:{elector?.id ?? -1L}";

    private static float VoteScore(Empire empire, HolyRomanState state, Actor elector, Actor candidate, double maxPower, bool random)
    {
        Kingdom from = elector.kingdom, realm = candidate.kingdom;
        float score = 0f;
        if (elector == candidate) score += 1f;
        if (SameHouse(elector, candidate)) score += 0.5f;
        FixedFaction faction = elector.GetFaction();
        if (faction != null && faction == candidate.GetFaction()) score += 0.3f;
        if (from != null && realm != null && from != realm) score += Mathf.Clamp(Opinion(from, realm) / 100f, -0.3f, 0.3f);
        if (from?.hasReligion() == true && realm?.hasReligion() == true) score += from.religion == realm.religion ? 0.1f : -0.4f;
        if (maxPower > 0d && realm != null) score += 0.25f * (float)Math.Min(1d, realm.GetNationalPower() / maxPower);
        if (state.house_terms >= 2 && SpecificClanManager.Get(state.house_clan_id) is SpecificClan house &&
            SpecificClanManager.SameLineage(house, candidate.GetSpecificClan())) score += 0.15f;
        int party = PapacySystem.Party(from);
        if (party != PapacySystem.PartyNone && party == PapacySystem.Party(realm)) score += 0.1f;
        if (state.campaign != null && state.campaign.TryGetValue(CampaignKey(realm, from), out long paid))
            score += ImperialElectionRules.BribeScore(paid, StateDebtSystem.AnnualStableIncome(from));
        if (random) score += UnityEngine.Random.Range(-0.1f, 0.1f);
        return score;
    }

    private static double MaxPower(IEnumerable<Actor> candidates) =>
        candidates.Select(candidate => candidate.kingdom?.GetNationalPower() ?? 0d).DefaultIfEmpty(0d).Max();

    // 票势前几名(不含随机项)
    private static List<Actor> TopCandidates(Empire empire, HolyRomanState state, List<Actor> electors, List<Actor> pool, int take)
    {
        double max = MaxPower(pool);
        return pool.OrderByDescending(candidate => electors.Sum(elector => VoteScore(empire, state, elector, candidate, max, false)))
            .Take(take).ToList();
    }

    private static float[,] ScoreMatrix(Empire empire, HolyRomanState state, List<Actor> electors, List<Actor> candidates)
    {
        double max = MaxPower(candidates);
        var scores = new float[electors.Count, candidates.Count];
        for (int e = 0; e < electors.Count; e++)
        for (int c = 0; c < candidates.Count; c++)
            scores[e, c] = VoteScore(empire, state, electors[e], candidates[c], max, true);
        return scores;
    }

    private static string TallyText(List<Actor> candidates, ImperialElectionRules.Result tally)
    {
        if (tally.runoff)
            return $"{candidates[tally.winner].getName()} {tally.winnerVotes} · {candidates[tally.rival].getName()} {tally.rivalVotes}";
        return string.Join(" · ", Enumerable.Range(0, candidates.Count).Where(i => tally.firstRound[i] > 0)
            .OrderByDescending(i => tally.firstRound[i]).Select(i => $"{candidates[i].getName()} {tally.firstRound[i]}"));
    }

    // 中央无君时(KingBehCheck)：会选或维持大空位。返回 true = 已处理，不再走普通选任
    public static bool TryHoldElection(Kingdom seat)
    {
        try
        {
            if (seat?.data == null || seat.hasKing() || !seat.IsEmpire()) return false;
            Empire empire = seat.GetEmpire();
            if (empire == null || empire.CoreKingdom != seat || !IsElective(empire) || !HasDiet(empire)) return false;
            HolyRomanState state = State(empire);
            if (state.interregnum_since >= 0d) return true;
            if (KingOfRomans(empire) != null) return false;
            return RunElection(empire, state, inInterregnum: false);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 会选失败: {exception.Message}");
            return false;
        }
    }

    // 会选：选出即位返回 true；进入大空位也返回 true；装不上人(SetActor 没成)返回 false 交给普通选任
    private static bool RunElection(Empire empire, HolyRomanState state, bool inInterregnum)
    {
        List<Actor> electors = SeatedElectors(empire);
        List<Actor> pool = ElectionCandidates(empire);
        if (!inInterregnum)
        {
            string reason = pool.Count == 0 ? "无人可选"
                : electors.Count < MinElectors ? $"在座选帝侯只有{electors.Count}位"
                : Authority(empire) < RefuseToConveneAuthority && UnityEngine.Random.value < RefuseToConveneChance
                    ? $"权威{Authority(empire)}，选帝侯不肯会选" : null;
            if (reason != null)
            {
                StartInterregnum(empire, state, reason, pool);
                return true;
            }
        }
        else if (pool.Count == 0) return true;
        if (electors.Count == 0) return inInterregnum && VicarDecides(empire, state, pool);
        List<Actor> candidates = TopCandidates(empire, state, electors, pool, MaxCandidates);
        float[,] scores = ScoreMatrix(empire, state, electors, candidates);
        ImperialElectionRules.Result tally = ImperialElectionRules.Tally(scores);
        string text = TallyText(candidates, tally);
        if (tally.tie)
        {
            // 平票：首席选帝侯所投者即位；不在大空位时另一位自立为对立国王
            int first = tally.winner, second = tally.rival;
            bool chiefFirst = scores[0, first] >= scores[0, second];
            Actor emperor = candidates[chiefFirst ? first : second], rival = candidates[chiefFirst ? second : first];
            if (!Install(empire, state, emperor, inInterregnum ? "hre_election_outcome_chief" : "hre_election_outcome_double", text))
                return inInterregnum;
            if (!inInterregnum)
            {
                var antiVoters = new List<Kingdom>();
                var loyalVoters = new List<Kingdom>();
                for (int e = 0; e < electors.Count; e++)
                {
                    bool forRival = scores[e, chiefFirst ? second : first] > scores[e, chiefFirst ? first : second];
                    (forRival ? antiVoters : loyalVoters).Add(electors[e].kingdom);
                }
                ProclaimAntiKing(empire, state, emperor, rival, tally.winnerVotes, antiVoters, loyalVoters);
            }
            return true;
        }
        Actor winner = candidates[tally.winner];
        bool done = Install(empire, state, winner, tally.runoff ? "hre_election_outcome_runoff" : "hre_election_outcome_first", text);
        return done || inInterregnum;
    }

    // 即位：照 OfficeSelector 的办法置 Electing 后 SetActor(换家族走 TryElectHouse)
    private static bool Install(Empire empire, HolyRomanState state, Actor winner, string outcomeKey, string tally)
    {
        Kingdom core = empire.CoreKingdom;
        OfficeObject office = core?.GetOffice();
        if (office == null || winner?.data == null) return false;
        office.is_local = true;
        office.meta_object = core;
        Electing = true;
        try
        {
            office.SetActor(winner);
        }
        finally
        {
            Electing = false;
        }
        if (empire.Emperor != winner) return false;
        int years = state.interregnum_since >= 0d ? Date.getYearsSince(state.interregnum_since) : -1;
        if (years >= 0)
        {
            Record(empire, string.Format(LM.Get("hre_interregnum_ended_history"), Math.Max(1, years), winner.getName()));
            state.interregnum_since = -1d;
            state.vicar_id = state.vicar_realm_id = -1L;
            state.vicar_name = "";
        }
        state.campaign?.Clear();
        NoteElection(state, winner.getName(), tally, LM.Get(outcomeKey));
        FiscalDiagnostics.Count("神罗·会选");
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 会选：{winner.getName()}({winner.kingdom?.GetKingdomName()})" +
                           $"当选，{tally}，{LM.Get(outcomeKey)}");
        return true;
    }

    private static void NoteElection(HolyRomanState state, string winner, string tally, string outcome)
    {
        state.elections ??= new List<ElectionRecord>();
        state.elections.Add(new ElectionRecord { at = World.world.getCurWorldTime(), winner = winner, tally = tally, outcome = outcome });
        while (state.elections.Count > ElectionLogSize) state.elections.RemoveAt(0);
    }

    #region 大空位

    private static void StartInterregnum(Empire empire, HolyRomanState state, string reason, List<Actor> candidates)
    {
        state.interregnum_since = World.world.getCurWorldTime();
        PickVicar(empire, state, candidates);
        state.year_turmoil = true;
        ChangeAuthority(empire, AuthInterregnumStart, "hre_auth_interregnum");
        Record(empire, string.Format(LM.Get("hre_interregnum_history"), state.vicar_name));
        NoteElection(state, LM.Get("label_none"), reason, LM.Get("hre_election_outcome_interregnum"));
        FiscalDiagnostics.Count("神罗·大空位");
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 大空位：{reason}，帝国代理 {state.vicar_name}");
    }

    // 帝国代理：在座世俗选帝侯中国力最强、不是候选的一位；没有就最强诸侯
    private static void PickVicar(Empire empire, HolyRomanState state, List<Actor> candidates)
    {
        var excluded = new HashSet<Actor>(candidates ?? new List<Actor>());
        Actor vicar = SecularElectorKings(empire).Where(id => id >= 0).Select(id => World.world.units.get(id))
                          .Where(actor => actor?.data != null && actor.kingdom != null && !excluded.Contains(actor))
                          .OrderByDescending(actor => actor.kingdom.GetNationalPower()).FirstOrDefault()
                      ?? Members(empire).Where(realm => realm.hasKing() && Estate(realm) == EstateNone && !IsBanned(realm))
                          .OrderByDescending(realm => realm.GetNationalPower()).Select(realm => realm.king).FirstOrDefault();
        state.vicar_id = vicar?.id ?? -1L;
        state.vicar_realm_id = vicar?.kingdom?.id ?? -1L;
        state.vicar_name = vicar == null ? LM.Get("label_none") : $"{vicar.getName()}({vicar.kingdom?.GetKingdomName()})";
    }

    // 满五年：帝国代理定夺，投他自己最属意的人(代理自己也行)
    private static bool VicarDecides(Empire empire, HolyRomanState state, List<Actor> pool)
    {
        Actor vicar = Vicar(empire);
        if (vicar == null)
        {
            PickVicar(empire, state, null);
            vicar = Vicar(empire);
        }
        if (vicar == null || pool.Count == 0) return false;
        double max = MaxPower(pool);
        Actor pick = pool.OrderByDescending(candidate => VoteScore(empire, state, vicar, candidate, max, false)).First();
        return Install(empire, state, pick, "hre_election_outcome_vicar", state.vicar_name);
    }

    #endregion

    #region 对立国王

    private static void ProclaimAntiKing(Empire empire, HolyRomanState state, Actor emperor, Actor rival, int votes,
        List<Kingdom> antiVoters, List<Kingdom> loyalVoters)
    {
        Kingdom realm = rival.kingdom, core = empire.CoreKingdom;
        if (realm?.data == null || realm == core || realm.GetEmpire() != empire) return;
        state.anti_king_id = rival.id;
        state.anti_king_realm_id = realm.id;
        state.anti_king_name = rival.getName();
        state.year_turmoil = true;
        ChangeAuthority(empire, AuthDoubleElection, "hre_auth_double_election", rival.getName());
        Record(empire, string.Format(LM.Get("hre_double_election_history"), emperor.getName(), rival.getName(), votes));
        FiscalDiagnostics.Count("神罗·双重选举");
        War war;
        DeclaringContest = true;
        try
        {
            war = World.world.diplomacy.startWar(realm, core, WarTypeLibrary.normal);
        }
        finally
        {
            DeclaringContest = false;
        }
        if (war?.data == null) return;
        war.SetEmpireWarType(EmpireWarType.对立国王);
        war.data.name = string.Format(LM.Get("hre_contest_war_name"), realm.GetKingdomName());
        state.anti_king_war_id = war.data.id;
        foreach (Kingdom member in antiVoters.Distinct())
            if (member?.data != null && !member.isRekt() && member != realm && member != core && member.GetEmpire() == empire &&
                !war.hasKingdom(member) && UnityEngine.Random.value < ContestJoinChance) war.joinAttackers(member);
        foreach (Kingdom member in loyalVoters.Distinct())
            if (member?.data != null && !member.isRekt() && member != realm && member != core && member.GetEmpire() == empire &&
                !war.hasKingdom(member) && UnityEngine.Random.value < ContestJoinChance) war.joinDefenders(member);
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 双重选举：{emperor.getName()}即位，" +
                           $"{rival.getName()}({realm.GetKingdomName()})自立为对立国王，开{war.data.name}");
    }

    public static War ContestWar(Empire empire) => LiveWar(State(empire)?.anti_king_war_id ?? -1L);

    // 争位之战里攻下中央都城(CityPatch 用)：正统只扣 10
    public static bool InContest(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null) return false;
        foreach (War war in a.getWars())
            if (war?.data != null && !war.hasEnded() && war.GetEmpireWarType() == EmpireWarType.对立国王 && war.hasKingdom(b))
                return true;
        return false;
    }

    // 争位之战收场(OnWarEnded)：攻方胜，对立国王的领国成为中央；守方胜或议和，对立国王放弃
    private static void SettleContest(Empire empire, HolyRomanState state, WarWinner winner)
    {
        Kingdom realm = LiveKingdom(state.anti_king_realm_id);
        string name = state.anti_king_name;
        state.anti_king_id = state.anti_king_realm_id = state.anti_king_war_id = -1L;
        state.anti_king_name = "";
        state.year_turmoil = true;
        Actor claimant = realm?.king;
        if (winner == WarWinner.Attackers && realm != null && realm.GetEmpire() == empire && claimant?.data != null &&
            realm != empire.CoreKingdom &&
            Install(empire, state, claimant, "hre_election_outcome_contest", name))
        {
            Concede(empire, state);
            ChangeAuthority(empire, AuthAntiKingWon, "hre_auth_anti_king_won", claimant.getName());
            Record(empire, string.Format(LM.Get("hre_contest_won_history"), claimant.getName()));
            return;
        }
        ChangeAuthority(empire, AuthAntiKingFailed, "hre_auth_anti_king_failed", name);
        Record(empire, string.Format(LM.Get("hre_contest_failed_history"), name));
    }

    #endregion

    #region 年结

    // 每年(Yearly)：大空位重开会选、争位之战期满、拉票
    private static void ElectionYearly(Empire empire, HolyRomanState state)
    {
        state.campaign ??= new Dictionary<string, long>();
        state.elections ??= new List<ElectionRecord>();
        if (state.anti_king_war_id >= 0)
        {
            War war = ContestWar(empire);
            if (war == null)
            {
                state.anti_king_id = state.anti_king_realm_id = state.anti_king_war_id = -1L;
                state.anti_king_name = "";
            }
            else
            {
                state.year_turmoil = true;
                if (WarSituation.WarYears(war) >= ContestMaxYears) World.world.wars.endWar(war, WarWinner.Peace);
            }
        }
        if (state.interregnum_since >= 0d)
        {
            InterregnumYearly(empire, state);
            return;
        }
        foreach (string key in state.campaign.Keys.ToList())
        {
            long left = state.campaign[key] / 2;
            if (left <= 0) state.campaign.Remove(key);
            else state.campaign[key] = left;
        }
        Campaign(empire, state);
    }

    private static void InterregnumYearly(Empire empire, HolyRomanState state)
    {
        // 别处已立了皇帝(如共主继承)：虚悬就此结束
        if (empire.Emperor != null)
        {
            state.interregnum_since = -1d;
            state.vicar_id = state.vicar_realm_id = -1L;
            state.vicar_name = "";
            return;
        }
        state.year_turmoil = true;
        ChangeAuthority(empire, AuthInterregnumYear, "hre_auth_interregnum_year");
        if (Vicar(empire) == null || LiveKingdom(state.vicar_realm_id)?.GetEmpire() != empire) PickVicar(empire, state, null);
        int years = Date.getYearsSince(state.interregnum_since);
        List<Actor> pool = ElectionCandidates(empire);
        if (years >= InterregnumMaxYears) VicarDecides(empire, state, pool);
        else if (UnityEngine.Random.value < ImperialElectionRules.InterregnumChance(years)) RunElection(empire, state, inInterregnum: true);
    }

    // 拉票：皇帝年迈、没有储君时，票势前两名的候选给还没倒向自己的选帝侯送钱
    private static void Campaign(Empire empire, HolyRomanState state)
    {
        Actor emperor = empire.Emperor;
        if (emperor == null || KingOfRomans(empire) != null) return;
        bool adultHeirs = EmpireCraftKingdomBehCheckHeir.HeirCandidates(empire.CoreKingdom).Any(actor => actor.isAdult());
        if (emperor.getAge() < (adultHeirs ? CampaignAge : CampaignAgeNoHeir)) return;
        List<Actor> electors = SeatedElectors(empire);
        List<Actor> pool = ElectionCandidates(empire);
        if (electors.Count == 0 || pool.Count < 2) return;
        double max = MaxPower(pool);
        var spent = new List<string>();
        foreach (Actor candidate in TopCandidates(empire, state, electors, pool, 2))
        {
            Kingdom realm = candidate.kingdom;
            List<Actor> targets = electors.Where(elector => elector.kingdom != realm &&
                                                            pool.OrderByDescending(other => VoteScore(empire, state, elector, other, max, false))
                                                                .First() != candidate).ToList();
            long budget = ImperialElectionRules.CampaignBudget(realm.GetMoney(), StateDebtSystem.AnnualStableIncome(realm));
            if (targets.Count == 0 || budget < targets.Count) continue;
            long share = budget / targets.Count;
            foreach (Actor elector in targets)
            {
                realm.SubMoney((int)share, TreasuryCategory.Gift);
                elector.kingdom.AddMoney((int)share, TreasuryCategory.Gift);
                string key = CampaignKey(realm, elector.kingdom);
                state.campaign[key] = (state.campaign.TryGetValue(key, out long had) ? had : 0L) + share;
            }
            spent.Add($"{candidate.getName()}送{targets.Count}位共{share * targets.Count}");
        }
        if (spent.Count > 0)
            LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 拉票(皇帝{emperor.getAge()}岁)：{string.Join("；", spent)}");
    }

    // 界面用：拉票中的两位候选与眼下倒向他们的在座选帝侯数
    public static List<(Actor candidate, int votes)> CampaignStanding(Empire empire)
    {
        HolyRomanState state = State(empire);
        var result = new List<(Actor, int)>();
        if (state?.campaign == null || state.campaign.Count == 0 || empire.Emperor == null) return result;
        List<Actor> electors = SeatedElectors(empire);
        List<Actor> pool = ElectionCandidates(empire);
        if (electors.Count == 0 || pool.Count == 0) return result;
        double max = MaxPower(pool);
        var leaning = electors.Select(elector => pool.OrderByDescending(other => VoteScore(empire, state, elector, other, max, false)).First())
            .ToList();
        foreach (Actor candidate in TopCandidates(empire, state, electors, pool, 2))
            result.Add((candidate, leaning.Count(pick => pick == candidate)));
        return result;
    }

    // 选立储君过票：储君与票势最高的诸侯决选，皇帝替储君出的钱分给选帝侯；过半才立(返回 false = 选帝侯没答应)
    private static bool KingOfRomansVote(Empire empire, HolyRomanState state, Actor heir, long budget)
    {
        List<Actor> electors = SeatedElectors(empire).Where(elector => elector != heir).ToList();
        Kingdom core = empire.CoreKingdom;
        if (electors.Count == 0)
        {
            if (budget > 0) core.SubMoney((int)budget, TreasuryCategory.Gift);
            return true;
        }
        if (budget >= electors.Count)
        {
            long share = budget / electors.Count;
            foreach (Actor elector in electors.Where(elector => elector.kingdom != core))
            {
                core.SubMoney((int)share, TreasuryCategory.Gift);
                elector.kingdom.AddMoney((int)share, TreasuryCategory.Gift);
                string key = CampaignKey(core, elector.kingdom);
                state.campaign ??= new Dictionary<string, long>();
                state.campaign[key] = (state.campaign.TryGetValue(key, out long had) ? had : 0L) + share;
            }
        }
        List<Actor> pool = ElectionCandidates(empire);
        Actor rival = TopCandidates(empire, state, electors, pool, 1).FirstOrDefault();
        if (rival == null) return true;
        var candidates = new List<Actor> { heir, rival };
        float[,] scores = ScoreMatrix(empire, state, electors, candidates);
        int yes = 0;
        for (int e = 0; e < electors.Count; e++)
            if (scores[e, 0] >= scores[e, 1]) yes++;
        bool passed = yes * 2 > electors.Count;
        NoteElection(state, passed ? heir.getName() : LM.Get("label_none"), $"{heir.getName()} {yes} · {rival.getName()} {electors.Count - yes}",
            LM.Get(passed ? "hre_election_outcome_heir" : "hre_election_outcome_heir_refused"));
        if (!passed) Record(empire, string.Format(LM.Get("hre_king_of_romans_refused_history"), heir.getName(), rival.getName()));
        return passed;
    }

    // 年度诊断的会选一段
    private static string ElectionStatus(Empire empire, HolyRomanState state)
    {
        if (state.interregnum_since >= 0d)
            return $"帝位虚悬{Date.getYearsSince(state.interregnum_since)}年，代理 {state.vicar_name}，" +
                   $"今年会选几率{ImperialElectionRules.InterregnumChance(Date.getYearsSince(state.interregnum_since)):P0}";
        string anti = state.anti_king_id >= 0 ? $"对立国王{state.anti_king_name}在争位；" : "";
        List<(Actor candidate, int votes)> standing = CampaignStanding(empire);
        string campaign = standing.Count > 0
            ? "拉票中：" + string.Join("、", standing.Select(item => $"{item.candidate.getName()}{item.votes}票"))
            : "未拉票";
        ElectionRecord last = state.elections?.LastOrDefault();
        return anti + campaign + (last == null ? "" : $"；上次{Date.getYearsSince(last.at)}年前 {last.winner}({last.tally}，{last.outcome})");
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗教改革之后(封建制帝国，原型施马尔卡尔登同盟、三十年战争与威斯特伐利亚和约)：
//   · 新教会从哪来：别的帝国脱离罗马另立的国教会(PapacySystem.TryBreakWithRome)；选帝制帝国的皇帝不随宗教改革脱离罗马
//     (KeepsOldFaith)，本文化施行宗教改革后，由一位与皇帝最不睦的世俗诸侯在自己的都城另立新教会(SeedReformation)；
//   · 诸侯改宗：世上有从皇帝所奉宗教分出去的新教会时，成员国每年有小概率改奉它——
//     与奉新教会的国家接壤更容易，主教领不改，教皇党诸侯少改，与皇帝交恶的多改；自由城市同样会改；
//   · 诸侯同盟：改奉新教会(与皇帝所奉宗教有分合关系)的成员国两家以上、合计占帝国城数两成以上时，
//     可能以最强一家的都城为名结成同盟；奉不相干的别教的诸侯不算(那不是宗教改革)；
//   · 宗教战争：同盟每年有一成二可能向皇帝开战，同盟诸国随盟主进攻，与皇帝同教的诸侯多半助战；
//   · 结局：皇帝胜——同盟诸国改回皇帝的宗教，同盟解散，正统 +10；
//           否则(同盟胜或打成平手)订立和约(以一座自由城市或同盟盟主都城为名)——诸侯取得完全主权
//           (恢复外交与开战权、不再给中央出兵)，教随国定，帝国从此有名无实：正统 -5(长期)，议会更难通过议案。
// 加冕远征(原型罗马远征)：教廷不是皇帝自家领国时(在本帝国诸侯手里也一样)，皇帝要亲自远征教廷受冕——
//   花两成年度经常收入(军费)，一年后到达才加冕；远征期间不开议会，正统 -3，诸侯更不听调遣。
public static partial class HolyRomanSystem
{
    private const float ConvertChance = 0.04f, ConvertNeighbourBonus = 0.06f;
    private const float LeagueChance = 0.15f, LeagueCityShare = 0.2f, ReligiousWarChance = 0.12f, CatholicLeagueChance = 0.6f;
    private const float ReformationSeedChance = 0.15f;
    public const int LeagueCrushedLegitimacy = 10, PeaceLegitimacy = -10, SovereignLegitimacy = -5, RomzugLegitimacy = -3;
    private const float RomzugCost = 0.2f;
    private const double RomzugYears = 1d;
    public const int RomzugNone = 0, RomzugTravelling = 1, RomzugArrived = 2;

    #region 诸侯改宗与同盟

    private static void LeaguesYearly(Empire empire, HolyRomanState state)
    {
        Kingdom core = empire.CoreKingdom;
        if (!core.hasReligion()) return;
        SeedReformation(empire);
        PrincelyReformation(empire);
        if (state.sovereign_princes) return;
        List<Kingdom> dissenters = Members(empire).Where(member => member.hasReligion() && Schismatic(member.religion, core.religion))
            .ToList();
        if (state.league_leader_id < 0)
        {
            if (dissenters.Count < 2 || UnityEngine.Random.value >= LeagueChance) return;
            if (dissenters.Sum(member => member.cities.Count) < empire.countCities() * LeagueCityShare) return;
            Kingdom leader = dissenters.OrderByDescending(member => member.countTotalWarriors()).First();
            state.league_leader_id = leader.id;
            state.league_member_ids = dissenters.Select(member => member.id).ToList();
            state.league_name = string.Format(LM.Get("hre_league_name"), leader.capital?.GetCityName() ?? leader.GetKingdomName());
            state.league_formed_at = World.world.getCurWorldTime();
            Record(empire, string.Format(LM.Get("hre_league_formed_history"), leader.GetKingdomName(), dissenters.Count,
                state.league_name));
            return;
        }
        Kingdom head = World.world.kingdoms.get(state.league_leader_id);
        if (head?.data == null || head.isRekt() || head.GetEmpire() != empire || !head.hasReligion() ||
            !Schismatic(head.religion, core.religion))
        {
            head = dissenters.OrderByDescending(member => member.countTotalWarriors()).FirstOrDefault();
            if (head == null)
            {
                DissolveLeague(state);
                return;
            }
            state.league_leader_id = head.id;
        }
        state.league_member_ids = dissenters.Select(member => member.id).ToList();
        if (LeagueWar(empire) != null || UnityEngine.Random.value >= ReligiousWarChance) return;
        DeclareReligiousWar(empire, state, head, dissenters);
    }

    public static War LeagueWar(Empire empire) => LiveWar(State(empire)?.league_war_id ?? -1L);

    public static List<Kingdom> LeagueMembers(Empire empire)
    {
        HolyRomanState state = State(empire);
        return state?.league_member_ids?.Select(id => World.world.kingdoms.get(id))
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.GetEmpire() == empire).ToList() ?? new List<Kingdom>();
    }

    // 两教有分合关系：一方是从另一方分出去的(宗教改革另立的新教会与它的母宗教)
    public static bool Schismatic(Religion a, Religion b) =>
        a?.data != null && b?.data != null && a != b &&
        (a.GetOrCreate().ParentReligionId == b.data.id || b.GetOrCreate().ParentReligionId == a.data.id);

    // 从某宗教分出去的新教会
    private static List<Religion> ChurchesOf(Religion old) =>
        old?.data == null ? new List<Religion>()
            : World.world.religions.Where(religion => religion?.data != null && !religion.isRekt() && religion != old &&
                                                      religion.GetOrCreate().ParentReligionId == old.data.id).ToList();

    // 选帝制帝国的皇帝不随本文化的宗教改革脱离罗马(见 PapacySystem.BrokeWithRome、TryBreakWithRome)：宗教改革由诸侯开头，
    // 皇帝与奉旧教的诸侯对抗新教同盟。已另立过国教会的(如改回世袭时脱离过)照旧算脱离
    public static bool KeepsOldFaith(Empire empire) =>
        IsElective(empire) && (PapacySystem.State(empire)?.national_church_id ?? -1L) < 0;

    // 诸侯另立新教会：本文化施行了宗教改革、皇帝仍奉旧教、世上还没有从皇帝所奉宗教分出去的新教会时，每年一成五可能由
    // 与皇帝最不睦的世俗诸侯(不同族)在自己的都城另立新教会，圣地就在那里，之后各诸侯陆续改宗(PrincelyReformation)。
    // 以前新教会只能来自别的帝国脱离罗马，帝国自己施行宗教改革又是整个改宗——一国独大的西方或握着教廷的帝国，
    // 诸侯同盟与和约永远凑不齐(玩家日志 0 次)
    private static void SeedReformation(Empire empire)
    {
        Religion old = empire.CoreKingdom.religion;
        if (!KeepsOldFaith(empire) || !InstitutionSystem.IsEnacted(empire, PapacySystem.NodeReformation) ||
            ChurchesOf(old).Count > 0 || UnityEngine.Random.value >= ReformationSeedChance) return;
        Kingdom founder = Members(empire)
            .Where(member => SecularPrince(member) && member.hasKing() && member.capital != null && member.religion == old &&
                             !SameHouse(member.king, empire.Emperor))
            .OrderBy(member => Opinion(member, empire.CoreKingdom)).ThenByDescending(member => member.cities.Count).FirstOrDefault();
        if (founder == null) return;
        Actor head = founder.king;
        if (head.city != founder.capital) head.joinCity(founder.capital);
        Religion church = World.world.religions.newReligion(head, false);
        if (church?.data == null) return;
        church.data.name = string.Format(LM.Get("hre_reformed_church_name"), founder.capital.GetCityName());
        church.SetCity(founder.capital);
        var data = church.GetOrCreate();
        data.ParentReligionId = old.data.id;
        data.SplitAt = World.world.getCurWorldTime();
        Convert(founder, old, church);
        Record(empire, string.Format(LM.Get("hre_reformation_founded_history"), founder.GetKingdomName(), church.name, old.name));
    }

    // 诸侯改宗：改奉从皇帝所奉宗教分出去的新教会
    private static void PrincelyReformation(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        Religion old = core.religion;
        List<Religion> churches = ChurchesOf(old);
        if (churches.Count == 0) return;
        foreach (Kingdom member in Members(empire))
        {
            if (!member.hasReligion() || member.religion != old || IsChurchEstate(member) || member.capital == null) continue;
            Religion church = churches.FirstOrDefault(item => NeighbourFaith(member, item)) ?? churches[0];
            float chance = ConvertChance + (NeighbourFaith(member, church) ? ConvertNeighbourBonus : 0f);
            int party = PapacySystem.Party(member);
            if (party == PapacySystem.PartyPope) chance *= 0.3f;
            if (Opinion(member, core) < 0) chance *= 1.5f;
            if (UnityEngine.Random.value >= chance) continue;
            Convert(member, old, church);
            Record(empire, string.Format(LM.Get("hre_prince_converted_history"), member.GetKingdomName(), church.name, old.name));
        }
    }

    private static bool NeighbourFaith(Kingdom kingdom, Religion religion) =>
        World.world.kingdoms.Any(other => other?.data != null && !other.isRekt() && other != kingdom && other.religion == religion &&
                                          other.IsNeighbourWith(kingdom));

    private static void Convert(Kingdom kingdom, Religion from, Religion to)
    {
        kingdom.setReligion(to);
        foreach (City city in kingdom.cities.ToList())
        {
            if (city?.data == null || city.isRekt()) continue;
            city.setReligion(to);
            foreach (Actor actor in city.units.ToList())
                if (actor?.data != null && !actor.isRekt() && (from == null || actor.religion == from)) actor.setReligion(to);
        }
    }

    private static void DeclareReligiousWar(Empire empire, HolyRomanState state, Kingdom head, List<Kingdom> dissenters)
    {
        Kingdom core = empire.CoreKingdom;
        if (head.isInWarWith(core)) return;
        War war = World.world.diplomacy.startWar(head, core, WarTypeLibrary.normal);
        if (war?.data == null) return;
        war.data.name = string.Format(LM.Get("hre_religious_war_name"), state.league_name);
        state.league_war_id = war.data.id;
        foreach (Kingdom member in dissenters)
            if (member != head && !war.hasKingdom(member) && !member.isInWarWith(head)) war.joinAttackers(member);
        foreach (Kingdom member in Members(empire))
            if (!war.hasKingdom(member) && member.religion == core.religion && !IsBanned(member) &&
                UnityEngine.Random.value < CatholicLeagueChance) war.joinDefenders(member);
        Record(empire, string.Format(LM.Get("hre_religious_war_history"), state.league_name, head.GetKingdomName(),
            empire.Emperor?.getName() ?? core.GetKingdomName()));
    }

    private static void OnLeagueWarEnded(Empire empire, HolyRomanState state, War war, bool attackersWon, bool defendersWon)
    {
        state.league_war_id = -1L;
        Kingdom core = empire.CoreKingdom;
        List<Kingdom> members = LeagueMembers(empire);
        if (defendersWon && core.hasReligion())
        {
            foreach (Kingdom member in members) Convert(member, member.religion, core.religion);
            empire.AddMandate(LeagueCrushedLegitimacy);
            Record(empire, string.Format(LM.Get("hre_league_crushed_history"), state.league_name, core.religion.name));
            DissolveLeague(state);
            return;
        }
        // 同盟胜或打成平手：订立和约，诸侯取得完全主权
        City venue = Members(empire).Where(IsFreeCity).Select(kingdom => kingdom.capital).FirstOrDefault(city => city != null)
                     ?? World.world.kingdoms.get(state.league_leader_id)?.capital ?? core.capital;
        state.sovereign_princes = true;
        // 诸侯取得完全主权：帝国治安令作废
        state.imperial_peace_at = -1d;
        state.privileged_ids?.Clear();
        state.peace_name = string.Format(LM.Get("hre_peace_name"), venue?.GetCityName() ?? empire.GetEmpireName());
        state.peace_at = World.world.getCurWorldTime();
        foreach (Kingdom member in Members(empire))
        {
            Regime regime = member.GetRegime();
            if (regime == null) continue;
            regime.SetAllowDiplomacy(true);
            regime.SetAllowSupportCenterArmy(false);
        }
        if (core.hasReligion()) PapacySystem.ReligiousPeace(core.religion);
        empire.AddMandate(PeaceLegitimacy);
        Record(empire, string.Format(LM.Get("hre_peace_history"), state.peace_name, empire.GetEmpireFullName()));
        DissolveLeague(state);
    }

    private static void DissolveLeague(HolyRomanState state)
    {
        state.league_leader_id = -1L;
        state.league_member_ids?.Clear();
        state.league_name = "";
        state.league_formed_at = -1d;
        state.league_war_id = -1L;
    }

    #endregion

    #region 加冕远征

    // 远征途中已经加冕(教廷页直接加冕，或教廷没了改自行加冕)：远征就此作罢。以前远征的记录要等"到达"才清，
    // 而到达的判定在加冕结算里、已加冕的不再走到，这位皇帝一辈子都算远征在外(正统 -3、议会不开)
    public static bool RomzugUnderway(Empire empire)
    {
        HolyRomanState state = State(empire);
        return state != null && state.romzug_at >= 0d && empire.Emperor != null && state.romzug_emperor_id == empire.Emperor.id &&
               !PapacySystem.IsCrowned(empire);
    }

    // PapacySystem.Coronation 调用：没在远征 / 还在路上 / 到了
    public static int Romzug(Empire empire)
    {
        HolyRomanState state = State(empire);
        if (state == null || state.romzug_at < 0d) return RomzugNone;
        if (empire.Emperor == null || state.romzug_emperor_id != empire.Emperor.id)
        {
            state.romzug_at = -1d;
            state.romzug_emperor_id = -1L;
            return RomzugNone;
        }
        if (Date.getYearsSince(state.romzug_at) < RomzugYears) return RomzugTravelling;
        state.romzug_at = -1d;
        state.romzug_emperor_id = -1L;
        return RomzugArrived;
    }

    // 封建制帝国的皇帝去教廷受冕：先出征(花军费)，一年后到达
    public static bool StartRomzug(Empire empire, Kingdom papacy)
    {
        if (!HasDiet(empire) || papacy == null || empire.Emperor == null) return false;
        HolyRomanState state = State(empire);
        Kingdom core = empire.CoreKingdom;
        int cost = (int)Math.Min(Math.Max(0L, core.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(core) * RomzugCost));
        if (cost > 0) core.SubMoney(cost, TreasuryCategory.Military);
        state.romzug_at = World.world.getCurWorldTime();
        state.romzug_emperor_id = empire.Emperor.id;
        Record(empire, string.Format(LM.Get("hre_romzug_history"), empire.Emperor.getName(), papacy.GetKingdomName(),
            PapacySystem.HolyCity(PapacySystem.ReligionOf(empire))?.GetCityName() ?? papacy.capital?.GetCityName() ?? ""));
        return true;
    }

    #endregion
}

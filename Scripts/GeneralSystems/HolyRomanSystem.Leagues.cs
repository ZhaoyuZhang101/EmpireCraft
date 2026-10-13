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
//   · 诸侯改宗：世上有从皇帝所奉宗教分出去的新教会时，成员国每年有小概率改奉它——
//     与奉新教会的国家接壤更容易，主教领不改，教皇党诸侯少改，与皇帝交恶的多改；自由城市同样会改；
//   · 诸侯同盟：改奉别教的成员国两家以上、合计占帝国城数两成以上时，可能以最强一家的都城为名结成同盟；
//   · 宗教战争：同盟每年有一成二可能向皇帝开战，同盟诸国随盟主进攻，与皇帝同教的诸侯多半助战；
//   · 结局：皇帝胜——同盟诸国改回皇帝的宗教，同盟解散，正统 +10；
//           否则(同盟胜或打成平手)订立和约(以一座自由城市或同盟盟主都城为名)——诸侯取得完全主权
//           (恢复外交与开战权、不再给中央出兵)，教随国定，帝国从此有名无实：正统 -5(长期)，议会更难通过议案。
// 加冕远征(原型罗马远征)：教廷不在本帝国时，皇帝要亲自远征教廷受冕——花一成半到两成年度经常收入(军费)，
//   一年后到达才加冕；远征期间不开议会，正统 -3，诸侯更不听调遣。
public static partial class HolyRomanSystem
{
    private const float ConvertChance = 0.04f, ConvertNeighbourBonus = 0.06f;
    private const float LeagueChance = 0.15f, LeagueCityShare = 0.2f, ReligiousWarChance = 0.12f, CatholicLeagueChance = 0.6f;
    public const int LeagueCrushedLegitimacy = 10, PeaceLegitimacy = -10, SovereignLegitimacy = -5, RomzugLegitimacy = -3;
    private const float RomzugCost = 0.2f;
    private const double RomzugYears = 1d;
    public const int RomzugNone = 0, RomzugTravelling = 1, RomzugArrived = 2;

    #region 诸侯改宗与同盟

    private static void LeaguesYearly(Empire empire, HolyRomanState state)
    {
        Kingdom core = empire.CoreKingdom;
        if (!core.hasReligion()) return;
        PrincelyReformation(empire);
        if (state.sovereign_princes) return;
        List<Kingdom> dissenters = Members(empire).Where(member => member.hasReligion() && member.religion != core.religion).ToList();
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
        if (head?.data == null || head.isRekt() || head.GetEmpire() != empire || !head.hasReligion() || head.religion == core.religion)
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

    // 诸侯改宗：改奉从皇帝所奉宗教分出去的新教会
    private static void PrincelyReformation(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        Religion old = core.religion;
        List<Religion> churches = World.world.religions.Where(religion => religion?.data != null && !religion.isRekt() &&
                                                                          religion != old &&
                                                                          religion.GetOrCreate().ParentReligionId == old.data.id)
            .ToList();
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

    public static bool RomzugUnderway(Empire empire)
    {
        HolyRomanState state = State(empire);
        return state != null && state.romzug_at >= 0d && empire.Emperor != null && state.romzug_emperor_id == empire.Emperor.id;
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

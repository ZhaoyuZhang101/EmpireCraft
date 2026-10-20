using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诸侯私战与帝国法院(封建制帝国，见 HasDiet)：
//   · 私战(帝国改革颁行之前)：普通诸侯(非中央、非被禁、君主成年、不在打仗)为法理向接壤的同帝国诸侯(非中央——皇帝家族领国
//     不卷入私战)兴兵。只争守方手里属于攻方法理的非都城，至多两座(与攻方接壤的优先)；没有这样的城就打不起来，
//     所以一城小邦、自由城市打不着。攻方兵力要有守方八成；每年每个够格攻方 5% × (2 − 权威/50)(系数夹 0.2~2，
//     见 ImperialAuthorityRules.FeudChance)；每帝国同时至多两场、每年至多新开一场；同一对诸侯打完十年内不再私战；开战权威 −1；
//     规模受限——不拉姻亲、不扩封建附庸与共主领国、不升级世界大战(WarPatch 见 ContainedWar)，中央与别的成员不援
//     (MemberWarAid 本就不管成员打成员)；两家之间只有争的城能易手(CityPatch 见 BlocksFeudCity)，都城永远不在其内；
//     打满三年强制议和。结局：攻方胜或议和——争城里攻方占了一半以上地块的割给攻方；守方胜——攻方赔款、打下的争城退还；
//   · 帝国法院(帝国改革颁行之后)：私战禁止，领土纠纷改为诉讼——够格原告(普通诸侯或中央，持有法理的一方)每年 15% 起诉，
//     一案一城，每帝国待裁至多三案；原告有余钱时半数会花一成年收"打点"(钱进中央国库)；审理满一年后每年五成宣判。
//     判给几率见 ImperialAuthorityRules.CourtGrantChance，否则驳回；判给后被告可能抗判(DefyRulingChance)——
//     遵判城归原告、权威 +1；抗判记抗命(−2)再 −1，下次议会先议对它的禁令(抗命名单按时间先后，它排第一)；
//   · 违禁私战：颁行之后权威不到 30 时，兵力是守方一倍半以上、又与守方交恶的诸侯每年 2% 照样兴兵(同上规则)，
//     开战即记抗命、权威再 −2。
// 治安令只管对外开战与出兵，不管私战。
public static partial class HolyRomanSystem
{
    private const float FeudMinStrength = 0.8f, IllegalFeudStrength = 1.5f, IllegalFeudChance = 0.02f, FeudHeldShare = 0.5f;
    private const int FeudMaxCities = 2, FeudMaxConcurrent = 2, FeudPairCooldownYears = 10, FeudMaxYears = 3;
    private const int IllegalFeudAuthority = 30, AuthFeud = -1, AuthIllegalFeud = -2;
    private const float LawsuitChance = 0.15f, VerdictChance = 0.5f, BribeShare = 0.1f, BribeOdds = 0.5f;
    private const int LawsuitMaxPending = 3, LawsuitHearingYears = 1, AuthRulingObeyed = 1, AuthRulingDefied = -1;
    private const int CourtLogSize = 8;

    // 正在由本系统兴起私战(startWar 前后)：开战钩子据此不拉姻亲、不扩封建、不升级世界大战
    public static bool DeclaringFeud;

    // 只许两家对打的内战：私战(开战那一刻起)
    public static bool ContainedWar(War war) =>
        DeclaringFeud || war?.data != null && war.GetEmpireWarType() == EmpireWarType.私战;

    public static int ActiveFeuds(HolyRomanState state) => state?.private_wars?.Count(record => record.ended_at < 0d) ?? 0;

    private static void FeudsYearly(Empire empire, HolyRomanState state)
    {
        state.private_wars ??= new List<PrivateWarRecord>();
        state.lawsuits ??= new List<LawsuitRecord>();
        state.court_log ??= new List<string>();
        double now = World.world.getCurWorldTime();
        foreach (PrivateWarRecord record in state.private_wars.ToList())
        {
            if (record.ended_at >= 0d)
            {
                if (Date.getYearsSince(record.ended_at) >= FeudPairCooldownYears) state.private_wars.Remove(record);
                continue;
            }
            War war = LiveWar(record.war_id);
            if (war == null)
            {
                // 没走到结束钩子(读档、战争被别处直接移除)：只记结束，不结算
                record.ended_at = now;
                continue;
            }
            state.year_turmoil = true;
            if (WarSituation.WarYears(war) >= FeudMaxYears) World.world.wars.endWar(war, WarWinner.Peace);
        }
        bool reformed = ReformEnacted(empire);
        if (reformed) LawsuitsYearly(empire, state);
        TryFeud(empire, state, illegal: reformed);
    }

    #region 私战

    private static bool AtWar(Kingdom kingdom) => kingdom.getWars().Any(war => war?.data != null && !war.hasEnded());

    private static bool Rebelling(Kingdom kingdom) => kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling();

    // 能兴私战的诸侯
    private static bool FeudCapable(Empire empire, Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom && SecularPrince(kingdom) &&
        !IsBanned(kingdom) && kingdom.hasKing() && kingdom.king.isAdult() && !AtWar(kingdom) && !Rebelling(kingdom);

    // claimant 对这座城有法理：城所属法理在它的法理头衔里，或法理主国就是它
    private static bool ClaimsCity(Kingdom claimant, List<long> titles, City city)
    {
        var title = city?.GetTitle();
        if (title == null || title.isRekt()) return false;
        return titles.Contains(title.id) || (title.main_kingdom ?? title.owner?.kingdom) == claimant;
    }

    // holder 手里属于 claimant 法理的非都城(与 claimant 接壤的在前)
    private static List<City> ClaimedFrom(Kingdom claimant, Kingdom holder)
    {
        List<long> titles = claimant.GetRealmTitleIds();
        return holder.cities.Where(city => city?.data != null && !city.isRekt() && city != holder.capital &&
                                           !EnfeoffmentHelper.IsRealmCapital(city) && ClaimsCity(claimant, titles, city))
            .OrderByDescending(city => city.neighbours_cities.Any(other => other?.kingdom == claimant))
            .ThenBy(city => city.zones?.Count ?? 0).ToList();
    }

    private static bool FeudCooling(HolyRomanState state, Kingdom a, Kingdom b) =>
        state.private_wars.Any(record => record.ended_at >= 0d &&
                                         (record.attacker_id == a.id && record.defender_id == b.id ||
                                          record.attacker_id == b.id && record.defender_id == a.id));

    // 私战对象：接壤的同帝国诸侯(非中央)，手里有攻方法理的城，兵力够；违禁私战另要兵力一倍半、两家交恶
    private static (Kingdom defender, List<City> cities) FeudTarget(Empire empire, HolyRomanState state, Kingdom attacker,
        List<Kingdom> members, bool illegal)
    {
        float strength = Troops(attacker);
        Kingdom best = null;
        List<City> bestCities = null;
        foreach (Kingdom defender in members)
        {
            if (defender == attacker || defender.isRekt() || !attacker.IsNeighbourWith(defender) || AtWar(defender) ||
                Rebelling(defender) || FeudCooling(state, attacker, defender)) continue;
            float theirs = Troops(defender);
            if (strength < theirs * (illegal ? IllegalFeudStrength : FeudMinStrength)) continue;
            if (illegal && Opinion(attacker, defender) >= 0) continue;
            List<City> cities = ClaimedFrom(attacker, defender);
            if (cities.Count == 0) continue;
            if (best != null && cities.Count <= bestCities.Count) continue;
            best = defender;
            bestCities = cities;
        }
        return best == null ? (null, null) : (best, bestCities.Take(FeudMaxCities).ToList());
    }

    // 每年至多新开一场；同时至多两场
    private static void TryFeud(Empire empire, HolyRomanState state, bool illegal)
    {
        int authority = Authority(empire);
        if (illegal && authority >= IllegalFeudAuthority || ActiveFeuds(state) >= FeudMaxConcurrent) return;
        float chance = illegal ? IllegalFeudChance : ImperialAuthorityRules.FeudChance(authority);
        List<Kingdom> members = Members(empire);
        foreach (Kingdom attacker in members.Where(member => FeudCapable(empire, member)).OrderBy(_ => UnityEngine.Random.value))
        {
            (Kingdom defender, List<City> cities) = FeudTarget(empire, state, attacker, members, illegal);
            if (defender == null || UnityEngine.Random.value >= chance) continue;
            DeclareFeud(empire, state, attacker, defender, cities, illegal);
            return;
        }
    }

    private static void DeclareFeud(Empire empire, HolyRomanState state, Kingdom attacker, Kingdom defender, List<City> cities,
        bool illegal)
    {
        War war;
        DeclaringFeud = true;
        try
        {
            war = World.world.diplomacy.startWar(attacker, defender, WarTypeLibrary.normal);
        }
        finally
        {
            DeclaringFeud = false;
        }
        if (war?.data == null) return;
        war.SetEmpireWarType(EmpireWarType.私战);
        string first = cities[0].GetCityName();
        string names = string.Join("、", cities.Select(city => city.GetCityName()));
        war.data.name = string.Format(LM.Get(cities.Count > 1 ? "hre_feud_war_name_many" : "hre_feud_war_name"),
            attacker.GetKingdomName(), first);
        state.private_wars.Add(new PrivateWarRecord
        {
            war_id = war.data.id, attacker_id = attacker.id, defender_id = defender.id,
            attacker_name = attacker.GetKingdomName(), defender_name = defender.GetKingdomName(), city_names = names,
            city_ids = cities.Select(city => city.id).ToList(), at = World.world.getCurWorldTime(), illegal = illegal
        });
        state.private_war_count++;
        state.year_turmoil = true;
        ChangeAuthority(empire, AuthFeud, "hre_auth_feud", attacker.GetKingdomName(), defender.GetKingdomName());
        if (illegal)
        {
            MarkDefiance(empire, state, attacker);
            ChangeAuthority(empire, AuthIllegalFeud, "hre_auth_illegal_feud", attacker.GetKingdomName());
        }
        Record(empire, string.Format(LM.Get(illegal ? "hre_feud_illegal_history" : "hre_feud_history"),
            attacker.GetKingdomName(), names, defender.GetKingdomName()));
        FiscalDiagnostics.Count(illegal ? "神罗·违禁私战" : "神罗·私战");
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} {(illegal ? "违禁私战" : "私战")}：" +
                           $"{attacker.GetKingdomName()}(兵{Troops(attacker):0})争{defender.GetKingdomName()}(兵{Troops(defender):0})的{names}，" +
                           $"权威{Authority(empire)}");
    }

    // 私战进行中：交战两家之间，不在争城清单里的城一律不易手(CityPatch 攻城结算、改换归属时调用)
    public static bool BlocksFeudCity(Kingdom newKingdom, City city)
    {
        Kingdom holder = city?.kingdom;
        if (newKingdom?.data == null || holder?.data == null || holder == newKingdom) return false;
        try
        {
            foreach (War war in newKingdom.getWars())
            {
                if (war?.data == null || war.hasEnded() || war.GetEmpireWarType() != EmpireWarType.私战) continue;
                if (!(war.isAttacker(newKingdom) && war.isDefender(holder) || war.isDefender(newKingdom) && war.isAttacker(holder)))
                    continue;
                PrivateWarRecord record = FeudRecord(holder.GetEmpire(), war.data.id) ?? FeudRecord(newKingdom.GetEmpire(), war.data.id);
                return record != null && !record.city_ids.Contains(city.id);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 私战拦城失败: {exception.Message}");
        }
        return false;
    }

    private static PrivateWarRecord FeudRecord(Empire empire, long warId) =>
        empire?.data?.holy_roman?.private_wars?.FirstOrDefault(record => record.war_id == warId && record.ended_at < 0d);

    // 私战收场(OnWarEnded 调用)：攻方胜或议和，争城里攻方占了一半以上地块的割给攻方；守方胜，攻方赔款、打下的争城退还
    private static void SettleFeud(Empire empire, HolyRomanState state, War war, WarWinner winner)
    {
        PrivateWarRecord record = FeudRecord(empire, war.data.id);
        if (record == null) return;
        record.ended_at = World.world.getCurWorldTime();
        state.year_turmoil = true;
        Kingdom attacker = LiveKingdom(record.attacker_id), defender = LiveKingdom(record.defender_id);
        bool defendersWon = winner == WarWinner.Defenders;
        var taken = new List<string>();
        int returned = 0;
        foreach (long id in record.city_ids)
        {
            City city = World.world.cities.get(id);
            if (city?.data == null || city.isRekt()) continue;
            if (defendersWon)
            {
                if (attacker != null && defender != null && city.kingdom == attacker)
                {
                    WarGoalSystem.QueueCession(city, defender);
                    returned++;
                }
                continue;
            }
            if (attacker == null) continue;
            if (city.kingdom == attacker) taken.Add(city.GetCityName());
            else if (city.kingdom == defender && city.zones != null && city.zones.Count > 0 &&
                     city.GetOccupiedTileZoneCount(attacker) >= city.zones.Count * FeudHeldShare)
            {
                WarGoalSystem.QueueCession(city, attacker);
                taken.Add(city.GetCityName());
            }
        }
        string text;
        if (defendersWon)
        {
            if (attacker != null && defender != null) WarReparationsSystem.Impose(attacker, defender);
            text = string.Format(LM.Get("hre_feud_lost_log"), record.attacker_name, record.defender_name, returned);
        }
        else
            text = taken.Count > 0
                ? string.Format(LM.Get("hre_feud_won_log"), record.attacker_name, record.defender_name, string.Join("、", taken))
                : string.Format(LM.Get("hre_feud_draw_log"), record.attacker_name, record.defender_name);
        CourtLog(state, text);
        Record(empire, text);
        FiscalDiagnostics.Count("神罗·私战割城", taken.Count);
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 私战收场({winner})：{text}");
    }

    private static Kingdom LiveKingdom(long id)
    {
        Kingdom kingdom = id >= 0 ? World.world.kingdoms.get(id) : null;
        return kingdom?.data != null && !kingdom.isRekt() ? kingdom : null;
    }

    private static void CourtLog(HolyRomanState state, string text)
    {
        state.court_log ??= new List<string>();
        state.court_log.Add(text);
        while (state.court_log.Count > CourtLogSize) state.court_log.RemoveAt(0);
    }

    #endregion

    #region 帝国法院

    private static bool CanSue(Empire empire, Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && kingdom.GetEmpire() == empire && !IsBanned(kingdom) && kingdom.hasKing() &&
        (kingdom == empire.CoreKingdom || SecularPrince(kingdom)) && !Rebelling(kingdom);

    private static void LawsuitsYearly(Empire empire, HolyRomanState state)
    {
        foreach (LawsuitRecord suit in state.lawsuits.ToList())
        {
            Kingdom plaintiff = LiveKingdom(suit.plaintiff_id), defendant = LiveKingdom(suit.defendant_id);
            City city = World.world.cities.get(suit.city_id);
            if (plaintiff == null || defendant == null || plaintiff.GetEmpire() != empire || defendant.GetEmpire() != empire ||
                city?.data == null || city.isRekt() || city.kingdom != defendant)
            {
                // 两造不在帝国了、城已易手：撤案
                state.lawsuits.Remove(suit);
                continue;
            }
            if (Date.getYearsSince(suit.filed_at) < LawsuitHearingYears || UnityEngine.Random.value >= VerdictChance) continue;
            state.lawsuits.Remove(suit);
            Verdict(empire, state, suit, plaintiff, defendant, city);
        }
        if (state.lawsuits.Count >= LawsuitMaxPending) return;
        List<Kingdom> realms = empire.kingdoms_list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
                                                                     kingdom.GetEmpire() == empire).ToList();
        foreach (Kingdom plaintiff in realms.Where(kingdom => CanSue(empire, kingdom)).OrderBy(_ => UnityEngine.Random.value))
        {
            (Kingdom defendant, City city) = SuitTarget(state, plaintiff, realms);
            if (city == null || UnityEngine.Random.value >= LawsuitChance) continue;
            File(empire, state, plaintiff, defendant, city);
            if (state.lawsuits.Count >= LawsuitMaxPending) return;
        }
    }

    // 告谁、告哪座城：别家手里自己法理的非都城，接壤的在前；同一座城不重复立案
    private static (Kingdom defendant, City city) SuitTarget(HolyRomanState state, Kingdom plaintiff, List<Kingdom> realms)
    {
        foreach (Kingdom holder in realms.OrderByDescending(kingdom => plaintiff.IsNeighbourWith(kingdom)))
        {
            if (holder == plaintiff || holder.isInWarWith(plaintiff)) continue;
            City city = ClaimedFrom(plaintiff, holder).FirstOrDefault(item => state.lawsuits.All(suit => suit.city_id != item.id));
            if (city != null) return (holder, city);
        }
        return (null, null);
    }

    private static void File(Empire empire, HolyRomanState state, Kingdom plaintiff, Kingdom defendant, City city)
    {
        Kingdom core = empire.CoreKingdom;
        long bribe = (long)(StateDebtSystem.AnnualStableIncome(plaintiff) * BribeShare);
        bool bribed = plaintiff != core && bribe > 0 && plaintiff.GetMoney() >= bribe * 3 && UnityEngine.Random.value < BribeOdds;
        if (bribed)
        {
            plaintiff.SubMoney((int)bribe, TreasuryCategory.Gift);
            core.AddMoney((int)bribe, TreasuryCategory.Gift);
        }
        state.lawsuits.Add(new LawsuitRecord
        {
            plaintiff_id = plaintiff.id, defendant_id = defendant.id, city_id = city.id,
            plaintiff_name = plaintiff.GetKingdomName(), defendant_name = defendant.GetKingdomName(), city_name = city.GetCityName(),
            filed_at = World.world.getCurWorldTime(), bribed = bribed
        });
        FiscalDiagnostics.Count("神罗·诉讼");
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 帝国法院立案：{plaintiff.GetKingdomName()}诉" +
                           $"{defendant.GetKingdomName()}争{city.GetCityName()}" + (bribed ? $"(打点{bribe})" : ""));
    }

    private static bool IsElectorRealm(Empire empire, Kingdom kingdom) =>
        empire.GetCabinetMembers().Any(actor => actor?.kingdom == kingdom && !actor.isRekt());

    private static bool ImperialHouse(Empire empire, Kingdom kingdom) =>
        kingdom == empire.CoreKingdom || SameHouse(kingdom.king, empire.Emperor);

    private static void Verdict(Empire empire, HolyRomanState state, LawsuitRecord suit, Kingdom plaintiff, Kingdom defendant,
        City city)
    {
        int authority = Authority(empire);
        float grant = ImperialAuthorityRules.CourtGrantChance(IsElectorRealm(empire, plaintiff), IsElectorRealm(empire, defendant),
            ImperialHouse(empire, plaintiff), ImperialHouse(empire, defendant), authority, suit.bribed);
        string text;
        if (UnityEngine.Random.value >= grant)
        {
            state.lawsuits_refused++;
            text = string.Format(LM.Get("hre_lawsuit_refused_log"), suit.plaintiff_name, suit.defendant_name, suit.city_name);
        }
        else
        {
            float troops = Troops(plaintiff);
            bool stronger = Troops(defendant) >= Math.Max(1f, troops) * 2f;
            if (UnityEngine.Random.value < ImperialAuthorityRules.DefyRulingChance(authority, stronger))
            {
                MarkDefiance(empire, state, defendant);
                ChangeAuthority(empire, AuthRulingDefied, "hre_auth_ruling_defied", defendant.GetKingdomName());
                FiscalDiagnostics.Count("神罗·抗判");
                text = string.Format(LM.Get("hre_lawsuit_defied_log"), suit.defendant_name, suit.city_name, suit.plaintiff_name);
            }
            else
            {
                city.joinAnotherKingdom(plaintiff);
                state.court_rulings++;
                state.lawsuits_granted++;
                ChangeAuthority(empire, AuthRulingObeyed, "hre_auth_ruling_obeyed", suit.city_name);
                FiscalDiagnostics.Count("神罗·判城");
                text = string.Format(LM.Get("hre_court_ruling_history"), suit.city_name, suit.plaintiff_name, suit.defendant_name);
            }
        }
        CourtLog(state, text);
        Record(empire, text);
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 帝国法院宣判(判给几率{grant:P0}，权威{authority})：{text}");
    }

    #endregion

    // 年度诊断的私战与法院一段
    private static string FeudStatus(Empire empire, HolyRomanState state)
    {
        bool reformed = ReformEnacted(empire);
        List<Kingdom> members = Members(empire);
        int eligible = members.Where(member => FeudCapable(empire, member))
            .Count(member => FeudTarget(empire, state, member, members, reformed).defender != null);
        int authority = Authority(empire);
        string feuds = $"进行中{ActiveFeuds(state)}场、累计{state.private_war_count}场";
        if (!reformed)
            return $"{feuds}；够格攻方{eligible}家，每家{ImperialAuthorityRules.FeudChance(authority):P1}";
        return $"法院待裁{state.lawsuits?.Count ?? 0}案，累计判城{state.lawsuits_granted}、驳回{state.lawsuits_refused}；违禁私战" +
               (authority >= IllegalFeudAuthority ? $"不会有(权威{authority}，要不到{IllegalFeudAuthority})" : $"够格{eligible}家") +
               $"；{feuds}";
    }
}

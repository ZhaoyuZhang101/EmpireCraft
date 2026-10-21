using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 帝国解体(选帝制的封建帝国，见 IsElective；用户 2026-10-21 选"神罗衰落只限选帝制"，普通封建帝国照样有议会、权威，
// 但不走保护同盟、退位解散)：帝国权威长期很低时，帝国外的强国做诸侯同盟的保护人，拉走一批成员。
//   · 条件：权威低于 20 连续满 12 年(回到 25 以上才重计，见 authority_low_since)；每年一成；两批之间至少 20 年；
//   · 保护人：帝国外的独立国家或别的帝国中央(不是别国的成员、不是教廷)，不与中央交战，与至少一个成员接壤，
//     国力(帝国按整个帝国算)是中央的 1.2 倍以上；同宗教的优先，再按国力；
//   · 被拉走的：与保护人接壤(连同彼此接壤连成一片)的成员里，对皇帝好感不到 10、或改奉别教、或有自主外交的，
//     二到五国，合计城数不超过帝国三成五；不含中央，和约之前不含主教领；
//   · 做法：各国离开帝国(恢复外交与开战权)；保护人不是帝国时由他做盟主与这几国结成"{盟主都城}同盟"(模组同盟)；
//     保护人是帝国中央时同盟会被解散(帝国与同盟不兼容)，改为这几国自己结盟、与保护人立"保护约"；
//   · 保护约三十年：保护人与被保护者互不攻伐(WarGate)；原帝国中央或其成员攻打被保护者时，保护人自动加入守方(WarPatch)；
//   · 权威 −10、正统 −10；年结比对快照见到这几国离开不再另扣。
// 皇帝退位、帝国解散：
//   · 条件：权威低于 10 连续满 20 年(回到 15 以上重计)，且(已有保护同盟离去，或已订和约，或成员数不到历史最多的一半)，
//     且立国满 80 年；每年一成；
//   · 做法：记"{皇帝}退位，{帝国}解散，诸侯各自独立"；各成员恢复外交与开战权；原中央记称帝失败冷却(十年内不再称帝)；
//     帝国解散(EmpireManager.dissolveEmpire)，皇帝仍是自家领国的君主；主教领出缺时 TryAppointBishop 见帝国没了自然清标记；
//   · 神力按钮：权威不到 10 时议会页出现"皇帝退位"(点两下才生效)；
//   · 模组设置「帝国解体」(默认开，见 ConfigActions.HreDissolutionCallBack)关掉时，保护同盟与退位解散都不会发生。
public static partial class HolyRomanSystem
{
    // 模组设置「帝国解体」
    public static bool DissolutionEnabled = true;
    public const int CollapseAuthority = 10, CollapseRecover = 15, CollapseYears = 20, DissolveMinAge = 80;
    private const float AbdicationChance = 0.1f;

    public const int ProtectorLowYears = 12;
    private const int ProtectorGapYears = 20, ProtectionYears = 30;
    private const int MinDeparting = 2, MaxDeparting = 5, DepartOpinion = 10, DepartedLogSize = 8;
    private const float ProtectorChance = 0.1f, ProtectorPower = 1.2f, DepartingCityShare = 0.35f;
    public const int AuthProtectorLeague = -10, ProtectorLegitimacy = -10;

    // 被保护者 protectedRealm 与保护人 protector 在保护约期内
    public static bool ProtectedBy(Kingdom protectedRealm, Kingdom protector)
    {
        if (protectedRealm?.data == null || protector?.data == null || protectedRealm == protector) return false;
        var data = protectedRealm.GetOrCreate();
        return data.hre_protector_id == protector.id && data.hre_protected_at >= 0d &&
               Date.getYearsSince(data.hre_protected_at) < ProtectionYears;
    }

    // 刚随保护同盟离开帝国的(快照比对不另扣权威)
    private static bool LeftUnderProtection(Kingdom kingdom)
    {
        var data = kingdom?.data == null ? null : kingdom.GetOrCreate();
        return data != null && data.hre_protected_at >= 0d && Date.getYearsSince(data.hre_protected_at) < 2;
    }

    // 返回 true = 皇帝退位、帝国已解散
    private static bool DeclineYearly(Empire empire, HolyRomanState state)
    {
        state.departed_log ??= new List<string>();
        state.peak_members = Math.Max(state.peak_members, Members(empire).Count + 1);
        int authority = Authority(empire);
        if (authority < CollapseAuthority && state.authority_collapse_since < 0d)
            state.authority_collapse_since = World.world.getCurWorldTime();
        else if (authority >= CollapseRecover) state.authority_collapse_since = -1d;
        if (!DissolutionEnabled || !IsElective(empire)) return false;
        if (state.authority_low_since >= 0d && Date.getYearsSince(state.authority_low_since) >= ProtectorLowYears &&
            (state.last_protector_at < 0d || Date.getYearsSince(state.last_protector_at) >= ProtectorGapYears) &&
            UnityEngine.Random.value < ProtectorChance)
            TryProtectorLeague(empire, state);
        if (!HasDiet(empire) || AbdicationBlocker(empire, state) != null || UnityEngine.Random.value >= AbdicationChance) return false;
        Abdicate(empire, state);
        return true;
    }

    // 退位解散还差什么(年度诊断、界面用)，条件已齐时为 null
    public static string AbdicationBlocker(Empire empire, HolyRomanState state)
    {
        if (!IsElective(empire)) return LM.Get("hre_dissolve_need_elective");
        if (state.authority_collapse_since < 0d) return string.Format(LM.Get("hre_dissolve_need_low"), CollapseAuthority);
        int years = Date.getYearsSince(state.authority_collapse_since);
        if (years < CollapseYears) return string.Format(LM.Get("hre_dissolve_need_years"), CollapseAuthority, years, CollapseYears);
        bool broken = state.last_protector_at >= 0d || state.sovereign_princes || (Members(empire).Count + 1) * 2 < state.peak_members;
        if (!broken) return LM.Get("hre_dissolve_need_break");
        int age = empire.data.created_time > 0d ? Date.getYearsSince(empire.data.created_time) : 0;
        return age < DissolveMinAge ? string.Format(LM.Get("hre_dissolve_need_age"), age, DissolveMinAge) : null;
    }

    // 皇帝退位、帝国解散
    private static void Abdicate(Empire empire, HolyRomanState state)
    {
        Kingdom core = empire.CoreKingdom;
        Actor emperor = empire.Emperor;
        string text = string.Format(LM.Get("hre_dissolve_history"), emperor?.getName() ?? core.GetKingdomName(),
            empire.GetEmpireFullName());
        Record(empire, text);
        emperor?.RecordPersonalHistory(text);
        foreach (Kingdom member in Members(empire))
        {
            Regime regime = member.GetRegime();
            regime?.SetAllowDiplomacy(true);
            regime?.SetAllowSupportCenterArmy(false);
        }
        // 原中央十年内不再称帝(沿用称帝失败的冷却)
        core.GetOrCreate().empire_formation_failed_timestamp = World.world.getCurWorldTime();
        FiscalDiagnostics.Count("神罗·帝国解散");
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 皇帝退位、帝国解散：权威{Authority(empire)}，" +
                           $"成员{Members(empire).Count}国(最多{state.peak_members})，立国{Date.getYearsSince(empire.data.created_time)}年");
        ModClass.EMPIRE_MANAGER.dissolveEmpire(empire);
    }

    // 神力按钮：皇帝退位(权威不到 10 时)
    public static bool ActAbdicate(Empire empire)
    {
        if (!DissolutionEnabled || !HasDiet(empire) || Authority(empire) >= CollapseAuthority) return false;
        Abdicate(empire, State(empire));
        return true;
    }

    private static bool IsPapacy(Kingdom kingdom) => kingdom.hasReligion() && PapacySystem.Papacy(kingdom.religion) == kingdom;

    // 能拉走的成员：对皇帝好感不到 10、或改奉别教、或有自主外交
    private static bool Discontent(Empire empire, HolyRomanState state, Kingdom member)
    {
        Kingdom core = empire.CoreKingdom;
        if (member == core || IsChurchEstate(member) && !state.sovereign_princes) return false;
        // 新入帝国十年内、神力拉入未满期的不被拉走(入帝国就得了自主外交，以前一入帝国就算"不满"，见 MembershipCooldown)
        if (!MembershipCooldown.Settled(member)) return false;
        return Opinion(member, core) < DepartOpinion ||
               member.hasReligion() && core.hasReligion() && member.religion != core.religion ||
               member.GetRegime()?.IsAllowDiplomacy() == true;
    }

    // 保护人与要拉走的成员(没有就 null)
    private static (Kingdom protector, List<Kingdom> departing) FindProtector(Empire empire, HolyRomanState state)
    {
        Kingdom core = empire.CoreKingdom;
        List<Kingdom> members = Members(empire);
        List<Kingdom> discontent = members.Where(member => Discontent(empire, state, member)).ToList();
        if (discontent.Count < MinDeparting) return (null, null);
        double central = core.GetNationalPower();
        int total = Math.Max(1, empire.countCities());
        IEnumerable<Kingdom> protectors = World.world.kingdoms.list
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.GetEmpire() != empire &&
                              (kingdom.GetEmpire() == null || kingdom.IsEmpire()) && !IsPapacy(kingdom) &&
                              !kingdom.isInWarWith(core) && discontent.Any(member => member.IsNeighbourWith(kingdom)) &&
                              WarSituation.Power(kingdom) >= central * ProtectorPower)
            .OrderByDescending(kingdom => kingdom.hasReligion() && kingdom.religion == core.religion)
            .ThenByDescending(kingdom => WarSituation.Power(kingdom));
        foreach (Kingdom protector in protectors)
        {
            // 与保护人接壤的不满者为起点，连同彼此接壤的连成一片
            var picked = new List<Kingdom>();
            var queue = new Queue<Kingdom>(discontent.Where(member => member.IsNeighbourWith(protector)));
            var seen = new HashSet<Kingdom>(queue);
            int cities = 0;
            while (queue.Count > 0 && picked.Count < MaxDeparting)
            {
                Kingdom member = queue.Dequeue();
                if ((cities + member.cities.Count) > total * DepartingCityShare) continue;
                picked.Add(member);
                cities += member.cities.Count;
                foreach (Kingdom next in discontent.Where(other => !seen.Contains(other) && other.IsNeighbourWith(member)))
                {
                    seen.Add(next);
                    queue.Enqueue(next);
                }
            }
            if (picked.Count >= MinDeparting) return (protector, picked);
        }
        return (null, null);
    }

    private static void TryProtectorLeague(Empire empire, HolyRomanState state)
    {
        (Kingdom protector, List<Kingdom> departing) = FindProtector(empire, state);
        if (protector == null) return;
        double now = World.world.getCurWorldTime();
        state.last_protector_at = now;
        foreach (Kingdom member in departing)
        {
            empire.leave(member, true, isLeave: true);
            Regime regime = member.GetRegime();
            regime?.SetAllowDiplomacy(true);
            regime?.SetAllowSupportCenterArmy(false);
            var data = member.GetOrCreate();
            data.hre_estate = EstateNone;
            data.hre_protector_id = protector.id;
            data.hre_protected_at = now;
            data.hre_protected_from_id = empire.data.id;
        }
        bool imperialProtector = protector.IsEmpire();
        Kingdom head = imperialProtector ? departing.OrderByDescending(member => member.GetNationalPower()).First() : protector;
        string league = string.Format(LM.Get("hre_league_name"), head.capital?.GetCityName() ?? head.GetKingdomName());
        FormProtectedLeague(league, imperialProtector ? departing : new[] { protector }.Concat(departing).ToList());
        string names = string.Join("、", departing.Select(member => member.GetKingdomName()));
        string text = string.Format(LM.Get(imperialProtector ? "hre_protector_pact_history" : "hre_protector_league_history"),
            league, protector.GetKingdomName(), empire.GetEmpireFullName(), names);
        ChangeAuthority(empire, AuthProtectorLeague, "hre_auth_protector_league", league, protector.GetKingdomName());
        empire.AddMandate(ProtectorLegitimacy);
        Record(empire, text);
        state.departed_log.Add(text);
        while (state.departed_log.Count > DepartedLogSize) state.departed_log.RemoveAt(0);
        FiscalDiagnostics.Count("神罗·保护同盟");
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 保护同盟：{text}(权威{Authority(empire)}，" +
                           $"保护人国力{WarSituation.Power(protector):0}、中央{empire.CoreKingdom.GetNationalPower():0})");
    }

    // 结盟(第一家为盟主)：盟主已有同盟就并入它的同盟，否则新结一个模组同盟；别的家已在别的同盟的先退出
    private static void FormProtectedLeague(string name, List<Kingdom> realms)
    {
        List<Kingdom> group = realms.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && !kingdom.IsInEmpire() &&
                                                      !kingdom.IsEmpire()).ToList();
        if (group.Count < 2) return;
        try
        {
            Kingdom head = group[0];
            Alliance alliance = head.hasAlliance() ? head.getAlliance() : null;
            if (alliance == null)
            {
                foreach (Kingdom kingdom in group.Where(kingdom => kingdom.hasAlliance()).ToList())
                    kingdom.getAlliance().leave(kingdom);
                alliance = World.world.alliances.newAlliance(group[0], group[1]);
                if (alliance == null) return;
                ModAllianceService.Mark(alliance);
                alliance.setName(name);
            }
            foreach (Kingdom member in group.Where(kingdom => kingdom.getAlliance() != alliance))
            {
                if (member.hasAlliance()) member.getAlliance().leave(member);
                alliance.join(member, true, true);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 保护同盟结盟失败: {exception.Message}");
        }
    }

    // 开战即援(WarPatch.new_war)：原帝国中央或其成员攻打被保护者时，保护人加入守方
    public static void OnWarStarted(War war)
    {
        try
        {
            Kingdom defender = war?.main_defender, attacker = war?.main_attacker;
            if (defender?.data == null || attacker?.data == null) return;
            var data = defender.GetOrCreate();
            if (data.hre_protector_id < 0 || data.hre_protected_at < 0d || Date.getYearsSince(data.hre_protected_at) >= ProtectionYears)
                return;
            Empire former = attacker.GetEmpire();
            if (former?.data == null || former.data.id != data.hre_protected_from_id) return;
            Kingdom protector = LiveKingdom(data.hre_protector_id);
            if (protector == null || war.hasKingdom(protector) || protector.isInWarWith(defender) || protector.GetEmpire() == former)
                return;
            war.joinDefenders(protector);
            if (war.hasKingdom(protector))
                TranslateHelper.LogEventMessage(string.Format(LM.Get("hre_protector_aid_history"), protector.GetKingdomName(),
                    defender.GetKingdomName(), attacker.GetKingdomName()), protector);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 保护人援助失败: {exception.Message}");
        }
    }

    // 年度诊断的帝国存续一段
    private static string DeclineStatus(Empire empire, HolyRomanState state)
    {
        if (!DissolutionEnabled) return "模组设置已关帝国解体";
        string low = state.authority_low_since < 0d ? $"权威{Authority(empire)}，未在式微"
            : $"权威低于{LowAuthority}已{Date.getYearsSince(state.authority_low_since)}年(满{ProtectorLowYears}年外国可拉人)";
        string last = state.last_protector_at >= 0d ? $"，上次保护同盟{Date.getYearsSince(state.last_protector_at)}年前" : "";
        string abdication = AbdicationBlocker(empire, state) ?? "条件已齐，每年一成";
        return $"{low}{last}，历史最多{state.peak_members}国；退位解散：{abdication}";
    }
}

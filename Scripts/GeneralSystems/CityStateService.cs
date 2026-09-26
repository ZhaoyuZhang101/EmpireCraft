using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 西方古典共和(城邦)阶段：一城一国。
//   · 城邦不占第二座城：攻下的城由本地贵族立为新城邦、加入攻城方的同盟；对方只剩这一座城时整国入盟；
//   · 城邦的国民新建城市即独立为殖民城邦，与母邦结盟；
//   · 联盟只用于没有称帝的城邦：攻城方/母邦已属于帝国时，新城邦或被征服的城邦直接并入该帝国
//     (本模组里帝国和原版联盟是互不兼容的两套东西)；
//   · 城邦国名默认用城市名，不能建立法理；
//   · 称帝后是古典共和帝国：成员完全自治；首领在施行「元老院」之前由最强者担任，之后由元老院推举。
public static class CityStateService
{
    public static bool IsCityState(Kingdom kingdom) =>
        kingdom != null && !kingdom.isRekt() && kingdom.GetRegime()?.type == RegimeType.ClassicalRepublic;

    // 元老院推举由制度特性 senate_election 提供
    public static bool HasSenate(Empire empire) =>
        empire != null && InstitutionSystem.HasFeature(empire, InstitutionFeatures.SenateElection);

    public static void ApplyCityName(Kingdom kingdom)
    {
        if (!IsCityState(kingdom) || kingdom.capital == null || kingdom.capital.isRekt()) return;
        string name = kingdom.capital.GetCityName();
        if (string.IsNullOrWhiteSpace(name)) return;
        kingdom.data.name = name;
        kingdom.RememberInitialRandomKingdomName(name, overwrite: true);
        kingdom.SetKingdomCoreName(name, name);
    }

    // 城邦加入古典共和帝国后完全自治：保留外交权和军队，不交税、不向中央输兵
    public static void GrantAutonomy(Kingdom kingdom)
    {
        Regime regime = kingdom?.GetRegime();
        if (regime == null) return;
        regime.SetAllowDiplomacy(true);
        regime.SetAllowArmy(true);
        regime.SetAllowSupportCenterArmy(false);
        regime.SetTaxLevel(TaxLevel.None);
    }

    // CityManager.buildNewCity 之后调用：开国的第一座城用来定国名，之后新建的城独立为殖民城邦
    public static void OnCityBuilt(City city, Actor founder)
    {
        Kingdom mother = city?.kingdom;
        if (!IsCityState(mother)) return;
        if (mother.cities.Count <= 1 || city == mother.capital)
        {
            ApplyCityName(mother);
            return;
        }
        Actor leader = founder != null && !founder.isRekt() && founder.isAlive() && !founder.isKing() &&
                       founder.city == city
            ? founder
            : FindLocalLeader(city, null);
        if (leader == null) return;
        Kingdom colony = SpinOff(city, leader, mother);
        if (colony == null) return;
        bool inEmpire = Attach(mother, colony);
        Record(mother, inEmpire ? "city_state_colony_empire_history" : "city_state_colony_history",
            mother.GetKingdomName(), colony.GetKingdomName());
    }

    // CityPatch.FinishedCapture 调用。返回 true 表示已按城邦规则处理，城市不归攻城方。
    public static bool TryResolveCapture(City city, Kingdom captor, Kingdom defender)
    {
        if (city == null || !IsCityState(captor) || defender == null || defender == captor || defender.isRekt())
            return false;
        string captorName = captor.GetKingdomName();

        // 对方只剩这一座城：整国加入攻城方的同盟
        if (defender.cities.Count <= 1 && !defender.IsEmpire())
        {
            EndWarsBetween(captor, defender);
            bool joinedEmpire = Attach(captor, defender);
            Record(captor, joinedEmpire ? "city_state_submit_empire_history" : "city_state_submit_alliance_history",
                captorName, defender.GetKingdomName());
            return true;
        }

        // 否则由本地贵族把这座城立为新城邦，加入攻城方的同盟；找不到人就守不住，城仍归原主
        Actor leader = FindLocalLeader(city, defender.king);
        if (leader == null) return true;
        if (defender.king?.city == city) defender.kingFledCity();
        Kingdom newState = SpinOff(city, leader, captor);
        if (newState == null) return true;
        bool spunIntoEmpire = Attach(captor, newState);
        Record(captor, spunIntoEmpire ? "city_state_capture_spin_empire_history" : "city_state_capture_spin_history",
            captorName, newState.GetKingdomName(), leader.getName());
        return true;
    }

    private static Kingdom SpinOff(City city, Actor leader, Kingdom parent)
    {
        Kingdom state = city.makeOwnKingdom(leader);
        if (state == null) return null;
        // 同文化的殖民城邦先继承母邦现行制度，之后再由各国逐步完成封建化。
        if (parent != null && IsCityState(parent) &&
            CultureService.GetRealmCulture(state) == CultureService.GetRealmCulture(parent) &&
            state.GetRegime()?.type != RegimeType.ClassicalRepublic)
        {
            state.SetRegimeType(RegimeType.ClassicalRepublic);
            state.LoadRegime();
            state.SystemChange();
        }
        if (state.HasMainTitle()) state.RemoveMainTitle();
        ApplyCityName(state);
        return state;
    }

    // 本地领袖：优先贵族阶层，其次城中其他成年人，按威望取最高者
    private static Actor FindLocalLeader(City city, Actor exclude)
    {
        List<Actor> locals = (city.units ?? new List<Actor>()).ToList()
            .Where(actor => actor != null && actor != exclude && !actor.isRekt() && actor.isAlive() &&
                            actor.isAdult() && !actor.isKing() && actor.isUnitFitToRule())
            .ToList();
        return locals.Where(actor => actor.GetOrCreate().socialClass == SocialClass.Noble)
                   .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault()
               ?? locals.OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
    }

    public static bool TryCrownLocalRuler(Kingdom realm)
    {
        City city = realm?.capital;
        if (city == null || city.isRekt()) return false;
        Actor leader = city.leader;
        if (leader == null || leader.isRekt() || !leader.isAlive() || !leader.isAdult() ||
            leader.isKing())
            leader = FindLocalLeader(city, realm.king);
        if (leader == null) return false;
        GraceEdictService.Crown(realm, leader);
        return realm.king == leader;
    }

    private static void EndWarsBetween(Kingdom victor, Kingdom loser)
    {
        foreach (War war in loser.getWars().ToList())
        {
            bool opposing = (war._list_attackers.Contains(victor) && war._list_defenders.Contains(loser)) ||
                            (war._list_defenders.Contains(victor) && war._list_attackers.Contains(loser));
            if (opposing && war.isAlive() && !war.hasEnded()) war.lostWar(loser);
        }
    }

    // 攻城方/母邦在帝国里：并入该帝国(先退出原版联盟，两者不兼容)，返回 true；否则入盟，返回 false
    private static bool Attach(Kingdom leader, Kingdom member)
    {
        Empire empire = leader?.GetEmpire();
        if (empire != null && !empire.isRekt() && !empire.IsArchived())
        {
            if (member.hasAlliance()) member.getAlliance().leave(member);
            empire.join(member, pForce: true);
            return true;
        }
        PersonalUnionService.JoinAlliance(leader, member);
        ShareSovereign(leader, member);
        return false;
    }

    // 入盟的城邦同步盟主(战胜国/母邦)的颜色，并与盟主共用同一位君主；联盟以盟主城邦的名字命名。
    // 这里直接 setKing 而不走官职任命：任命会把君主迁到对方都城，他就离开了自己的城邦。
    private static void ShareSovereign(Kingdom leader, Kingdom member)
    {
        if (leader == null || member == null || member.isRekt()) return;
        member.updateColor(leader.getColor());
        Actor sovereign = leader.king;
        if (sovereign == null || sovereign.isRekt() || !sovereign.isAlive()) return;
        PersonalUnionService.CrownInUnion(member, sovereign);
        PersonalUnionService.NameAllianceAfterLeader(leader);
    }

    private static void Record(Kingdom kingdom, string key, params object[] args)
    {
        string content = string.Format(LM.Get(key), args);
        LogService.LogInfo(content);
        Empire empire = kingdom?.GetEmpire();
        if (empire != null && !empire.isRekt()) empire.RecordHistory(directContent: content, kingdomId: kingdom.id);
    }
}

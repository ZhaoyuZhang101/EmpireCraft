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

// 西方古典共和(城邦)阶段：一城一国(名下多出来的城每月分出一座，见 EnforceOneCity)。
//   · 城邦不占第二座城：攻下的城由本地贵族立为新城邦、加入攻城方的同盟；对方只剩这一座城时整国入盟；
//   · 城邦的国民新建城市即独立为殖民城邦，与母邦结盟；
//   · 联盟只用于没有称帝的城邦：攻城方/母邦已属于帝国时，新城邦或被征服的城邦直接并入该帝国
//     (本模组里帝国和原版联盟是互不兼容的两套东西)；
//   · 城邦国名默认用城市名，不能建立法理；
//   · 称帝后是古典共和帝国：成员完全自治；首领在施行「元老院」之前由最强者担任，之后由元老院推举；
//   · 施行「元首制」后：元首在家族里相传；核心与它的行省不再一城一国——核心打下的城归核心，再设行省派总督
//     (见 Empire.IsPrincipate)，原有的自治城邦照旧。
public static class CityStateService
{
    public static bool IsCityState(Kingdom kingdom) =>
        kingdom != null && !kingdom.isRekt() && kingdom.GetRegime()?.type == RegimeType.ClassicalRepublic;

    // 元首制的核心与行省(没有外交权的成员)：不再一城一国
    public static bool IsPrincipateRealm(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire == null || !IsCityState(kingdom) || !empire.IsPrincipate()) return false;
        return kingdom == empire.CoreKingdom || kingdom.GetRegime()?.IsAllowDiplomacy() == false;
    }

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

    // 城邦加入古典共和帝国(诸侯加入封建制帝国)后完全自治：保留外交权和军队，不向中央输兵。
    // "不交税"是不向中央上缴(古典共和的自治城邦不交，封建诸侯照封建成员的分成交，见 TreasurySystem.TaxShares)，
    // 不是本国不收税：以前把税级设成"无"，成员一分税收不上；现在税级为"无"的改回"低"，原有的税级不动。
    // 帝国自由城市、主教领(见 HolyRomanSystem.Members)本就没有外交开战权，不给
    public static void GrantAutonomy(Kingdom kingdom)
    {
        Regime regime = kingdom?.GetRegime();
        if (regime == null) return;
        if (HolyRomanSystem.Estate(kingdom) == HolyRomanSystem.EstateNone)
        {
            regime.SetAllowDiplomacy(true);
            regime.SetAllowArmy(true);
            regime.SetAllowSupportCenterArmy(false);
        }
        TreasurySystem.EnsureCollectsTax(regime);
    }

    // 旧档修补(每年随帝国年度结算，见 ConstitutionalEconomySystem)：帝国自由城市、主教领入帝国时被 GrantAutonomy
    // 误给了外交开战权，收回；诸侯订立和约、各家取得完全主权之后不收
    public static void RepairEstateAutonomy(Empire empire)
    {
        if (empire?.data == null || empire.kingdoms_list == null || empire.data.holy_roman?.sovereign_princes == true) return;
        foreach (Kingdom member in empire.kingdoms_list)
        {
            if (member?.data == null || member.isRekt() || member == empire.CoreKingdom ||
                HolyRomanSystem.Estate(member) == HolyRomanSystem.EstateNone) continue;
            Regime regime = member.GetRegime();
            if (regime == null || !regime.IsAllowDiplomacy()) continue;
            regime.SetAllowDiplomacy(false);
            regime.SetAllowSupportCenterArmy(false);
        }
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
        if (IsPrincipateRealm(mother)) return;
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

    // 一城一国：城邦名下多出来的城(建城时还没施行城邦共和、后来才改制；和约割让、归附等别的来路)每月分出一座，
    // 由本城的主事者立为殖民城邦，与母邦结盟、共用一位君主(母邦在帝国里的并入帝国)。RoyalHouseSystem 每月调用
    public static void EnforceOneCity()
    {
        if (World.world?.kingdoms == null) return;
        foreach (Kingdom mother in World.world.kingdoms.list.ToList())
        {
            if (!IsCityState(mother) || mother.capital == null || mother.cities == null || mother.cities.Count <= 1 ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(mother) ||
                IsPrincipateRealm(mother)) continue;
            City city = mother.cities.Where(candidate => candidate?.data != null && !candidate.isRekt() &&
                                                         candidate != mother.capital && !candidate.GetOrCreate().placed_by_hand)
                .OrderBy(candidate => candidate.id).FirstOrDefault();
            if (city == null) continue;
            if (!city.hasLeader()) CityPopulationSystem.EnsureLeader(city);
            Actor current = city.leader;
            Actor leader = current != null && !current.isRekt() && current.isAlive() && current.isAdult() &&
                           !current.isKing() && current.isUnitFitToRule()
                ? current
                : FindLocalLeader(city, mother.king);
            if (leader == null) continue;
            try
            {
                Kingdom colony = SpinOff(city, leader, mother);
                if (colony == null) continue;
                bool inEmpire = Attach(mother, colony);
                Record(mother, inEmpire ? "city_state_colony_empire_history" : "city_state_colony_history",
                    mother.GetKingdomName(), colony.GetKingdomName());
            }
            catch (global::System.Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][城邦] {mother.GetKingdomName()} 分出殖民城邦失败: {exception.Message}");
            }
        }
    }

    // CityPatch.FinishedCapture 调用。返回 true 表示已按城邦规则处理，城市不归攻城方。
    public static bool TryResolveCapture(City city, Kingdom captor, Kingdom defender)
    {
        if (city == null || !IsCityState(captor) || defender == null || defender == captor || defender.isRekt() ||
            IsPrincipateRealm(captor))
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
    internal static Actor FindLocalLeader(City city, Actor exclude)
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
        // 别的帝国的成员被城邦打下、入城邦同盟：先退出原帝国(帝国与同盟互不兼容)。以前留在帝国里，帝国的仗又把它拉回去，
        // 对面的城邦一打再打，同一座城一会儿入这家同盟、一会儿入那家，日志里成串地刷"即将被…捕获"
        Empire former = member.GetEmpire();
        if (former != null && !former.isRekt() && !former.IsArchived() && former.CoreKingdom != member) former.leave(member);
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

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EmpireCraft.Scripts.GeneralSystems;

// 周制模拟春秋战国：诸侯可以互相征伐，但都尊奉天子、共同攘夷。
//   西周：王室强盛，诸侯不得擅自开战，天子征伐、诸侯从征(诸侯军团本就随王师作战，见 FrontSystem.Realm)；
//   春秋(王畿兵力不及最强诸侯、天命 < 60、或王畿失陷东迁)：
//     · 诸侯互相征伐兼并，打下都城即灭其国、并入自己，仍是周室诸侯；
//     · 尊王：诸侯不得攻王畿，敢犯天子者霸主与诸侯共讨；
//     · 攘夷：夷狄(他族、他文化之国)攻打任一诸侯，天子与霸主出兵来援(诸侯军团随之同上战线)；
//     · 霸主会盟：最强且尊王的诸侯受天子赐胙为霸主，诸侯每年以赋税一成纳贡，霸主可令交战诸侯罢兵、讨伐不朝之国与僭王；
//     · 卿大夫专权：大国可能被权臣瓜分(三家分晋)或篡位(田氏代齐)；子爵的夷狄大国可能僭越称王；
//   战国(发生权臣瓜分、诸侯兼并到七国以下、或天命 < 30)：
//     · 大国相继称王，霸主与会盟废止，可以攻王畿、灭周；
//     · 变法：大国推行变法，战时兵额 +25%、赋税每年多收一成，旧贵族怨望；
//     · 合纵连横：最强一国独大时，其余诸侯合纵，它攻打合纵之国则各国来援；弱小之国可能连横依附于它，彼此不攻；
//   统一：攻下王畿(或在和约中割得王畿)者取周而代之(接续为新王朝)；天子(或代周者)直辖天下一半的城，
//         或直辖三分之一且再无一国及其三分之一，即称皇帝，改行郡县律令制。
//   战国兼并：诸侯互伐以吞并或割地为务(见 WarGoalSystem)；称王、变法之国每年主动伐接壤弱国；一城小国可能举国降于大国；
//         和约割走诸侯都城同攻破都城，灭其国；合纵之国每场仗只有四成出兵来援。
// 科技推时代(用户 2026-10-17 选"科技推时代")：春秋要先有青铜铸造、战国要先有冶铁(科技树关着不限)，没有这门科技，
//   王室再弱也停在原来的时代(王畿失陷照样东迁，等科技到了再入春秋)；有了下一时代的科技，衰落门槛放宽：
//   西周有了冶铁，天命低于 70 即入春秋；春秋有了炼钢，天命低于 40 或诸侯并到九国及以下即入战国。神力推进不受限。
// 看得见(用户 2026-10-17)：春秋战国之始、会盟赐胙、霸主讨伐、攘夷勤王、称王变法、灭国合纵等大事弹世界提示(见 Announce)；
//   夷狄来犯、天子出兵的那场仗改名"尊王攘夷"。
// 天子主动(用户 2026-10-17 选"天子主动按钮"，见天下大势面板)：
//   · 赐胙：西周、春秋时天子花国库赐胙于一个诸侯，十年内该国尊王——对王室好感 +40、霸主纳贡不因交恶而不朝、
//     春秋不僭越称王、勤王时优先出兵；天命 +1。天子自己每年也有一成二的机会赐胙于霸主或最强的诸侯(国库要留三倍)；
//   · 号召攘夷：夷狄正在侵犯诸侯或王畿，天子、霸主与接壤夷狄的尊王诸侯(至多三国)一齐出兵来援，天命 +2；
//     夷狄只是与天下接壤，天子兴师讨之(战名"尊王攘夷·某国讨某国")，霸主与诸侯从征，天命 +1，五年一次。
// 战国时诸侯不再留在帝国里：各自离开帝国(郡仍归王畿)，成为自由度很高的诸侯——有自己的颜色、国库、官制、军队，
// 只记着所属周室(KingdomExtraData.zhou_sphere_id，标"某诸侯国")，地图上整个周室(王畿连同诸侯)外围一圈加粗边框；
// 春秋战国的规则照旧，判断"是不是周室诸侯"一律看 SphereOf(帝国成员或战国离开帝国的诸侯)。统一后诸侯各自独立。
public static class ZhouEraSystem
{
    public const int WesternZhou = 0, Spring = 1, Warring = 2, Unified = 3;
    private const int ConferenceYears = 10;
    private const int LogSize = 30;
    // 合纵之国出兵来援的概率(十分之几)
    private const int CoalitionAnswer = 4;
    // 战国兼并：称王或变法之国每年向接壤弱国开战的概率；一个周室每年至多新开几场
    private const float ConquestChance = 0.25f;
    private const int ConquestsPerYear = 2;
    // 一城小国被国力四倍以上的接壤大国举国降服的概率(每年)
    private const float SubmitChance = 0.06f;

    public static bool Applies(Empire empire) =>
        empire != null && !empire.isRekt() && !empire.IsArchived() &&
        empire.CoreKingdom?.GetRegime()?.type == RegimeType.ZhouFeudalism;

    public static ZhouEraState State(Empire empire)
    {
        if (empire?.data == null) return null;
        return empire.data.zhou ??= new ZhouEraState();
    }

    public static int Era(Empire empire) => Applies(empire) ? State(empire).era : -1;

    // 科技推时代：进入下一时代须先有的科技(西周→春秋青铜铸造，春秋→战国冶铁)；有了再下一门，衰落门槛放宽
    public const string SpringTech = "bronze_casting", WarringTech = "ironworking", LateWarringTech = "steelmaking";

    public static string GateTech(int era) => era == WesternZhou ? SpringTech : era == Spring ? WarringTech : null;

    private static bool Knows(Empire empire, string tech) =>
        TechnologySystem.IsEnabled && TechnologySystem.HasTech(InstitutionSystem.GetPrimaryCulture(empire), tech);

    public static bool TechReady(Empire empire, int era)
    {
        string tech = GateTech(era);
        return tech == null || !TechnologySystem.IsEnabled || Knows(empire, tech);
    }

    // 春秋、战国王室衰微：天子不能再从诸侯手里收地。削藩、收回法理、按法理整合与划界、
    // 战时代管、收复旧都、绝嗣收归都不再发生；诸侯打下的地归自己(尽有其地)，王畿只靠自己的兵守土开疆
    public static bool RoyalReclaimBlocked(Empire empire) => Applies(empire) && State(empire).era >= Spring;

    // 这国所属的周室：周制帝国的成员，或战国时离开帝国、仍记着所属周室的诸侯；都不是返回 null
    public static Empire SphereOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        Empire member = kingdom.GetEmpire();
        if (member != null) return Applies(member) ? member : null;
        long id = kingdom.GetOrCreate().zhou_sphere_id;
        if (id < 0 || ModClass.EMPIRE_MANAGER == null) return null;
        Empire sphere = ModClass.EMPIRE_MANAGER.get(id);
        return Applies(sphere) && State(sphere).era >= Warring ? sphere : null;
    }

    // 周制下一个文化只有一个天下共主：本文化的周制王朝(没有返回 null)
    public static Empire RealmOfCulture(string culture) => Realms(culture).FirstOrDefault();

    // 本天下的各周制王朝，正统的在前：按资历定下的王室(见 TribalFissionService.SettleRoyalHouse)，其次先立的。
    // 以前取遍历到的第一个，几个部落联盟同时改了周制时谁是周室说不准(用户 2026-10-26 选"按资历定唯一王室")
    private static IEnumerable<Empire> Realms(string culture)
    {
        if (!CultureService.IsValidCulture(culture) || ModClass.EMPIRE_MANAGER == null) return Enumerable.Empty<Empire>();
        // 按天下(同文化或同天下标识，见 CultureService.SameKin)：华夏、山海……同一天下里只有一个周室(以前按文化一字不差)
        return ModClass.EMPIRE_MANAGER.Where(empire => Applies(empire) &&
                                                       CultureService.SameKin(CultureService.GetEmpireDefaultCulture(empire), culture))
            .OrderByDescending(empire => State(empire).royal_settled_at >= 0d)
            .ThenBy(empire => empire.data.created_time).ThenBy(empire => empire.id);
    }

    // 本天下已有定下的周室(不再按资历另定)：按资历定的，或已走过西周初年 freshYears 年的(旧档、别的文化先进了周制)
    public static bool HasSettledRealm(string culture, int freshYears) =>
        Realms(culture).Any(empire =>
        {
            ZhouEraState state = State(empire);
            return state.royal_settled_at >= 0d || state.era > WesternZhou ||
                   state.era_since >= 0d && World.world != null && Date.getYearsSince(state.era_since) >= freshYears;
        });

    // 本天下已有周制王朝、这国又不是它的王畿：不能另行称帝或发动正统之争，天下只有一个正统核心。
    // 这样的国家只能受封为诸侯(春秋战国照样可以称王)；取周而代之要攻下王畿(见 Unified)。
    // 按所在的天下算(2026-10-27：赵受汉正统天命、据有九成八，却被远在高丽天下、一城不占的另一个华夏周室挡着，永远称不了帝——
    // 以前按文化圈全世界只认一个周室，天下层出来以后一个文化圈可以有好几个天下)：周室与这国不在同一个天下就不挡；
    // 这国不在任何天下里、或周室的天下认不出来，照旧按文化圈挡
    public static bool BlocksRivalEmpire(Kingdom kingdom)
    {
        if (kingdom?.data == null) return false;
        EmpireCore mine = TianxiaSystem.MandateOf(kingdom) ?? TianxiaSystem.LeadOf(kingdom.capital);
        return Realms(CultureService.GetRealmCulture(kingdom))
            .Any(realm => realm.CoreKingdom != kingdom && SameTianxia(realm, mine));
    }

    private static bool SameTianxia(Empire realm, EmpireCore mine)
    {
        if (mine == null) return true;
        EmpireCore theirs = TianxiaSystem.Lead(EmpireCoreManager.Get(realm)) ?? TianxiaSystem.LeadOf(realm.CoreKingdom?.capital);
        return theirs == null || theirs == mine;
    }

    // 不服王化：与周室同文化，却不在周室之内(不是成员、也不是战国离开帝国的诸侯)、也没向别国称臣纳贡的独立之国
    public static bool Unsubmitted(Empire empire, Kingdom kingdom) =>
        Applies(empire) && kingdom?.data != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom &&
        SphereOf(kingdom) == null && !kingdom.IsInEmpire() && !kingdom.IsEmpire() && !kingdom.HasTakenAlliance() &&
        CultureService.SameKin(CultureService.GetRealmCulture(kingdom), CultureService.GetEmpireDefaultCulture(empire));

    // 天子兴师讨伐的不服王化之国：与王畿接壤、兵力不及王师的，先挑最弱的
    public static Kingdom SubdueTarget(Empire empire)
    {
        if (!Applies(empire) || empire.CoreKingdom == null) return null;
        int royal = empire.countWarriors();
        // 接壤或隔海相望(见 StraitReach)
        return empire.GetKingdomNeighbours()
            .Concat(World.world.kingdoms.list.Where(kingdom => Unsubmitted(empire, kingdom) && StraitReach.Across(empire.CoreKingdom, kingdom)))
            .Distinct()
            .Where(kingdom => Unsubmitted(empire, kingdom) && !kingdom.IsLocalRebelling() && !kingdom.IsFactionRebelling() &&
                              !kingdom.isInWarWith(empire.CoreKingdom) && kingdom.countTotalWarriors() < royal)
            .OrderBy(kingdom => kingdom.countTotalWarriors()).FirstOrDefault();
    }

    // 讨伐不服王化之战：攻下其都城即迫其称臣、受封为诸侯(见 FeudalConquestService.TryResolveCapitalConquest)，
    // 不按吞并、割地议和(见 WarGoalSystem.OwnEnding)
    public static void MarkSubdueWar(War war, Empire empire, Kingdom target)
    {
        if (war?.data == null || empire == null || target == null) return;
        war.SetEmpireWarType(EmpireWarType.征伐不服);
        string royal = EmpireCraft.Scripts.GameLibrary.EmpireCraftNamePlateLibrary.GetDisplayName(empire.CoreKingdom);
        war.data.name = string.Format(LM.Get("zhou_subdue_war_name"),
            string.IsNullOrWhiteSpace(royal) ? empire.GetEmpireName() : royal, target.GetKingdomName());
    }

    // 战国时离开帝国的周诸侯
    // 同一周室、已入战国的两国(含王畿)：彼此征伐以兼并为务
    public static bool WarringRivals(Kingdom a, Kingdom b) =>
        a != null && b != null && a != b && SphereOf(a) is Empire empire && SphereOf(b) == empire && State(empire).era == Warring;

    public static bool IsDetachedVassal(Kingdom kingdom) => kingdom?.GetEmpire() == null && SphereOf(kingdom) != null;

    // 进宗法分封时按共事年数定下的爵位(用户 2026-10-26 选"爵位按资历"，见 TribalFissionService.Enfeoff；国家类别见
    // EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType)：周制、在周室之内的诸侯照定下的，不再按城数、族属改；
    // 宗室(公)、郡照政体配置；离了周室仍是周制的(独立、不服王化)暂不用，再受封时照旧；改了政体的作废
    public static KingdomType? SeniorPeerage(Kingdom kingdom, Regime regime)
    {
        if (kingdom?.data == null || regime == null) return null;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (string.IsNullOrEmpty(data.tribal_peerage) || !Enum.TryParse(data.tribal_peerage, out KingdomType rank)) return null;
        if (regime.type != RegimeType.ZhouFeudalism)
        {
            data.tribal_peerage = "";
            return null;
        }
        Empire empire = SphereOf(kingdom);
        if (empire == null || kingdom == empire.CoreKingdom || regime.bureau_config?.kingdoms?.ContainsKey(rank) != true) return null;
        // 郡(不世袭、不通外交)照政体配置
        if (regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession && !regime.IsAllowDiplomacy()) return null;
        if (SpecificClanManager.SameLineage(kingdom.GetSpecificClan(), empire.EmpireSpecificClan)) return null;
        return rank;
    }

    public static IEnumerable<Kingdom> Vassals(Empire empire)
    {
        if (empire?.data == null) return Enumerable.Empty<Kingdom>();
        IEnumerable<Kingdom> members = empire.kingdoms_list?.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
            kingdom != empire.CoreKingdom && kingdom.hasCities()) ?? Enumerable.Empty<Kingdom>();
        if (!Applies(empire) || State(empire).era < Warring || World.world?.kingdoms == null) return members;
        IEnumerable<Kingdom> detached = World.world.kingdoms.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
            kingdom.hasCities() && kingdom.GetEmpire() == null && kingdom.GetOrCreate().zhou_sphere_id == empire.id);
        return members.Concat(detached).ToList();
    }

    // 战国离开帝国的诸侯(帝国名下的成员不在内)
    public static List<Kingdom> DetachedVassals(Empire empire)
    {
        if (!Applies(empire) || State(empire).era < Warring || World.world?.kingdoms == null) return new List<Kingdom>();
        return World.world.kingdoms.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.hasCities() &&
                                                     kingdom.GetEmpire() == null &&
                                                     kingdom.GetOrCreate().zhou_sphere_id == empire.id).ToList();
    }

    // 周室"天下"的城与人口：战国时诸侯不在帝国名下，帝国自己的统计(国力、赋税用)不含他们，显示时连他们一起算
    public static int SphereCities(Empire empire) =>
        (empire?.countCities() ?? 0) + DetachedVassals(empire).Sum(kingdom => kingdom.cities?.Count ?? 0);

    public static int SpherePopulation(Empire empire) =>
        (empire?.CountPopulation() ?? 0) + DetachedVassals(empire).Sum(kingdom => kingdom.getPopulationPeople());

    // 战国：诸侯(郡除外)离开帝国，记下所属周室；爵位(国家类别)照旧
    private static void Detach(Empire empire)
    {
        int count = 0;
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == empire.CoreKingdom ||
                kingdom.GetKingdomType() == KingdomType.ZhouFeudalism_jun) continue;
            var data = kingdom.GetOrCreate();
            KingdomType rank = data.kingdomType;
            data.zhou_sphere_id = empire.id;
            empire.leave(kingdom, pRecalc: false);
            data.kingdomType = rank;
            kingdom.GetRegime()?.SetAllowDiplomacy(true);
            count++;
        }
        if (count == 0) return;
        empire.recalculate();
        RedrawMap();
        Log(empire, string.Format(LM.Get("zhou_log_detached"), count));
    }

    // 周室归属变了：整图重画，外框跟着变(原版只在地块所属国或邻接变化时重画)
    private static void RedrawMap() => World.world?.zone_calculator?.dirtyAndClear();

    public static int Power(Kingdom kingdom) =>
        kingdom?.data == null || kingdom.isRekt() ? 0 : (kingdom.cities?.Count ?? 0) * 4 + kingdom.countTotalWarriors();

    public static bool IsHegemon(Kingdom kingdom) =>
        SphereOf(kingdom) is Empire empire && State(empire).hegemon_id == kingdom.id;

    public static bool IsKing(Kingdom kingdom) =>
        SphereOf(kingdom) is Empire empire && State(empire).kings.Contains(kingdom.id);

    public static bool IsReformed(Kingdom kingdom) =>
        SphereOf(kingdom) is Empire empire && State(empire).reforms.Contains(kingdom.id);

    // 夷狄：不在周室之内、与天子不同族或不同文化的国家
    public static bool IsBarbarian(Empire empire, Kingdom kingdom)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || kingdom?.data == null || kingdom.isRekt() || SphereOf(kingdom) == empire) return false;
        return kingdom.species_id != core.species_id ||
               !CultureService.SameKin(CultureService.GetRealmCulture(kingdom), CultureService.GetRealmCulture(core));
    }

    // 春秋、战国周室内部的战争：交战双方都是同一周室的成员(西周时的叛乱照旧由诸侯随天子平定)
    public static bool IsInternalWar(War war)
    {
        Kingdom attacker = war?.main_attacker, defender = war?.main_defender;
        Empire empire = SphereOf(attacker);
        return empire != null && State(empire).era >= Spring && SphereOf(defender) == empire;
    }

    // 卿族坐大：春秋时六城以上(可能被三分)、或四城以上而国君年幼年老的大国
    public static bool MinistersRising(Kingdom kingdom)
    {
        Empire empire = SphereOf(kingdom);
        if (empire == null || State(empire).era != Spring || kingdom == empire.CoreKingdom) return false;
        int cities = kingdom.cities?.Count ?? 0;
        Actor ruler = kingdom.king;
        bool weakRuler = ruler == null || !ruler.isAdult() || ruler.getAge() > 60;
        return cities >= 6 || cities >= 4 && weakRuler;
    }

    // 战国变法：战时兵额 +25%
    public static float LevyFactor(Kingdom kingdom) => IsReformed(kingdom) ? 1.25f : 1f;

    #region 诸侯征伐(供宣战规则调用)

    // 春秋、战国：周室诸侯可以自己开战
    public static bool VassalMayWage(Kingdom kingdom)
    {
        Empire empire = SphereOf(kingdom);
        return empire != null && kingdom != empire.CoreKingdom && State(empire).era >= Spring &&
               (IsDetachedVassal(kingdom) || kingdom.GetRegime()?.IsAllowDiplomacy() == true);
    }

    // 诸侯能否向 target 开战：同为周室诸侯可以(春秋不得攻王畿)；周室之外按常规
    public static bool IsValidVassalWarTarget(Kingdom initiator, Kingdom target, out bool handled)
    {
        handled = false;
        if (!VassalMayWage(initiator) || target?.data == null || target.isRekt()) return false;
        Empire empire = SphereOf(initiator);
        if (SphereOf(target) != empire) return false;
        handled = true;
        if (target == initiator) return false;
        if (target == empire.CoreKingdom && State(empire).era < Warring) return false;
        if (!initiator.IsNeighbourWith(target)) return false;
        if (initiator.hasAlliance() && initiator.getAlliance() == target.getAlliance()) return false;
        // 合纵之国不互攻；连横之国与所依附之国不互攻
        ZhouEraState state = State(empire);
        if (state.coalition.Contains(initiator.id) && state.coalition.Contains(target.id)) return false;
        if (state.horizontal.Contains(initiator.id) && target.id == state.coalition_target ||
            state.horizontal.Contains(target.id) && initiator.id == state.coalition_target) return false;
        bool hostile = (World.world?.diplomacy?.getOpinion(initiator, target)?.total ?? 0) < 0;
        return hostile || Power(initiator) >= Power(target) * 1.3f;
    }

    #endregion

    #region 灭国：打下诸侯都城即灭其国

    [ThreadStatic] private static bool _annexing;

    // 城破事件先记下，下次结算时处理：不在攻城流程中途改朝换代、成批转移城市
    private static readonly List<(long city, long loser, long winner, bool royal)> Pending = new();

    public static void QueueCapitalTaken(City city, Kingdom loser, Kingdom winner, bool royal)
    {
        if (city == null || loser == null || winner == null) return;
        Pending.Add((city.id, loser.id, winner.id, royal));
    }

    private static void ProcessPending()
    {
        if (Pending.Count == 0) return;
        var events = Pending.ToList();
        Pending.Clear();
        foreach (var item in events)
        {
            City city = World.world.cities.get(item.city);
            Kingdom loser = World.world.kingdoms.get(item.loser);
            Kingdom winner = World.world.kingdoms.get(item.winner);
            if (city == null || loser == null || winner == null || city.kingdom != winner) continue;
            if (item.royal) OnRoyalCapitalTaken(city, loser, winner);
            else OnCapitalTaken(city, loser, winner);
        }
    }

    public static void OnCapitalTaken(City city, Kingdom loser, Kingdom winner)
    {
        if (_annexing || city == null || loser?.data == null || winner?.data == null || loser == winner) return;
        Empire empire = SphereOf(winner);
        if (empire == null || SphereOf(loser) != empire || State(empire).era < Spring) return;
        if (loser == empire.CoreKingdom) return;
        _annexing = true;
        try
        {
            string loserName = loser.GetKingdomName();
            foreach (City other in loser.cities?.ToList() ?? new List<City>())
                if (other != null && !other.isRekt() && other.kingdom == loser) other.joinAnotherKingdom(winner, true);
            ZhouEraState state = State(empire);
            state.annexed++;
            state.kings.Remove(loser.id);
            state.reforms.Remove(loser.id);
            Announce(empire, string.Format(LM.Get("zhou_log_annexed"), winner.GetKingdomName(), loserName), winner, history: true);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][春秋战国] 灭国处理失败: {exception.Message}");
        }
        finally
        {
            _annexing = false;
        }
    }

    // 战国攻下王畿：周亡，攻者取周而代之
    public static void OnRoyalCapitalTaken(City city, Kingdom royal, Kingdom winner)
    {
        Empire empire = royal?.GetEmpire();
        if (!Applies(empire) || empire.CoreKingdom != royal || winner?.data == null || SphereOf(winner) != empire ||
            State(empire).era < Warring) return;
        string zhou = empire.GetEmpireName();
        Announce(empire, string.Format(LM.Get("zhou_log_zhou_fell"), winner.GetKingdomName(), zhou), winner);
        ZhouEraState carried = State(empire);
        long oldId = empire.id;
        if (!empire.ReplaceEmpire(winner)) return;
        Empire successor = winner.GetEmpire();
        if (successor?.data == null) return;
        // 各诸侯改奉代周者
        winner.GetOrCreate().zhou_sphere_id = -1L;
        foreach (Kingdom kingdom in World.world.kingdoms)
            if (kingdom?.data != null && kingdom.GetOrCreate().zhou_sphere_id == oldId) kingdom.GetOrCreate().zhou_sphere_id = successor.id;
        successor.data.zhou = carried;
        RedrawMap();
        carried.hegemon_id = -1L;
        carried.royal_capital_id = winner.capital?.id ?? -1L;
        carried.coalition.Clear();
        carried.coalition_target = -1L;
        successor.RecordHistory(directContent: string.Format(LM.Get("zhou_history_zhou_fell"), winner.GetKingdomName(), zhou),
            kingdomId: winner.id);
    }

    #endregion

    #region 定期结算

    public static void Update(Empire empire)
    {
        if (World.world == null) return;
        try { ProcessPending(); }
        catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][春秋战国] 城破处理失败: {exception.Message}"); }
        if (!Applies(empire)) return;
        ZhouEraState state = State(empire);
        double now = World.world.getCurWorldTime();
        if (state.era_since < 0d)
        {
            state.era_since = now;
            state.royal_capital_id = empire.CoreKingdom.capital?.id ?? -1L;
        }
        if (state.last_month >= 0d && now >= state.last_month && Date.getMonthsSince(state.last_month) < 1) return;
        state.last_month = now;
        try
        {
            Monthly(empire, state);
            if (state.last_year < 0d || now < state.last_year || Date.getYearsSince(state.last_year) >= 1)
            {
                state.last_year = now;
                Yearly(empire, state);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][春秋战国] {empire.GetEmpireName()} 结算失败: {exception}");
        }
    }

    private static void Monthly(Empire empire, ZhouEraState state)
    {
        if (state.era < Spring) return;
        // 战国：诸侯离开帝国(读档、新并入的诸侯也是)
        if (state.era == Warring) Detach(empire);
        // 同一周室同一色系(含战国离开帝国的诸侯)
        KingdomColorService.ApplySphereFamily(empire);
        // 诸侯奉王室，不做别的帝国的朝贡国(旧档里已经朝贡的解除)
        foreach (Kingdom vassal in DetachedVassals(empire))
            if (vassal.HasTakenAlliance()) vassal.RemoveTakenAlliance();
        Kingdom core = empire.CoreKingdom;
        // 尊王：诸侯犯天子，霸主与诸侯共讨(加入天子一方)
        foreach (War war in core.getWars().ToList())
        {
            if (war == null || war.hasEnded()) continue;
            bool coreAttacker = war.isAttacker(core);
            Kingdom foe = coreAttacker ? war.main_defender : war.main_attacker;
            if (foe == null || SphereOf(foe) != empire) continue;
            if (state.era >= Warring) continue;
            var rallied = new List<string>();
            foreach (Kingdom loyal in Loyalists(empire, state, foe).Take(3))
            {
                bool before = war.isAttacker(loyal) || war.isDefender(loyal);
                JoinSide(war, loyal, coreAttacker);
                if (!before && (war.isAttacker(loyal) || war.isDefender(loyal))) rallied.Add(loyal.GetKingdomName());
            }
            if (rallied.Count > 0)
                Announce(empire, string.Format(LM.Get("zhou_log_rally"), foe.GetKingdomName(), string.Join("、", rallied)), core,
                    history: true);
        }
        // 攘夷：夷狄攻打诸侯，天子与霸主出兵来援(只在春秋；战国不再尊王攘夷)
        if (state.era == Spring)
        foreach (Kingdom vassal in Vassals(empire).ToList())
            foreach (War war in vassal.getWars().ToList())
            {
                if (war == null || war.hasEnded() || !war.isDefender(vassal)) continue;
                Kingdom attacker = war.main_attacker;
                if (!IsBarbarian(empire, attacker)) continue;
                bool already = war.isDefender(core);
                JoinSide(war, core, attackerSide: false);
                Kingdom hegemon = World.world.kingdoms.get(state.hegemon_id);
                if (hegemon != null && hegemon != vassal && !hegemon.isRekt()) JoinSide(war, hegemon, attackerSide: false);
                else hegemon = null;
                if (already || !war.isDefender(core)) continue;
                war.data.name = string.Format(LM.Get("zhou_repel_war_name"), vassal.GetKingdomName(), attacker.GetKingdomName());
                bool withHegemon = hegemon != null && war.isDefender(hegemon);
                Announce(empire, withHegemon
                        ? string.Format(LM.Get("zhou_log_repel_hegemon"), core.GetKingdomName(), hegemon.GetKingdomName(),
                            vassal.GetKingdomName(), attacker.GetKingdomName())
                        : string.Format(LM.Get("zhou_log_repel"), core.GetKingdomName(), vassal.GetKingdomName(),
                            attacker.GetKingdomName()), core, history: true);
            }
        // 合纵：所抗之国攻打合纵之国，各国来援
        if (state.era >= Warring && World.world.kingdoms.get(state.coalition_target) is Kingdom target)
            foreach (War war in target.getWars().ToList())
            {
                if (war == null || war.hasEnded() || !war.isAttacker(target)) continue;
                if (!state.coalition.Contains(war.main_defender?.id ?? -1L)) continue;
                foreach (long id in state.coalition)
                    if (World.world.kingdoms.get(id) is Kingdom member && member != war.main_defender &&
                        Answers(member, war))
                        JoinSide(war, member, attackerSide: false);
            }
    }

    // 合纵之国各怀心思：每场仗只有四成会出兵来援(按国与这场仗定下，不每月重掷)
    private static bool Answers(Kingdom member, War war) =>
        (uint)(member.id * 31 + war.data.id * 17) % 10 < CoalitionAnswer;

    // 尊王的诸侯：不是犯上者、不在与天子交战，按国力排序，霸主优先，受胙之国其次
    private static IEnumerable<Kingdom> Loyalists(Empire empire, ZhouEraState state, Kingdom rebel) =>
        Vassals(empire).Where(kingdom => kingdom != rebel && !kingdom.isInWarWith(empire.CoreKingdom) &&
                                         !state.kings.Contains(kingdom.id))
            .OrderByDescending(kingdom => kingdom.id == state.hegemon_id).ThenByDescending(kingdom => HasZuo(state, kingdom))
            .ThenByDescending(Power);

    private static void JoinSide(War war, Kingdom kingdom, bool attackerSide)
    {
        if (war == null || kingdom?.data == null || kingdom.isRekt() || war.hasEnded()) return;
        if (war.isAttacker(kingdom) || war.isDefender(kingdom)) return;
        // 已经和对方议和退出过这场仗的，不再被攘夷、尊王、合纵拉回去
        if (GamePatches.EmpirePeacePatch.HasLeft(kingdom, war, attackerSide)) return;
        Kingdom enemy = attackerSide ? war.main_defender : war.main_attacker;
        if (enemy == null || enemy == kingdom) return;
        try
        {
            if (attackerSide) war.joinAttackers(kingdom);
            else war.joinDefenders(kingdom);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][春秋战国] 参战失败: {exception.Message}");
        }
    }

    private static void Yearly(Empire empire, ZhouEraState state)
    {
        Kingdom core = empire.CoreKingdom;
        // 新加入的诸侯在春秋、战国也有外交与征伐之权(郡除外)
        if (state.era >= Spring)
            foreach (Kingdom vassal in Vassals(empire))
                if (vassal.GetKingdomType() != KingdomType.ZhouFeudalism_jun)
                    vassal.GetRegime()?.SetAllowDiplomacy(true);
        state.kings.RemoveAll(id => SphereOf(World.world.kingdoms.get(id)) != empire);
        state.reforms.RemoveAll(id => SphereOf(World.world.kingdoms.get(id)) != empire);
        AutoZuo(empire, state);
        switch (state.era)
        {
            case WesternZhou:
                // 王畿失陷照样东迁；没有青铜铸造就停在西周，科技到了再入春秋
                bool moved = EastwardMove(empire, state);
                if (TechReady(empire, WesternZhou) && (moved || state.eastward_at >= 0d || WeakRoyalty(empire, state)))
                    EnterSpring(empire, state);
                break;
            case Spring:
                SpringYear(empire, state);
                if (!TechReady(empire, Spring)) break;
                bool late = Knows(empire, LateWarringTech);
                int vassals = Vassals(empire).Count();
                if (state.partitioned || empire.Mandate < (late ? 40 : 30) || vassals <= (late ? 9 : 7) && state.annexed >= 1)
                    EnterWarring(empire, state);
                break;
            case Warring:
                WarringYear(empire, state);
                TryUnify(empire, state);
                break;
        }
    }

    // 王畿失陷：周室东迁
    private static bool EastwardMove(Empire empire, ZhouEraState state)
    {
        City old = World.world.cities.get(state.royal_capital_id);
        Kingdom core = empire.CoreKingdom;
        if (old == null || old.isRekt() || old.kingdom == core || core.capital == null) return false;
        Announce(empire, string.Format(LM.Get("zhou_log_eastward"), old.GetCityName(), core.capital.GetCityName()), history: true);
        state.royal_capital_id = core.capital.id;
        state.eastward_at = World.world.getCurWorldTime();
        return true;
    }

    // 王畿兵力不及最强诸侯(只比兵力)，或天命低于 60(有了冶铁 70)。
    // 王畿连同王室直辖的郡一起算(用户 2026-10-26 选"王畿不止一城"：进宗法分封时最早共事的几部改为王室直辖的郡，
    // 见 TribalFissionService.SettleRoyalHouse；郡本就不离王畿，见 Detach)，只跟郡以外的诸侯比；
    // 开国保护期：改周制后 RoyalGraceYears 年内王室不算衰微(以前王畿只有一城，多半转周第一年就入春秋)，王畿失陷照样东迁
    public const int RoyalGraceYears = 30;

    private static bool WeakRoyalty(Empire empire, ZhouEraState state)
    {
        if (state.era_since >= 0d && World.world.getCurWorldTime() >= state.era_since &&
            Date.getYearsSince(state.era_since) < RoyalGraceYears) return false;
        List<Kingdom> vassals = Vassals(empire).ToList();
        int royal = empire.CoreKingdom.countTotalWarriors() +
                    vassals.Where(vassal => vassal.GetKingdomType() == KingdomType.ZhouFeudalism_jun).Sum(vassal => vassal.countTotalWarriors());
        return empire.Mandate < (Knows(empire, WarringTech) ? 70 : 60) ||
               vassals.Any(vassal => vassal.GetKingdomType() != KingdomType.ZhouFeudalism_jun && vassal.countTotalWarriors() > royal);
    }

    public static void EnterSpring(Empire empire, ZhouEraState state)
    {
        if (state.era >= Spring) return;
        state.era = Spring;
        state.era_since = World.world.getCurWorldTime();
        state.vassals_at_spring = Vassals(empire).Count();
        state.annexed = 0;
        foreach (Kingdom vassal in Vassals(empire))
            if (vassal.GetKingdomType() != KingdomType.ZhouFeudalism_jun)
                vassal.GetRegime()?.SetAllowDiplomacy(true);
        Log(empire, LM.Get("zhou_log_spring"));
        string spring = string.Format(LM.Get("zhou_history_spring"), empire.GetEmpireName());
        empire.RecordHistory(directContent: spring, kingdomId: empire.CoreKingdom.id);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventOnly(spring, empire.CoreKingdom);
        HoldConference(empire, state, null);
    }

    public static void EnterWarring(Empire empire, ZhouEraState state)
    {
        if (state.era >= Warring) return;
        state.era = Warring;
        state.era_since = World.world.getCurWorldTime();
        Detach(empire);
        if (state.hegemon_id >= 0) Log(empire, LM.Get("zhou_log_hegemony_ends"));
        state.hegemon_id = -1L;
        Log(empire, LM.Get("zhou_log_warring"));
        string warring = string.Format(LM.Get("zhou_history_warring"), empire.GetEmpireName());
        empire.RecordHistory(directContent: warring, kingdomId: empire.CoreKingdom.id);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventOnly(warring, empire.CoreKingdom);
    }

    #endregion

    #region 春秋：霸主、卿大夫、僭越

    private static void SpringYear(Empire empire, ZhouEraState state)
    {
        Kingdom hegemon = World.world.kingdoms.get(state.hegemon_id);
        bool valid = hegemon != null && !hegemon.isRekt() && SphereOf(hegemon) == empire &&
                     !hegemon.isInWarWith(empire.CoreKingdom) && !state.kings.Contains(hegemon.id) &&
                     Power(hegemon) >= 0.7f * Vassals(empire).Where(other => other != hegemon).Select(Power).DefaultIfEmpty(0).Max();
        if (!valid)
        {
            if (hegemon != null) Announce(empire, string.Format(LM.Get("zhou_log_hegemon_lost"), hegemon.GetKingdomName()), hegemon);
            state.hegemon_id = -1L;
            HoldConference(empire, state, null);
        }
        else if (Date.getYearsSince(state.hegemon_since) >= ConferenceYears)
            HoldConference(empire, state, null);
        hegemon = World.world.kingdoms.get(state.hegemon_id);
        if (hegemon != null)
        {
            CollectTribute(empire, state, hegemon);
            Arbitrate(empire, state, hegemon);
            PunishUsurper(empire, state, hegemon);
            PunishDefiant(empire, state, hegemon);
        }
        foreach (Kingdom vassal in Vassals(empire).ToList())
        {
            if (TryPartition(empire, state, vassal)) break;
            TryUsurpation(empire, vassal);
            TryProclaimKing(empire, state, vassal, spring: true);
        }
    }

    // 会盟：最强且尊王的诸侯为霸主；chosen 不为空时由天子直接赐胙
    public static Kingdom HoldConference(Empire empire, ZhouEraState state, Kingdom chosen)
    {
        if (state.era != Spring) return null;
        Kingdom hegemon = chosen ?? Vassals(empire)
            .Where(kingdom => !kingdom.isInWarWith(empire.CoreKingdom) && !state.kings.Contains(kingdom.id) &&
                              kingdom.GetKingdomType() != KingdomType.ZhouFeudalism_jun)
            .OrderByDescending(Power).FirstOrDefault();
        if (hegemon == null) return null;
        state.conferences++;
        bool renewed = state.hegemon_id == hegemon.id;
        state.hegemon_id = hegemon.id;
        state.hegemon_since = World.world.getCurWorldTime();
        empire.AddMandate(3);
        string place = hegemon.capital?.GetCityName() ?? hegemon.GetKingdomName();
        Announce(empire, string.Format(LM.Get(renewed ? "zhou_log_conference_renew" : "zhou_log_conference"),
            hegemon.GetKingdomName(), place), hegemon);
        if (!renewed)
            empire.RecordHistory(directContent: string.Format(LM.Get("zhou_history_hegemon"), hegemon.GetKingdomName(), place),
                kingdomId: hegemon.id);
        return hegemon;
    }

    // 一年的赋税收入(按国库账目折算；没有账目时按国库的十分之一估)
    private static int YearlyRevenue(Kingdom kingdom)
    {
        TreasuryReport report = TreasurySystem.Report(kingdom);
        if (report != null && report.months > 0 && report.income > 0)
            return (int)Math.Min(int.MaxValue, report.income * 12L / report.months);
        return Mathf.Max(0, kingdom.GetMoney()) / 10;
    }

    // 纳贡：诸侯每年以赋税一成献给霸主；国库交不出、或与霸主交恶不肯交的，记为不朝
    private static void CollectTribute(Empire empire, ZhouEraState state, Kingdom hegemon)
    {
        int total = 0;
        state.defiant.Clear();
        foreach (Kingdom vassal in Vassals(empire).Where(vassal => vassal != hegemon))
        {
            if (vassal.isInWarWith(hegemon) || state.kings.Contains(vassal.id) ||
                !HasZuo(state, vassal) && (World.world.diplomacy?.getOpinion(vassal, hegemon)?.total ?? 0) < -20)
            {
                state.defiant.Add(vassal.id);
                continue;
            }
            int tribute = YearlyRevenue(vassal) / 10;
            if (tribute <= 0) continue;
            if (vassal.GetMoney() < tribute)
            {
                state.defiant.Add(vassal.id);
                continue;
            }
            vassal.SubMoney(tribute, TreasuryCategory.Tribute);
            total += tribute;
        }
        if (total > 0) hegemon.AddMoney(total, TreasuryCategory.Tribute);
    }

    // 讨不庭：霸主讨伐接壤的不朝之国(比它弱的才动手)
    private static void PunishDefiant(Empire empire, ZhouEraState state, Kingdom hegemon)
    {
        foreach (long id in state.defiant.ToList())
        {
            Kingdom defiant = World.world.kingdoms.get(id);
            if (defiant == null || defiant.isRekt() || defiant == hegemon || hegemon.isInWarWith(defiant) ||
                !hegemon.IsNeighbourWith(defiant) || Power(defiant) * 1.2f > Power(hegemon) || Random.value > 0.3f)
                continue;
            War war = World.world.diplomacy.startWar(hegemon, defiant, WarTypeLibrary.normal);
            if (war == null) continue;
            Announce(empire, string.Format(LM.Get("zhou_log_punish_defiant"), hegemon.GetKingdomName(), defiant.GetKingdomName()),
                hegemon, history: true);
            return;
        }
    }

    // 霸主调停：诸侯互攻，霸主令其罢兵(存亡继绝：弱小的一方被打时更可能出面)
    private static void Arbitrate(Empire empire, ZhouEraState state, Kingdom hegemon)
    {
        foreach (Kingdom vassal in Vassals(empire).ToList())
            foreach (War war in vassal.getWars().ToList())
            {
                if (war == null || war.hasEnded() || !war.isAttacker(vassal)) continue;
                Kingdom victim = war.main_defender;
                if (victim == null || SphereOf(victim) != empire || victim == empire.CoreKingdom) continue;
                if (vassal == hegemon || victim == hegemon || state.kings.Contains(victim.id)) continue;
                float chance = Power(victim) * 2 < Power(vassal) ? 0.5f : 0.25f;
                if (Random.value > chance) continue;
                vassal.EndWarWith(victim);
                Announce(empire, string.Format(LM.Get("zhou_log_arbitrate"), hegemon.GetKingdomName(), vassal.GetKingdomName(),
                    victim.GetKingdomName()), hegemon);
            }
    }

    // 霸主讨伐僭越称王者(同文化之国，记为伐不臣，不是攘夷)
    private static void PunishUsurper(Empire empire, ZhouEraState state, Kingdom hegemon)
    {
        foreach (long id in state.kings.ToList())
        {
            Kingdom usurper = World.world.kingdoms.get(id);
            if (usurper == null || usurper.isRekt() || usurper == hegemon || hegemon.isInWarWith(usurper) ||
                !hegemon.IsNeighbourWith(usurper) || Random.value > 0.25f) continue;
            War war = World.world.diplomacy.startWar(hegemon, usurper, WarTypeLibrary.normal);
            if (war == null) continue;
            war.SetEmpireWarType(EmpireCraft.Scripts.Enums.EmpireWarType.伐不臣);
            war.data.name = string.Format(LM.Get("zhou_punish_war_name"), hegemon.GetKingdomName(), usurper.GetKingdomName());
            Announce(empire, string.Format(LM.Get("zhou_log_punish"), hegemon.GetKingdomName(), usurper.GetKingdomName()),
                hegemon, history: true);
            return;
        }
    }

    // 三家分晋：六城以上的大国，国君年幼或年老时更易被权臣瓜分为三
    private static bool TryPartition(Empire empire, ZhouEraState state, Kingdom vassal)
    {
        List<City> cities = vassal.cities?.Where(city => city != null && !city.isRekt()).ToList();
        if (cities == null || cities.Count < 6 || vassal.capital == null) return false;
        Actor ruler = vassal.king;
        float chance = 0.02f + (ruler == null || !ruler.isAdult() || ruler.getAge() > 60 ? 0.02f : 0f);
        if (Random.value > chance) return false;
        return Partition(empire, state, vassal, cities);
    }

    public static bool Partition(Empire empire, ZhouEraState state, Kingdom vassal, List<City> cities = null)
    {
        cities ??= vassal.cities?.Where(city => city != null && !city.isRekt()).ToList() ?? new List<City>();
        City capital = vassal.capital;
        if (capital == null || cities.Count < 3) return false;
        WorldTile Tile(City city) => city.getTile();
        float Distance(City a, City b) => Tile(a) == null || Tile(b) == null ? 0f : Toolbox.SquaredDistTile(Tile(a), Tile(b));
        City second = cities.Where(city => city != capital).OrderByDescending(city => Distance(city, capital)).First();
        City third = cities.Where(city => city != capital && city != second)
            .OrderByDescending(city => Mathf.Min(Distance(city, capital), Distance(city, second))).First();
        var seeds = new[] { capital, second, third };
        var groups = seeds.ToDictionary(seed => seed, _ => new List<City>());
        foreach (City city in cities)
            groups[seeds.OrderBy(seed => Distance(city, seed)).First()].Add(city);
        string name = vassal.GetKingdomName();
        var heirs = new List<string>();
        foreach (City seed in new[] { second, third })
        {
            Actor minister = seed.leader ?? seed.units?.FirstOrDefault(actor => actor != null && actor.isAlive() &&
                                                                                actor.isAdult() && actor != vassal.king);
            if (minister == null) continue;
            Kingdom split = seed.makeOwnKingdom(minister);
            if (split == null) continue;
            foreach (City city in groups[seed])
                if (city != seed && city.kingdom == vassal) city.joinAnotherKingdom(split);
            split.SetRegimeType(RegimeType.ZhouFeudalism);
            split.LoadRegime();
            split.GetRegime()?.SetAllowDiplomacy(true);
            split.GetRegime()?.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            empire.join(split, pForce: true);
            heirs.Add(split.GetKingdomName());
        }
        if (heirs.Count == 0) return false;
        state.partitioned = true;
        Announce(empire, string.Format(LM.Get("zhou_log_partition"), name, string.Join("、", heirs)), vassal);
        empire.RecordHistory(directContent: string.Format(LM.Get("zhou_history_partition"), name, string.Join("、", heirs)),
            kingdomId: vassal.id);
        return true;
    }

    // 田氏代齐：四城以上的大国，权臣(最大的非都城城主)篡夺君位
    private static void TryUsurpation(Empire empire, Kingdom vassal)
    {
        if ((vassal.cities?.Count ?? 0) < 4 || vassal.capital == null || Random.value > 0.015f) return;
        City seat = vassal.cities.Where(city => city != null && !city.isRekt() && city != vassal.capital && city.leader != null)
            .OrderByDescending(city => city.units?.Count ?? 0).FirstOrDefault();
        Actor usurper = seat?.leader;
        Actor ruler = vassal.king;
        if (usurper == null || usurper.isRekt() || !usurper.isAlive() || ruler == null ||
            SpecificClanManager.SameLineage(usurper.GetSpecificClan(), ruler.GetSpecificClan())) return;
        seat.removeLeader();
        vassal.setKing(usurper);
        usurper.setCity(vassal.capital);
        Announce(empire, string.Format(LM.Get("zhou_log_usurp"), usurper.getName(), vassal.GetKingdomName()), vassal, history: true);
    }

    // 称王：春秋时子爵的夷狄大国僭越称王；战国时三城以上的大国相继称王
    private static void TryProclaimKing(Empire empire, ZhouEraState state, Kingdom vassal, bool spring)
    {
        if (state.kings.Contains(vassal.id) || vassal.GetKingdomType() == KingdomType.ZhouFeudalism_jun) return;
        if (spring)
        {
            if (vassal.GetKingdomType() != KingdomType.ZhouFeudalism_zi || HasZuo(state, vassal)) return;
            int strongest = Vassals(empire).Select(Power).DefaultIfEmpty(0).Max();
            if (Power(vassal) < 0.6f * strongest || Random.value > 0.03f) return;
            ProclaimKing(empire, state, vassal, usurp: true);
        }
        else
        {
            if ((vassal.cities?.Count ?? 0) < 3 || Random.value > 0.35f) return;
            ProclaimKing(empire, state, vassal, usurp: false);
        }
    }

    public static void ProclaimKing(Empire empire, ZhouEraState state, Kingdom vassal, bool usurp)
    {
        if (state.kings.Contains(vassal.id)) return;
        state.kings.Add(vassal.id);
        if (usurp) empire.AddMandate(-10);
        Announce(empire, string.Format(LM.Get(usurp ? "zhou_log_usurp_king" : "zhou_log_proclaim_king"),
            vassal.GetKingdomName()), vassal, history: true);
        if (state.hegemon_id == vassal.id) state.hegemon_id = -1L;
    }

    #endregion

    #region 战国：称王、变法、合纵、统一

    private static void WarringYear(Empire empire, ZhouEraState state)
    {
        foreach (Kingdom vassal in Vassals(empire).ToList())
        {
            TryProclaimKing(empire, state, vassal, spring: false);
            if (!state.reforms.Contains(vassal.id) && (vassal.cities?.Count ?? 0) >= 3 && Random.value < 0.06f)
            {
                state.reforms.Add(vassal.id);
                empire.AddMandate(-1);
                // 旧贵族怨望
                ClaimPolicySystem.AddGrievance(empire, SocialClass.Noble, 5f);
                Announce(empire, string.Format(LM.Get("zhou_log_reform"), vassal.GetKingdomName()), vassal, history: true);
            }
            // 变法之国赋税每年多收一成
            if (state.reforms.Contains(vassal.id))
            {
                int bonus = YearlyRevenue(vassal) / 10;
                if (bonus > 0) vassal.AddMoney(bonus, TreasuryCategory.ResidentTax);
            }
        }
        UpdateCoalition(empire, state);
        int wars = 0;
        foreach (Kingdom vassal in Vassals(empire).OrderByDescending(Power).ToList())
            if (wars < ConquestsPerYear && TryConquest(empire, state, vassal)) wars++;
        foreach (Kingdom vassal in Vassals(empire).ToList()) TrySubmit(empire, state, vassal);
    }

    // 战国兼并：称王或变法之国不在打仗时，每年有一定可能向接壤、国力不到自己三分之二的诸侯(或王畿)开战，以吞并为务
    private static bool TryConquest(Empire empire, ZhouEraState state, Kingdom vassal)
    {
        if (vassal?.data == null || vassal.isRekt() || vassal.hasEnemies() ||
            !state.kings.Contains(vassal.id) && !state.reforms.Contains(vassal.id) || Random.value > ConquestChance) return false;
        int power = Power(vassal);
        Kingdom target = Vassals(empire).Append(empire.CoreKingdom)
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom != vassal &&
                              Power(kingdom) * 3 < power * 2 && IsValidVassalWarTarget(vassal, kingdom, out _))
            .OrderBy(Power).FirstOrDefault();
        if (target == null) return false;
        War war = World.world.diplomacy.startWar(vassal, target, WarTypeLibrary.normal);
        if (war == null) return false;
        Log(empire, string.Format(LM.Get("zhou_log_conquest"), vassal.GetKingdomName(), target.GetKingdomName()));
        return true;
    }

    // 一城小国：被国力四倍以上、不在交战的接壤大国(称王之国优先)包围，每年有一定可能举国降服
    private static void TrySubmit(Empire empire, ZhouEraState state, Kingdom small)
    {
        if (small?.data == null || small.isRekt() || small == empire.CoreKingdom || (small.cities?.Count ?? 0) != 1 ||
            small.hasEnemies() || Random.value > SubmitChance) return;
        int power = Power(small);
        Kingdom lord = Vassals(empire)
            .Where(kingdom => kingdom != small && !kingdom.hasEnemies() && kingdom.IsNeighbourWith(small) &&
                              Power(kingdom) >= power * 4)
            .OrderByDescending(kingdom => state.kings.Contains(kingdom.id)).ThenByDescending(Power).FirstOrDefault();
        if (lord == null) return;
        string name = small.GetKingdomName();
        _annexing = true;
        try
        {
            foreach (City city in small.cities.ToList())
                if (city != null && !city.isRekt() && city.kingdom == small) city.joinAnotherKingdom(lord);
        }
        finally
        {
            _annexing = false;
        }
        state.annexed++;
        state.kings.Remove(small.id);
        state.reforms.Remove(small.id);
        Announce(empire, string.Format(LM.Get("zhou_log_submit"), name, lord.GetKingdomName()), lord, history: true);
    }

    private static void UpdateCoalition(Empire empire, ZhouEraState state)
    {
        var realms = Vassals(empire).Append(empire.CoreKingdom).Where(kingdom => kingdom != null).ToList();
        int total = realms.Sum(Power);
        Kingdom strongest = realms.OrderByDescending(Power).FirstOrDefault();
        int second = realms.Where(kingdom => kingdom != strongest).Select(Power).DefaultIfEmpty(0).Max();
        bool dominant = strongest != null && total > 0 && Power(strongest) >= 0.3f * total && Power(strongest) >= 1.5f * second;
        if (!dominant)
        {
            if (state.coalition_target >= 0) Announce(empire, LM.Get("zhou_log_coalition_ends"));
            state.coalition_target = -1L;
            state.coalition.Clear();
            state.horizontal.Clear();
            return;
        }
        bool formed = state.coalition_target != strongest.id;
        if (formed) state.horizontal.Clear();
        state.horizontal.RemoveAll(id => SphereOf(World.world.kingdoms.get(id)) != empire);
        // 连横：与独大之国接壤、国力不到它一成半的弱国，每年有一成可能依附于它(不入合纵，彼此不攻)
        foreach (Kingdom weak in realms.Where(kingdom => kingdom != strongest && kingdom != empire.CoreKingdom &&
                                                         !state.horizontal.Contains(kingdom.id) &&
                                                         Power(kingdom) < 0.15f * Power(strongest) &&
                                                         kingdom.IsNeighbourWith(strongest) &&
                                                         !kingdom.isInWarWith(strongest)).ToList())
        {
            if (Random.value > 0.1f) continue;
            state.horizontal.Add(weak.id);
            Announce(empire, string.Format(LM.Get("zhou_log_horizontal"), weak.GetKingdomName(), strongest.GetKingdomName()), weak);
        }
        var members = realms.Where(kingdom => kingdom != strongest && !state.horizontal.Contains(kingdom.id) &&
                                              Power(kingdom) >= 0.1f * Power(strongest))
            .Select(kingdom => kingdom.id).ToList();
        if (members.Count < 2) return;
        state.coalition_target = strongest.id;
        state.coalition = members;
        if (formed)
            Announce(empire, string.Format(LM.Get("zhou_log_coalition"), members.Count, strongest.GetKingdomName()), strongest,
                history: true);
    }

    // 天子(或代周者)直辖天下一半的城，或直辖三分之一且再无一国及其三分之一：称皇帝，改行郡县
    private static void TryUnify(Empire empire, ZhouEraState state)
    {
        // 天下 = 王畿、郡与诸侯(含战国离开帝国的)
        var realms = Vassals(empire).Append(empire.CoreKingdom).Distinct()
            .Where(kingdom => kingdom?.cities != null && !kingdom.isRekt()).ToList();
        int total = realms.Sum(kingdom => kingdom.cities.Count);
        int core = empire.CoreKingdom?.cities?.Count ?? 0;
        int rival = realms.Where(kingdom => kingdom != empire.CoreKingdom).Select(kingdom => kingdom.cities.Count)
            .DefaultIfEmpty(0).Max();
        if (total <= 0 || core * 2 < total && (core * 3 < total || rival * 3 > core)) return;
        Unify(empire, state);
    }

    public static void Unify(Empire empire, ZhouEraState state)
    {
        Kingdom core = empire.CoreKingdom;
        // 战国离开帝国的诸侯从此各自独立(要靠征伐一统)
        foreach (Kingdom kingdom in Vassals(empire).Where(IsDetachedVassal).ToList())
            kingdom.GetOrCreate().zhou_sphere_id = -1L;
        state.era = Unified;
        RedrawMap();
        state.era_since = World.world.getCurWorldTime();
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            kingdom.SetRegimeType(RegimeType.LvLing);
            kingdom.LoadRegime();
            if (kingdom == core) continue;
            kingdom.GetRegime()?.SetAllowDiplomacy(false);
        }
        // 先在制度树里补上郡县，再按新阶段重建中央官制(秦汉三公九卿)
        InstitutionSystem.SyncEnactedNodeForRegime(empire, RegimeType.LvLing);
        empire.data.centerOffice?.Init(core);
        core.SystemChange();
        Announce(empire, string.Format(LM.Get("zhou_log_unified"), core.GetKingdomName()), core);
        // 秦式统一：同统一战争给开国气象、受天命(用户 2026-10-19 选"以天命为准"；以前取周统一不算统一，没有开国气象)。
        // 要据有所在天下过半才算：周室只是天下一角的小圈子时，取周统一只是改行郡县(用户 2026-10-27 存档：晋取周统一时
        // 只据有汉正统 7%，也受了天命、得了开国气象，半年就失命，华夏的乱世从头算起)
        var unifiedData = core.GetOrCreate();
        unifiedData.unify_since = -1d;
        if (TianxiaSystem.LeadOf(core.capital) is EmpireCore lead)
        {
            (Dictionary<Kingdom, int> held, int total) = TianxiaSystem.Holdings(lead);
            int count = held.TryGetValue(core, out int owned) ? owned : 0;
            if (total > 0 && count * 2 > total)
            {
                unifiedData.unified_at = World.world.getCurWorldTime();
                TianxiaSystem.Grant(lead, core, count, total, TianxiaSystem.Name(lead));
            }
        }
        else unifiedData.unified_at = World.world.getCurWorldTime();
        empire.RecordHistory(directContent: string.Format(LM.Get("zhou_history_unified"), core.GetKingdomName(),
            empire.GetEmpireName()), kingdomId: core.id);
    }

    #endregion

    #region 天子：赐胙、号召攘夷

    public const int ZuoYears = 10, ZuoOpinion = 40, RepelCooldownYears = 5;
    private const float AutoZuoChance = 0.12f;

    public static bool HasZuo(ZhouEraState state, Kingdom vassal) =>
        state?.zuo != null && vassal != null && state.zuo.TryGetValue(vassal.id, out double at) &&
        World.world != null && Date.getYearsSince(at) < ZuoYears;

    // 一年赋税的二十分之一，至少 30
    public static int ZuoCost(Empire empire) => Math.Max(30, YearlyRevenue(empire.CoreKingdom) / 20);

    public static bool CanGrantZuo(Empire empire, Kingdom vassal)
    {
        if (!Applies(empire) || vassal?.data == null || vassal.isRekt()) return false;
        ZhouEraState state = State(empire);
        Kingdom core = empire.CoreKingdom;
        return state.era <= Spring && vassal != core && SphereOf(vassal) == empire && !HasZuo(state, vassal) &&
               !state.kings.Contains(vassal.id) && !vassal.isInWarWith(core) && core.GetMoney() >= ZuoCost(empire);
    }

    public static bool GrantZuo(Empire empire, Kingdom vassal)
    {
        if (!CanGrantZuo(empire, vassal)) return false;
        Kingdom core = empire.CoreKingdom;
        if (!TreasurySystem.TrySpend(core, ZuoCost(empire), TreasuryCategory.Rites, discretionary: false)) return false;
        ZhouEraState state = State(empire);
        state.zuo ??= new Dictionary<long, double>();
        state.zuo.Keys.Where(id => SphereOf(World.world.kingdoms.get(id)) != empire).ToList().ForEach(id => state.zuo.Remove(id));
        state.zuo[vassal.id] = World.world.getCurWorldTime();
        state.defiant.Remove(vassal.id);
        empire.AddMandate(1);
        Announce(empire, string.Format(LM.Get("zhou_log_zuo"), core.GetKingdomName(), vassal.GetKingdomName()), vassal,
            history: true);
        return true;
    }

    // 进宗法分封时长期共事、按资历受封为侯的部落当即受胙(见 TribalFissionService.Enfeoff；用户 2026-10-26 选"爵位按资历")：
    // 不花国库、不加天命，十年内尊王，同天子赐胙
    public static void GrantFoundingZuo(Empire empire, List<Kingdom> vassals)
    {
        if (!Applies(empire) || vassals == null) return;
        ZhouEraState state = State(empire);
        if (state.era > Spring) return;
        state.zuo ??= new Dictionary<long, double>();
        double now = World.world.getCurWorldTime();
        var names = new List<string>();
        foreach (Kingdom vassal in vassals)
        {
            if (vassal?.data == null || vassal.isRekt() || SphereOf(vassal) != empire || vassal == empire.CoreKingdom) continue;
            state.zuo[vassal.id] = now;
            state.defiant.Remove(vassal.id);
            names.Add(vassal.GetKingdomName());
        }
        if (names.Count == 0) return;
        Announce(empire, string.Format(LM.Get("zhou_log_founding_zuo"), empire.CoreKingdom.GetKingdomName(), string.Join("、", names)),
            history: true);
    }

    // 天子每年有一成二的机会赐胙于霸主或最强的诸侯(国库至少留三倍)
    private static void AutoZuo(Empire empire, ZhouEraState state)
    {
        if (state.era > Spring || Random.value > AutoZuoChance) return;
        Kingdom pick = Vassals(empire)
            .Where(vassal => vassal.GetKingdomType() != KingdomType.ZhouFeudalism_jun && CanGrantZuo(empire, vassal))
            .OrderByDescending(vassal => vassal.id == state.hegemon_id).ThenByDescending(Power).FirstOrDefault();
        if (pick != null && empire.CoreKingdom.GetMoney() >= ZuoCost(empire) * 3) GrantZuo(empire, pick);
    }

    // 受胙之国对王室的好感(见 EmpireCraftOpinionAddition)
    public static int ZuoOpinionOf(Kingdom main, Kingdom target)
    {
        if (main?.data == null || target?.data == null || main == target) return 0;
        Empire empire = SphereOf(main);
        return empire != null && target == empire.CoreKingdom && HasZuo(State(empire), main) ? ZuoOpinion : 0;
    }

    // 天下大势面板的"四夷"：正在侵犯诸侯或王畿的夷狄(victim 为被犯之国)在前，与天下接壤的夷狄在后
    public static List<(Kingdom barbarian, Kingdom victim)> RepelTargets(Empire empire)
    {
        var result = new List<(Kingdom, Kingdom)>();
        if (!Applies(empire) || State(empire).era > Spring) return result;
        var seen = new HashSet<long>();
        foreach (Kingdom member in Vassals(empire).Append(empire.CoreKingdom).ToList())
            foreach (War war in member?.getWars()?.ToList() ?? new List<War>())
            {
                if (war == null || war.hasEnded() || !war.isDefender(member)) continue;
                Kingdom attacker = war.main_attacker;
                if (!IsBarbarian(empire, attacker) || !seen.Add(attacker.id)) continue;
                result.Add((attacker, member));
            }
        foreach (Kingdom kingdom in World.world.kingdoms.list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
                     kingdom.isCiv() && !seen.Contains(kingdom.id) && IsBarbarian(empire, kingdom) &&
                     empire.IsNeighbourWith(kingdom)).OrderByDescending(Power).ToList())
            result.Add((kingdom, null));
        return result;
    }

    // 兴师讨伐接壤的夷狄五年一次(援救被犯之国不限)
    public static int RepelCooldown(Empire empire)
    {
        ZhouEraState state = State(empire);
        return state == null || state.repel_called_at < 0d ? 0
            : Math.Max(0, RepelCooldownYears - Date.getYearsSince(state.repel_called_at));
    }

    public static bool CallToRepel(Empire empire, Kingdom barbarian)
    {
        if (!Applies(empire) || !IsBarbarian(empire, barbarian)) return false;
        ZhouEraState state = State(empire);
        if (state.era > Spring) return false;
        Kingdom core = empire.CoreKingdom;
        var helpers = Loyalists(empire, state, barbarian).Where(kingdom => kingdom.IsNeighbourWith(barbarian)).Take(3).ToList();
        if (World.world.kingdoms.get(state.hegemon_id) is Kingdom hegemon && !hegemon.isRekt() && SphereOf(hegemon) == empire &&
            !helpers.Contains(hegemon) && !hegemon.isInWarWith(core))
            helpers.Insert(0, hegemon);
        var wars = Vassals(empire).Append(core).SelectMany(member => member.getWars())
            .Where(war => war != null && !war.hasEnded() && war.main_attacker == barbarian && SphereOf(war.main_defender) == empire)
            .Distinct().ToList();
        if (wars.Count > 0)
        {
            var victims = new List<string>();
            foreach (War war in wars)
            {
                JoinSide(war, core, attackerSide: false);
                foreach (Kingdom helper in helpers)
                    if (helper != war.main_defender) JoinSide(war, helper, attackerSide: false);
                war.data.name = string.Format(LM.Get("zhou_repel_war_name"), war.main_defender.GetKingdomName(),
                    barbarian.GetKingdomName());
                victims.Add(war.main_defender.GetKingdomName());
            }
            empire.AddMandate(2);
            Announce(empire, string.Format(LM.Get("zhou_log_call_repel"), core.GetKingdomName(), Helpers(helpers),
                string.Join("、", victims.Distinct()), barbarian.GetKingdomName()), core, history: true);
            return true;
        }
        if (RepelCooldown(empire) > 0 || core.isInWarWith(barbarian) || !empire.IsNeighbourWith(barbarian)) return false;
        War campaign = World.world.diplomacy.startWar(core, barbarian, WarTypeLibrary.normal);
        if (campaign == null) return false;
        campaign.SetEmpireWarType(EmpireWarType.攘夷);
        campaign.data.name = string.Format(LM.Get("zhou_campaign_war_name"), core.GetKingdomName(), barbarian.GetKingdomName());
        foreach (Kingdom helper in helpers) JoinSide(campaign, helper, attackerSide: true);
        state.repel_called_at = World.world.getCurWorldTime();
        empire.AddMandate(1);
        Announce(empire, string.Format(LM.Get("zhou_log_campaign"), core.GetKingdomName(), Helpers(helpers),
            barbarian.GetKingdomName()), core, history: true);
        return true;
    }

    private static string Helpers(List<Kingdom> helpers) =>
        helpers.Count == 0 ? LM.Get("zhou_helpers_none") : string.Join("、", helpers.Select(kingdom => kingdom.GetKingdomName()));

    #endregion

    #region 神力

    // 推进时代：西周 → 春秋 → 战国 → 统一(由天下最强者统一，诸侯最强时先取周而代之)
    public static void GodAdvance(Empire empire)
    {
        if (!Applies(empire)) return;
        ZhouEraState state = State(empire);
        switch (state.era)
        {
            case WesternZhou:
                EnterSpring(empire, state);
                break;
            case Spring:
                EnterWarring(empire, state);
                break;
            case Warring:
                Kingdom strongest = Vassals(empire).OrderByDescending(Power).FirstOrDefault();
                if (strongest != null && Power(strongest) > Power(empire.CoreKingdom))
                {
                    City royal = empire.CoreKingdom.capital;
                    OnRoyalCapitalTaken(royal, empire.CoreKingdom, strongest);
                    empire = SphereOf(strongest);
                    if (!Applies(empire)) return;
                }
                Unify(empire, State(empire));
                break;
        }
    }

    #endregion

    // 大事：记进列国纪事，并弹世界提示；history 为真时连史册一起记(史册另有写法的只弹提示)
    private static void Announce(Empire empire, string text, Kingdom kingdom = null, bool history = false)
    {
        Log(empire, text);
        kingdom ??= empire?.CoreKingdom;
        if (history) EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(text, kingdom);
        else EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventOnly(text, kingdom);
    }

    public static void Log(Empire empire, string text)
    {
        ZhouEraState state = State(empire);
        if (state == null || string.IsNullOrEmpty(text)) return;
        state.log.Insert(0, new CourtLogEntry { time = World.world?.getCurWorldTime() ?? 0d, text = text });
        if (state.log.Count > LogSize) state.log.RemoveRange(LogSize, state.log.Count - LogSize);
    }
}

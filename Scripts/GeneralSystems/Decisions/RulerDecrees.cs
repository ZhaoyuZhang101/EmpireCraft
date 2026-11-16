using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems.Decisions;

// 君主的大政(用户 2026-11-17 "希望给玩家迁都和设置多个首都和直隶的功能""希望玩家也能够改制：比如内阁制度，比如分封制度比如行省，
// 又比如郡县""但是这些需要皇帝权威很高""要走决议天平")：
//   迁都、设陪京、撤陪京、设直隶，改制四项——设内阁、行分封、行郡县、划行省。
// 都是决议天平上的决议(见 DecisionSystem、DecisionScale)，同别的决议一样立项、每月推进、办满了才施行；不一样的地方：
//   · 只由人亲自发起(手机席位的「可议之事」、帝国窗口与王国国务窗的「可议之事」)，天平自己过秤时不替君主发起——
//     各国自己的迁都、设京、改制仍走原来的路(还于旧都、多京的年度安排、朝议里的诉求)；
//   · 要权威够高(帝国看正统、王国看君权，见 Authority)：不到门槛的列在「可议之事」里、标"权威不足"，发不起；
//   · 要天平压得过去(读数 ≥ 1)：权威比门槛高出多少是推力，事体越大祖制的阻力越重(见 DecisionScale.Measure)；
//   · 迁都、设陪京、撤陪京、设直隶要点一座城：每座可选的城各列一条(见 DecisionSystem.AdviseFor、Candidates)。
// 各项怎么施行：
//   · 迁都：帝国中央照神力「营宫殿」的办法(改京师记录、旧都记作前都、行政区随都城重划)；别的国把都城迁过去并锚定
//     (KingdomExtraData.decreed_capital，主封号同步不把它迁回封号首府)。都记工役，算大兴土木；
//   · 设陪京、撤陪京、设直隶：见 MultiCapitalSystem.Decreed；
//   · 设内阁、行郡县：推制度树上对应的节点(同朝议里「设立内阁」「转天朝制度」两条诉求推的是同一个节点)，节点办成才真的改；
//   · 行分封：华夏的帝国改行周制分封(同朝议里「转周制」)，上回改制要过了 EnfeoffCooldownYears 年；
//   · 划行省：按法理把直辖的地重划成省、遣流官(现成的帝国改划，用户 2026-11-17 问答选"按法理重划成省")。
public static class RulerDecrees
{
    public const string MoveCapital = "ruler_move_capital", AddJing = "ruler_add_jing", RemoveJing = "ruler_remove_jing",
        SetZhili = "ruler_set_zhili", ReformCabinet = "ruler_reform_cabinet", ReformEnfeoff = "ruler_reform_enfeoff",
        ReformPrefecture = "ruler_reform_prefecture", ReformProvince = "ruler_reform_province";

    // 权威的门槛(0~100)
    public const int MoveCapitalAuthority = 60, JingAuthority = 65, ZhiliAuthority = 70, ReformAuthority = 75;
    // 上回改制过了这么多年才能再行分封
    public const int EnfeoffCooldownYears = 20;
    // 一回至多列这么多座可选的城
    private const int MaxCandidates = 8;

    // 各项的权威门槛、祖制的阻力(天平右盘的倍数)、要不要点一座城、用度(金；个人的钱不够由国库可支配的部分出，见 DecisionSystem.Pay)
    private static readonly Dictionary<string, (int authority, float gravity, bool targets, int cost)> Rules = new()
    {
        [MoveCapital] = (MoveCapitalAuthority, 1.3f, true, 200), [AddJing] = (JingAuthority, 1.15f, true, 120), [RemoveJing] = (JingAuthority, 1f, true, 0),
        [SetZhili] = (ZhiliAuthority, 1.25f, true, 80), [ReformCabinet] = (ReformAuthority, 1.5f, false, 100),
        [ReformEnfeoff] = (ReformAuthority, 1.6f, false, 100), [ReformPrefecture] = (ReformAuthority, 1.6f, false, 100),
        [ReformProvince] = (ReformAuthority, 1.4f, false, 100)
    };

    // 排查用(独立核心的 --try-decrees force，见 .server/Server/DecreeProbe.cs)：不看权威门槛，好让各项的施行在真的世界数据上走一遍。游戏里恒为 false
    internal static bool ProbeIgnoresAuthority;

    public static bool IsManual(string id) => id != null && Rules.ContainsKey(id);
    public static int MinAuthority(string id) => id != null && Rules.TryGetValue(id, out var rule) ? rule.authority : 0;
    public static float Gravity(string id) => id != null && Rules.TryGetValue(id, out var rule) ? rule.gravity : 1f;
    public static bool HasTargets(string id) => id != null && Rules.TryGetValue(id, out var rule) && rule.targets;

    // 权威(0~100)：帝国中央看正统，别的国看君权(同决议天平、制度门槛一个读法)
    public static int Authority(Kingdom realm)
    {
        if (realm?.data == null || realm.isRekt()) return 0;
        Empire empire = realm.GetEmpire();
        return empire != null && empire.CoreKingdom == realm ? empire.Legitimacy : RealmAuthority.Get(realm);
    }

    private static bool Ruler(Actor actor) =>
        actor?.data != null && actor.isAlive() && actor.isKing() && actor.kingdom?.data != null && !actor.kingdom.isRekt();

    // 皇帝本人坐镇的帝国(不是帝国中央的君主返回空)
    private static Empire Court(Actor actor)
    {
        if (!Ruler(actor)) return null;
        Empire empire = actor.kingdom.GetEmpire();
        return empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom == actor.kingdom ? empire : null;
    }

    private static bool Enough(Actor actor, string id) =>
        Ruler(actor) && (ProbeIgnoresAuthority || Authority(actor.kingdom) >= MinAuthority(id));

    // 办不成的缘由(文字键)：权威不够的单说，其余都是"条件不合"
    public static string WhyNot(string id, Actor actor) =>
        Ruler(actor) && Authority(actor.kingdom) < MinAuthority(id) ? "decision_outcome_authority" : "decision_outcome_unfit";

    // ───────── 可选的城 ─────────
    public static List<City> Candidates(string id, Actor actor)
    {
        if (!Ruler(actor)) return new List<City>();
        Empire court = Court(actor);
        switch (id)
        {
            case MoveCapital: return CapitalCandidates(actor.kingdom);
            case AddJing: return court == null ? new List<City>() : MultiCapitalSystem.DecreeCandidates(court, MaxCandidates);
            case RemoveJing: return court == null ? new List<City>() : MultiCapitalSystem.DecreedCities(court);
            case SetZhili: return court == null ? new List<City>() : MultiCapitalSystem.ZhiliCandidates(court);
            default: return new List<City>();
        }
    }

    // 迁都可去的城：本国直辖的城，不是眼下的都城，没被占着、不在代管里；人口多的在前
    private static List<City> CapitalCandidates(Kingdom realm)
    {
        var result = new List<City>();
        if (realm?.cities == null || realm.capital == null) return result;
        foreach (City city in realm.cities)
        {
            if (city?.data == null || city.isRekt() || city.kingdom != realm || city == realm.capital) continue;
            if (city.GetOrCreate().OccupiedStatus?.Count > 0 || WartimeCustodySystem.InCustody(city)) continue;
            result.Add(city);
        }
        return result.OrderByDescending(city => CityPopulationSystem.GetTotal(city)).ThenBy(city => city.data.id).Take(MaxCandidates).ToList();
    }

    private static bool CanMoveCapital(Actor actor)
    {
        if (!Enough(actor, MoveCapital)) return false;
        Empire court = Court(actor);
        // 选帝制的帝都随当选的家族走(见 HolyRomanSystem)；周室的王畿另有一套(见 ZhouEraSystem)
        if (court != null && (HolyRomanSystem.IsElective(court) || ZhouEraSystem.RoyalReclaimBlocked(court))) return false;
        return CapitalCandidates(actor.kingdom).Count > 0;
    }

    // ───────── 立项：把点的那座城记到决议上 ─────────
    private static bool StartWithCity(Actor actor, PlotAsset asset, bool forced)
    {
        City target = DecisionSystem.PendingTarget;
        if (target?.data == null || actor == null || actor.isRekt() || World.world?.plots == null || actor.plot?.isActive() == true) return false;
        Plot plot = World.world.plots.newPlot(actor, asset, forced);
        if (plot == null) return false;
        plot.target_city = target;
        return true;
    }

    private static bool StartPlain(Actor actor, PlotAsset asset, bool forced)
    {
        if (actor == null || actor.isRekt() || World.world?.plots == null || actor.plot?.isActive() == true) return false;
        return World.world.plots.newPlot(actor, asset, forced) != null;
    }

    private static City TargetOf(Actor actor) => actor?.plot?.target_city;

    // 办着的时候点的那座城还得在可选的里头(城丢了、被占了就作罢)
    private static bool TargetStillFits(string id, Actor actor)
    {
        City target = TargetOf(actor);
        return target?.data != null && !target.isRekt() && Candidates(id, actor).Contains(target);
    }

    // ───────── 施行 ─────────
    private static bool DoMoveCapital(Actor actor)
    {
        City target = TargetOf(actor);
        Kingdom realm = actor?.kingdom;
        if (!CanMoveCapital(actor) || target?.data == null || target.isRekt() || target.kingdom != realm || target == realm.capital) return false;
        City old = realm.capital;
        Empire court = Court(actor);
        EmpireCore core = court == null ? null : EmpireCoreManager.Get(court);
        if (core != null)
        {
            // 帝国中央：京师的记录跟着改(不改的话下个月"还于旧都"又迁回去)，旧都记作前都(多京里排头一个陪京)
            if (old != null && old != target) core.former_capital = old.data.id;
            else if (core.former_capital == target.data.id) core.former_capital = -1L;
            core.SetCoreCapital(target);
        }
        realm.GetOrCreate().decreed_capital = target.data.id;
        realm.setCapital(target);
        if (realm.capital != target) return false;
        if (court != null)
        {
            CoreCapitalRestorationSystem.AlignAdministration(court, realm, target);
            MultiCapitalSystem.Yearly(court);
        }
        AdministrativeControlSystem.Refresh(realm);
        PureMapPatch.Redraw(target);
        if (old != null) PureMapPatch.Redraw(old);
        // 迁都营建宫室、官署，征发民力：记工役，算大兴土木(见 CorveeSystem)
        CorveeSystem.Add(realm, CorveeSystem.CapitalMove, true);
        if (court != null)
            EventRecorder.Record(court, string.Format(LM.Get("empire_move_capital_history"), court.GetEmpireFullName(), target.GetCityName()), actor);
        else
            EventRecorder.Record(realm, string.Format(LM.Get("empire_move_capital_history"), realm.GetKingdomName(), target.GetCityName()), actor);
        LogService.LogInfo($"[EmpireCraft][大政] {realm.GetKingdomName()} 奉诏迁都 {target.GetCityName()}(旧都 {old?.GetCityName()})");
        return true;
    }

    private static bool DoJing(Actor actor, string id)
    {
        Empire court = Court(actor);
        City target = TargetOf(actor);
        if (court == null || target?.data == null || !Enough(actor, id)) return false;
        bool done = id == AddJing ? MultiCapitalSystem.DecreeJing(court, target)
            : id == RemoveJing ? MultiCapitalSystem.RevokeJing(court, target)
            : MultiCapitalSystem.DecreeZhili(court, target);
        if (done) LogService.LogInfo($"[EmpireCraft][大政] {court.GetEmpireFullName()} 奉诏 {id} {target.GetCityName()}");
        return done;
    }

    // 制度树上声明了这条诉求的节点(同朝议里那条诉求推的是同一个)
    private static InstitutionNodeConfig ReformNode(Empire court, string claim, TemporaryFactionType? push)
    {
        if (court == null) return null;
        InstitutionNodeConfig node = InstitutionSystem.FindClaimReformTarget(court, claim);
        return node ?? (push.HasValue ? ClaimRules.FindPushTarget(court, push.Value) : null);
    }

    private static bool NodeReady(Empire court, InstitutionNodeConfig node, out bool force)
    {
        force = false;
        if (court == null || node == null) return false;
        if (InstitutionSystem.CanStartReform(court, node, out _, false)) return true;
        force = InstitutionSystem.CanStartReform(court, node, out _, true);
        return force;
    }

    private static bool CanReformByNode(Actor actor, string id, string claim, TemporaryFactionType? push)
    {
        if (!Enough(actor, id)) return false;
        Empire court = Court(actor);
        return NodeReady(court, ReformNode(court, claim, push), out _);
    }

    private static bool DoReformByNode(Actor actor, string id, string claim, TemporaryFactionType? push)
    {
        Empire court = Court(actor);
        InstitutionNodeConfig node = ReformNode(court, claim, push);
        if (!Enough(actor, id) || !NodeReady(court, node, out bool force)) return false;
        if (!InstitutionSystem.StartReform(court, node.id, force)) return false;
        LogService.LogInfo($"[EmpireCraft][大政] {court.GetEmpireFullName()} 奉诏改制 {id}：推行 {node.id}{(force ? "(强推)" : "")}");
        return true;
    }

    // 行分封：华夏的帝国、眼下不是周制、上回改制过了 EnfeoffCooldownYears 年、中央没有领土战争
    private static bool CanEnfeoff(Actor actor)
    {
        if (!Enough(actor, ReformEnfeoff)) return false;
        Empire court = Court(actor);
        if (court == null || !RealmNamingSystem.IsHuaxia(court)) return false;
        Kingdom core = court.CoreKingdom;
        return core.GetRegime()?.type is RegimeType type && type != RegimeType.ZhouFeudalism && type != RegimeType.Modern && type != RegimeType.Republic &&
               core.GetSystemChangeYear() >= EnfeoffCooldownYears && !court.HasTerritorialWar();
    }

    private static bool DoEnfeoff(Actor actor)
    {
        if (!CanEnfeoff(actor)) return false;
        Empire court = Court(actor);
        InstitutionSystem.ChangeRegimeForTransition(court, RegimeType.ZhouFeudalism);
        if (court.CoreKingdom.GetRegime()?.type != RegimeType.ZhouFeudalism) return false;
        InstitutionSystem.SyncEnactedNodeForRegime(court, RegimeType.ZhouFeudalism);
        EventRecorder.Record(court, string.Format(LM.Get("ruler_reform_enfeoff_history"), court.GetEmpireFullName()), actor);
        LogService.LogInfo($"[EmpireCraft][大政] {court.GetEmpireFullName()} 奉诏改行分封");
        return true;
    }

    // ───────── 登记 ─────────
    private static void Add(string id, string icon, float progress, Func<Actor, bool> possible, Func<Actor, bool> proceed, Func<Actor, bool> act)
    {
        bool targets = HasTargets(id);
        var asset = new PlotAsset
        {
            id = id, path_icon = "ui/icons/plots/" + icon, group_id = "empirecraft_diplomacy", is_basic_plot = true, min_level = 1,
            progress_needed = progress, can_be_done_by_king = true, money_cost = Rules[id].cost,
            check_is_possible = actor => Guard(id, "条件", actor, possible),
            check_should_continue = actor => Guard(id, "复核", actor, proceed),
            action = actor => Guard(id, "施行", actor, act)
        };
        if (targets)
        {
            asset.check_target_city = true;
            asset.try_to_start_advanced = StartWithCity;
        }
        else asset.try_to_start_advanced = StartPlain;
        AssetManager.plots_library.add(asset);
    }

    private static bool Guard(string id, string stage, Actor actor, Func<Actor, bool> body)
    {
        try
        {
            return body(actor);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][大政] {id} {stage}出错: {exception.Message}");
            return false;
        }
    }

    // 在决议库里登记这八条(见 EmpireCraftPlotsAddition.init)
    public static void Register()
    {
        Add(MoveCapital, "plot_empire_move_back_to_capital", 24f, CanMoveCapital,
            actor => CanMoveCapital(actor) && TargetStillFits(MoveCapital, actor), DoMoveCapital);
        Add(AddJing, "plot_empire_move_back_to_capital", 15f,
            actor => Enough(actor, AddJing) && Candidates(AddJing, actor).Count > 0,
            actor => Enough(actor, AddJing) && TargetStillFits(AddJing, actor), actor => DoJing(actor, AddJing));
        Add(RemoveJing, "plot_empire_move_back_to_capital", 9f,
            actor => Enough(actor, RemoveJing) && Candidates(RemoveJing, actor).Count > 0,
            actor => Enough(actor, RemoveJing) && TargetStillFits(RemoveJing, actor), actor => DoJing(actor, RemoveJing));
        Add(SetZhili, "plot_empire_take_back_title", 18f,
            actor => Enough(actor, SetZhili) && Candidates(SetZhili, actor).Count > 0,
            actor => Enough(actor, SetZhili) && TargetStillFits(SetZhili, actor), actor => DoJing(actor, SetZhili));
        Add(ReformCabinet, "plot_empire_take_back_title", 24f,
            actor => CanReformByNode(actor, ReformCabinet, "设立内阁", null),
            actor => CanReformByNode(actor, ReformCabinet, "设立内阁", null),
            actor => DoReformByNode(actor, ReformCabinet, "设立内阁", null));
        Add(ReformPrefecture, "plot_empire_take_back_title", 30f,
            actor => CanReformByNode(actor, ReformPrefecture, "转天朝制度", TemporaryFactionType.转天朝制度),
            actor => CanReformByNode(actor, ReformPrefecture, "转天朝制度", TemporaryFactionType.转天朝制度),
            actor => DoReformByNode(actor, ReformPrefecture, "转天朝制度", TemporaryFactionType.转天朝制度));
        Add(ReformEnfeoff, "plot_empire_take_back_title", 30f, CanEnfeoff, CanEnfeoff, DoEnfeoff);
        Add(ReformProvince, "plot_empire_take_back_title", 24f,
            actor => Enough(actor, ReformProvince) && AdministrationReorganizationSystem.CanPropose(actor),
            actor => Enough(actor, ReformProvince) && AdministrationReorganizationSystem.CanPropose(actor),
            AdministrationReorganizationSystem.Begin);
    }
}

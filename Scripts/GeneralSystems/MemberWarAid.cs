using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 成员的仗：中央不直接插手行省、诸侯、军府与外敌的战争——
//   · 求援(member_request_aid)：挨打的成员由长官发起决议，决议通过后中央才出兵加入守方；
//     世袭诸侯、藩镇(有自主外交的)还要中央对它有好感才肯出兵；
//   · 驰援邻省(member_aid_neighbour)：与挨打成员接壤的成员也要发起决议才出兵相助；
//   · 中央自己的仗(中央或盟友领头)，听命于中央的成员照旧随征；
//     成员的仗即便中央已出兵，别的成员也不自动跟进(见 FeudalVassalService.ExpandSide、CheckJoinWar)。
public static class MemberWarAid
{
    public const string RequestPlot = "member_request_aid";
    public const string NeighbourPlot = "member_aid_neighbour";
    public const string CenterPlot = "member_aid_center";

    // 某一方领头的是本帝国的非核心成员：这是成员的仗，不是中央的
    public static bool IsMemberWar(War war, Empire empire, bool attackers)
    {
        if (war?.data == null || empire?.CoreKingdom == null) return false;
        Kingdom lead = attackers ? war.getMainAttacker() : war.getMainDefender();
        return lead != null && lead != empire.CoreKingdom && lead.GetEmpire() == empire;
    }

    // 王国 AI 每月一查：长官没在办别的决议时，本国挨打就向中央求援，邻近成员挨打就议驰援
    public static void TryRaise(Kingdom member)
    {
        Actor king = member?.king;
        if (king == null || king.isRekt() || !king.isAlive() || king.plot?.isActive() == true ||
            World.world?.plots == null || Context(member) == null) return;
        string id = FindRequestWar(member) != null ? RequestPlot :
            FindNeighbourWar(member, out _, out _) ? NeighbourPlot :
            FindCenterWar(member) != null ? CenterPlot : null;
        if (id == null) return;
        PlotAsset asset = AssetManager.plots_library.get(id);
        asset?.try_to_start_advanced?.Invoke(king, asset, false);
    }

    public static War FindRequestWar(Kingdom member) =>
        Context(member) == null ? null : member.getWars().FirstOrDefault(war => RequestValid(member, war));

    public static bool FindNeighbourWar(Kingdom member, out War target, out Kingdom ally)
    {
        target = null;
        ally = null;
        Empire empire = Context(member);
        if (empire == null || member.getWars().Any(war => war != null && !war.hasEnded())) return false;
        foreach (Kingdom other in empire.kingdoms_list.ToList())
        {
            War war = other?.getWars().FirstOrDefault(candidate => NeighbourValid(member, candidate, other));
            if (war == null) continue;
            target = war;
            ally = other;
            return true;
        }
        return false;
    }

    // 求援：成员是守方，对方没有本帝国的人(叛乱的除外)，中央没下场、也没就这场仗与对方议和过
    public static bool RequestValid(Kingdom member, War war)
    {
        Empire empire = Context(member);
        if (empire == null || !Aidable(war, member, empire)) return false;
        Kingdom core = empire.CoreKingdom;
        if (war.hasKingdom(core) || EmpirePeacePatch.HasLeft(core, war, joiningAttackers: false)) return false;
        if (core.getWars().Any(other => other.GetEmpireWarType() == EmpireWarType.藩王索取皇位)) return false;
        return EmpirePeacePatch.Obeys(empire, member) || core.isOpinionTowardsKingdomGood(member);
    }

    // 驰援：本成员没在打仗，与挨打的同帝国成员接壤；世袭诸侯、藩镇要对它有好感
    public static bool NeighbourValid(Kingdom member, War war, Kingdom ally)
    {
        Empire empire = Context(member);
        if (empire == null || ally == null || ally == member || ally == empire.CoreKingdom || ally.isRekt() ||
            ally.GetEmpire() != empire || !Aidable(war, ally, empire) || war.hasKingdom(member)) return false;
        if (member.getWars().Any(other => other != null && !other.hasEnded())) return false;
        if (EmpirePeacePatch.HasLeft(member, war, joiningAttackers: false) || !member.IsNeighbourWith(ally)) return false;
        return EmpirePeacePatch.Obeys(empire, member) || member.isOpinionTowardsKingdomGood(ally);
    }

    public static bool StartRequest(Actor actor, PlotAsset asset, bool forced)
    {
        Kingdom member = actor?.kingdom;
        if (member?.king != actor || actor.plot?.isActive() == true) return false;
        War war = FindRequestWar(member);
        if (war == null) return false;
        Plot plot = World.world.plots.newPlot(actor, asset, forced);
        plot.target_war = war;
        plot.target_kingdom = war.getMainAttacker();
        return true;
    }

    public static bool StartNeighbour(Actor actor, PlotAsset asset, bool forced)
    {
        Kingdom member = actor?.kingdom;
        if (member?.king != actor || actor.plot?.isActive() == true) return false;
        if (!FindNeighbourWar(member, out War war, out Kingdom ally)) return false;
        Plot plot = World.world.plots.newPlot(actor, asset, forced);
        plot.target_war = war;
        plot.target_kingdom = ally;
        return true;
    }

    public static bool RequestContinues(Actor actor) =>
        actor?.kingdom?.king == actor && RequestValid(actor.kingdom, actor.plot?.target_war);

    public static bool NeighbourContinues(Actor actor) =>
        actor?.kingdom?.king == actor && NeighbourValid(actor.kingdom, actor.plot?.target_war, actor.plot?.target_kingdom);

    // 求援决议通过：中央加入守方，并为这场仗出征(京师禁军与扩大范围的兵开往成员那里，见 WarScopeSystem)
    public static bool AnswerRequest(Actor actor)
    {
        if (!RequestContinues(actor)) return false;
        Kingdom member = actor.kingdom;
        Empire empire = member.GetEmpire();
        if (!Join(actor.plot.target_war, empire.CoreKingdom)) return false;
        WarScopeSystem.Dispatch(empire.CoreKingdom, actor.plot.target_war);
        empire.data.timestamp_invite_war_cool_down = World.world.getCurWorldTime();
        TranslateHelper.LogEmpireJoinWar(empire, member);
        empire.RecordHistory(directContent: string.Format(LM.Get("empire_join_member_war_history"),
            empire.GetEmpireFullName(), member.GetKingdomName()), kingdomId: member.id);
        // 封建制帝国：护佑诸侯，战后按胜负涨帝国权威(见 HolyRomanSystem.Authority)
        HolyRomanSystem.NoteProtection(empire, actor.plot.target_war, member);
        return true;
    }

    // 驰援决议通过：本成员加入邻近成员的守方
    public static bool AidNeighbour(Actor actor)
    {
        if (!NeighbourContinues(actor)) return false;
        Kingdom member = actor.kingdom;
        Kingdom ally = actor.plot.target_kingdom;
        if (!Join(actor.plot.target_war, member)) return false;
        WarScopeSystem.Dispatch(member, actor.plot.target_war);
        string text = string.Format(LM.Get("member_aid_neighbour_history"), member.GetKingdomName(), ally.GetKingdomName());
        TranslateHelper.LogEventMessage(text, member);
        member.GetEmpire().RecordHistory(directContent: text, kingdomId: member.id);
        return true;
    }

    // 发兵勤王：中央自己的仗吃紧(就近动员升到一级以上，见 WarScopeSystem)，本成员还没为这场仗出征。
    // 已随中央参战的(听命于中央的)出征即可；没参战的(世袭诸侯、藩镇)一并加入中央一方，要对中央有好感。
    // 中央与本帝国(没在叛乱的)成员之间的仗不算。needy = false 时不看中央是否吃紧(谋划已开始、强制发起)
    public static War FindCenterWar(Kingdom member, bool needy = true) =>
        Context(member) is Empire empire
            ? empire.CoreKingdom.getWars().FirstOrDefault(war => CenterValid(member, war, needy))
            : null;

    public static bool CenterValid(Kingdom member, War war, bool needy = true)
    {
        Empire empire = Context(member);
        Kingdom core = empire?.CoreKingdom;
        if (core == null || war?.data == null || !war.isAlive() || war.hasEnded() || !war.hasKingdom(core) ||
            war.GetEmpireWarType() == EmpireWarType.劫掠 || ZhouEraSystem.IsInternalWar(war) ||
            WarScopeSystem.DispatchedFor(member, war)) return false;
        bool attacking = war.isAttacker(core);
        var opponents = attacking ? war._list_defenders : war._list_attackers;
        if (opponents.Any(kingdom => kingdom != null && !kingdom.isRekt() && kingdom.GetEmpire() == empire &&
                                     !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling())) return false;
        if (war.hasKingdom(member))
        {
            if (attacking ? !war.isAttacker(member) : !war.isDefender(member)) return false;
        }
        else if (member.getWars().Any(other => other != null && !other.hasEnded()) ||
                 EmpirePeacePatch.HasLeft(member, war, joiningAttackers: attacking)) return false;
        if (needy && WarScopeSystem.Level(core) < WarScopeSystem.Wider) return false;
        return EmpirePeacePatch.Obeys(empire, member) || member.isOpinionTowardsKingdomGood(core);
    }

    public static bool StartCenter(Actor actor, PlotAsset asset, bool forced)
    {
        Kingdom member = actor?.kingdom;
        if (member?.king != actor || actor.plot?.isActive() == true) return false;
        War war = FindCenterWar(member, needy: !forced);
        if (war == null) return false;
        Plot plot = World.world.plots.newPlot(actor, asset, forced);
        plot.target_war = war;
        plot.target_kingdom = member.GetEmpire().CoreKingdom;
        return true;
    }

    public static bool CenterContinues(Actor actor) =>
        actor?.kingdom?.king == actor && CenterValid(actor.kingdom, actor.plot?.target_war, needy: false);

    // 勤王决议通过：没参战的加入中央一方，为这场仗出征
    public static bool AidCenter(Actor actor)
    {
        if (!CenterContinues(actor)) return false;
        Kingdom member = actor.kingdom;
        War war = actor.plot.target_war;
        Kingdom core = member.GetEmpire().CoreKingdom;
        if (!war.hasKingdom(member))
        {
            if (war.isAttacker(core)) war.joinAttackers(member);
            else war.joinDefenders(member);
            if (!war.hasKingdom(member)) return false;
        }
        WarScopeSystem.Dispatch(member, war);
        string text = string.Format(LM.Get("member_aid_center_history"), member.GetKingdomName());
        TranslateHelper.LogEventMessage(text, member);
        member.GetEmpire().RecordHistory(directContent: text, kingdomId: member.id);
        return true;
    }

    // 能发起这几项决议的成员：帝国的非核心成员、没在叛乱；战国周室已不管诸侯的仗；
    // 分裂进程里割据的成员不奉诏，也不向中央求援、不驰援、不勤王(见 SchismSystem，用户 2026-10-21 选"先割据后独立")
    private static Empire Context(Kingdom member)
    {
        if (member?.data == null || member.isRekt() || member.IsEmpire() ||
            member.IsFactionRebelling() || member.IsLocalRebelling() || SchismSystem.IsBreakaway(member)) return null;
        Empire empire = member.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null ||
            empire.CoreKingdom.isRekt()) return null;
        return ZhouEraSystem.Era(empire) >= ZhouEraSystem.Warring ? null : empire;
    }

    // 成员守、对方是外敌(本帝国叛乱的成员也算外敌)；劫掠与周室诸侯间的征伐不算
    private static bool Aidable(War war, Kingdom defender, Empire empire)
    {
        if (war?.data == null || !war.isAlive() || war.hasEnded() || !war.isDefender(defender) ||
            war.GetEmpireWarType() == EmpireWarType.劫掠 || ZhouEraSystem.IsInternalWar(war)) return false;
        return !war._list_attackers.Any(kingdom => kingdom != null && !kingdom.isRekt() &&
                                                   kingdom.GetEmpire() == empire &&
                                                   !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling());
    }

    private static bool Join(War war, Kingdom kingdom)
    {
        if (war?.data == null || kingdom == null || kingdom.isRekt() || war.hasEnded() || war.hasKingdom(kingdom))
            return false;
        war.joinDefenders(kingdom);
        return war._list_defenders.Contains(kingdom);
    }
}

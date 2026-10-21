using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 分裂进程(用户 2026-10-21 反馈"突然裂开这个设定很突兀，总得有点偏向现实点的吧"，选定四项全做：预警前兆、先割据后独立、
// 争位内战代替集体出走、连锁观望；设计见 Docs/plan-schism-process.md)。以前汗位/哈里发/苏丹更替之乱、应仁之乱、雄主身后分裂
// 都是条件一满足就在同一个月里成批宣布独立，土崩瓦解检定一中当月就有一省首义，事先看不出征兆，事后也没有转圜。现在走四段：
//   1 预警：各路径照原有的筛选与每家机会选出不稳成员(更替之乱、应仁之乱、雄主身后守单次上限，见 UpheavalGuard.Budget)，
//     帝国进入"离心"，史书、世界日志记下前兆，帝国窗口列出名单；成员照常纳税出兵。
//     汗位/哈里发/苏丹更替之乱、应仁之乱 12~36 个月，雄主身后 6~12 个月，土崩瓦解 6~18 个月；
//   2 割据：不稳成员名存实亡——仍在帝国里、地图上仍是帝国的疆域(颜色不变)，但不向中央缴税(TreasurySystem.TaxShares)、
//     不听命不随征(EmpirePeacePatch.Obeys/FollowsIntoWar、CheckJoinWar)、不奉诏出兵(WarScopeSystem 经 Obeys；神罗议会征召算抗命)、
//     中央不替它付饷(CorpsSystem 经 Obeys)、不求援也不勤王(MemberWarAid)；6~24 个月后破局；
//   3 破局：换君引起的(汗位、雄主身后、哈里发、苏丹、应仁)——割据成员里国力最强的一家举兵争位(见 PickClaimant)，
//     开"某某争位之战"：争位者为攻方、中央为守方，其余割据成员各八成随攻方，没割据的成员各六成随守方，别的观望
//     (设计写"听命中央的成员"；汗国的兀鲁思、世袭埃米尔、藩都有自主外交、不算 Obeys，照字面守方几乎只剩中央，按"没割据的"算)；
//     土崩瓦解——割据成员里离心最重的一家首义(走 ProvincialCollapseSystem 原首义)，其余观望，每月按原响应公式 × 声势(见 Momentum)；
//   4 结局：争位者胜——汗位、雄主身后、哈里发由争位者的国接任中央(Empire.TransferCoreTo，国号不改、不走篡位的宗室出走)，
//     苏丹、应仁由争位者一家执政(MilitaryRegency.Usurp)，割据的复奉号令；争位者败(或已亡)——留在帝国，诸部复服，守方胜正统 +5；
//     决胜：攻下对方都城即定胜负，打满两年后一方兵力达对方两倍对方认输(见 ContestMonthly)；
//     满五年强制议和或中途议和——争位者与随攻方的割据成员离开帝国各自独立(Empire.leave，不另开战)，没随攻方的复奉号令；
//     土崩瓦解——两年收束时没响应的，帝国存续就复奉号令，灭亡就照 TryFall/Absorb 的直辖规则。
//     以争位、分裂收场才记大分裂冷却(UpheavalGuard.Mark)，土崩瓦解每省起义各自记，中途化解不记。
// 化解(预警、割据两阶段，每月)：正统回到该类门槛 +8 且积弊均值低于该类积弊门槛(土崩瓦解另要内忧降到 0.2 以下——它的首义看的是
//   内忧，正统、积弊都不差、只因国库亏空之类起事的，光看这两项会一开就化解)；中央在本阶段开始后打赢一仗(主攻或主守且是胜方，
//   见 OnWarEnded)；新君(新将军、新苏丹)在位满 3 年且正统不低于门槛(土崩瓦解不是换君引起的，进程里换了君才算)；
//   不稳成员都已不在帝国里。雄主身后只认打赢一仗与成员都不在。
// 一个帝国同时只走一个进程；进程进行中、大分裂冷却里不开新的(UpheavalGuard.Blocked)。旧档默认无进程，旧机制下已出走的成员不追认。
// 争位之战复用 EmpireWarType.对立国王(神罗的争位之战)：核对过各处按类型处理的地方都是通用含义——只许两家对打(HolyRomanSystem.ContainedWar)、
//   不算守土(EmpirePeacePatch.NotDefence)、不按开战目标议和(WarGoalSystem.OwnEnding)、打下中央都城只扣 10(CityPatch 经
//   HolyRomanSystem.InContest)、史书里的战争类别"争位之战"；神罗自己的结算按战争 id(anti_king_war_id)认，不会误收；
//   MusterSystem、WarCommandSystem、CorpsSystem 的叛乱集合里本就没有它(争位不是叛乱，与神罗争位一致)；
//   WarBoxCompatibility 的内战集合补上了它(相持分裂时争位一方先离开帝国，不该再由 WarBox 签赔款和约；神罗的不受影响)。
public static class SchismSystem
{
    // 类别(存档里存数值，只能往后加)
    public const int None = 0, Khan = 1, Hero = 2, Caliph = 3, Sultan = 4, Onin = 5, Collapse = 6;
    // 阶段：预警(离心)、割据、争位内战(换君类)、首义观望(土崩瓦解)
    public const int Warning = 1, Breakaway = 2, Contest = 3, Rising = 4;
    private const int BreakawayMinMonths = 6, BreakawayMaxMonths = 24;
    // 化解：正统回到门槛 +ResolveMargin；新君在位满 RulerYears 年；土崩瓦解另要内忧降到 CollapseCalm 以下
    private const int ResolveMargin = 8, RulerYears = 3;
    private const float CollapseCalm = 0.2f;
    // 争位之战：其余割据成员随攻方、没割据的成员随守方的机会(照神罗对立国王的写法)；满 ContestMaxYears 年强制议和；
    // 守方胜中央正统 +DefenderMandate
    private const float RebelJoinChance = 0.8f, LoyalJoinChance = 0.6f;
    private const int ContestMaxYears = 5, DefenderMandate = 5;
    // 决胜(用户 2026-10-21 要分裂"偏向现实"：争位多半靠拿下都城或兵力压倒对方定局，不是非把中央整个打垮)：
    // 攻下对方都城即定胜负；打满 DecisiveYears 年后一方(连同随它参战的成员)兵力达对方 DecisiveRatio 倍，对方认输。
    // 以前只有打垮中央或争位者覆灭才分胜负，几乎都拖到五年强制议和、天下二分
    private const int DecisiveYears = 2;
    private const float DecisiveRatio = 2f;
    // 声势：叛省近 RecentMonths 个月打下了城 ×GainFactor；中央收复了首义省的都城 ×RecaptureFactor
    private const int RecentMonths = 12;
    private const float GainFactor = 1.5f, RecaptureFactor = 0.3f;
    // 史书、世界日志里点名的成员至多几家(其余以"等 N 家"带过)
    private const int NamesShown = 4;
    private const string Tag = "[EmpireCraft][分裂进程]";

    // 正在开争位之战(startWar 前后，战争类型还没设上)：开战钩子据此只许两家对打(见 HolyRomanSystem.ContainedWar)
    public static bool Declaring;

    // 上次推进的时间(不存档)；各帝国上次记日志的声势(只在变了时记一行)
    private static double _lastTick = -1d;
    private static readonly Dictionary<long, float> LoggedMomentum = new();

    // 换世界、读档时清(见 SaveManagerPatch.ClearRuntimeState)；进程本身在 EmpireData 里，读档后照存档续上
    public static void ResetWorldState()
    {
        _lastTick = -1d;
        LoggedMomentum.Clear();
        Declaring = false;
    }

    #region 查询

    public static bool Active(Empire empire) => empire?.data != null && empire.data.schism_kind != None;

    public static int KindOf(Empire empire) => Active(empire) ? empire.data.schism_kind : None;

    public static int StageOf(Empire empire) => Active(empire) ? empire.data.schism_stage : 0;

    public static bool InRising(Empire empire) => KindOf(empire) == Collapse && StageOf(empire) == Rising;

    // 割据中(不缴税、不随征、不奉诏)：割据、争位、首义观望三个阶段里名单上的成员。国家上的标记要与所在帝国、该帝国的进程对得上，
    // 离开了帝国、进程已收场的旧标记不算
    public static bool IsBreakaway(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        KingdomExtension.KingdomExtraData extra = kingdom.GetOrCreate();
        if (extra.schism_breakaway_at < 0d) return false;
        Empire empire = kingdom.GetEmpire();
        return empire?.data != null && empire.id == extra.schism_breakaway_empire_id && empire.data.schism_kind != None &&
               empire.data.schism_stage >= Breakaway && kingdom != empire.CoreKingdom;
    }

    // 名单上还在帝国里的成员(预警时离心、之后割据、首义后观望)
    public static List<Kingdom> Members(Empire empire)
    {
        var list = new List<Kingdom>();
        if (!Active(empire) || empire.data.schism_members == null || World.world == null) return list;
        foreach (long id in empire.data.schism_members)
        {
            Kingdom kingdom = LiveKingdom(id);
            if (kingdom != null && kingdom.GetEmpire() == empire && kingdom != empire.CoreKingdom) list.Add(kingdom);
        }
        return list;
    }

    // 土崩瓦解首义之后还在观望的割据之省(ProvincialCollapseSystem 按它们判响应)
    public static List<Kingdom> Watchers(Empire empire) => InRising(empire) ? Members(empire) : new List<Kingdom>();

    // 争位之战(CheckJoinWar 据此不把别的成员拉进来)：守方是中央，按守方所在帝国的进程认
    public static bool IsContestWar(War war)
    {
        if (war?.data == null || war.GetEmpireWarType() != EmpireWarType.对立国王) return false;
        Empire empire = war.getMainDefender()?.GetEmpire();
        return StageOf(empire) == Contest && empire.data.schism_war_id == war.data.id;
    }

    // 日志用："汗位更替之乱·割据"
    public static string Describe(Empire empire) =>
        Active(empire) ? $"{KindName(empire.data.schism_kind)}·{StageName(empire.data.schism_stage)}" : "无";

    #endregion

    #region 开进程

    // 开进程(各路径原本"按机会逐家宣布独立"的地方)：members 是按原有筛选与每家机会选出的不稳成员(换君类已守住单次上限)；
    // gateLegitimacy、gateMalady 是该类的门槛(化解看)；ruler 是这回的新君(新将军、新苏丹)；detail 只进日志。
    // 选不出人不开；已有进程、大分裂冷却里不开(各路径先查过 UpheavalGuard.Blocked，这里再兜一次)
    public static bool Begin(Empire empire, int kind, List<Kingdom> members, int gateLegitimacy, float gateMalady, Actor ruler,
        string detail = null)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || World.world == null || kind == None) return false;
        Kingdom core = empire.CoreKingdom;
        List<Kingdom> unstable = (members ?? new List<Kingdom>())
            .Where(member => member?.data != null && !member.isRekt() && member != core && member.GetEmpire() == empire)
            .Distinct().ToList();
        if (core == null || unstable.Count == 0) return false;
        if (Active(empire) || UpheavalGuard.InCooldown(empire))
        {
            LogService.LogInfo($"{Tag} {empire.GetEmpireName()} 本当开{KindName(kind)}，" +
                               $"{(Active(empire) ? $"已有进程({Describe(empire)})" : "大分裂冷却中")}，作罢");
            return false;
        }
        EmpireData data = empire.data;
        double now = World.world.getCurWorldTime();
        data.schism_kind = kind;
        data.schism_started_at = now;
        data.schism_gate_legitimacy = gateLegitimacy;
        data.schism_gate_malady = gateMalady;
        // 土崩瓦解不是换君引起的：当今不算"新君"，进程里换了君才从那时起算(见 TrackRuler)
        data.schism_ruler_id = ruler?.id ?? -1L;
        data.schism_ruler_at = kind == Collapse || ruler == null ? -1d : now;
        data.schism_members = unstable.Select(member => member.id).ToList();
        data.schism_claimant_id = -1L;
        data.schism_claimant_name = "";
        data.schism_claimant_realm = "";
        data.schism_war_id = -1L;
        ResetRisen(data);
        EnterStage(data, Warning, WarningMonths(kind), now);
        LogService.LogInfo($"{Tag} {empire.GetEmpireName()} {KindName(kind)}·预警开始{(detail == null ? "" : $"({detail})")}：" +
                           $"不稳成员 {LogNames(unstable)}(共 {unstable.Count} 家 {unstable.Sum(member => member.cities?.Count ?? 0)} 城，" +
                           $"帝国 {UpheavalGuard.Cities(empire)} 城)，预警 {data.schism_stage_months} 个月");
        Chronicle(empire, kind, "warning", empire.GetEmpireName(), RulerName(empire, kind), Title(empire, kind), Names(unstable),
            unstable.Count);
        return true;
    }

    private static int WarningMonths(int kind) => kind switch
    {
        Hero => UnityEngine.Random.Range(6, 13),
        Collapse => UnityEngine.Random.Range(6, 19),
        _ => UnityEngine.Random.Range(12, 37)
    };

    #endregion

    #region 每月推进

    // 每月(战略调度，见 EmpireCraftStrategicScheduler，挂在帝国存续整理之后)：各帝国的进程往前走一步
    public static void Tick()
    {
        if (World.world == null || ModClass.EMPIRE_MANAGER == null) return;
        double now = World.world.getCurWorldTime();
        if (_lastTick >= 0d && now >= _lastTick && Date.getMonthsSince(_lastTick) < 1) return;
        _lastTick = now;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (!Active(empire) || empire.isRekt() || empire.IsArchived() ||
                Compatibility.AncientWarfareCompatibility.OwnsObject(empire)) continue;
            try
            {
                Advance(empire);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"{Tag} {empire.GetEmpireName()} 推进失败: {exception.Message}");
            }
        }
    }

    private static void Advance(Empire empire)
    {
        EmpireData data = empire.data;
        Kingdom core = empire.CoreKingdom;
        if (core == null || core.isRekt()) return;
        double now = World.world.getCurWorldTime();
        // 记的时间晚于现在(换了世界没清到)：阶段从现在起算
        if (data.schism_stage_at < 0d || now < data.schism_stage_at) data.schism_stage_at = now;
        Prune(empire);
        switch (data.schism_stage)
        {
            case Warning:
            case Breakaway:
                TrackRuler(empire);
                string reason = Resolution(empire);
                if (reason != null)
                {
                    Resolve(empire, reason);
                    return;
                }
                if (Date.getMonthsSince(data.schism_stage_at) < data.schism_stage_months) return;
                if (data.schism_stage == Warning) EnterBreakaway(empire);
                else if (data.schism_kind == Collapse) BreakOutCollapse(empire);
                else Proclaim(empire);
                return;
            case Contest:
                ContestMonthly(empire);
                return;
            case Rising:
                // 收束由 ProvincialCollapseSystem 调 ReleaseWatchers/EndRising；瓦解已不在进行(别处结束了它)的兜底收场
                if (!ProvincialCollapseSystem.Active(empire)) EndRising(empire, ReleaseWatchers(empire), false);
                return;
            default:
                LogService.LogWarning($"{Tag} {empire.GetEmpireName()} 进程阶段 {data.schism_stage} 不认得，清掉");
                Restore(empire, Members(empire));
                Clear(empire);
                return;
        }
    }

    // 名单上已不在帝国里的(覆灭、被吞、另行离开、成了中央)移出，割据标记一并清掉
    private static void Prune(Empire empire)
    {
        EmpireData data = empire.data;
        data.schism_members ??= new List<long>();
        foreach (long id in data.schism_members.ToList())
        {
            Kingdom kingdom = World.world.kingdoms.get(id);
            bool alive = kingdom?.data != null && !kingdom.isRekt();
            if (alive && kingdom.GetEmpire() == empire && kingdom != empire.CoreKingdom) continue;
            data.schism_members.Remove(id);
            if (alive) Unflag(kingdom, empire);
        }
    }

    // 看"新君在位满三年"的那位：汗位、雄主身后、哈里发、土崩瓦解看皇帝，苏丹、应仁看将军、苏丹；进程里又换了人，从这时起算
    private static void TrackRuler(Empire empire)
    {
        EmpireData data = empire.data;
        Actor ruler = Ruler(empire, data.schism_kind);
        long id = ruler?.id ?? -1L;
        if (id == data.schism_ruler_id) return;
        data.schism_ruler_id = id;
        data.schism_ruler_at = ruler == null ? -1d : World.world.getCurWorldTime();
    }

    // 化解的缘由(日志用)；不化解返回 null
    private static string Resolution(Empire empire)
    {
        EmpireData data = empire.data;
        if (data.schism_members.Count == 0) return "不稳成员都已不在帝国里";
        if (data.schism_last_victory_at >= 0d && data.schism_last_victory_at >= data.schism_stage_at)
            return "中央在本阶段打赢了一仗";
        if (data.schism_kind == Hero) return null;
        int legitimacy = empire.Legitimacy, gate = data.schism_gate_legitimacy;
        float malady = MaladySystem.Average(empire);
        if (legitimacy >= gate + ResolveMargin && malady < data.schism_gate_malady)
        {
            if (data.schism_kind != Collapse)
                return $"正统回到{legitimacy}(门槛{gate}+{ResolveMargin})，积弊均{malady:0}(低于{data.schism_gate_malady:0})";
            float crisis = ProvincialCollapseSystem.Crisis(empire);
            if (crisis < CollapseCalm)
                return $"正统回到{legitimacy}(门槛{gate}+{ResolveMargin})，积弊均{malady:0}(低于{data.schism_gate_malady:0})，" +
                       $"内忧{crisis:0.00}(低于{CollapseCalm:0.0})";
        }
        double now = World.world.getCurWorldTime();
        if (data.schism_ruler_at >= 0d && now >= data.schism_ruler_at && Date.getYearsSince(data.schism_ruler_at) >= RulerYears &&
            legitimacy >= gate)
            return $"新君在位满{RulerYears}年，正统{legitimacy}(不低于门槛{gate})";
        return null;
    }

    // 化解：割据的复奉号令，史书记"人心渐定"一类；不记大分裂冷却
    private static void Resolve(Empire empire, string reason)
    {
        EmpireData data = empire.data;
        int kind = data.schism_kind;
        string stage = StageName(data.schism_stage);
        List<Kingdom> members = Members(empire);
        Restore(empire, members);
        LogService.LogInfo($"{Tag} {empire.GetEmpireName()} {KindName(kind)}在{stage}阶段化解：{reason}" +
                           $"{(members.Count > 0 ? $"；{LogNames(members)} 复奉号令" : "")}");
        if (members.Count > 0)
            Chronicle(empire, kind, "resolved", empire.GetEmpireName(), RulerName(empire, kind), Title(empire, kind), members.Count);
        Clear(empire);
    }

    // 预警期满：名单上的成员割据
    private static void EnterBreakaway(Empire empire)
    {
        EmpireData data = empire.data;
        double now = World.world.getCurWorldTime();
        List<Kingdom> members = Members(empire);
        foreach (Kingdom member in members) Flag(member, empire, now);
        EnterStage(data, Breakaway, UnityEngine.Random.Range(BreakawayMinMonths, BreakawayMaxMonths + 1), now);
        LogService.LogInfo($"{Tag} {empire.GetEmpireName()} {KindName(data.schism_kind)}·预警期满，进入割据：{LogNames(members)}" +
                           $"(共 {members.Count} 家 {members.Sum(member => member.cities?.Count ?? 0)} 城)不再缴税、不随征、不奉诏，" +
                           $"{data.schism_stage_months} 个月后破局");
        Chronicle(empire, data.schism_kind, "breakaway", empire.GetEmpireName(), RulerName(empire, data.schism_kind),
            Title(empire, data.schism_kind), Names(members), members.Count);
    }

    #endregion

    #region 争位内战

    // 破局(换君类)：割据成员里最强的一家举兵争位，开争位之战(照 HolyRomanSystem.ProclaimAntiKing 的写法)
    private static void Proclaim(Empire empire)
    {
        EmpireData data = empire.data;
        int kind = data.schism_kind;
        Kingdom core = empire.CoreKingdom;
        List<Kingdom> rebels = Members(empire);
        Kingdom claimant = PickClaimant(empire, kind, rebels);
        if (claimant == null)
        {
            Resolve(empire, "割据成员里没有能举兵争位的(无君、无都城或正在叛乱)");
            return;
        }
        War war;
        Declaring = true;
        try
        {
            war = World.world.diplomacy.startWar(claimant, core, WarTypeLibrary.normal);
        }
        finally
        {
            Declaring = false;
        }
        if (war?.data == null)
        {
            Resolve(empire, $"{claimant.GetKingdomName()} 举兵未成(开不了战)");
            return;
        }
        war.SetEmpireWarType(EmpireWarType.对立国王);
        war.data.name = string.Format(LM.Get("schism_war_name"), claimant.GetKingdomName());
        data.schism_claimant_id = claimant.id;
        data.schism_claimant_name = claimant.king?.getName() ?? "";
        data.schism_claimant_realm = claimant.GetKingdomName();
        data.schism_war_id = war.data.id;
        data.schism_core_capital_id = core.capital?.data?.id ?? -1L;
        data.schism_claimant_capital_id = claimant.capital?.data?.id ?? -1L;
        EnterStage(data, Contest, 0, World.world.getCurWorldTime());
        // 其余割据成员各八成随攻方；没割据的成员各六成随守方(见文件头)，别的观望
        var followers = new List<Kingdom>();
        foreach (Kingdom member in rebels)
        {
            if (member == claimant || war.hasKingdom(member) || UnityEngine.Random.value >= RebelJoinChance) continue;
            war.joinAttackers(member);
            if (war.isAttacker(member)) followers.Add(member);
        }
        var listed = new HashSet<long>(data.schism_members);
        var loyal = new List<Kingdom>();
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (member?.data == null || member.isRekt() || member == core || listed.Contains(member.id) ||
                member.GetEmpire() != empire || member.IsFactionRebelling() || member.IsLocalRebelling() || war.hasKingdom(member) ||
                member.isInWarWith(core) || UnityEngine.Random.value >= LoyalJoinChance) continue;
            war.joinDefenders(member);
            if (war.isDefender(member)) loyal.Add(member);
        }
        LogService.LogInfo($"{Tag} {empire.GetEmpireName()} {KindName(kind)}·破局：{data.schism_claimant_name}({claimant.GetKingdomName()}，" +
                           $"国力 {claimant.GetNationalPower():0})举兵争位，开{war.data.name}；随攻方 {LogNames(followers)}，" +
                           $"随守方 {LogNames(loyal)}，割据观望 {Math.Max(0, rebels.Count - 1 - followers.Count)} 家；满{ContestMaxYears}年强制议和");
        Chronicle(empire, kind, "contest", empire.GetEmpireName(), data.schism_claimant_name, claimant.GetKingdomName(),
            Title(empire, kind), war.data.name);
    }

    // 争位者：割据成员里国力最强的一家(要有君主、都城，没在叛乱)。汗位、雄主身后先挑君主与本朝同宗的；
    // 哈里发先挑阿拉伯政体的(能称对立哈里发)；苏丹、应仁先挑君主够格执政的(不属皇族一系的成年人，见 MilitaryRegency.CanTakeOver)
    private static Kingdom PickClaimant(Empire empire, int kind, List<Kingdom> rebels)
    {
        SpecificClan royal = empire.EmpireSpecificClan;
        return rebels
            .Where(kingdom => kingdom.hasKing() && kingdom.king?.data != null && kingdom.capital != null && kingdom.cities?.Count > 0 &&
                              !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling())
            .OrderByDescending(kingdom => kind switch
            {
                Khan or Hero => royal != null && SpecificClanManager.SameLineage(kingdom.king.GetSpecificClan(), royal),
                Caliph => kingdom.GetRegime()?.type == RegimeType.Arabic,
                Sultan or Onin => MilitaryRegency.CanTakeOver(empire, kingdom.king),
                _ => false
            })
            .ThenByDescending(kingdom => kingdom.GetNationalPower())
            .FirstOrDefault();
    }

    // 争位之战每月：争位者已亡或离开帝国，算兵败；中央已不在这场仗里(原中央覆灭、由别家接续)，算争位者胜；满五年强制议和
    private static void ContestMonthly(Empire empire)
    {
        EmpireData data = empire.data;
        War war = LiveWar(data.schism_war_id);
        if (war == null)
        {
            // 没走到结束钩子(战争被别处直接移除)：不结算，割据的复奉号令
            Abandon(empire, "争位之战已不在");
            return;
        }
        Kingdom claimant = LiveKingdom(data.schism_claimant_id);
        if (claimant == null || claimant.GetEmpire() != empire || !war.hasKingdom(claimant))
        {
            World.world.wars.endWar(war, WarWinner.Defenders);
            return;
        }
        if (!war.hasKingdom(empire.CoreKingdom))
        {
            World.world.wars.endWar(war, WarWinner.Attackers);
            return;
        }
        // 决胜：攻下对方都城即定胜负(都城落到对方那一边任何一家手里都算)
        if (CapitalFallen(war, data.schism_core_capital_id, attackerSide: true))
        {
            LogService.LogInfo($"{Tag} {empire.GetEmpireName()} 争位之战：{data.schism_claimant_name}一方攻下中央都城，定局");
            World.world.wars.endWar(war, WarWinner.Attackers);
            return;
        }
        if (CapitalFallen(war, data.schism_claimant_capital_id, attackerSide: false))
        {
            LogService.LogInfo($"{Tag} {empire.GetEmpireName()} 争位之战：中央攻下{data.schism_claimant_realm}都城，定局");
            World.world.wars.endWar(war, WarWinner.Defenders);
            return;
        }
        float years = WarSituation.WarYears(war);
        if (years >= DecisiveYears)
        {
            int attackers = SideWarriors(war, attackers: true), defenders = SideWarriors(war, attackers: false);
            if (attackers >= defenders * DecisiveRatio && attackers > 0 || defenders >= attackers * DecisiveRatio && defenders > 0)
            {
                bool attackersWin = attackers > defenders;
                LogService.LogInfo($"{Tag} {empire.GetEmpireName()} 争位之战打满{years:0.0}年，兵力 攻{attackers}/守{defenders}，" +
                                   $"{(attackersWin ? "守方" : "攻方")}认输");
                World.world.wars.endWar(war, attackersWin ? WarWinner.Attackers : WarWinner.Defenders);
                return;
            }
        }
        if (years >= ContestMaxYears) World.world.wars.endWar(war, WarWinner.Peace);
    }

    // 开战时某一方的都城(cityId)眼下落在另一方手里
    private static bool CapitalFallen(War war, long cityId, bool attackerSide)
    {
        if (cityId < 0 || World.world?.cities == null) return false;
        City city = World.world.cities.get(cityId);
        Kingdom holder = city?.kingdom;
        if (holder?.data == null || holder.isRekt() || !war.hasKingdom(holder)) return false;
        return attackerSide ? war.isAttacker(holder) : war.isDefender(holder);
    }

    // 一方(连同随它参战的成员)眼下的兵力
    private static int SideWarriors(War war, bool attackers) =>
        (attackers ? war._list_attackers : war._list_defenders)
        .Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).Sum(kingdom => kingdom.countTotalWarriors());

    // 战争结束(WarPatch.end_war，在神罗的结算之后)：记中央打赢一仗(化解用)；争位之战的结局
    public static void OnWarEnded(War war, WarWinner winner)
    {
        try
        {
            if (war?.data == null || ModClass.EMPIRE_MANAGER == null || World.world == null) return;
            // 打赢一仗：核心是主攻方或主守方，且是胜方
            Kingdom victor = winner == WarWinner.Attackers ? war.getMainAttacker()
                : winner == WarWinner.Defenders ? war.getMainDefender() : null;
            if (victor?.data != null && !victor.isRekt() && victor.GetEmpire() is Empire won && won.data != null &&
                won.CoreKingdom == victor)
                won.data.schism_last_victory_at = World.world.getCurWorldTime();
            long id = war.data.id;
            foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
            {
                if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.data.schism_kind == None ||
                    empire.data.schism_stage != Contest || empire.data.schism_war_id != id) continue;
                Settle(empire, war, winner);
                break;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{Tag} 战争结束结算失败: {exception.Message}");
        }
    }

    // 争位之战的结局：攻方胜且争位者还在帝国里——立为新君(新苏丹、新将军)，立不起来按相持分裂；守方胜或争位者已亡、已离开——兵败，
    // 诸部复服(守方胜正统 +5)；议和(满五年强制或中途)——相持分裂，争位者与随攻方的割据成员离开帝国各自独立
    private static void Settle(Empire empire, War war, WarWinner winner)
    {
        EmpireData data = empire.data;
        int kind = data.schism_kind;
        // 同盟把两场仗并成一场(Merged)不是议和：争位之战已不单独存在，进程作罢，不按相持分裂
        if (winner == WarWinner.Merged)
        {
            data.schism_war_id = -1L;
            Abandon(empire, "争位之战并入了另一场仗");
            return;
        }
        Kingdom claimant = LiveKingdom(data.schism_claimant_id);
        bool present = claimant != null && claimant.GetEmpire() == empire && claimant != empire.CoreKingdom;
        string person = !string.IsNullOrEmpty(data.schism_claimant_name) ? data.schism_claimant_name : claimant?.king?.getName() ?? "";
        string realm = claimant?.GetKingdomName() ?? data.schism_claimant_realm;
        string name = empire.GetEmpireName(), title = Title(empire, kind);
        int years = Mathf.Max(1, Mathf.FloorToInt(WarSituation.WarYears(war)));
        // 随攻方的割据成员(争位者之外、还在帝国里的)：结束钩子里双方名单还在
        List<Kingdom> followers = Members(empire).Where(member => member != claimant && war.isAttacker(member)).ToList();
        data.schism_war_id = -1L;
        if (winner == WarWinner.Attackers && present)
        {
            // 先记冷却再立新君：换了家族时先朝宗室下个月的另立随之被挡住(见 EmpirePartitionSystem.Restore)
            UpheavalGuard.Mark(empire, $"{KindName(kind)}·争位者得胜", detail: $"{realm}的{person}入主");
            if (Install(empire, kind, claimant))
            {
                List<Kingdom> back = Members(empire);
                Restore(empire, back);
                LogService.LogInfo($"{Tag} {name} {KindName(kind)}·争位之战攻方胜：{person}({realm})" +
                                   $"{(kind is Sultan or Onin ? $"代为{title}执政" : "的国接任中央")}，{LogNames(back)} 复奉号令");
                Chronicle(empire, kind, "won", name, person, realm, title);
                Clear(empire);
                return;
            }
            LogService.LogInfo($"{Tag} {name} {KindName(kind)}·争位之战攻方胜，但{person}({realm})立不起来，按相持分裂收场");
        }
        if (!present || winner == WarWinner.Defenders)
        {
            List<Kingdom> back = Members(empire);
            Restore(empire, back);
            if (winner == WarWinner.Defenders) empire.AddMandate(DefenderMandate);
            UpheavalGuard.Mark(empire, $"{KindName(kind)}·争位者兵败", detail: present ? $"{realm}归降" : $"{realm}已亡或已离开帝国");
            LogService.LogInfo($"{Tag} {name} {KindName(kind)}·争位之战{(winner == WarWinner.Defenders ? "守方胜" : $"收场({winner})")}：" +
                               $"{person}({realm}){(present ? "兵败归降" : "已亡或已离开帝国")}，{LogNames(back)} 复奉号令" +
                               $"{(winner == WarWinner.Defenders ? $"，中央正统 +{DefenderMandate}" : "")}");
            Chronicle(empire, kind, "failed", name, person, realm, title);
            Clear(empire);
            return;
        }
        // 相持分裂：争位者与随攻方的割据成员离开帝国、各自独立，不另开战(不用 DeclareIndependence，那会开新仗)
        var leavers = new List<Kingdom> { claimant };
        leavers.AddRange(followers);
        int cities = leavers.Sum(kingdom => kingdom.cities?.Count ?? 0), total = UpheavalGuard.Cities(empire);
        foreach (Kingdom leaver in leavers)
        {
            Unflag(leaver, empire);
            if (leaver.GetEmpire() == empire && leaver != empire.CoreKingdom) empire.leave(leaver, pRecalc: true, isLeave: true);
        }
        List<Kingdom> stayed = Members(empire);
        Restore(empire, stayed);
        UpheavalGuard.Mark(empire, $"{KindName(kind)}·相持分裂", detail: $"带走 {cities}/{total} 城");
        LogService.LogInfo($"{Tag} {name} {KindName(kind)}·争位之战相持{years}年({winner})：{person}({realm})与 {LogNames(followers)} " +
                           $"离开帝国自立(共 {leavers.Count} 家 {cities} 城)，没随攻方的 {LogNames(stayed)} 复奉号令");
        Chronicle(empire, kind, "split", name, person, realm, years, leavers.Count);
        Clear(empire);
    }

    // 争位者立为新君：汗位、雄主身后、哈里发由争位者的国接任中央；苏丹、应仁由争位者一家执政
    private static bool Install(Empire empire, int kind, Kingdom claimant)
    {
        Actor king = claimant.king;
        if (king?.data == null || !king.isAlive()) return false;
        if (kind is Sultan or Onin) return MilitaryRegency.Usurp(empire, king);
        if (!claimant.hasCapital()) return false;
        Unflag(claimant, empire);
        return empire.TransferCoreTo(claimant, keepName: true, settledContest: true) && empire.CoreKingdom == claimant;
    }

    // 进程作罢(争位之战被别处移除之类)：割据的复奉号令；已打过争位之战，照样记冷却
    private static void Abandon(Empire empire, string reason)
    {
        int kind = empire.data.schism_kind;
        List<Kingdom> back = Members(empire);
        Restore(empire, back);
        UpheavalGuard.Mark(empire, $"{KindName(kind)}·作罢", detail: reason);
        LogService.LogInfo($"{Tag} {empire.GetEmpireName()} {KindName(kind)}作罢：{reason}；{LogNames(back)} 复奉号令");
        Clear(empire);
    }

    #endregion

    #region 土崩瓦解：首义观望

    // 破局(土崩瓦解)：割据之省里离心最重的一省首义(ProvincialCollapseSystem.BeginRising)，其余观望
    private static void BreakOutCollapse(Empire empire)
    {
        EmpireData data = empire.data;
        List<Kingdom> members = Members(empire);
        ProvincialCollapseSystem.RisingStart start = ProvincialCollapseSystem.BeginRising(empire, members, out Kingdom first);
        // 近代革命战争还在打：下月再看
        if (start == ProvincialCollapseSystem.RisingStart.Wait) return;
        if (start != ProvincialCollapseSystem.RisingStart.Started || first == null)
        {
            Resolve(empire, "割据之省无一能首义(或开国之君在位、已不再适用瓦解)");
            return;
        }
        EnterStage(data, Rising, 0, World.world.getCurWorldTime());
        List<Kingdom> watchers = Members(empire);
        LoggedMomentum[empire.id] = 1f;
        LogService.LogInfo($"{Tag} {empire.GetEmpireName()} 土崩瓦解·破局：{first.GetKingdomName()} 首义，" +
                           $"其余 {watchers.Count} 处割据观望({LogNames(watchers)})，首义声势 ×1.00");
        Chronicle(empire, Collapse, "rising", first.GetKingdomFullName(), empire.GetEmpireFullName(), watchers.Count);
    }

    // 一省起义(首义或响应，见 ProvincialCollapseSystem.Rise)：记下起事时的城数与都城(声势用)，移出观望名单
    public static void OnRisen(Empire empire, Kingdom province, int cities, City capital)
    {
        EmpireData data = empire?.data;
        if (data == null || data.schism_kind != Collapse || province?.data == null) return;
        data.schism_risen ??= new List<long>();
        data.schism_risen_cities ??= new List<int>();
        data.schism_risen_capitals ??= new List<long>();
        data.schism_risen.Add(province.id);
        data.schism_risen_cities.Add(cities);
        data.schism_risen_capitals.Add(capital?.data != null ? capital.id : -1L);
        data.schism_members?.Remove(province.id);
        Unflag(province, empire);
    }

    // 声势(用户 2026-10-21 选"连锁观望"，见设计第四节)：首义各省的都城都还在自己手里 ×1；叛省近 12 个月打下了城
    // (比起事时多，"中央在打这些叛省的仗里丢了城")×1.5；中央收复了任何一家首义省的都城 ×0.3；首义各省都已平定
    // (覆灭，或都城失守后归回帝国)×0——观望的就此不动，两年收束时复奉号令。update 为假时只看不记(帝国窗口用)
    public static float Momentum(Empire empire, bool update = true)
    {
        EmpireData data = empire?.data;
        if (!InRising(empire) || data.schism_risen == null || data.schism_risen.Count == 0 || World.world == null) return 1f;
        double now = World.world.getCurWorldTime();
        int gain = 0;
        bool alive = false, recaptured = false;
        for (int index = 0; index < data.schism_risen.Count; index++)
        {
            Kingdom rebel = LiveKingdom(data.schism_risen[index]);
            if (rebel != null && rebel.GetEmpire() != empire)
            {
                alive = true;
                int before = data.schism_risen_cities != null && index < data.schism_risen_cities.Count ? data.schism_risen_cities[index] : 0;
                gain += Mathf.Max(0, (rebel.cities?.Count ?? 0) - before);
            }
            long seat = data.schism_risen_capitals != null && index < data.schism_risen_capitals.Count ? data.schism_risen_capitals[index] : -1L;
            City capital = seat >= 0 ? World.world.cities.get(seat) : null;
            if (capital?.data != null && !capital.isRekt() && capital.kingdom != null && capital.kingdom.GetEmpire() == empire)
                recaptured = true;
        }
        if (update)
        {
            if (gain > data.schism_rebel_gain) data.schism_rebel_gain_at = now;
            data.schism_rebel_gain = gain;
        }
        bool recent = data.schism_rebel_gain_at >= 0d && now >= data.schism_rebel_gain_at &&
                      Date.getMonthsSince(data.schism_rebel_gain_at) < RecentMonths;
        float factor = !alive ? 0f : (recent ? GainFactor : 1f) * (recaptured ? RecaptureFactor : 1f);
        if (update && (!LoggedMomentum.TryGetValue(empire.id, out float logged) || !Mathf.Approximately(logged, factor)))
        {
            LoggedMomentum[empire.id] = factor;
            LogService.LogInfo($"{Tag} {empire.GetEmpireName()} 土崩瓦解·首义声势 ×{factor:0.00}(起义 {data.schism_risen.Count} 省" +
                               $"{(alive ? "" : "，都已平定")}；叛省比起事时多 {gain} 城{(recent ? "，近一年打下过城" : "")}；" +
                               $"{(recaptured ? "中央已收复首义省都城" : "首义省都城都还在手")})");
        }
        return factor;
    }

    // 土崩瓦解收束(两年满、近代革命战争打完、帝国已不适用瓦解)：还在观望的割据之省先复奉号令(帝国灭亡时照直辖规则并入或独立，
    // 见 ProvincialCollapseSystem.TryFall/Absorb——Absorb 按 Obeys 认直辖，割据标记得先清)；返回它们的名字
    public static List<string> ReleaseWatchers(Empire empire)
    {
        var names = new List<string>();
        if (!InRising(empire)) return names;
        foreach (Kingdom member in Members(empire))
        {
            names.Add(member.GetKingdomName());
            Unflag(member, empire);
        }
        empire.data.schism_members?.Clear();
        return names;
    }

    // 收束之后：帝国存续时史书记观望的复奉号令；进程清掉(各省起义时已各自记过大分裂冷却)
    public static void EndRising(Empire empire, List<string> watchers, bool fell)
    {
        if (!InRising(empire)) return;
        int count = watchers?.Count ?? 0;
        LogService.LogInfo($"{Tag} {empire.GetEmpireName()} 土崩瓦解收束：{(fell ? "帝国灭亡" : "帝国存续")}，观望的 " +
                           $"{(count > 0 ? string.Join("、", watchers) : "无")}{(count > 0 ? fell ? " 照直辖规则并入或独立" : " 复奉号令" : "")}");
        if (!fell && count > 0 && !empire.isRekt() && !empire.IsArchived())
            Chronicle(empire, Collapse, "settled", empire.GetEmpireName(), count);
        Clear(empire);
    }

    #endregion

    #region 帝国窗口、悬停提示

    // 类别的叫法(印度的哈里发称大君、苏丹称摄政，见 DynastyNaming.CultureVariant)
    public static string KindLabel(Empire empire) => LocalizedVariant(empire, "schism_kind_" + KindKey(KindOf(empire)));

    public static string StageLabel(Empire empire) => LM.Get("schism_stage_" + StageOf(empire));

    // 时限：预警、割据还剩几个月；争位打了几年；首义以来几个月
    public static string TimeLabel(Empire empire)
    {
        if (!Active(empire) || World.world == null) return "";
        EmpireData data = empire.data;
        int left = Math.Max(0, data.schism_stage_months - Math.Max(0, Date.getMonthsSince(data.schism_stage_at)));
        switch (data.schism_stage)
        {
            case Warning: return string.Format(LM.Get("schism_left_warning"), left);
            case Breakaway: return string.Format(LM.Get("schism_left_breakaway"), left);
            case Contest:
                War war = LiveWar(data.schism_war_id);
                return string.Format(LM.Get("schism_left_contest"), war == null ? 0 : Mathf.FloorToInt(WarSituation.WarYears(war)),
                    ContestMaxYears);
            case Rising:
                return string.Format(LM.Get("schism_left_rising"), Math.Max(0, ProvincialCollapseSystem.RisingMonths(empire)),
                    ProvincialCollapseSystem.RisingWindowMonths);
            default: return "";
        }
    }

    // 争位者："某国·某人"(争位阶段)
    public static string ClaimantLabel(Empire empire)
    {
        if (StageOf(empire) != Contest) return null;
        EmpireData data = empire.data;
        string war = LiveWar(data.schism_war_id)?.data?.name;
        return $"{LiveKingdom(data.schism_claimant_id)?.GetKingdomName() ?? data.schism_claimant_realm}·{data.schism_claimant_name}" +
               (string.IsNullOrEmpty(war) ? "" : $"  ({war})");
    }

    // 化解的条件(预警、割据两阶段)；之后的阶段已不能化解，返回 null
    public static string ResolveHint(Empire empire)
    {
        int stage = StageOf(empire);
        if (stage != Warning && stage != Breakaway) return null;
        EmpireData data = empire.data;
        int gate = data.schism_gate_legitimacy;
        if (data.schism_kind == Hero) return LM.Get("schism_resolve_hero");
        return string.Format(LM.Get(data.schism_kind == Collapse ? "schism_resolve_collapse" : "schism_resolve_hint"),
            gate + ResolveMargin, data.schism_gate_malady.ToString("0"), RulerYears, gate);
    }

    // 成员在进程里的处境：离心、割据、割据观望、举兵争位；不在名单上返回 null
    public static string StatusText(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        Empire empire = kingdom.GetEmpire();
        if (!Active(empire) || kingdom == empire.CoreKingdom || empire.data.schism_members?.Contains(kingdom.id) != true) return null;
        int stage = empire.data.schism_stage;
        string key = stage == Contest && kingdom.id == empire.data.schism_claimant_id ? "schism_member_claimant"
            : stage == Warning ? "schism_member_unstable"
            : stage == Rising ? "schism_member_watching"
            : "schism_member_breakaway";
        return string.Format(LM.Get(key), KindLabel(empire));
    }

    #endregion

    #region 内部

    private static void EnterStage(EmpireData data, int stage, int months, double now)
    {
        data.schism_stage = stage;
        data.schism_stage_at = now;
        data.schism_stage_months = months;
    }

    private static void Flag(Kingdom kingdom, Empire empire, double now)
    {
        KingdomExtension.KingdomExtraData extra = kingdom.GetOrCreate();
        extra.schism_breakaway_at = now;
        extra.schism_breakaway_empire_id = empire.id;
    }

    // 清掉割据标记(只清本帝国打上的)
    private static void Unflag(Kingdom kingdom, Empire empire)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return;
        KingdomExtension.KingdomExtraData extra = kingdom.GetOrCreate();
        if (extra.schism_breakaway_at < 0d || extra.schism_breakaway_empire_id != empire.id) return;
        extra.schism_breakaway_at = -1d;
        extra.schism_breakaway_empire_id = -1L;
    }

    // 复奉号令
    private static void Restore(Empire empire, List<Kingdom> members)
    {
        foreach (Kingdom member in members) Unflag(member, empire);
    }

    private static void ResetRisen(EmpireData data)
    {
        data.schism_risen = new List<long>();
        data.schism_risen_cities = new List<int>();
        data.schism_risen_capitals = new List<long>();
        data.schism_rebel_gain = 0;
        data.schism_rebel_gain_at = -1d;
    }

    // 进程收场：清空(中央上次打赢一仗的时间留着)
    private static void Clear(Empire empire)
    {
        EmpireData data = empire.data;
        data.schism_kind = None;
        data.schism_stage = 0;
        data.schism_stage_at = -1d;
        data.schism_stage_months = 0;
        data.schism_started_at = -1d;
        data.schism_gate_legitimacy = 0;
        data.schism_gate_malady = 0f;
        data.schism_ruler_id = -1L;
        data.schism_ruler_at = -1d;
        data.schism_members = new List<long>();
        data.schism_claimant_id = -1L;
        data.schism_claimant_name = "";
        data.schism_claimant_realm = "";
        data.schism_war_id = -1L;
        data.schism_core_capital_id = data.schism_claimant_capital_id = -1L;
        ResetRisen(data);
        LoggedMomentum.Remove(empire.id);
    }

    private static Actor Ruler(Empire empire, int kind) => kind is Sultan or Onin ? MilitaryRegency.Regent(empire) : empire.Emperor;

    private static string RulerName(Empire empire, int kind) => Ruler(empire, kind)?.getName() ?? "";

    // 称呼：汗；哈里发(印度称大君)；苏丹(印度称摄政王)；征夷大将军；土崩瓦解不用
    private static string Title(Empire empire, int kind)
    {
        string key = kind switch
        {
            Khan or Hero => "schism_title_khan",
            Caliph => "sultanate_caliph",
            Sultan => "sultanate_regent_title",
            Onin => "bakufu_shogun_title",
            _ => null
        };
        return key == null ? "" : LocalizedVariant(empire, key);
    }

    private static string LocalizedVariant(Empire empire, string key) =>
        LM.Get(DynastyNaming.CultureVariant(empire?.CoreKingdom, key) ?? key);

    private static string KindKey(int kind) => kind switch
    {
        Khan => "khan",
        Hero => "hero",
        Caliph => "caliph",
        Sultan => "sultan",
        Onin => "onin",
        Collapse => "collapse",
        _ => "none"
    };

    // 日志用的类别、阶段名
    private static string KindName(int kind) => kind switch
    {
        Khan => "汗位更替之乱",
        Hero => "雄主身后分裂",
        Caliph => "哈里发更替之乱",
        Sultan => "苏丹更替之乱",
        Onin => "应仁之乱",
        Collapse => "土崩瓦解",
        _ => "分裂"
    };

    private static string StageName(int stage) => stage switch
    {
        Warning => "预警",
        Breakaway => "割据",
        Contest => "争位",
        Rising => "首义观望",
        _ => "无"
    };

    // 史书、世界日志：每个类别 × 每个阶段一句(schism_<阶段>_<类别>，有文化变体的先用，如印度的哈里发、苏丹)
    private static void Chronicle(Empire empire, int kind, string stage, params object[] args)
    {
        string key = $"schism_{stage}_{KindKey(kind)}";
        string variant = DynastyNaming.CultureVariant(empire.CoreKingdom, key) ?? key;
        if (!LM.Has(variant)) return;
        EventRecorder.Record(empire, string.Format(LM.Get(variant), args));
    }

    // 史书里点名的成员(至多 NamesShown 家)
    private static string Names(List<Kingdom> kingdoms) =>
        string.Join(LM.Get("schism_name_separator"), kingdoms.Take(NamesShown).Select(kingdom => kingdom.GetKingdomName()));

    private static string LogNames(List<Kingdom> kingdoms) =>
        kingdoms.Count == 0 ? "无" : string.Join("、", kingdoms.Select(kingdom => kingdom.GetKingdomName()));

    private static Kingdom LiveKingdom(long id)
    {
        Kingdom kingdom = id >= 0 && World.world?.kingdoms != null ? World.world.kingdoms.get(id) : null;
        return kingdom?.data != null && !kingdom.isRekt() ? kingdom : null;
    }

    private static War LiveWar(long id)
    {
        War war = id >= 0 ? World.world?.wars?.get(id) : null;
        return war?.data != null && war.isAlive() && !war.hasEnded() ? war : null;
    }

    #endregion
}

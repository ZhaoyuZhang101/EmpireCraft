using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems.Decisions;

// 决议(用户叫"帝国决策")的运行：模组自己的一套，游戏和独立服务器都走它，日后移植新工程时照搬(用户 2026-11-14
// "记得用自己的方式实现，因为这个核心我之后要移植到 unity 新项目"，选"模组自建决议框架")。
// 决议的内容(谁能办、办成了做什么)仍写在各决议的定义里(EmpireCraftPlotsAddition 等，group_id = empirecraft_diplomacy)；
// 怎么发起、办多久、何时作罢由这里定，规则是模组自己的：
//   · 发起走天平(见 DecisionScale)：君主、城主每 ConsiderMonths 个月、只是族长的每 ChiefConsiderMonths 个月想一回；
//     月初主线程给本月该想的人取快照(Plan)，在别的核上逐人逐项过秤(推力÷阻力)，排出候选；到了各人的时候(Dispatch)，
//     主线程按候选从高到低跑决议自己的条件(读活的世界)、看付不付得起，第一项过的就发起；经过记进 LastCouncil 给天平页看；
//     门槛：身份(定义的 can_be_done_by_*)、资望(见 Standing，按名望折算，定义的 min_level 当资望档)、定义里的才干与名望下限、
//     世界法则、花费(先出个人钱袋，君主城主不够的走国库公费，见 Pay)、定义自己的条件；
//     别处直接发起的(国家 AI 点名、窗口按钮、终端)照旧经 newPlot，一并收进来(见 DecisionPatch)；
//   · 推进：每月一回，进度加 PaceBase + 才干/2(才干取内政、军事、智力、外交里最高的一项)；
//     办满就执行(开战类先等大军集结，见 WarReadinessSystem.Hold)；每月复核还该不该办(定义的 check_should_continue、目标还在)，
//     不该办了就作罢；办了 LapseFactor 倍预计工期(至少 LapseMinMonths 个月)还没办成也作罢；
//   · 原版的谋划对象只当个"牌子"(游戏里显示进度、别的系统查 actor.plot 用)：原版不再推进它(见 DecisionPatch)，进度由这里写上去；
//   · 存档：随模组存档(SaveData.decisions)；牌子没了的(独立服务器不存原版谋划)读档后按记下的目标重新立牌子。
// 帧率：过秤在别的核(ParallelWork)；主线程每月取一回快照，平时每帧只验到了时候的几个人、推几件到期的决议，合计限时 TickMilliseconds。
public sealed class DecisionCase
{
    public long plot = -1;
    public string def;
    public long initiator = -1;
    public double started;
    // 下一回推进的世界时刻：各件错开在一个月里，免得月初一帧推完全部
    public double next;
    public float effort, progress;
    // 发起时天平的读数与砝码(天平页看；别处点名发起的没有)
    public float ratio;
    public List<ScaleWeight> weights;
    // 亲自掌着的君主点名发起的(手机上，见 TerminalHost.DecisionScale)
    public bool personal;
    public long war = -1, kingdom = -1, city = -1, actor = -1, alliance = -1;
}

public static class DecisionSystem
{
    public const string Group = "empirecraft_diplomacy";
    // 君主、城主半年想一回，只是族长的一年想一回
    private const int ConsiderMonths = 6, ChiefConsiderMonths = 12, LapseMinMonths = 24, DispatchPerTick = 8, AdvancePerTick = 16;
    private const float PaceBase = 6f, LapseFactor = 4f, StandingPerTier = 85f, MinEffort = 10f;
    // 每帧推进与发起合用的时限(毫秒)；到了就留到下一帧
    private const double TickMilliseconds = 0.35d;

    private static readonly Dictionary<string, PlotAsset> Defs = new();
    private static readonly List<PlotAsset> Initiable = new();
    private static readonly List<DecisionCase> Queue = new();
    private static int _queueCursor;
    private static readonly Dictionary<long, DecisionCase> Cases = new();
    private static readonly List<DecisionCase> Pending = new();
    private static readonly Dictionary<long, double> NextConsider = new();
    private static readonly List<Actor> Pool = new();
    private static readonly Stopwatch Clock = new();
    private static bool _adopted, _restoring;
    private static int _month = -1;

    public static int Started, Finished, Lapsed, Dropped;

    // 有人亲自掌着的(手机席位上的君主，见 TerminalHost)：天平不替他发起，只在他的"可议之事"里给群臣建议；
    // 别处点名发起的(打仗时的征粮、勤王、驰援、求援等)照旧
    public static Func<Actor, bool> Steered;
    public static readonly Dictionary<string, int> FinishedBy = new();

    public static void ResetWorldState()
    {
        Cases.Clear();
        Pending.Clear();
        NextConsider.Clear();
        Pool.Clear();
        Queue.Clear();
        ReadyList.Clear();
        LastCouncil.Clear();
        Unfit.Clear();
        LastDone.Clear();
        _month = -1;
        _queueCursor = _readyCursor = 0;
    }

    public static bool Manages(PlotAsset asset) => asset != null && Adopt() && Defs.ContainsKey(asset.id);
    public static bool Manages(Plot plot) => plot?.data != null && Cases.ContainsKey(plot.data.id);
    public static PlotAsset Def(string id) => id != null && Adopt() && Defs.TryGetValue(id, out PlotAsset asset) ? asset : null;

    // 接手模组的决议：原版人物 AI(try_new_plot)从 basic_plots 里挑谋划，把模组的拿出来，改由这里发起；
    // 定义里 is_basic_plot 的由人物自行发起，其余只由别处点名发起
    private static bool Adopt()
    {
        if (_adopted) return true;
        PlotsLibrary library = AssetManager.plots_library;
        if (library == null || library.list.Count == 0) return false;
        _adopted = true;
        foreach (PlotAsset asset in library.list)
        {
            if (asset?.id == null || asset.group_id != Group || Defs.ContainsKey(asset.id)) continue;
            Defs[asset.id] = asset;
            if (!asset.is_basic_plot) continue;
            Initiable.Add(asset);
        }
        library.basic_plots.RemoveAll(asset => asset != null && Defs.ContainsKey(asset.id));
        LogService.LogInfo($"[EmpireCraft][决议] 接手 {Defs.Count} 项决议，其中 {Initiable.Count} 项由人物自行发起");
        return true;
    }

    // ───────── 立项(原版 newPlot 之后，见 DecisionPatch) ─────────
    public static void OnPlotCreated(Plot plot, Actor author, PlotAsset asset)
    {
        if (_restoring || plot?.data == null || author == null || !Manages(asset)) return;
        double now = World.world.getCurWorldTime();
        Track(new DecisionCase
        {
            plot = plot.data.id, def = asset.id, initiator = author.getID(), started = now, next = now + Date.getMonthTime(),
            effort = Math.Max(MinEffort, asset.progress_needed)
        });
        Started++;
    }

    private static void Track(DecisionCase item)
    {
        Cases[item.plot] = item;
        Queue.Add(item);
    }

    // ───────── 每帧(战略调度经 FrameJobs 调来) ─────────
    public static void Tick()
    {
        if (World.world?.plots == null || !Adopt()) return;
        if (Pending.Count > 0) Restore();
        if (_month < 0) AdoptOrphans();
        int month = Date.getCurrentYear() * 12 + Date.getCurrentMonth();
        if (month != _month)
        {
            _month = month;
            using (FrameProfiler.Measure("决议·名单")) RebuildPool();
            Plan();
        }
        Clock.Restart();
        using (FrameProfiler.Measure("决议·推进")) Advance();
        using (FrameProfiler.Measure("决议·发起")) Dispatch();
    }

    // 推进到期的决议(每件每月一回，各自的时刻错开)：轮着看，每帧限量限时
    private static void Advance()
    {
        if (Queue.Count == 0) return;
        double now = World.world.getCurWorldTime();
        for (int seen = 0, steps = 0, n = Queue.Count; seen < n && steps < AdvancePerTick && Queue.Count > 0; seen++)
        {
            if (Clock.Elapsed.TotalMilliseconds > TickMilliseconds) break;
            if (_queueCursor >= Queue.Count) _queueCursor = 0;
            DecisionCase item = Queue[_queueCursor];
            if (!Cases.TryGetValue(item.plot, out DecisionCase live) || !ReferenceEquals(live, item))
            {
                Queue.RemoveAt(_queueCursor);
                continue;
            }
            if (now < item.next)
            {
                _queueCursor++;
                continue;
            }
            steps++;
            item.next = now + Date.getMonthTime();
            Plot plot = World.world.plots.get(item.plot);
            bool closed;
            try
            {
                closed = Step(item, plot, now);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][决议] {item.def} 推进出错，作罢：{exception.GetType().Name}: {exception.Message}");
                closed = Close(item, plot, finished: false);
            }
            if (closed) Queue.RemoveAt(_queueCursor);
            else _queueCursor++;
        }
    }

    // 推进一件：返回 true = 已了结(办成或作罢)
    private static bool Step(DecisionCase item, Plot plot, double now)
    {
        PlotAsset def = Def(item.def);
        Actor author = plot?.getAuthor();
        if (def == null || plot?.data == null || !plot.isActive() || author == null || !author.isAlive() || author.plot != plot)
        {
            Dropped++;
            Cases.Remove(item.plot);
            // 牌子还挂着(出错时被关掉的、发起人换了别的)：摘下来
            if (plot?.data != null && plot.getState() == PlotState.Active) plot.finishPlot(PlotState.Cancelled, author);
            return true;
        }
        if (def.check_should_continue != null && !def.check_should_continue(author) || !plot.checkInitiatorAndTargets())
            return Close(item, plot, finished: false);
        item.progress = Math.Min(item.effort, item.progress + Pace(author));
        Show(item, plot, now);
        if (item.progress < item.effort)
        {
            if (Date.getMonthsSince(item.started) > LapseMonths(item, author)) return Close(item, plot, finished: false);
            return false;
        }
        if (WarReadinessSystem.Hold(plot)) return false;
        if (def.action != null && def.action(author)) def.post_action?.Invoke(author);
        return Close(item, plot, finished: true);
    }

    private static float Talent(Actor actor) =>
        Math.Max(0, Math.Min(30, Math.Max(Math.Max(actor.intelligence, actor.stewardship), Math.Max(actor.diplomacy, actor.warfare))));

    private static float Pace(Actor actor) => PaceBase + Talent(actor) * 0.5f;

    private static int LapseMonths(DecisionCase item, Actor author) =>
        Math.Max(LapseMinMonths, (int)Math.Ceiling(item.effort / Pace(author) * LapseFactor));

    // 牌子上的进度(游戏里谋划的进度条、别的系统查"进度满了没有")
    private static void Show(DecisionCase item, Plot plot, double now)
    {
        plot.data.progress_current = item.progress;
        plot.progress_target = item.progress;
        plot.last_update_progress = now;
    }

    private static bool Close(DecisionCase item, Plot plot, bool finished)
    {
        Cases.Remove(item.plot);
        Actor author = plot?.getAuthor();
        if (finished)
        {
            NoteDone(item, author);
            Finished++;
            FinishedBy[item.def] = FinishedBy.TryGetValue(item.def, out int done) ? done + 1 : 1;
        }
        else Lapsed++;
        if (plot?.data != null && plot.isActive()) plot.finishPlot(finished ? PlotState.Finished : PlotState.Cancelled, author);
        return true;
    }

    // ───────── 发起：天平(见 DecisionScale) ─────────
    // 资望：按名望折算的档次，定义里的 min_level 当资望档用(游戏存档里君主名望中位约 220、四分之三位约 340)
    public static int Standing(Actor actor) => 1 + (int)(Math.Max(0, actor.renown) / StandingPerTier);

    // 过完秤、等主线程办的：某人到了时候(due)，按候选从高到低验条件、发起
    private sealed class Ready
    {
        public long actor;
        public double due;
        public List<DecisionScale.Option> options;
    }

    // 一回议决的经过(决议天平页看)：谁、何时、各候选的读数与砝码、结果
    public sealed class Council
    {
        public long actor;
        public string name;
        public double at;
        public readonly List<(string def, float ratio, List<ScaleWeight> weights, string outcome)> options = new();
    }

    private static readonly List<DecisionScale.DefInfo> DefInfos = new(), ChiefDefInfos = new();
    private static readonly List<Ready> ReadyList = new();
    private static readonly Dictionary<long, Council> LastCouncil = new();
    // 各国各项决议上回办成的世界时刻(按 DefInfo.index)、各类上回办成的时刻(按 DecisionKind)；不存档，读档后从头记
    private static readonly Dictionary<long, (double[] byDef, double[] byKind)> LastDone = new();
    // 各人各项决议上回验条件不合的时刻(见 DecisionScale.UnfitMonths)；不存档
    private static readonly Dictionary<long, double[]> Unfit = new();
    private static int _readyCursor;
    public static int Planned, Weighed;

    public static Council LastCouncilOf(Kingdom realm) => realm?.data != null && LastCouncil.TryGetValue(realm.data.id, out Council council) ? council : null;

    // 想发起决议的人：各国君主、各城城主、各族族长(每月重排一回)
    private static void RebuildPool()
    {
        Pool.Clear();
        var seen = new HashSet<long>();
        void Add(Actor actor)
        {
            if (actor?.data != null && actor.isAlive() && seen.Add(actor.getID())) Pool.Add(actor);
        }
        foreach (Kingdom kingdom in World.world.kingdoms) Add(kingdom?.king);
        foreach (City city in World.world.cities) Add(city?.leader);
        foreach (Clan clan in World.world.clans) Add(clan?.getChief());
        if (NextConsider.Count > Pool.Count * 4 + 500) NextConsider.Clear();
        if (Unfit.Count > Pool.Count * 2 + 500) Unfit.Clear();
    }

    private static void BuildDefInfos()
    {
        DefInfos.Clear();
        ChiefDefInfos.Clear();
        for (int i = 0; i < Initiable.Count; i++)
        {
            PlotAsset def = Initiable[i];
            var info = new DecisionScale.DefInfo
            {
                index = i, id = def.id, kind = DecisionScale.KindOf(def.id), minStanding = def.min_level, minRenown = def.min_renown_actor,
                minRealmRenown = def.min_renown_kingdom, minIntelligence = def.min_intelligence, minDiplomacy = def.min_diplomacy,
                minWarfare = def.min_warfare, minStewardship = def.min_stewardship, cost = def.money_cost,
                byKing = def.can_be_done_by_king, byLeader = def.can_be_done_by_leader, byClan = def.can_be_done_by_clan_member
            };
            DefInfos.Add(info);
            if (def.can_be_done_by_clan_member || def.can_be_done_by_leader) ChiefDefInfos.Add(info);
        }
    }

    // 世界法则每月看一回(过秤时只读这一份)
    private static void RefreshLaws()
    {
        bool diplomacy = WorldLawLibrary.world_law_diplomacy?.isEnabled() != false;
        bool rebellion = WorldLawLibrary.world_law_rebellions?.isEnabled() != false;
        foreach (DecisionScale.DefInfo info in DefInfos)
        {
            PlotAsset def = Initiable[info.index];
            info.lawsAllow = (!def.requires_diplomacy || diplomacy) && (!def.requires_rebellion || rebellion);
        }
    }

    // 月初：本月该想事的人取快照(主线程)，交给别的核过秤，过完的排进 ReadyList 等到时候再办
    private static void Plan()
    {
        if (Initiable.Count == 0 || Pool.Count == 0) return;
        if (DefInfos.Count != Initiable.Count) BuildDefInfos();
        RefreshLaws();
        double now = World.world.getCurWorldTime();
        double month = Date.getMonthTime();
        var snapshots = new List<DecisionScale.Snapshot>();
        var dues = new List<double>();
        var official = new List<bool>();
        var realms = new Dictionary<long, DecisionScale.Snapshot>();
        Dictionary<long, int> busy = null;
        using (FrameProfiler.Measure("决议·取快照"))
        {
            foreach (Actor actor in Pool)
            {
                if (actor?.data == null || !actor.isAlive() || actor.plot != null) continue;
                Kingdom realm = actor.kingdom;
                if (realm?.data == null || realm.isRekt() || !actor.isKingdomCiv()) continue;
                long id = actor.getID();
                bool isOfficial = actor.isKing() || actor.isCityLeader();
                double interval = (isOfficial ? ConsiderMonths : ChiefConsiderMonths) * month;
                if (!NextConsider.TryGetValue(id, out double due))
                {
                    // 头一回见到：错开到往后一段里的某个时候，免得读档后大家同一个月一齐想
                    NextConsider[id] = due = now + Randy.randomFloat(0f, (float)interval);
                }
                if (due >= now + month) continue;
                NextConsider[id] = Math.Max(due, now) + interval;
                if (Steered != null && Steered(actor)) continue;
                busy ??= BusyByRealm();
                snapshots.Add(Take(actor, realm, now, month, realms, busy));
                dues.Add(Math.Max(due, now));
                official.Add(isOfficial);
            }
        }
        if (snapshots.Count == 0) return;
        var results = new List<DecisionScale.Option>[snapshots.Count];
        using (FrameProfiler.Measure("决议·过秤"))
            ParallelWork.For(snapshots.Count, i => results[i] = DecisionScale.Weigh(snapshots[i], official[i] ? DefInfos : ChiefDefInfos));
        Planned += snapshots.Count;
        for (int i = 0; i < snapshots.Count; i++)
        {
            if (results[i] == null || results[i].Count == 0) continue;
            Weighed++;
            ReadyList.Add(new Ready { actor = snapshots[i].actor, due = dues[i], options = results[i] });
        }
        if (_readyCursor > 0) ReadyList.RemoveRange(0, Math.Min(_readyCursor, ReadyList.Count));
        _readyCursor = 0;
        ReadyList.Sort((a, b) => a.due.CompareTo(b.due));
    }

    private static Dictionary<long, int> BusyByRealm()
    {
        var busy = new Dictionary<long, int>();
        foreach (DecisionCase item in Cases.Values)
        {
            Kingdom realm = World.world.units.get(item.initiator)?.kingdom;
            if (realm?.data == null) continue;
            busy[realm.data.id] = busy.TryGetValue(realm.data.id, out int count) ? count + 1 : 1;
        }
        return busy;
    }

    private static DecisionScale.Snapshot Take(Actor actor, Kingdom realm, double now, double month,
        Dictionary<long, DecisionScale.Snapshot> realms, Dictionary<long, int> busy)
    {
        long realmId = realm.data.id;
        if (!realms.TryGetValue(realmId, out DecisionScale.Snapshot shared))
        {
            Empire empire = realm.GetEmpire();
            float authority = empire != null && empire.CoreKingdom == realm ? empire.Legitimacy / 100f : RealmAuthority.Get(realm) / 100f;
            LastDone.TryGetValue(realmId, out var done);
            realms[realmId] = shared = new DecisionScale.Snapshot
            {
                realm = realmId, realmRenown = realm.getRenown(), treasury = StateSettlementSystem.DiscretionaryFunds(realm),
                wars = realm.GetWarsCached(false).Count, busy = busy.TryGetValue(realmId, out int count) ? count : 0,
                authority = Math.Max(0f, Math.Min(1f, authority)), lastByDef = done.byDef, lastByKind = done.byKind
            };
        }
        return new DecisionScale.Snapshot
        {
            actor = actor.getID(), realm = realmId, king = actor.isKing(), leader = actor.isCityLeader(), standing = Standing(actor),
            renown = actor.renown, intelligence = actor.intelligence, diplomacy = actor.diplomacy, warfare = actor.warfare,
            stewardship = actor.stewardship, talent = (int)Talent(actor), money = actor.money, realmRenown = shared.realmRenown,
            treasury = shared.treasury, wars = shared.wars, busy = shared.busy, authority = shared.authority, month = _month, now = now,
            lastByDef = shared.lastByDef, lastByKind = shared.lastByKind, monthSeconds = month,
            unfitByDef = Unfit.TryGetValue(actor.getID(), out double[] unfit) ? unfit : null
        };
    }

    // 平时每帧：到了时候的，按候选从高到低跑决议自己的条件(读活的世界，只能在主线程)，过了且付得起就发起
    private static void Dispatch()
    {
        if (ReadyList.Count == 0) return;
        double now = World.world.getCurWorldTime();
        double stale = now - 2d * Date.getMonthTime();
        // 到了时候的有几个：每帧至少办掉四分之一(快进时一个月只有十来帧，光按时限会越积越多)，其余按时限
        int due = 0;
        for (int i = _readyCursor; i < ReadyList.Count && ReadyList[i].due <= now; i++) due++;
        int must = (due + 3) / 4;
        for (int n = 0; _readyCursor < ReadyList.Count; n++)
        {
            if (n >= must && (n >= DispatchPerTick || Clock.Elapsed.TotalMilliseconds > TickMilliseconds)) break;
            Ready ready = ReadyList[_readyCursor];
            if (ready.due > now) break;
            _readyCursor++;
            // 积了两个月还没轮到的：不办了，下回到时候重新过秤
            if (ready.due < stale) continue;
            Actor actor = World.world.units.get(ready.actor);
            if (actor?.data == null || !actor.isAlive() || actor.plot != null || actor.kingdom?.data == null || actor.kingdom.isRekt()) continue;
            if (Steered != null && Steered(actor)) continue;
            try
            {
                Decide(actor, ready.options);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][决议] 发起出错：{exception.GetType().Name}: {exception.Message}");
            }
        }
        if (_readyCursor >= ReadyList.Count)
        {
            ReadyList.Clear();
            _readyCursor = 0;
        }
        else if (_readyCursor > 256)
        {
            ReadyList.RemoveRange(0, _readyCursor);
            _readyCursor = 0;
        }
    }

    private static void Decide(Actor actor, List<DecisionScale.Option> options)
    {
        var council = new Council { actor = actor.getID(), name = actor.getName(), at = World.world.getCurWorldTime() };
        bool done = false;
        foreach (DecisionScale.Option option in options)
        {
            PlotAsset def = Initiable[option.def];
            string outcome;
            if (done) outcome = "decision_outcome_skipped";
            else if (!def.canBeDoneByRole(actor) || def.check_is_possible != null && !def.check_is_possible(actor))
            {
                outcome = "decision_outcome_unfit";
                NoteUnfit(actor, option.def);
            }
            else if (!CanPay(actor, def.money_cost)) outcome = "decision_outcome_cost";
            else
            {
                bool started = def.try_to_start_advanced != null
                    ? def.try_to_start_advanced(actor, def, true)
                    : World.world.plots.newPlot(actor, def, true) != null;
                if (started && actor.plot != null)
                {
                    Pay(actor, def.money_cost);
                    if (actor.plot.data != null && Cases.TryGetValue(actor.plot.data.id, out DecisionCase item))
                    {
                        item.ratio = option.ratio;
                        item.weights = option.weights;
                    }
                    outcome = "decision_outcome_started";
                    done = true;
                }
                else outcome = "decision_outcome_unfit";
            }
            council.options.Add((def.id, option.ratio, option.weights, outcome));
        }
        LastCouncil[actor.kingdom.data.id] = council;
    }

    private static void NoteUnfit(Actor actor, int def)
    {
        long id = actor.getID();
        if (!Unfit.TryGetValue(id, out double[] unfit))
        {
            Unfit[id] = unfit = new double[Math.Max(1, Initiable.Count)];
            for (int i = 0; i < unfit.Length; i++) unfit[i] = -1d;
        }
        if (def >= 0 && def < unfit.Length) unfit[def] = World.world.getCurWorldTime();
    }

    // 办成了记下(新近办过的阻力)
    private static void NoteDone(DecisionCase item, Actor author)
    {
        Kingdom realm = author?.kingdom;
        if (realm?.data == null) return;
        int index = Initiable.FindIndex(def => def.id == item.def);
        if (!LastDone.TryGetValue(realm.data.id, out var done))
        {
            done = (new double[Math.Max(1, Initiable.Count)], new double[Enum.GetValues(typeof(DecisionKind)).Length]);
            for (int i = 0; i < done.byDef.Length; i++) done.byDef[i] = -1d;
            for (int i = 0; i < done.byKind.Length; i++) done.byKind[i] = -1d;
            LastDone[realm.data.id] = done;
        }
        double now = World.world.getCurWorldTime();
        if (index >= 0 && index < done.byDef.Length) done.byDef[index] = now;
        done.byKind[(int)DecisionScale.KindOf(item.def)] = now;
    }

    // 花费：先出个人钱袋；君主、城主办的是公事，个人不够的由国库(可支配的部分)出
    private static bool Public(Actor actor) => actor.isKing() || actor.isCityLeader();

    private static bool CanPay(Actor actor, int cost) =>
        cost <= 0 || actor.money >= cost || Public(actor) && StateSettlementSystem.DiscretionaryFunds(actor.kingdom) >= cost;

    private static void Pay(Actor actor, int cost)
    {
        if (cost <= 0) return;
        if (actor.money >= cost) actor.spendMoney(cost);
        else if (Public(actor)) TreasurySystem.TrySpend(actor.kingdom, cost, TreasuryCategory.Administration);
    }

    // 在办的决议(天平页看)
    public static IEnumerable<DecisionCase> CasesOf(Kingdom realm)
    {
        if (realm?.data == null) yield break;
        foreach (DecisionCase item in Cases.Values)
            if (World.world.units.get(item.initiator)?.kingdom == realm) yield return item;
    }

    public static float PaceOf(DecisionCase item)
    {
        Actor author = World.world.units.get(item.initiator);
        return author == null ? PaceBase : Pace(author);
    }

    public static int LapseMonthsOf(DecisionCase item)
    {
        Actor author = World.world.units.get(item.initiator);
        return author == null ? LapseMinMonths : LapseMonths(item, author);
    }

    // ───────── 亲自发起(手机席位) ─────────
    public sealed class Advice
    {
        public string id;
        public float ratio;
        public List<ScaleWeight> weights;
        public int cost;
        // null = 办得成；否则是缘由的文字键(decision_outcome_unfit、decision_outcome_cost)
        public string blocked;
    }

    // 这个人此刻能议的事：门槛过了的都过秤(读数从高到低)，前 maxChecks 项(通常就是全部)在主线程验条件与用度
    public static List<Advice> AdviseFor(Actor actor, int maxChecks = 64)
    {
        var list = new List<Advice>();
        if (actor?.data == null || !actor.isAlive() || actor.kingdom?.data == null || actor.kingdom.isRekt() || !Adopt()) return list;
        if (Initiable.Count == 0) return list;
        if (DefInfos.Count != Initiable.Count) BuildDefInfos();
        RefreshLaws();
        double now = World.world.getCurWorldTime();
        DecisionScale.Snapshot snapshot = Take(actor, actor.kingdom, now, Date.getMonthTime(), new Dictionary<long, DecisionScale.Snapshot>(), BusyByRealm());
        foreach (DecisionScale.Option option in DecisionScale.Weigh(snapshot, DefInfos, all: true))
        {
            PlotAsset def = Initiable[option.def];
            var advice = new Advice { id = def.id, ratio = option.ratio, weights = option.weights, cost = def.money_cost };
            if (list.Count >= maxChecks) advice.blocked = "decision_outcome_skipped";
            else if (!def.canBeDoneByRole(actor) || def.check_is_possible != null && !def.check_is_possible(actor)) advice.blocked = "decision_outcome_unfit";
            else if (!CanPay(actor, def.money_cost)) advice.blocked = "decision_outcome_cost";
            list.Add(advice);
        }
        return list;
    }

    // 亲自发起：到了办的时候再验一遍条件与用度，过了就立项付钱；成了返回 null，否则返回缘由的文字键
    public static string Launch(Actor actor, string id)
    {
        PlotAsset def = Def(id);
        if (def == null || !Initiable.Contains(def) || actor?.data == null || !actor.isAlive() || actor.kingdom?.data == null) return "decision_outcome_unfit";
        if (actor.plot != null && actor.plot.isActive()) return "decision_fail_busy";
        if (!def.canBeDoneByRole(actor) || def.check_is_possible != null && !def.check_is_possible(actor)) return "decision_outcome_unfit";
        if (!CanPay(actor, def.money_cost)) return "decision_outcome_cost";
        List<Advice> scale = null;
        bool started = def.try_to_start_advanced != null
            ? def.try_to_start_advanced(actor, def, true)
            : World.world.plots.newPlot(actor, def, true) != null;
        if (!started || actor.plot == null) return "decision_outcome_unfit";
        Pay(actor, def.money_cost);
        if (actor.plot.data != null && Cases.TryGetValue(actor.plot.data.id, out DecisionCase item))
        {
            item.personal = true;
            scale ??= AdviseFor(actor, 0);
            Advice mine = scale.FirstOrDefault(advice => advice.id == id);
            if (mine != null)
            {
                item.ratio = mine.ratio;
                item.weights = mine.weights;
            }
        }
        return null;
    }

    // ───────── 存档 ─────────
    public static List<DecisionCase> Save()
    {
        var list = new List<DecisionCase>();
        foreach (DecisionCase item in Cases.Values)
        {
            Plot plot = World.world?.plots?.get(item.plot);
            if (plot?.data != null)
            {
                item.war = plot.target_war?.data?.id ?? -1;
                item.kingdom = plot.target_kingdom?.data?.id ?? -1;
                item.city = plot.target_city?.data?.id ?? -1;
                item.actor = plot.target_actor?.data?.id ?? -1;
                item.alliance = plot.target_alliance?.data?.id ?? -1;
            }
            list.Add(item);
        }
        return list;
    }

    public static void Load(List<DecisionCase> saved)
    {
        ResetWorldState();
        if (saved != null) Pending.AddRange(saved.Where(item => item?.def != null));
    }

    // 旧档里还在办的模组决议(以前由原版推进，没有记录)：按牌子上的进度接过来
    private static void AdoptOrphans()
    {
        foreach (Plot plot in World.world.plots)
        {
            if (plot?.data == null || !plot.isActive() || Cases.ContainsKey(plot.data.id) || !Manages(plot.getAsset())) continue;
            Actor author = plot.getAuthor();
            if (author == null || !author.isAlive() || author.plot != plot) continue;
            PlotAsset def = plot.getAsset();
            double now = World.world.getCurWorldTime();
            Track(new DecisionCase
            {
                plot = plot.data.id, def = def.id, initiator = author.getID(), started = now,
                next = now + Randy.randomFloat(0f, Date.getMonthTime()),
                effort = Math.Max(MinEffort, def.progress_needed), progress = Math.Max(0f, plot.data.progress_current)
            });
        }
    }

    // 读档后接上牌子：原版谋划还在的(游戏里随原版存档)直接接上，没了的照记下的目标重新立
    private static void Restore()
    {
        int kept = 0, rebuilt = 0, gone = 0, busy = 0;
        foreach (DecisionCase item in Pending)
        {
            PlotAsset def = Def(item.def);
            Actor author = World.world.units.get(item.initiator);
            if (def == null || author == null || !author.isAlive())
            {
                gone++;
                continue;
            }
            Plot plot = World.world.plots.get(item.plot);
            if (plot?.data == null || !plot.isActive() || plot.getAsset() != def || author.plot != plot)
            {
                if (author.plot != null)
                {
                    // 读档后、接上之前别处已重新发起同一项(如征粮的月结)：并进那一件，进度取大的
                    if (author.plot.getAsset() == def && author.plot.data != null && Cases.TryGetValue(author.plot.data.id, out DecisionCase live))
                    {
                        live.progress = Math.Max(live.progress, Math.Min(item.progress, live.effort));
                        live.started = Math.Min(live.started, item.started);
                        kept++;
                    }
                    else busy++;
                    continue;
                }
                _restoring = true;
                try
                {
                    plot = World.world.plots.newPlot(author, def, true);
                }
                finally
                {
                    _restoring = false;
                }
                if (plot?.data == null) continue;
                plot.target_war = item.war >= 0 ? World.world.wars.get(item.war) : null;
                plot.target_kingdom = item.kingdom >= 0 ? World.world.kingdoms.get(item.kingdom) : null;
                plot.target_city = item.city >= 0 ? World.world.cities.get(item.city) : null;
                plot.target_actor = item.actor >= 0 ? World.world.units.get(item.actor) : null;
                plot.target_alliance = item.alliance >= 0 ? World.world.alliances.get(item.alliance) : null;
                item.plot = plot.data.id;
                rebuilt++;
            }
            kept++;
            double now = World.world.getCurWorldTime();
            if (item.next < now || item.next > now + Date.getMonthTime()) item.next = now + Randy.randomFloat(0f, Date.getMonthTime());
            Track(item);
            Show(item, plot, now);
        }
        LogService.LogInfo($"[EmpireCraft][决议] 读档接上在办的决议 {kept}/{Pending.Count} 件(其中重立牌子 {rebuilt} 件；发起人不在 {gone}、另有决议在办 {busy})");
        Pending.Clear();
    }

    public static string Summary() =>
        $"决议：在办 {Cases.Count}，发起 {Started}、办成 {Finished}、作罢 {Lapsed}、失主 {Dropped}" +
        (FinishedBy.Count == 0 ? "" : "(办成的：" + string.Join("、", FinishedBy.OrderByDescending(pair => pair.Value).Take(12).Select(pair => pair.Key + "×" + pair.Value)) + ")");
}

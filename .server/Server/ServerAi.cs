using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ai.behaviours;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Server;

// 国、城、人的行为任务(游戏里原版 AiSystem 的活)：
//   · 每个国家、每座城每帧走一步：任务表里依次的行为，一步一个；行为说"歇一会儿"就歇那么久(世界时间)；
//     一张任务表走完歇一秒再走下一张(原版国家的活计里两张任务之间夹着"歇一秒")；
//   · 城另走一遍原版的城市更新(City.update)，模组挂在上面的每帧逻辑跟着跑；
//   · 人：同游戏里无小人模式下一样，每人约一个月(五秒世界时间)把模组的人物任务整张走一遍(见 DormantActorAI)。
// 任务表照模组登记时的样子取(EmpireCraftBehaviourTaskLibrary：原版的国家、城市、人物"活计"里各添的那几张)。
internal static class ServerAi
{
    // 行为里"歇一会儿"要歇的秒数(见 Shim/World/Ai.cs)；没歇为负
    [ThreadStatic] internal static float Wait;
    private const float BetweenTasks = 1f, ActorSeconds = 5f;
    private const int ActorsPerStep = 60, MaxImmediate = 8;
    // 常速下一步(约二十分之一秒的世界时间)走一个行为。世界走得快、一步跨的世界时间长时照这个数补上，
    // 不然日期走得快、各国各城的行为还是一步一个，相对世界时间就稀了(用户 2026-11-16 "只是单纯的时间变快了")；一步至多补这么多个
    private const float ActionSeconds = 0.05f;
    private const int MaxActionsPerStep = 20, MaxActorsPerStep = 600;
    // 每座城约每 CityUpdateSeconds 世界秒更新一回、各城错开，攒下的世界时间一并交给它(城的行为照攒下的时间补步)。
    // 以前每一步把所有的城各更新一遍：模组挂在城市更新上的城主补位、入城归降、年度扫描……都是"到时候了没有"的轮询，
    // 六百座城一步就是几千次，一步跨的世界时间越短这笔固定的开销越重
    private const float CityUpdateSeconds = 1f;

    private sealed class CityClock
    {
        public float Owed;
        public bool Seeded;
    }

    private static readonly ConditionalWeakTable<City, CityClock> CityClocks = new();

    private sealed class State
    {
        public int Task, Action;
        public object Current;
        public float Timer;
    }

    private static readonly ConditionalWeakTable<object, State> States = new();
    private static List<BehaviourTaskKingdom> _kingdomTasks;
    private static List<BehaviourTaskCity> _cityTasks;
    private static List<BehaviourTaskActor> _actorTasks;
    private static readonly Dictionary<long, double> ActorLast = new();
    private static int _actorCursor;
    private static readonly HashSet<string> Reported = new();
    internal static int Steps, Failures;

    private static List<TTask> TasksOf<TTask>(IEnumerable<string> ids, AssetLibrary<TTask> library) where TTask : Asset =>
        (ids ?? Enumerable.Empty<string>()).Where(library.has).Select(library.get).Distinct().ToList();

    private static void EnsureTasks()
    {
        if (_kingdomTasks != null) return;
        _kingdomTasks = TasksOf(AssetManager.job_kingdom.t?.ShimTasks, AssetManager.tasks_kingdom);
        _cityTasks = TasksOf(AssetManager.job_city.t?.ShimTasks, AssetManager.tasks_city);
        _actorTasks = TasksOf(new[] { DormantActorAI.ModBehTask }, AssetManager.tasks_actor);
        Console.WriteLine("  行为任务：国家 " + _kingdomTasks.Sum(task => task.list.Count) + " 步(" + _kingdomTasks.Count + " 张)，城市 " +
                          _cityTasks.Sum(task => task.list.Count) + " 步，人物 " + _actorTasks.Sum(task => task.list.Count) + " 步");
    }

    // 换世界：各对象的进度作废
    internal static void Reset()
    {
        BirthdayQueue.Clear();
        _birthdayYear = int.MinValue;
        ActorLast.Clear();
        _actorCursor = 0;
        EmptyClans.Clear();
        _sweptMonth = double.MinValue;
    }

    // 主循环每一步(世界在走的时候)
    internal static void Tick(MapBox world, float elapsed)
    {
        EnsureTasks();
        // 行为里用的"世界"(原版是 MapBox.instance)
        BehaviourActionBase<Kingdom>.world = BehaviourActionBase<City>.world = BehaviourActionBase<Actor>.world = world;
        using (FrameProfiler.Measure("服务器·国家行为"))
            foreach (Kingdom kingdom in world.kingdoms.list.ToArray())
            {
                if (kingdom?.data == null || !kingdom.isAlive() || kingdom.wild) continue;
                if (kingdom.data.timer_new_king > 0f) kingdom.data.timer_new_king -= elapsed;
                Step<Kingdom, BehaviourActionKingdom, BehaviourTaskKingdom>(kingdom, _kingdomTasks, elapsed);
            }
        using (FrameProfiler.Measure("服务器·城市更新"))
        foreach (City city in world.cities.list.ToArray())
        {
            if (city?.data == null || !city.isAlive()) continue;
            CityClock clock = CityClocks.GetOrCreateValue(city);
            if (!clock.Seeded)
            {
                clock.Seeded = true;
                clock.Owed = (city.id & 7) / 8f * CityUpdateSeconds;
            }
            clock.Owed += elapsed;
            if (clock.Owed < CityUpdateSeconds) continue;
            float owed = clock.Owed;
            clock.Owed = 0f;
            try
            {
                city.update(owed);
            }
            catch (Exception exception)
            {
                Report("City.update", exception);
            }
        }
        using (FrameProfiler.Measure("服务器·人物行为")) StepActors(world, elapsed);
        using (FrameProfiler.Measure("服务器·过生日")) Birthdays(world, elapsed);
        if (++_sweep % SweepEvery == 0)
        {
            RemoveDeadKingdoms(world);
            // 结束了的仗清出名册(同原版隔一会儿查一遍)
            world.wars.ShimSweepEnded();
        }
        using (FrameProfiler.Measure("服务器·清空族")) SweepClans(world);
    }

    // 没有人的宗族撤掉(原版也是隔一会儿查一遍；以前没人查，从人口里生成的人各立一族、人并回人口后族还留着，
    // 跑到三百年攒下十几万个空族，存档、内存、凡是要把宗族过一遍的地方都被它拖着)。
    // 每个世界月看一回；连着两回都空着的才撤(换代那一阵族里可能一时没有在世的人)，眼下是哪个帝国皇族的不撤
    private static readonly HashSet<long> EmptyClans = new();
    private static double _sweptMonth = double.MinValue;
    internal static int ClansSwept;

    private static void SweepClans(MapBox world)
    {
        double now = world.getCurWorldTime();
        if (now >= _sweptMonth && now - _sweptMonth < Date.SecondsPerMonth) return;
        _sweptMonth = now;
        var royal = new HashSet<Clan>();
        try
        {
            foreach (EmpireCraft.Scripts.Layer.Empire empire in EmpireCraft.Scripts.ModClass.EMPIRE_MANAGER)
                if (empire?.EmpireClan != null) royal.Add(empire.EmpireClan);
        }
        catch (Exception exception)
        {
            Report("清空族", exception);
            return;
        }
        var still = new HashSet<long>();
        ClansSwept += world.clans.ShimRemoveAll(clan =>
        {
            if (clan.units.Count > 0 || royal.Contains(clan)) return false;
            if (EmptyClans.Contains(clan.id)) return true;
            still.Add(clan.id);
            return false;
        });
        EmptyClans.Clear();
        foreach (long id in still) EmptyClans.Add(id);
    }

    // 过生日(游戏里原版每年给每个人调一次 Actor.updateAge，模组挂在上面的老死、夭折、名望年结跟着跑)：
    // 服务器以前没人调，人永远不老死，跑久了满世界三百来岁的人。新的一年起把在世的人排进队，每步过 BirthdaysPerStep 个
    private const int BirthdaysPerStep = 200;
    private static readonly Queue<Actor> BirthdayQueue = new();
    private static int _birthdayYear = int.MinValue;

    private static void Birthdays(MapBox world, float elapsed)
    {
        int year = Date.getCurrentYear();
        if (_birthdayYear == int.MinValue) _birthdayYear = year;
        else if (year != _birthdayYear)
        {
            _birthdayYear = year;
            foreach (Actor unit in world.units.getSimpleList()) if (unit?.data != null && unit.isAlive()) BirthdayQueue.Enqueue(unit);
        }
        // 一年之内要过完：一步跨的世界时间长就多过几个
        int birthdays = Math.Max(BirthdaysPerStep, (int)Math.Ceiling(BirthdayQueue.Count * elapsed / (Date.SecondsPerYear * 0.5d)));
        for (int i = 0; i < birthdays && BirthdayQueue.Count > 0; i++)
        {
            Actor actor = BirthdayQueue.Dequeue();
            if (actor?.data == null || !actor.isAlive()) continue;
            try
            {
                actor.updateAge();
            }
            catch (Exception exception)
            {
                Report("Actor.updateAge", exception);
            }
        }
    }

    private const int SweepEvery = 25;
    private static int _sweep;

    // 没城没人的国撤掉(原版每隔一会儿查一遍)：先退出战事、同盟，再从国册里除名(模组挂在除名上的善后照常跑)
    private static void RemoveDeadKingdoms(MapBox world)
    {
        foreach (Kingdom kingdom in world.kingdoms.list.ToArray())
        {
            if (kingdom?.data == null || !kingdom.isAlive() || kingdom.wild) continue;
            bool ready;
            try
            {
                ready = kingdom.isReadyForRemoval();
            }
            catch (Exception exception)
            {
                Report("Kingdom.isReadyForRemoval", exception);
                continue;
            }
            if (!ready) continue;
            try
            {
                foreach (War war in world.wars.getWars(kingdom).ToArray()) war.removeFromWar(kingdom, true);
                kingdom.getAlliance()?.leave(kingdom);
                world.kingdoms.removeObject(kingdom);
            }
            catch (Exception exception)
            {
                Report("撤国", exception);
            }
        }
    }

    internal static void StepCity(City city, float elapsed) => Step<City, BehaviourActionCity, BehaviourTaskCity>(city, _cityTasks, elapsed);

    // 一个对象走一步(原版 AiSystem.run 的走法)
    private static void Step<TObject, TAction, TTask>(TObject owner, List<TTask> tasks, float elapsed)
        where TAction : BehaviourActionBase<TObject> where TTask : BehaviourTaskBase<TAction>
    {
        if (tasks == null || tasks.Count == 0) return;
        State state = States.GetOrCreateValue(owner);
        // 这一步的世界时间：歇着的从里头扣，够走几个行为就走几个(每个算 ActionSeconds，至少一个、至多 MaxActionsPerStep 个)
        float left = elapsed;
        for (int done = 0; done < MaxActionsPerStep; done++)
        {
            if (state.Timer > 0f)
            {
                if (state.Timer > left)
                {
                    state.Timer -= left;
                    return;
                }
                left -= state.Timer;
                state.Timer = 0f;
            }
            if (done > 0 && left < ActionSeconds) return;
            StepOnce<TObject, TAction, TTask>(owner, tasks, state);
            left -= ActionSeconds;
        }
    }

    private static void StepOnce<TObject, TAction, TTask>(TObject owner, List<TTask> tasks, State state)
        where TAction : BehaviourActionBase<TObject> where TTask : BehaviourTaskBase<TAction>
    {
        for (int guard = 0; guard < MaxImmediate; guard++)
        {
            if (state.Current is not TTask task)
            {
                state.Current = task = tasks[state.Task++ % tasks.Count];
                state.Action = 0;
            }
            if (state.Action >= task.list.Count)
            {
                state.Current = null;
                state.Timer = BetweenTasks;
                return;
            }
            TAction action = task.list[state.Action];
            Wait = -1f;
            BehResult result;
            Steps++;
            // 排查时(--profile)各种行为分开计时
            long began = FrameProfiler.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            try
            {
                result = action.execute(owner);
            }
            catch (Exception exception)
            {
                Report(action.id, exception);
                result = BehResult.Continue;
            }
            if (FrameProfiler.Enabled) FrameProfiler.Record(Label(action.id), System.Diagnostics.Stopwatch.GetTimestamp() - began);
            if (Wait >= 0f) state.Timer = Wait;
            switch (result)
            {
                case BehResult.Continue:
                    state.Action++;
                    break;
                case BehResult.Stop:
                    state.Current = null;
                    break;
                case BehResult.StepBack:
                    state.Action = Math.Max(0, state.Action - 1);
                    break;
                case BehResult.RestartTask:
                    state.Action = 0;
                    break;
                case BehResult.ImmediateRun:
                    continue;
            }
            return;
        }
    }

    // 人物：轮着来，每一步至多看 ActorsPerStep 个；到了时辰的把模组的人物任务整张走一遍
    private static void StepActors(MapBox world, float elapsed)
    {
        if (_actorTasks.Count == 0) return;
        List<Actor> actors = world.units.getSimpleList();
        int count = actors.Count;
        if (count == 0) return;
        double now = world.getCurWorldTime();
        // 每人约一个月轮到一回：一步跨的世界时间长就多看几个，不然一轮下来要好几个月
        int perStep = Math.Clamp((int)Math.Ceiling(count * elapsed / ActorSeconds), ActorsPerStep, MaxActorsPerStep);
        for (int i = 0; i < Math.Min(perStep, count); i++)
        {
            // 走任务时会有人死、被并回人口，名单当场变短：每回按眼下的人数取
            if (actors.Count == 0) break;
            Actor actor = actors[(_actorCursor + i) % actors.Count];
            if (actor?.data == null || !actor.isAlive() || actor.city == null || actor.kingdom == null || actor.kingdom.wild || !actor.isSapient()) continue;
            if (ActorLast.TryGetValue(actor.id, out double last) && now - last < ActorSeconds && now >= last) continue;
            ActorLast[actor.id] = now;
            foreach (BehaviourTaskActor task in _actorTasks)
            foreach (BehaviourActionActor action in task.list)
            {
                if (!actor.isAlive()) break;
                Steps++;
                try
                {
                    if (action.execute(actor) == BehResult.Stop) break;
                }
                catch (Exception exception)
                {
                    Report(action.id, exception);
                }
            }
        }
        _actorCursor = (_actorCursor + perStep) % Math.Max(1, count);
        if (ActorLast.Count > count * 4 + 1000) ActorLast.Clear();
    }

    private static readonly Dictionary<string, string> Labels = new();

    private static string Label(string id)
    {
        if (!Labels.TryGetValue(id ?? "", out string label)) Labels[id ?? ""] = label = "行为·" + id;
        return label;
    }

    internal static void Report(string where, Exception exception)
    {
        Failures++;
        string key = where + ":" + exception.GetType().Name;
        lock (Reported)
            if (Reported.Count > 200 || !Reported.Add(key)) return;
        Console.Error.WriteLine("[warn] 行为 " + where + " 出错: " + exception.GetType().Name + ": " + exception.Message +
                                (Shim.Verbose ? "\n" + exception.StackTrace : ""));
    }
}

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
        ActorLast.Clear();
        _actorCursor = 0;
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
            try
            {
                city.update(elapsed);
            }
            catch (Exception exception)
            {
                Report("City.update", exception);
            }
        }
        using (FrameProfiler.Measure("服务器·人物行为")) StepActors(world);
        if (++_sweep % SweepEvery == 0) RemoveDeadKingdoms(world);
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
        if (state.Timer > 0f)
        {
            state.Timer -= elapsed;
            return;
        }
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
            try
            {
                result = action.execute(owner);
            }
            catch (Exception exception)
            {
                Report(action.id, exception);
                result = BehResult.Continue;
            }
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
    private static void StepActors(MapBox world)
    {
        if (_actorTasks.Count == 0) return;
        List<Actor> actors = world.units.getSimpleList();
        int count = actors.Count;
        if (count == 0) return;
        double now = world.getCurWorldTime();
        for (int i = 0; i < Math.Min(ActorsPerStep, count); i++)
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
        _actorCursor = (_actorCursor + ActorsPerStep) % Math.Max(1, count);
        if (ActorLast.Count > count * 4 + 1000) ActorLast.Clear();
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

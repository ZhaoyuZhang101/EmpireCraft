using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.HelperFunc;

// 后台只能收到复制的数值，不能访问城市、单位、存档人口组或 Unity API。
public readonly struct PopulationGroupValue
{
    public readonly float Size;
    public readonly int Named, Class;
    public float Background => Math.Max(0f, Size - Named);
    public PopulationGroupValue(float size, int named, int socialClass)
    { Size = size; Named = named; Class = socialClass; }
}

public sealed class PopulationNumericInput
{
    public readonly PopulationGroupValue[] Groups;
    public readonly float PeoplePerSlot, Total, Capacity, BirthRate, DeathRate, Years;
    public readonly bool Growth, Famine;
    public PopulationNumericInput(PopulationGroupValue[] groups, float peoplePerSlot, bool growth = false,
        float total = 0, float capacity = 0, float birthRate = 0, float deathRate = 0, float years = 0, bool famine = false)
    {
        Groups = (PopulationGroupValue[])groups.Clone();
        PeoplePerSlot = peoplePerSlot;
        Growth = growth; Total = total; Capacity = capacity;
        BirthRate = birthRate; DeathRate = deathRate; Years = years; Famine = famine;
    }
}

public sealed class PopulationNumericResult
{
    public float BackgroundHouseholds;
    public readonly Dictionary<int, float> Workforce = new();
    public float[] Sizes;
    public int ThreadId;
}

public static class PopulationMathWorkers
{
    public const int MaximumPending = 64;
    public static int WorkerCount => Math.Min(4, Math.Max(1, Environment.ProcessorCount - 1));
    private static readonly ConcurrentQueue<Job> Queue = new();
    private static readonly SemaphoreSlim Ready = new(0);
    private static readonly object StartLock = new();
    private static bool _started;
    private static int _pending, _generation;
    public static int PendingCount => Volatile.Read(ref _pending);

    public sealed class Job
    {
        internal readonly PopulationNumericInput Input;
        internal readonly int Generation;
        internal readonly TaskCompletionSource<PopulationNumericResult> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Job(PopulationNumericInput input, int generation) { Input = input; Generation = generation; }
        public bool IsCompleted => Completion.Task.IsCompleted;
        public bool TryGetResult(out PopulationNumericResult result)
        {
            // 仅在完成后取结果；游戏主线程从不等待工作线程。
            result = Completion.Task.Status == TaskStatus.RanToCompletion ? Completion.Task.Result : null;
            if (Completion.Task.IsFaulted) _ = Completion.Task.Exception;
            return result != null;
        }
    }

    public static void Initialize()
    {
        lock (StartLock)
        {
            if (_started) return;
            _started = true;
            for (int i = 0; i < WorkerCount; i++)
                new Thread(Run) { IsBackground = true, Name = "EmpireCraft population " + i }.Start();
        }
    }

    public static Job Submit(PopulationNumericInput input)
    {
        Initialize();
        if (Interlocked.Increment(ref _pending) > MaximumPending)
        { Interlocked.Decrement(ref _pending); return null; }
        var job = new Job(input, Volatile.Read(ref _generation));
        Queue.Enqueue(job);
        Ready.Release();
        return job;
    }

    public static void Reset()
    {
        Interlocked.Increment(ref _generation);
        while (Queue.TryDequeue(out Job job))
        { job.Completion.TrySetCanceled(); Interlocked.Decrement(ref _pending); }
    }

    private static void Run()
    {
        for (;;)
        {
            Ready.Wait();
            if (!Queue.TryDequeue(out Job job)) continue;
            try
            {
                if (job.Generation != Volatile.Read(ref _generation)) job.Completion.TrySetCanceled();
                else
                {
                    PopulationNumericResult result = Compute(job.Input);
                    if (job.Generation == Volatile.Read(ref _generation)) job.Completion.TrySetResult(result);
                    else job.Completion.TrySetCanceled();
                }
            }
            catch (Exception exception) { job.Completion.TrySetException(exception); }
            finally { Interlocked.Decrement(ref _pending); }
        }
    }

    public static PopulationNumericResult Compute(PopulationNumericInput input)
    {
        var result = new PopulationNumericResult { ThreadId = Thread.CurrentThread.ManagedThreadId };
        float background = 0f;
        foreach (PopulationGroupValue group in input.Groups)
        {
            background += group.Background;
            float amount = group.Background / input.PeoplePerSlot;
            if (amount <= 0f) continue;
            result.BackgroundHouseholds += amount;
            result.Workforce.TryGetValue(group.Class, out float workers);
            result.Workforce[group.Class] = workers + amount * 0.6f;
        }
        if (!input.Growth) return result;
        const float InflowShare = 0.04f;
        result.Sizes = new float[input.Groups.Length];
        for (int i = 0; i < result.Sizes.Length; i++) result.Sizes[i] = input.Groups[i].Size;
        float births = input.Total * input.BirthRate * input.Years;
        // 城市主要靠四乡移民填满：不闹饥荒时，每年至少迁入空余住房的 InflowShare(城小房多时远比自然出生快)，
        // 再保底半户
        if (input.BirthRate > 0f && !input.Famine)
            births = Math.Max(births, Math.Max(0.5f * input.PeoplePerSlot,
                Math.Max(0f, input.Capacity - input.Total) * InflowShare) * input.Years);
        float change = births - background * input.DeathRate * input.Years;
        if (change > 0f) change = Math.Min(change, Math.Max(0f, input.Capacity - input.Total));
        if (input.Total > input.Capacity)
            change -= Math.Min(background, (input.Total - input.Capacity) * 0.25f * input.Years);
        change = Math.Max(change, -background);
        if (Math.Abs(change) < 0.001f) return result;
        if (background <= 0f)
        {
            float named = 0f;
            foreach (PopulationGroupValue group in input.Groups) named += group.Named;
            if (named <= 0f || change <= 0f) return result;
            for (int i = 0; i < result.Sizes.Length; i++) result.Sizes[i] += change * input.Groups[i].Named / named;
        }
        else
        {
            float ratio = change / background;
            for (int i = 0; i < result.Sizes.Length; i++)
            {
                PopulationGroupValue group = input.Groups[i];
                result.Sizes[i] = Math.Max(group.Named, group.Size + group.Background * ratio);
            }
        }
        return result;
    }
}

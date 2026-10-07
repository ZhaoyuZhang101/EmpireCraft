using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;

namespace EmpireCraft.Scripts.GeneralSystems;

// 主线程取得政绩快照。四个人口工作线程只排序 ID 与数值；不接触 Actor、城市或 Unity。
public static class PopulationJurenRanking
{
    private sealed class Request
    {
        public Kingdom Owner;
        public PopulationNumericInput Input;
        public PopulationMathWorkers.Job Job;
    }
    private static ConditionalWeakTable<City, Request> Requests = new();
    private static City _activeCity;
    private static HashSet<long> _kept;
    public static int WorkerResults { get; private set; }
    public static int FallbackResults { get; private set; }

    public static void Reset()
    {
        Requests = new();
        EndPass();
        WorkerResults = FallbackResults = 0;
    }

    private static PopulationRankValue[] Capture(City city)
    {
        var values = new List<PopulationRankValue>();
        foreach (Actor actor in city.units)
            if (actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.hasTrait("juren"))
                values.Add(new PopulationRankValue(actor.id, actor.GetIdentity()?.TotalPerformance ?? 0d));
        return values.ToArray();
    }

    public static void Prewarm(IEnumerable<City> cities)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        int visited = 0;
        foreach (City city in cities)
        {
            if (++visited > 4 || !SimulationFrameBudget.HasTime) break;
            if (city?.data == null || city.isRekt() || city.units == null || city.kingdom == null ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city) ||
                Requests.TryGetValue(city, out _)) continue;
            var input = new PopulationNumericInput(Capture(city), 5);
            Requests.Add(city, new Request { Owner = city.kingdom, Input = input,
                Job = input.Candidates.Length > 5 ? PopulationMathWorkers.Submit(input) : null });
        }
    }

    public static void BeginPass(City city)
    {
        EndPass();
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.units == null) return;
        using var timing = FrameProfiler.Measure("虚拟人口·举人排名");
        PopulationRankValue[] current = Capture(city);
        PopulationNumericResult result = null;
        if (Requests.TryGetValue(city, out Request request))
        {
            Requests.Remove(city);
            bool matches = request.Owner == city.kingdom && request.Input.Candidates.Length == current.Length;
            for (int i = 0; matches && i < current.Length; i++)
                matches = request.Input.Candidates[i].Id == current[i].Id &&
                          request.Input.Candidates[i].Score.Equals(current[i].Score);
            if (matches && request.Job != null) request.Job.TryGetResult(out result);
        }
        // 队列满、未完成、迁城或政绩变化时直接按当前快照排一次；绝不等待后台。
        if (result == null)
        {
            FallbackResults++;
            result = PopulationMathWorkers.Compute(new PopulationNumericInput(current, 5));
        }
        else WorkerResults++;
        _activeCity = city;
        _kept = new HashSet<long>(result.RankedIds);
    }

    public static bool TryIsKept(Actor actor, out bool kept)
    {
        kept = false;
        if (_activeCity == null || actor.city != _activeCity || _kept == null) return false;
        kept = _kept.Contains(actor.id);
        return true;
    }

    public static void EndPass() { _activeCity = null; _kept = null; }
}

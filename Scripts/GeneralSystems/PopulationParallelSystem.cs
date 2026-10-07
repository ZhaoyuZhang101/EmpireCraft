using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 此类仅在主线程调用。城市引用留在这里，工作队列里只有独立的数值快照。
public static class PopulationParallelSystem
{
    private sealed class Request
    {
        public Kingdom Owner;
        public CityPopulationData Data;
        public double At;
        public PopulationNumericInput Input;
        public PopulationMathWorkers.Job Job;
    }
    private static readonly Dictionary<City, Request> Workforce = new();
    private static readonly Dictionary<City, Request> Growth = new();
    private static bool _announced;

    public static void ResetWorldState()
    {
        Workforce.Clear();
        Growth.Clear();
        PopulationMathWorkers.Reset();
    }

    private static PopulationMathWorkers.Job Submit(PopulationNumericInput input)
    {
        if (!_announced)
        {
            _announced = true;
            LogService.LogInfo($"[EmpireCraft][多核人口] 虚拟人口计算启用 {PopulationMathWorkers.WorkerCount} 个后台线程；游戏对象仍由主线程更新");
        }
        return PopulationMathWorkers.Submit(input);
    }

    private static PopulationGroupValue[] CopyGroups(CityPopulationData data)
    {
        var values = new PopulationGroupValue[data.groups.Count];
        for (int i = 0; i < values.Length; i++)
        {
            PopGroup group = data.groups[i];
            values[i] = new PopulationGroupValue(group.size, group.named, (int)group.social_class);
        }
        return values;
    }

    private static bool Matches(PopulationNumericInput input, CityPopulationData data)
    {
        if (input.Groups.Length != data.groups.Count) return false;
        for (int i = 0; i < input.Groups.Length; i++)
        {
            PopulationGroupValue old = input.Groups[i];
            PopGroup group = data.groups[i];
            if (old.Size != group.size || old.Named != group.named || old.Class != (int)group.social_class) return false;
        }
        return true;
    }

    public static void PrewarmWorkforce(IEnumerable<City> cities, double now)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        int visited = 0;
        foreach (City city in cities)
        {
            if (++visited > 4 || !SimulationFrameBudget.HasTime) break;
            PrepareWorkforce(city, now);
        }
    }

    public static void PrepareWorkforce(City city, double now)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.data == null || city.isRekt() || city.kingdom == null ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city)) return;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data.last_economy < 0d || now < data.last_economy || Date.getMonthsSince(data.last_economy) <= 0) return;
        float perSlot = CityPopulationSystem.PeoplePerSlot(city);
        if (Workforce.TryGetValue(city, out Request old) && ReferenceEquals(old.Data, data) && old.Owner == city.kingdom &&
            old.Input.PeoplePerSlot == perSlot && Matches(old.Input, data)) return;
        var input = new PopulationNumericInput(CopyGroups(data), perSlot);
        Workforce[city] = new Request { Owner = city.kingdom, Data = data, Input = input, Job = Submit(input) };
    }

    public static PopulationNumericResult GetWorkforce(City city, CityPopulationData data, float perSlot)
    {
        if (Workforce.TryGetValue(city, out Request request))
        {
            Workforce.Remove(city);
            if (CityPopulationSystem.AbstractPopulationEnabled && ReferenceEquals(request.Data, data) && request.Owner == city.kingdom &&
                request.Input.PeoplePerSlot == perSlot && Matches(request.Input, data) &&
                request.Job != null && request.Job.TryGetResult(out PopulationNumericResult result)) return result;
        }
        // 未完成或快照过期时，直接计算当前小数组，不阻塞游戏线程。
        return PopulationMathWorkers.Compute(new PopulationNumericInput(CopyGroups(data), perSlot));
    }

    private static PopulationNumericInput GrowthInput(City city, CityPopulationData data, float years)
    {
        CityPopulationSystem.GrowthFactors factors = CityPopulationSystem.GetGrowthFactors(city, data);
        return new PopulationNumericInput(CopyGroups(data), CityPopulationSystem.PeoplePerSlot(city), true,
            factors.Total, factors.Capacity, factors.BirthRate, factors.DeathRate, years, factors.Famine);
    }

    public static void StartGrowth(City city, CityPopulationData data, double now, float years)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        PopulationNumericInput input = GrowthInput(city, data, years);
        Growth[city] = new Request { Owner = city.kingdom, Data = data, At = now, Input = input, Job = Submit(input) };
        if (Growth[city].Job == null) TryCompleteGrowth(city, true);
    }

    public static bool TryCompleteGrowth(City city, bool flush = false)
    {
        if (!Growth.TryGetValue(city, out Request request)) return true;
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.data == null || city.isRekt() || city.kingdom != request.Owner ||
            !ReferenceEquals(CityPopulationSystem.Get(city), request.Data) || request.Data.last_growth != request.At ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city))
        { Growth.Remove(city); return true; }
        if (!flush && request.Job != null && !request.Job.IsCompleted) return false;
        Growth.Remove(city);
        bool matches = Matches(request.Input, request.Data) && request.Input.PeoplePerSlot == CityPopulationSystem.PeoplePerSlot(city);
        PopulationNumericResult result = null;
        if (matches && request.Job != null) request.Job.TryGetResult(out result);
        if (result == null)
            result = PopulationMathWorkers.Compute(matches ? request.Input : GrowthInput(city, request.Data, request.Input.Years));
        // 只在主线程写回，过期的数据组会重算，避免覆盖征兵、迁移或理念变动。
        for (int i = 0; i < result.Sizes.Length; i++) request.Data.groups[i].size = result.Sizes[i];
        return true;
    }

    public static void Discard(City city)
    {
        if (city == null) return;
        Workforce.Remove(city);
        Growth.Remove(city);
    }

    public static List<City> FlushGrowth()
    {
        var cities = new List<City>(Growth.Keys);
        foreach (City city in cities) TryCompleteGrowth(city, true);
        return cities;
    }
}

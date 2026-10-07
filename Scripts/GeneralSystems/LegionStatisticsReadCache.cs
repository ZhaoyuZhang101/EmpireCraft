using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 主线程统计读缓存。仍逐次核对生命值、满编人数和来源的实际内容，不按时间延迟伤亡。
// 返回只读的当前视图；统计当场消费，不能交给后台或作为历史快照保存。
public static class LegionStatisticsReadCache
{
    public readonly struct Contribution
    {
        public readonly string Culture;
        public readonly float Size;
        internal Contribution(string culture, float size) { Culture = culture; Size = size; }
    }
    private sealed class Entry
    {
        internal object Data, Extra, Asset, Origins;
        internal int Health, Maximum;
        internal float Size, Full;
        internal LegionPopulationContribution[] Sources = Array.Empty<LegionPopulationContribution>();
        internal Contribution[] Values = Array.Empty<Contribution>();
        internal IReadOnlyList<Contribution> View = Array.Empty<Contribution>();
    }
    private static ConditionalWeakTable<Actor, Entry> Entries = new();
    private static object _world;
    public static long Hits { get; private set; }
    public static long Builds { get; private set; }

    public static void Reset()
    {
        Entries = new(); _world = World.world; Hits = Builds = 0;
    }

    public static IReadOnlyList<Contribution> Read(Actor actor)
    {
        if (!ReferenceEquals(_world, World.world)) Reset();
        if (!CityPopulationSystem.AbstractPopulationEnabled || actor?.data == null ||
            actor.isRekt() || !actor.isAlive()) return Array.Empty<Contribution>();
        ActorExtension.ActorExtraData extra = actor.GetOrCreate();
        if (extra.legion_size <= 1f || CityPopulationSystem.IsVehicle(actor)) return Array.Empty<Contribution>();
        int health = 0, maximum = 0;
        bool readable = true;
        try { health = actor.getHealth(); maximum = actor.getMaxHealth(); }
        catch { readable = false; }
        Entry entry = Entries.GetOrCreateValue(actor);
        List<LegionPopulationContribution> origins = extra.legion_population;
        bool same = readable && ReferenceEquals(entry.Data, actor.data) &&
            ReferenceEquals(entry.Extra, extra) && ReferenceEquals(entry.Asset, actor.asset) &&
            ReferenceEquals(entry.Origins, origins) && entry.Health == health && entry.Maximum == maximum &&
            entry.Size.Equals(extra.legion_size) && entry.Full.Equals(extra.legion_full) &&
            origins != null && origins.Count == entry.Sources.Length;
        if (same)
            for (int i = 0; i < origins.Count; i++)
            {
                var origin = origins[i];
                if (!ReferenceEquals(origin, entry.Sources[i]) || origin != null &&
                    (origin.culture != entry.Values[i].Culture || !origin.size.Equals(entry.Values[i].Size)))
                { same = false; break; }
            }
        if (same) { Hits++; return entry.View; }

        CityPopulationSystem.LegionAlive(actor);
        origins = extra.legion_population;
        int count = origins?.Count ?? 0;
        if (entry.Sources.Length != count)
        {
            entry.Sources = new LegionPopulationContribution[count];
            entry.Values = new Contribution[count];
            entry.View = Array.AsReadOnly(entry.Values);
        }
        for (int i = 0; i < count; i++)
        {
            var origin = origins[i];
            entry.Sources[i] = origin;
            entry.Values[i] = new Contribution(origin?.culture, origin?.size ?? 0f);
        }
        entry.Data = actor.data; entry.Extra = extra; entry.Asset = actor.asset;
        entry.Origins = origins; entry.Health = health; entry.Maximum = maximum;
        entry.Size = extra.legion_size; entry.Full = extra.legion_full;
        Builds++;
        return entry.View;
    }
}

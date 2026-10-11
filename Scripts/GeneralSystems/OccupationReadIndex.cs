using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 持久化字典仍为权威数据；这里只缓存不可修改的读取视图。
public static class OccupationReadIndex
{
    public sealed class Group
    {
        public readonly Kingdom Occupier;
        public readonly IReadOnlyList<TileZone> Zones;
        internal Group(Kingdom occupier, List<TileZone> zones) { Occupier = occupier; Zones = zones.AsReadOnly(); }
    }
    private sealed class Entry
    {
        internal object World, Source;
        internal long Revision = -1;
        internal IReadOnlyList<Group> Groups;
    }
    private static ConditionalWeakTable<City, Entry> Entries = new();
    public static long Revision { get; private set; }
    public static long Builds { get; private set; }
    public static long Hits { get; private set; }
    private static bool _active;

    public static void Invalidate(City city)
    {
        if (city == null) return;
        if (!CityPopulationSystem.AbstractPopulationEnabled) { if (_active) Reset(); return; }
        city.GetOrCreate().occupation_revision++;
        Revision++;
    }

    public static void Reset()
    {
        Entries = new(); Revision++; Builds = Hits = 0;
        ControlEntries = new();
        ControlBuilds = ControlHits = 0; _active = false;
    }

    public static IReadOnlyList<Group> Read(City city)
    {
        var data = city.GetOrCreate();
        _active = true;
        var entry = Entries.GetOrCreateValue(city);
        if (entry.Groups != null && ReferenceEquals(entry.World, World.world) &&
            ReferenceEquals(entry.Source, data.OccupiedStatus) && entry.Revision == data.occupation_revision)
        { Hits++; return entry.Groups; }
        var groups = new List<Group>();
        if (data.OccupiedStatus != null)
            foreach (var pair in data.OccupiedStatus)
            {
                Kingdom kingdom = World.world.kingdoms.get(pair.Key);
                if (kingdom == null || pair.Value == null) continue;
                var zones = new List<TileZone>(pair.Value.Count);
                foreach (int id in pair.Value)
                {
                    TileZone zone = World.world.zone_calculator.getZoneByID(id);
                    if (zone != null) zones.Add(zone);
                }
                groups.Add(new Group(kingdom, zones));
            }
        entry.World = World.world; entry.Source = data.OccupiedStatus; entry.Revision = data.occupation_revision;
        entry.Groups = groups.AsReadOnly(); Builds++;
        return entry.Groups;
    }

    public readonly struct Control
    {
        public readonly int Controlled, Total;
        public Control(int controlled, int total) { Controlled = controlled; Total = total; }
    }
    private sealed class ControlEntry
    {
        internal object World, Scope;
        internal int Frame = -1;
        internal long Revision;
        internal long Membership;
        internal Kingdom[] Attackers;
        internal Control Value;
    }
    private static ConditionalWeakTable<War, ControlEntry> ControlEntries = new();
    public static long ControlBuilds { get; private set; }
    public static long ControlHits { get; private set; }

    public static Control ReadControl(War war, object scope, Func<Control> calculate, Func<long> membership = null)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return calculate();
        _active = true;
        long signature = membership?.Invoke() ?? 0L;
        var entry = ControlEntries.GetOrCreateValue(war);
        bool sameAttackers = entry.Attackers != null && entry.Attackers.Length == war._list_attackers.Count;
        if (sameAttackers)
            for (int i = 0; i < entry.Attackers.Length; i++)
                if (entry.Attackers[i] != war._list_attackers[i]) { sameAttackers = false; break; }
        if (entry.Frame == Time.frameCount && entry.Revision == Revision && ReferenceEquals(entry.World, World.world) &&
            ReferenceEquals(entry.Scope, scope) && entry.Membership == signature && sameAttackers)
        { ControlHits++; return entry.Value; }
        Control value = calculate();
        entry.World = World.world; entry.Scope = scope; entry.Frame = Time.frameCount; entry.Revision = Revision;
        entry.Membership = signature;
        entry.Attackers = new Kingdom[war._list_attackers.Count]; war._list_attackers.CopyTo(entry.Attackers);
        entry.Value = value; ControlBuilds++;
        return value;
    }

    public static long Membership(IEnumerable<City> cities)
    {
        long hash = 17;
        if (cities != null)
            foreach (City city in cities)
                unchecked
                {
                    hash = hash * 31 + (city == null ? 0 : RuntimeHelpers.GetHashCode(city));
                    hash = hash * 31 + (city?.kingdom == null ? 0 : RuntimeHelpers.GetHashCode(city.kingdom));
                    hash = hash * 31 + (city?.zones?.Count ?? 0);
                    hash = hash * 31 + (city?.isRekt() == true ? 1 : 0);
                }
        return hash;
    }
}

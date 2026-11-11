using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 帧预算优先的国家(用户 2026-11-11 "预算可以优先帝国或者天下的主要国家")：各帝国的核心国、各天下受天命的共主、
// 在本天下据城一成五以上的大国。预算紧时它们先结：战略调度里单独先过一遍(每帧至少两国，月度各步每次至少推进一步)，
// 军团月度征召与战线分派、全图人口月结也把它们(连同所在帝国的成员、藩属)排在前面；小国照旧轮得到，只是往后排。
// 名单每 RefreshSeconds 秒(真实时间)重算一次
public static class RealmPriority
{
    private const float RefreshSeconds = 10f;
    private const float MajorShare = 0.15f;
    private static readonly HashSet<long> Major = new();
    private static readonly List<Kingdom> MajorList = new();
    private static float _refreshAt = -1f;
    private static MapBox _world;

    public static IReadOnlyList<Kingdom> Majors
    {
        get
        {
            Refresh();
            return MajorList;
        }
    }

    public static bool IsMajor(Kingdom kingdom)
    {
        if (kingdom == null) return false;
        Refresh();
        return Major.Contains(kingdom.id);
    }

    // 属于重要的一家：自己是，或者所在帝国的核心国是、宗主是
    public static bool InMajorRealm(Kingdom kingdom)
    {
        if (kingdom?.data == null) return false;
        Refresh();
        if (Major.Contains(kingdom.id)) return true;
        Kingdom core = kingdom.GetEmpire()?.CoreKingdom;
        if (core != null && Major.Contains(core.id)) return true;
        Kingdom sovereign = EmpireCoreControl.Sovereign(kingdom);
        return sovereign != null && Major.Contains(sovereign.id);
    }

    // 按重要的一家在前排进队列(同一档里照原来的次序)
    public static void EnqueueByPriority(Queue<Kingdom> queue, IEnumerable<Kingdom> kingdoms)
    {
        var rest = new List<Kingdom>();
        foreach (Kingdom kingdom in kingdoms)
        {
            if (InMajorRealm(kingdom)) queue.Enqueue(kingdom);
            else rest.Add(kingdom);
        }
        foreach (Kingdom kingdom in rest) queue.Enqueue(kingdom);
    }

    public static void EnqueueByPriority(Queue<City> queue, IEnumerable<City> cities, Func<City, bool> include)
    {
        var rest = new List<City>();
        foreach (City city in cities)
        {
            if (!include(city)) continue;
            if (InMajorRealm(city.kingdom)) queue.Enqueue(city);
            else rest.Add(city);
        }
        foreach (City city in rest) queue.Enqueue(city);
    }

    private static void Refresh()
    {
        MapBox world = World.world;
        float now = Time.realtimeSinceStartup;
        if (ReferenceEquals(world, _world) && now < _refreshAt && now >= _refreshAt - RefreshSeconds) return;
        _world = world;
        _refreshAt = now + RefreshSeconds;
        Major.Clear();
        MajorList.Clear();
        if (world?.kingdoms == null) return;
        try
        {
            IList<Empire> empires = ModClass.EMPIRE_MANAGER?.list;
            if (empires != null)
                foreach (Empire empire in empires)
                    if (empire != null && !empire.isRekt() && !empire.IsArchived()) Add(empire.CoreKingdom);
            foreach (EmpireCore lead in TianxiaSystem.Leads())
            {
                Add(TianxiaSystem.Holder(lead));
                (Dictionary<Kingdom, int> held, int total) = TianxiaSystem.Holdings(lead);
                if (total <= 0) continue;
                foreach (KeyValuePair<Kingdom, int> pair in held)
                    if (pair.Value >= total * MajorShare) Add(pair.Key);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][帧预算] 重要国家名单没算成：{exception.Message}");
        }
    }

    private static void Add(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild) return;
        if (Major.Add(kingdom.id)) MajorList.Add(kingdom);
    }
}

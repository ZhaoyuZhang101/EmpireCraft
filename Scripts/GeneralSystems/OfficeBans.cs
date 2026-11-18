using System.Collections.Generic;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 禁锢子孙(罢官株连、诛外戚永不录用)记在族谱上：以前只记在单位身上、子孙沿 getParents 往上查，
// 本人被并回人口(转成虚拟族人)禁令就丢了，父祖不在地图上子孙也查不到(用户 2026-11-18 选"改存族谱 id")。
// 单位身上的旧记录照旧认(旧档)，族谱这边另记一份、按族谱的父母往上查
public static class OfficeBans
{
    private const int MaxGenerations = 6;

    public static void Add(PersonalClanIdentity person, long realmId, bool kingdom)
    {
        if (person == null || realmId < 0) return;
        List<long> bans = kingdom ? person.banned_office_kingdoms ??= new List<long>()
            : person.banned_office_empires ??= new List<long>();
        if (!bans.Contains(realmId)) bans.Add(realmId);
    }

    // 本人或族谱上的祖上(至多六代)在这个帝国/国家被禁锢
    public static bool Banned(PersonalClanIdentity person, long realmId, bool kingdom)
    {
        if (person == null || realmId < 0) return false;
        var visited = new HashSet<long>();
        var pending = new Stack<(PersonalClanIdentity person, int depth)>();
        pending.Push((person, 0));
        while (pending.Count > 0)
        {
            (PersonalClanIdentity current, int depth) = pending.Pop();
            if (current == null || !visited.Add(current.id)) continue;
            List<long> bans = kingdom ? current.banned_office_kingdoms : current.banned_office_empires;
            if (bans != null && bans.Contains(realmId)) return true;
            if (depth >= MaxGenerations) continue;
            if (current.father > 0) pending.Push((SpecificClanManager.getPerson(current.father), depth + 1));
            if (current.mother > 0) pending.Push((SpecificClanManager.getPerson(current.mother), depth + 1));
        }
        return false;
    }
}

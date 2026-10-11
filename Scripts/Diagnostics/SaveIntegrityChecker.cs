using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Diagnostics;

/// <summary>
/// 存档一致性检查（只读，不修改任何数据）。读档完成后运行一次，把发现的问题统一以
/// "[存档检查]" 前缀写进日志，便于定位"幽灵帝国、死循环、年号错乱"这类状态错乱问题。
/// 修复由各系统自己的运行时逻辑负责，这里只负责把问题暴露出来。
/// </summary>
public static class SaveIntegrityChecker
{
    private const string Tag = "[存档检查]";

    public static int Run(string trigger)
    {
        var issues = new List<string>();
        try
        {
            CheckEmpires(issues);
            CheckKingdomMembership(issues);
        }
        catch (Exception exception)
        {
            issues.Add($"检查过程本身出错: {exception}");
        }

        if (issues.Count == 0)
        {
            LogService.LogInfo($"{Tag} ({trigger}) 未发现问题。");
            return 0;
        }
        LogService.LogWarning($"{Tag} ({trigger}) 共发现 {issues.Count} 项问题：");
        foreach (string issue in issues) LogService.LogWarning($"{Tag} {issue}");
        return issues.Count;
    }

    private static string Describe(Empire empire) =>
        $"帝国[{empire?.id}]{SafeName(empire)}";

    private static string SafeName(Empire empire)
    {
        try { return empire?.GetEmpireName() ?? ""; }
        catch { return ""; }
    }

    private static string Describe(Kingdom kingdom) =>
        kingdom == null ? "王国[null]" : $"王国[{kingdom.id}]{kingdom.data?.name}";

    private static IEnumerable<Empire> LiveEmpires() =>
        (ModClass.EMPIRE_MANAGER ?? Enumerable.Empty<Empire>())
        .Where(empire => empire?.data != null && !empire.IsArchived() && !empire.isRekt() &&
                         !AncientWarfareCompatibility.OwnsObject(empire));

    private static void CheckEmpires(List<string> issues)
    {
        var coreOwners = new Dictionary<Kingdom, Empire>();
        foreach (Empire empire in LiveEmpires().ToList())
        {
            string name = Describe(empire);
            Kingdom core = empire.CoreKingdom;
            List<Kingdom> members = empire.kingdoms_list?.Where(kingdom => kingdom != null).ToList() ??
                                    new List<Kingdom>();

            if (core == null || core.isRekt())
            {
                issues.Add($"{name}: 核心王国不存在或已灭亡");
                continue;
            }
            if (!members.Contains(core))
                issues.Add($"{name}: 核心王国 {Describe(core)} 不在成员列表中");
            if (core.GetEmpire() != empire)
                issues.Add($"{name}: 核心王国 {Describe(core)} 登记在帝国[{core.GetEmpireID()}]名下");
            if (coreOwners.TryGetValue(core, out Empire other))
                issues.Add($"{name} 与 {Describe(other)} 共用同一个核心王国 {Describe(core)}");
            else coreOwners[core] = empire;

            if (!members.Any(kingdom => !kingdom.isRekt() && kingdom.cities?.Count > 0))
                issues.Add($"{name}: 所有成员国都没有城市（无领土的幽灵帝国）");
            foreach (Kingdom member in members)
            {
                if (member.isRekt()) issues.Add($"{name}: 成员列表中有已灭亡的王国 {Describe(member)}");
                else if (member.GetEmpire() != empire)
                    issues.Add($"{name}: 成员 {Describe(member)} 登记在帝国[{member.GetEmpireID()}]名下");
            }

            CheckEmperor(empire, name, issues);
            CheckClaims(empire, name, issues);
            CheckParliament(empire, name, issues);
        }
    }

    private static void CheckEmperor(Empire empire, string name, List<string> issues)
    {
        Actor emperor = empire.Emperor;
        if (emperor == null || emperor.isRekt())
        {
            if (!empire.CoreKingdom.HasHeir()) issues.Add($"{name}: 没有皇帝，也没有继承人（空位）");
            return;
        }
        if (empire.data.currentHistory != null && empire.data.currentHistory.id != emperor.data.id)
            issues.Add($"{name}: 皇帝 {emperor.getName()} 未完成即位登记（当前朝代记录属于[{empire.data.currentHistory.id}]）");
        if (empire.HasYearName() && empire.data.newEmperor_timestamp >= 0)
        {
            int eraYears = empire.GetEmperorYear();
            int age = empire.CoreKingdom.getAge();
            if (eraYears > age + 1)
                issues.Add($"{name}: 年号已计 {eraYears} 年，超过核心王国存续的 {age} 年");
            if (emperor.getAge() > 0 && eraYears > emperor.getAge() + 1)
                issues.Add($"{name}: 年号已计 {eraYears} 年，超过皇帝 {emperor.getName()} 的年龄 {emperor.getAge()} 岁");
        }
    }

    private static void CheckClaims(Empire empire, string name, List<string> issues)
    {
        Regime regime = empire.CoreKingdom.GetRegime();
        List<FixedFaction> factions = regime?.PlayerFactions;
        if (factions == null) return;
        List<TemporaryFaction> started = factions
            .Where(faction => faction?.TemporaryFactions != null)
            .SelectMany(faction => faction.TemporaryFactions)
            .Where(claim => claim != null && claim.IsStarted() && !claim.IsLocallyPushed)
            .ToList();
        if (started.Count > 1)
            issues.Add($"{name}: 同时有 {started.Count} 个诉求处于推进状态: " +
                       string.Join("、", started.Select(claim => claim.type.ToString())));
        foreach (FixedFaction faction in factions.Where(faction => faction?.TemporaryFactions != null))
        {
            if (faction.TemporaryFactions.Any(claim => claim == null))
                issues.Add($"{name}: 派系 {faction.Name} 的诉求列表里有空对象");
        }
    }

    private static void CheckParliament(Empire empire, string name, List<string> issues)
    {
        ParliamentView parliament = ParliamentSystem.GetView(empire);
        if (!parliament.Exists) return;
        int orphanSeats = parliament.Seats.Count(seat => seat.Faction == null);
        if (orphanSeats > 0) issues.Add($"{name}: 议会有 {orphanSeats} 个议席属于已不存在的派系");
        if (parliament.TotalSeats > 0 && parliament.PrimeMinister == null)
            issues.Add($"{name}: 议会存在但没有总理大臣");
    }

    private static void CheckKingdomMembership(List<string> issues)
    {
        if (World.world?.kingdoms == null) return;
        var memberOf = new Dictionary<Kingdom, List<Empire>>();
        foreach (Empire empire in LiveEmpires())
        {
            foreach (Kingdom kingdom in empire.kingdoms_list ?? new List<Kingdom>())
            {
                if (kingdom == null || kingdom.isRekt()) continue;
                if (!memberOf.TryGetValue(kingdom, out List<Empire> owners))
                    memberOf[kingdom] = owners = new List<Empire>();
                owners.Add(empire);
            }
        }
        foreach (KeyValuePair<Kingdom, List<Empire>> pair in memberOf.Where(pair => pair.Value.Count > 1))
            issues.Add($"{Describe(pair.Key)} 同时出现在多个帝国的成员列表中: " +
                       string.Join("、", pair.Value.Select(Describe)));

        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt() || AncientWarfareCompatibility.Owns(kingdom)) continue;
            long empireId = kingdom.GetEmpireID();
            if (empireId <= 0) continue;
            Empire empire = ModClass.EMPIRE_MANAGER?.get(empireId);
            if (empire == null || empire.IsArchived())
                issues.Add($"{Describe(kingdom)} 登记的帝国[{empireId}]已经不存在或已解散");
            else if (!memberOf.ContainsKey(kingdom) && empire.CoreKingdom != kingdom)
                issues.Add($"{Describe(kingdom)} 登记在 {Describe(empire)} 名下，但不在其成员列表中");
        }
    }
}

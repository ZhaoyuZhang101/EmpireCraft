using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Diagnostics;

// 帝号去向诊断(玩家反馈"重进了帝国就失去帝号了"，没有日志可查)：
//   · 每次解散帝国都记一行：哪个帝国、核心国、成员与城数、由哪几层调用解散的(EmpireManager.dissolveEmpire 是唯一出口)；
//   · 读档时给各帝国记个底(核心国、政体、帝号类别、国号)，读档后第 1、6、12、36 个月各对一次账：
//     没了的、核心国换了或不再是帝国中央的、政体或帝号类别变了的都写进日志。
// 只读，不改任何数据；玩家的 Player.log 里搜"[帝号诊断]"即可
public static class EmpireLossDiagnostics
{
    private const string Tag = "[EmpireCraft][帝号诊断]";
    private static readonly int[] CheckMonths = { 1, 6, 12, 36 };

    private sealed class Snapshot
    {
        public string name;
        public long core_id;
        public string regime;
        public string type_key;
        public int kingdoms;
        public int cities;
    }

    private static readonly Dictionary<long, Snapshot> AtLoad = new();
    // 读档后解散的帝国：编号 → 解散时记下的调用处(对账时一并写出)
    private static readonly Dictionary<long, string> DissolvedSinceLoad = new();
    private static object _world;
    private static double _loadedAt = -1d;
    private static int _nextCheck;

    // 读档完成(见 DataManager.LoadAll)：给各帝国记底
    public static void OnLoaded()
    {
        try
        {
            AtLoad.Clear();
            DissolvedSinceLoad.Clear();
            _world = World.world;
            _loadedAt = World.world?.getCurWorldTime() ?? -1d;
            _nextCheck = 0;
            if (ModClass.EMPIRE_MANAGER == null) return;
            foreach (Empire empire in ModClass.EMPIRE_MANAGER)
            {
                if (empire?.data == null || empire.IsArchived()) continue;
                AtLoad[empire.id] = Take(empire);
            }
            LogService.LogInfo($"{Tag} 读档完成：{AtLoad.Count} 个帝国(" +
                               string.Join("、", AtLoad.Values.Take(12).Select(snapshot => snapshot.name)) +
                               (AtLoad.Count > 12 ? "……" : "") + ")");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{Tag} 读档记底失败: {exception.Message}");
        }
    }

    // 帝国的中央被改挂、摘掉帝国编号(见 KingdomExtension.SetEmpireID)：帝国还在的话记一行，带调用处
    public static void OnCoreMoved(Kingdom kingdom, long from, long to)
    {
        try
        {
            Empire empire = ModClass.EMPIRE_MANAGER?.get(from);
            if (empire?.data == null || empire.IsArchived() || empire.CoreKingdom != kingdom) return;
            LogService.LogInfo($"{Tag} {Name(empire)}({from}) 的中央 {Describe(kingdom)} 帝国编号改为 {to}；调用：{CallSite(3, 6)}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{Tag} 记录中央改挂失败: {exception.Message}");
        }
    }

    // 解散帝国之前(见 EmpireManager.dissolveEmpire)
    public static void OnDissolving(Empire empire)
    {
        if (empire?.data == null) return;
        try
        {
            string caller = CallSite(3, 5);
            Kingdom core = empire.CoreKingdom;
            string since = SinceLoad();
            LogService.LogInfo($"{Tag} 解散帝国 {Name(empire)}({empire.id})：核心国 {Describe(core)}，" +
                               $"成员 {empire.kingdoms_list?.Count ?? 0} 国、{empire.cities_list?.Count ?? 0} 城" +
                               (since.Length > 0 ? $"，{since}" : "") + $"；调用：{caller}");
            if (AtLoad.ContainsKey(empire.id)) DissolvedSinceLoad[empire.id] = caller;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{Tag} 记录解散失败: {exception.Message}");
        }
    }

    // 读档后按月对账(战略调度每帧调用，大多数时候第一行就返回)
    public static void Tick()
    {
        if (_loadedAt < 0d || _nextCheck >= CheckMonths.Length || World.world == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _loadedAt = -1d;
            return;
        }
        double now = World.world.getCurWorldTime();
        if (now < _loadedAt)
        {
            _loadedAt = -1d;
            return;
        }
        if (Date.getMonthsSince(_loadedAt) < CheckMonths[_nextCheck]) return;
        int months = CheckMonths[_nextCheck++];
        try
        {
            Compare(months);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{Tag} 对账失败: {exception.Message}");
        }
    }

    private static void Compare(int months)
    {
        var lines = new List<string>();
        foreach (KeyValuePair<long, Snapshot> pair in AtLoad)
        {
            Snapshot before = pair.Value;
            Empire empire = ModClass.EMPIRE_MANAGER?.get(pair.Key);
            if (empire?.data == null || empire.IsArchived())
            {
                lines.Add($"{before.name}({pair.Key}) 已不在" +
                          (DissolvedSinceLoad.TryGetValue(pair.Key, out string caller) ? $"，解散于 {caller}" : "，未经解散出口"));
                continue;
            }
            Snapshot now = Take(empire);
            var changes = new List<string>();
            Kingdom core = empire.CoreKingdom;
            if (now.core_id != before.core_id) changes.Add($"核心国 {before.core_id}→{now.core_id}");
            if (core != null && !core.isRekt() && !core.IsEmpire()) changes.Add($"核心国 {Describe(core)} 不再是帝国中央");
            if (now.regime != before.regime) changes.Add($"政体 {before.regime}→{now.regime}");
            if (now.type_key != before.type_key) changes.Add($"帝号类别 {before.type_key}→{now.type_key}");
            if (now.name != before.name) changes.Add($"国号 {before.name}→{now.name}");
            if (changes.Count > 0) lines.Add($"{before.name}({pair.Key})：{string.Join("，", changes)}");
        }
        int alive = ModClass.EMPIRE_MANAGER?.Count(empire => empire?.data != null && !empire.IsArchived()) ?? 0;
        LogService.LogInfo($"{Tag} 读档后第 {months} 个月：读档时 {AtLoad.Count} 个帝国，现有 {alive} 个" +
                           (lines.Count == 0 ? "，读档时的帝国都还在、帝号未变" : "；" + string.Join("；", lines)));
    }

    private static Snapshot Take(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        return new Snapshot
        {
            name = Name(empire),
            core_id = core?.id ?? -1L,
            regime = core?.data != null && !core.isRekt() ? core.GetRegime()?.type.ToString() ?? "" : "",
            type_key = empire.data.empire_type_key ?? "",
            kingdoms = empire.kingdoms_list?.Count ?? 0,
            cities = empire.cities_list?.Count ?? 0
        };
    }

    private static string Name(Empire empire)
    {
        try
        {
            string name = empire.GetEmpireFullName();
            return string.IsNullOrWhiteSpace(name) ? empire.data?.name ?? "" : name;
        }
        catch
        {
            return empire.data?.name ?? "";
        }
    }

    private static string Describe(Kingdom kingdom) =>
        kingdom?.data == null ? "无" : $"{kingdom.data.name}({kingdom.id}{(kingdom.isRekt() ? "，已灭" : "")}，{kingdom.cities?.Count ?? 0} 城)";

    private static string SinceLoad()
    {
        if (_loadedAt < 0d || World.world == null || !ReferenceEquals(_world, World.world)) return "";
        double now = World.world.getCurWorldTime();
        if (now < _loadedAt) return "";
        int months = Date.getMonthsSince(_loadedAt);
        return months <= CheckMonths[CheckMonths.Length - 1] ? $"读档后第 {months} 个月" : "";
    }

    // 调用链(跳过本方法、OnDissolving 与 dissolveEmpire 自己)：类名.方法名，由近及远
    private static string CallSite(int skip, int depth)
    {
        global::System.Diagnostics.StackFrame[] frames = new global::System.Diagnostics.StackTrace(skip, false).GetFrames();
        if (frames == null) return "未知";
        var parts = new List<string>();
        foreach (global::System.Diagnostics.StackFrame frame in frames)
        {
            global::System.Reflection.MethodBase method = frame?.GetMethod();
            if (method == null) continue;
            parts.Add($"{method.DeclaringType?.Name ?? "?"}.{method.Name}");
            if (parts.Count >= depth) break;
        }
        return parts.Count == 0 ? "未知" : string.Join(" ← ", parts);
    }
}

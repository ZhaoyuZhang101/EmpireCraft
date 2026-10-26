using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GeneralSystems;

// 科技合并与单目标旧存档的迁移；只整理数据，不触发研究完成奖励。
public static class TechnologyStateRules
{
    public const string Enlightenment = "secular_philosophy";
    public static string Canonical(string id) => id == "constitutionalism" ? Enlightenment : id;

    public static void Normalize(CultureTechState state)
    {
        state.researched_techs ??= new();
        state.tech_progress ??= new();
        state.timestamps ??= new();
        state.boosted ??= new();
        if (state.progress > 0f && !string.IsNullOrEmpty(state.current_tech))
        {
            state.tech_progress[state.current_tech] = Math.Max(state.progress,
                state.tech_progress.TryGetValue(state.current_tech, out float saved) ? saved : 0f);
            state.progress = 0f;
        }
        if (state.tech_progress.TryGetValue("constitutionalism", out float legacy))
        {
            state.tech_progress.Remove("constitutionalism");
            state.tech_progress.TryGetValue(Enlightenment, out float current);
            state.tech_progress[Enlightenment] = Math.Max(0f, current) + Math.Max(0f, legacy);
        }
        state.researched_techs = state.researched_techs.Select(Canonical).Distinct().ToList();
        state.boosted = state.boosted.Select(Canonical).Distinct().ToList();
        if (state.timestamps.TryGetValue("tech:constitutionalism", out double when))
        {
            state.timestamps.Remove("tech:constitutionalism");
            string key = "tech:" + Enlightenment;
            state.timestamps[key] = state.timestamps.TryGetValue(key, out double other) ? Math.Min(when, other) : when;
        }
        if (state.researched_techs.Contains(Enlightenment) &&
            state.tech_progress.TryGetValue(Enlightenment, out float invested))
        {
            state.research_bank += Math.Max(0f, invested);
            state.tech_progress.Remove(Enlightenment);
        }
        state.current_techs = Targets(state.current_techs, state.current_tech);
        state.player_targets = Targets(state.player_targets, state.player_target);
        state.current_techs.RemoveAll(state.researched_techs.Contains);
        state.player_targets.RemoveAll(state.researched_techs.Contains);
        SyncLegacy(state);
    }

    private static List<string> Targets(List<string> targets, string legacy) =>
        (targets ?? new List<string>()).Concat(string.IsNullOrEmpty(legacy) ? Array.Empty<string>() : new[] { legacy })
            .Where(id => !string.IsNullOrEmpty(id)).Select(Canonical).Distinct().ToList();

    public static void SyncLegacy(CultureTechState state)
    {
        state.current_tech = state.current_techs.FirstOrDefault() ?? "";
        state.player_target = state.player_targets.FirstOrDefault() ?? "";
    }
}

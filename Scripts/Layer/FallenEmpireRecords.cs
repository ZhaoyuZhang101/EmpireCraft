using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.Layer;

// 已亡帝国名录：帝国解散时就从帝国管理器里移除(见 EmpireManager.dissolveEmpire)，帝国列表的"已亡"一栏
// 以前按管理器里标了已亡的帝国显示，于是正常灭亡的帝国直接从列表消失。改按这份名录显示；史书按帝国编号存在 ALL_HISTORY_DATA。
// 皇族王国接续(见 Empire.ReplaceEmpire)是同一帝国的延续，不记
public class FallenEmpireRecord
{
    public long id { get; set; } = -1L;
    public string name { get; set; } = "";
    public string culture { get; set; } = "";
    public double established_at { get; set; } = -1d;
    // 灭亡时间；-1 = 旧档补记，不知道何时灭亡
    public double fallen_at { get; set; } = -1d;
    public int years { get; set; }
    public string last_ruler { get; set; } = "";
}

public static class FallenEmpireRecords
{
    public static List<FallenEmpireRecord> Records = new();

    public static void ResetWorldState() => Records = new List<FallenEmpireRecord>();

    public static void Load(List<FallenEmpireRecord> records) =>
        Records = records?.Where(record => record != null).ToList() ?? new List<FallenEmpireRecord>();

    public static List<FallenEmpireRecord> Save() => Records;

    // 解散之前调用(核心国还在，取得到文化、国号)
    public static void Record(Empire empire, bool known = true)
    {
        if (empire?.data == null || Records.Any(record => record?.id == empire.data.id)) return;
        string culture = "";
        try
        {
            culture = empire.GetCulture() ?? "";
        }
        catch
        {
            // 旧档已亡帝国没有核心王国
        }
        EmpireCraftHistory last = empire.data.currentHistory ?? empire.data.history?.LastOrDefault();
        double established = empire.data.timestamp_established_time;
        int recorded = empire.data.history?.Sum(history => history?.total_time ?? 0) ?? 0;
        int years = known && established > 0 ? Date.getYearsSince(established) + 1 : recorded;
        string ruler = last?.emperor;
        if (string.IsNullOrWhiteSpace(ruler)) ruler = empire.Emperor?.getName() ?? "";
        string name = "";
        try
        {
            name = empire.GetEmpireFullName();
        }
        catch
        {
            name = last?.empire_full_name ?? "";
        }
        Records.Add(new FallenEmpireRecord
        {
            id = empire.data.id,
            name = string.IsNullOrWhiteSpace(name) ? last?.empire_full_name ?? empire.data.name ?? "" : name,
            culture = culture,
            established_at = established,
            fallen_at = known ? World.world?.getCurWorldTime() ?? -1d : -1d,
            years = years < 1 ? 1 : years,
            last_ruler = ruler
        });
    }

    // 旧档：以前解散不从管理器移除，已亡帝国还留在管理器里，补进名录
    public static void AdoptArchived(IEnumerable<Empire> empires)
    {
        foreach (Empire empire in empires?.ToList() ?? new List<Empire>())
            if (empire?.data != null && empire.IsArchived()) Record(empire, known: false);
    }

    public static List<EmpireCraftHistory> Histories(FallenEmpireRecord record)
    {
        if (record == null || ModClass.ALL_HISTORY_DATA == null ||
            !ModClass.ALL_HISTORY_DATA.TryGetValue(record.id, out List<EmpireCraftHistory> histories) || histories == null)
            return new List<EmpireCraftHistory>();
        return histories.Where(history => history != null).ToList();
    }

    // 清理灭亡超过若干年的(旧档补记的不知何时灭亡，按立国算)；史书仍留在 ALL_HISTORY_DATA，帝国法理窗口照样查得到
    public static void PurgeOlderThanYears(int years)
    {
        Records.RemoveAll(record =>
        {
            double at = record.fallen_at >= 0d ? record.fallen_at : record.established_at;
            return at > 0d && Date.getYearsSince(at) > years;
        });
    }
}

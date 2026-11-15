using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Terminal;

namespace EmpireCraft.Scripts.GeneralSystems;

// 编年(用户 2026-11-16 "我作为管理员希望能看到世界历史")：世界上发出的每条世界消息(开战议和、换君、立国亡国、称帝篡位、
// 各系统报的事)按发出时的文字记一笔，随模组存档。终端管理员页的「史」读它(见 TerminalHost.History)。
//   · 文字在发出那一刻定下来(以后改国号、人死了不跟着变)，去掉颜色标记；
//   · 大事 = 别国也听得到的事(ScreenFeedPatch.PublicNews)，管理员页可只看大事；
//   · 同一句话同一个月只记一次(同一件事几处一起报)；
//   · 至多 Cap 条(一年四五十条)，满了先丢最早的小事，大事留到 MajorCap 条才丢最早的大事——几百年后还查得到开头那些朝代兴亡。
// 只做字符串处理，不扫地图、不碰别的对象。
[Serializable]
public sealed class ChronicleEntry
{
    // 世界时刻(getCurWorldTime)
    public double t;
    public string text = "";
    public bool major;
    public long kingdom = -1L;
    // 终端管理员页抄好的那一行(不存档；丢了别的条目重排时不用重算)
    [NonSerialized] internal object View;
}

public static class WorldChronicle
{
    public const int Cap = 4000, MajorCap = 2500;
    private static List<ChronicleEntry> _entries = new();
    private static int _majors;
    // 本月已记过的句子(同月去重)
    private static readonly HashSet<string> ThisMonth = new(StringComparer.Ordinal);
    private static int _month = int.MinValue;

    public static IReadOnlyList<ChronicleEntry> Entries => _entries;
    // 记过多少条(含丢掉的)：管理员页按它判断有没有新的
    public static long Stamp { get; private set; }
    // 丢过几回(丢的可能在中间)：管理员页按它判断要不要整份重抄
    public static long Trims { get; private set; }

    public static void ResetWorldState()
    {
        _entries = new List<ChronicleEntry>();
        ThisMonth.Clear();
        _month = int.MinValue;
        _majors = 0;
        Stamp = Trims = 0;
    }

    public static List<ChronicleEntry> Save() => _entries;

    public static void Load(List<ChronicleEntry> saved)
    {
        ResetWorldState();
        if (saved == null) return;
        foreach (ChronicleEntry entry in saved)
            if (entry != null && !string.IsNullOrWhiteSpace(entry.text))
            {
                _entries.Add(entry);
                if (entry.major) _majors++;
            }
        while (_entries.Count > Cap) Trim();
        Stamp = _entries.Count;
    }

    // 世界消息发出时(ScreenFeedPatch.BeforeNewHistory)
    public static void Note(WorldLogMessage message)
    {
        if (message == null || World.world == null) return;
        try
        {
            string text = TerminalIntel.Strip(message.GetEmpireHistoryFormatedText())?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            int month = Date.getCurrentYear() * 12 + Date.getCurrentMonth();
            if (month != _month)
            {
                _month = month;
                ThisMonth.Clear();
            }
            if (!ThisMonth.Add(text)) return;
            bool modEvent = message.asset_id == EmpireCraftWorldLogLibrary.empirecraft_event_log?.id;
            Kingdom about = modEvent ? ScreenFeedPatch.PendingKingdom ?? message.kingdom : message.kingdom;
            bool major = ScreenFeedPatch.PublicNews(message);
            _entries.Add(new ChronicleEntry { t = World.world.getCurWorldTime(), text = text, major = major, kingdom = about?.data != null ? about.getID() : -1L });
            if (major) _majors++;
            if (_entries.Count > Cap) Trim();
            Stamp++;
        }
        catch (Exception)
        {
            // 这条读不出来就不记
        }
    }

    // 丢一条：大事还没超额就丢最早的小事，否则丢最早的一条
    private static void Trim()
    {
        int at = 0;
        if (_majors <= MajorCap)
            for (int i = 0; i < _entries.Count; i++)
                if (!_entries[i].major)
                {
                    at = i;
                    break;
                }
        if (_entries[at].major) _majors--;
        _entries.RemoveAt(at);
        Trims++;
    }
}

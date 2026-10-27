using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 历代教首(用户 2026-10-27 选"历代教首"：宗教窗口教廷页列出)：教宗、哈里发每换一任记一笔——
// 何时即位、怎么选立(枢机推举、世俗强立，其余算继位)、何时、怎么结束(去世、被废、教廷失陷)。
// 现任教首就是据有圣地之国的君主(PapacySystem.Pope)，换人不一定走推举(战乱易手、继位)，所以按"现任是否变了"核：
// 推举新教宗时、每年宗教结算时、打开宗教窗口时各核一次。本功能之前的教首不补记
public static class ReligionHeads
{
    public const int MaxHeads = 60;

    public static void Note(Religion religion)
    {
        if (religion?.data == null || religion.isRekt() || World.world == null || !ReligionFaith.HasHierarchy(religion)) return;
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        data.Heads ??= new List<ReligionExtension.ReligionHeadRecord>();
        Actor head = PapacySystem.Pope(religion);
        ReligionExtension.ReligionHeadRecord last = data.Heads.Count > 0 ? data.Heads[data.Heads.Count - 1] : null;
        bool reigning = last != null && last.to < 0d;
        if (reigning && head != null && last.actor_id == head.id) return;
        if (!reigning && head == null) return;
        double now = World.world.getCurWorldTime();
        if (reigning)
        {
            Actor former = World.world.units.get(last.actor_id);
            bool alive = former?.data != null && !former.isRekt() && former.isAlive();
            if (alive) last.name = former.getName();
            last.to = now;
            last.end = !alive ? "religion_head_end_died" : head == null ? "religion_head_end_see_lost" : "religion_head_end_deposed";
        }
        if (head == null) return;
        data.Heads.Add(new ReligionExtension.ReligionHeadRecord
        {
            actor_id = head.id,
            name = head.getName(),
            from = now,
            chosen_by = data.LeaderID == head.id ? data.PopeChosenBy : 0
        });
        if (data.Heads.Count > MaxHeads) data.Heads.RemoveRange(0, data.Heads.Count - MaxHeads);
    }

    public static void YearlyPass()
    {
        if (World.world?.religions == null) return;
        foreach (Religion religion in World.world.religions.list)
            Note(religion);
    }

    public static List<ReligionExtension.ReligionHeadRecord> Of(Religion religion) =>
        religion?.data == null ? new List<ReligionExtension.ReligionHeadRecord>()
            : religion.GetOrCreate().Heads ?? new List<ReligionExtension.ReligionHeadRecord>();
}

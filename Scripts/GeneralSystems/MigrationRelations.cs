using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 移民的外交面(用户 2026-10-19 选"影响好感并有难民"；以前开放移民不经邻国同意就抽人、清除移民把人倒到国外，都没有后果)：
//   · 开放移民从邻国抽人：被抽走人口的邻国(帝国按核心)十年内对本国好感 −25；
//   · 清除移民：被赶出去的人送回同文化的邻国，没有就涌入接壤的外国城做难民——接收国十年内对驱逐国好感 −40，
//     接收难民的城五年内稳定度少 6(难民涌入，见 CityStabilitySystem.Natural)。
public static class MigrationRelations
{
    private const int GrievanceYears = 10, RefugeeYears = 5;
    private const int DrainedOpinion = -25, ExpelledOpinion = -40;
    public const float RefugeeStabilityLoss = 6f;

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    public static void Drained(Kingdom victim, Kingdom culprit) => Note(victim, culprit, expelled: false);

    public static void Expelled(Kingdom receiver, Kingdom culprit) => Note(receiver, culprit, expelled: true);

    private static void Note(Kingdom victim, Kingdom culprit, bool expelled)
    {
        Kingdom a = Realm(victim), b = Realm(culprit);
        if (a?.data == null || b?.data == null || a == b || World.world == null) return;
        var data = a.GetOrCreate();
        Dictionary<long, double> book = expelled
            ? data.migration_expelled ??= new Dictionary<long, double>()
            : data.migration_drained ??= new Dictionary<long, double>();
        book[b.id] = World.world.getCurWorldTime();
    }

    // 好感(见 EmpireCraftOpinionAddition)：main 对 target
    public static int Opinion(Kingdom main, Kingdom target)
    {
        Kingdom a = Realm(main), b = Realm(target);
        if (a?.data == null || b?.data == null || a == b) return 0;
        var data = a.GetOrCreate();
        if (Recent(data.migration_expelled, b.id, GrievanceYears)) return ExpelledOpinion;
        return Recent(data.migration_drained, b.id, GrievanceYears) ? DrainedOpinion : 0;
    }

    private static bool Recent(Dictionary<long, double> book, long id, int years) =>
        book != null && book.TryGetValue(id, out double at) && at >= 0d && Date.getYearsSince(at) < years;

    public static void TakeRefugees(City city)
    {
        if (city?.data != null && World.world != null) city.GetOrCreate().refugees_at = World.world.getCurWorldTime();
    }

    public static float RefugeeLoss(City city)
    {
        if (city?.data == null) return 0f;
        double at = city.GetOrCreate().refugees_at;
        return at >= 0d && Date.getYearsSince(at) < RefugeeYears ? RefugeeStabilityLoss : 0f;
    }

    public static string Describe() => LM.Get("stability_refugee_detail");
}

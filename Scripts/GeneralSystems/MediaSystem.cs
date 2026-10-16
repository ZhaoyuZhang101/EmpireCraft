using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GeneralSystems;

// 传播媒介：思想靠什么传开。都按本国(在帝国里按帝国)已采纳的科技算(见 TechnologySystem.RealmHasTech)。
//
// 印刷等级——书印得多不多，决定理念书籍的影响力、能不能外溢到邻城、新思潮能不能在民间大规模传开：
//   0 无纸        竹简缣帛，理念书籍几乎传不开，也传不出本文化；
//   1 造纸术      手抄本，只在本城读书人里流传，不外溢；
//   2 雕版印刷    开始成批印行，影响外溢到邻城(减半)；
//   3 活字印刷    大量印行；
//   4 机械印刷    蒸汽轮转印报，报刊让新思潮真正成为群众运动。
// 新兴理念(传统理念以外的各种主义)在民间改信的概率按印刷等级打折，只有机械印刷才能全速大规模传播。
//
// 通信等级——思想能不能跨国：0 驿传(只能靠接壤往来)，1 电报，2 广播，3 互联网。
// 双方都有通信技术、且接收方是开放国家(言论没有严格审查)时，不接壤的国家之间也会互相传入理念、形成外国理念压力。
public static class MediaSystem
{
    public const int PrintNone = 0, PrintPaper = 1, PrintBlock = 2, PrintMovable = 3, PrintPress = 4;

    private static readonly string[] PrintTechs = { "paper", "printing", "movable_type", "steam_press" };
    private static readonly string[] CommTechs = { "telegraph", "radio", "internet" };

    private static readonly float[] BookFactors = { 0f, 0.5f, 0.75f, 1f, 1.25f };
    private static readonly float[] NeighbourFactors = { 0f, 0f, 0.5f, 1f, 1.5f };
    private static readonly float[] MassFactors = { 0.1f, 0.25f, 0.45f, 0.7f, 1f };
    private static readonly float[] CrossBorderExposure = { 0f, 4f, 8f, 15f };
    private static readonly float[] CrossBorderPressure = { 0f, 0.3f, 0.6f, 1f };

    // 本文化没启用科技系统时按活字印刷处理(各项系数为 1，不改变原来的平衡)
    public static int PrintLevel(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return PrintMovable;
        if (!TechnologySystem.IsActiveFor(TechnologySystem.GetCultureOf(kingdom))) return PrintMovable;
        int level = 0;
        while (level < PrintTechs.Length && TechnologySystem.RealmHasTech(kingdom, PrintTechs[level])) level++;
        return level;
    }

    public static int CommLevel(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return 0;
        if (!TechnologySystem.IsActiveFor(TechnologySystem.GetCultureOf(kingdom))) return 0;
        int level = 0;
        while (level < CommTechs.Length && TechnologySystem.RealmHasTech(kingdom, CommTechs[level])) level++;
        return level;
    }

    public static float BookFactor(Kingdom kingdom) => BookFactors[PrintLevel(kingdom)];
    public static float NeighbourFactor(Kingdom kingdom) => NeighbourFactors[PrintLevel(kingdom)];

    // 新兴理念能否在民间大规模传播(传统理念不受印刷限制)
    public static bool IsMassIdeology(PartyIdeology ideology) => !IdeologyFamilies.IsTraditional(ideology);
    public static float MassFactor(int printLevel) => MassFactors[printLevel];
    public static float MassFactor(Kingdom kingdom, PartyIdeology ideology) =>
        IsMassIdeology(ideology) ? MassFactors[PrintLevel(kingdom)] : 1f;

    // 开放国家：言论没有严格审查(没有宪法按"一般"算，也是开放的)
    public static bool IsOpen(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return false;
        Empire empire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
        return empire == null || ConstitutionSystem.GetSpeech(empire) != ConstitutionSpeech.Strict;
    }

    // 两国之间通过通信技术连得上的等级(双方取低)；接收方不开放时为 0
    public static int LinkLevel(Kingdom source, Kingdom target)
    {
        if (!IsOpen(target)) return 0;
        int a = CommLevel(source), b = CommLevel(target);
        return a < b ? a : b;
    }

    public static float CrossBorderExposurePerYear(int link) => CrossBorderExposure[link];
    public static float CrossBorderPressureFactor(int link) => CrossBorderPressure[link];
}

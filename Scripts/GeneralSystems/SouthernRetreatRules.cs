using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 南渡偏安的数值规则(见 SouthernRetreatSystem)，不碰游戏对象，单独测
public static class SouthernRetreatRules
{
    // 偏安几年；剩下的国土至少几城才算另立朝廷；两次南渡至少隔几年
    public const int ShelterYears = 15, MinCities = 6, CooldownYears = 60;
    // 正统保底：南渡当年这么多，偏安期满降到正统崩坏线
    public const int Floor = 45;

    // 能不能南渡：京师(朝廷原来的都城)落到别国，朝廷已另有都城，还有人承统(皇帝在或有储君)，剩下的城够多，离上次南渡够久(-1 没南渡过)
    public static bool Triggers(bool seatLost, bool movedCourt, bool successor, int cities, int yearsSinceLast) =>
        seatLost && movedCourt && successor && cities >= MinCities && (yearsSinceLast < 0 || yearsSinceLast >= CooldownYears);

    public static bool Sheltered(int yearsSince) => yearsSince >= 0 && yearsSince < ShelterYears;

    // 正统保底：南渡当年 Floor，逐年降到 collapseLine；过了偏安期不再保底(返回 0)
    public static int LegitimacyFloor(int yearsSince, int collapseLine)
    {
        if (!Sheltered(yearsSince)) return 0;
        float share = Math.Min(1f, Math.Max(0f, (float)yearsSince / ShelterYears));
        return (int)Math.Round(Floor - (Floor - collapseLine) * share);
    }

    // 正统要补多少：别的各项加起来不到保底的补齐，够了不补
    public static int LegitimacyBonus(int others, int yearsSince, int collapseLine) =>
        Math.Max(0, LegitimacyFloor(yearsSince, collapseLine) - others);
}

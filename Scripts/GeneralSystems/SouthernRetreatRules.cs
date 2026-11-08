using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 南明结成的数值规则(见 SouthernRetreatSystem)，不碰游戏对象，单独测
public static class SouthernRetreatRules
{
    // 正统保底与陆续来投的年限；朝廷至少几城才算另立朝廷；两次至少隔几年
    public const int ShelterYears = 15, MinCities = 6, CooldownYears = 60;
    // 正统保底：结成当年这么多，期满降到正统崩坏线
    public const int Floor = 45;

    // 异族占了天下的几分之一就算(三分之一)
    public static bool HoldsThird(int held, int total) => total > 0 && held > 0 && held * 3 >= total;

    // 结不结成：异族占了京师或天下三分之一，还有人承统(皇帝在或有储君)，剩下的城够多，离上次够久(-1 没结成过)
    public static bool Triggers(bool alienHoldsCapital, bool alienHoldsThird, bool successor, int cities, int yearsSinceLast) =>
        (alienHoldsCapital || alienHoldsThird) && successor && cities >= MinCities &&
        (yearsSinceLast < 0 || yearsSinceLast >= CooldownYears);

    public static bool Sheltered(int yearsSince) => yearsSince >= 0 && yearsSince < ShelterYears;

    // 来投的国：都城在入侵者都城以南(地图 y 轴朝北)；入侵者没有都城就不看方位
    public static bool South(float realmY, float? invaderY) => invaderY == null || realmY < invaderY.Value;

    // 正统保底：结成当年 Floor，逐年降到 collapseLine；期满不再保底(返回 0)
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

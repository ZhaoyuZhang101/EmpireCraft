using System;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class StateSettlementRules
{
    public static bool NearBestSite(int score, int best) => score > 0 && (long)score * 100L >= (long)best * 95L;

    // 和城市扩张共用一个上限。和平扩张被截在上限，不能等到第 limit+1 区才触发。
    public static bool AtZoneLimit(int zones, int limit) => limit > 0 && zones >= limit;

    public static float MigrantPeople(float civilians, int peoplePerSlot, int retainHouseholds, int maxPercent) =>
        Math.Max(0f, Math.Min(civilians * Math.Min(25, Math.Max(0, maxPercent)) / 100f,
            civilians - Math.Max(30, retainHouseholds) * Math.Max(1, peoplePerSlot)));

    public static int AdministrativeCost(int cities, int baseGold, int perCityGold) =>
        (int)Math.Min(int.MaxValue, Math.Max(50L, baseGold) + Math.Max(0L, perCityGold) * Math.Max(0, cities - 1));

    public static bool CanPay(int treasury, int cost, int reserve) =>
        cost >= 0 && (long)treasury - cost >= Math.Max(0, reserve);
}

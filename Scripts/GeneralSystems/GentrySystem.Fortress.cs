using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 坞堡(用户 2026-10-27 问答选"地方坐大：坞堡")：丙等以上(门第 40 以上)的世家在郡望之城占着田产或族众百人以上，就在那里筑坞堡——
//   · 守城：这座城的守势 ×FortressDefense(红夷大炮破城时只剩一半加成，同关城，见 FrontSystem)；
//   · 安定：自然稳定度 +FortressStability；
//   · 朝廷管不深：赋税、征兵收上来的再打 FortressYield 折(见 AdministrativeControlSystem.Yield)；
//   · 坐大：乱世里据城自守的门槛放宽到丙等、机会加倍(见 TryAutonomy)。
// 族众越多坞堡越大(10-30，见 GentrySystem.Kin)：百人起、千人到顶，守势加到 ×1.5、赋税征兵到八折、稳定到 +5。
// 每轮门第结算开始时按上一轮的郡望、田产与族众重排(见 Tick)。
public static partial class GentrySystem
{
    public const float FortressDefense = 1.25f, FortressStability = 3f, FortressYield = 0.9f, FortressPedigree = 40f;
    private const float FortressDefenseMore = 0.25f, FortressStabilityMore = 2f, FortressYieldMore = 0.1f;
    private static readonly Dictionary<long, long> Fortresses = new();

    private static void BuildFortresses()
    {
        Fortresses.Clear();
        foreach (long id in GentryIds)
        {
            SpecificClan clan = SpecificClanManager.Get(id);
            if (clan == null || !clan.gentry || clan.gentry_pedigree < FortressPedigree || clan.gentry_seat_city_id <= 0) continue;
            City seat = Seat(clan);
            if (seat?.data == null || SeatLand(clan, seat) <= 0f && clan.gentry_kin < FortressKin) continue;
            // 一城一座：族众多的那家
            if (Fortresses.TryGetValue(seat.data.id, out long other) && SpecificClanManager.Get(other) is SpecificClan held &&
                held.gentry_kin >= clan.gentry_kin) continue;
            Fortresses[seat.data.id] = clan.id;
        }
    }

    public static SpecificClan FortressOf(City city) =>
        city?.data != null && Fortresses.TryGetValue(city.data.id, out long id) ? SpecificClanManager.Get(id) : null;

    public static bool HasFortress(City city) => FortressOf(city)?.gentry == true;

    public static float FortressFactor(City city)
    {
        SpecificClan clan = FortressOf(city);
        return clan?.gentry == true ? FortressDefense + FortressDefenseMore * FortressScale(clan) : 1f;
    }

    public static float YieldFactor(City city)
    {
        SpecificClan clan = FortressOf(city);
        return clan?.gentry == true ? FortressYield - FortressYieldMore * FortressScale(clan) : 1f;
    }

    // 自然稳定度加成(见 CityStabilitySystem.LocalPressures)：坞堡 +3~+5，世家赈济过的两年里再 +6
    public static float StabilityBonus(City city, out string detail)
    {
        detail = null;
        float bonus = 0f;
        SpecificClan clan = FortressOf(city);
        if (clan?.gentry == true)
        {
            bonus += FortressStability + FortressStabilityMore * FortressScale(clan);
            detail = string.Format(LM.Get("stability_gentry_fortress"), JunWang(clan));
        }
        if (Relieved(city))
        {
            bonus += ReliefStability;
            detail = detail == null ? LM.Get("stability_gentry_relief") : detail + " / " + LM.Get("stability_gentry_relief");
        }
        return bonus;
    }
}

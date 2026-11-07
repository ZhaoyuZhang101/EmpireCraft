using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 南渡偏安(用户 2026-11-08 "南明很弱, 基本每次北京城一破, 大明就直接散架了""我希望还是能像历史一样")：
//   以前京师一破，正统跌到 0、积弊转眼满额，每年都起民变，南方各省被民变一块块吃掉，碎成一堆小国，南明没有存在的余地。
//   现在华夏王朝的京师(朝廷原来坐的都城)落到别国手里、朝廷另有都城、有人承统、剩下的国土还有 MinCities 城：南渡一次
//   (两次至少隔 CooldownYears 年)。此后 ShelterYears 年偏安——
//   · 正统保底：当年 45，逐年降到正统崩坏线(见 SouthernRetreatRules.LegitimacyFloor、MonarchyLegitimacy)；
//   · 积弊满额、正统崩坏都不起民变、不进割据混战；苛政民变照寻常规矩(打着仗的地方不起，见 HarshRuleSystem)；
//   · 积弊照涨(积弊只能延缓，不能削减)，偏安期一过老毛病照样回来。东晋、南宋、南明都是这样撑下来的。
//   京师按朝廷实际坐的都城算(明的帝国法理是吴，京师却在北京)；剧本开局就偏安的(绍兴年间的宋)京师就是临安，丢了临安才算。
public static class SouthernRetreatSystem
{
    private static readonly Dictionary<long, double> Checked = new();
    private static object _world;

    public static bool Sheltered(Empire empire)
    {
        double at = empire?.data?.southern_retreat_at ?? -1d;
        return at >= 0d && World.world != null && SouthernRetreatRules.Sheltered(YearsSince(at));
    }

    // 正统一项(见 MonarchyLegitimacy)：别的各项加起来不到保底的补齐
    public static int LegitimacyBonus(Empire empire, int others)
    {
        double at = empire?.data?.southern_retreat_at ?? -1d;
        return at < 0d || World.world == null ? 0
            : SouthernRetreatRules.LegitimacyBonus(others, YearsSince(at), DynasticCycleRules.CollapseLegitimacy);
    }

    private static int YearsSince(double at) => World.world.getCurWorldTime() < at ? 0 : Date.getYearsSince(at);

    // 每月看一次(见 Empire.update)：记下朝廷坐的都城；那座城落到别国手里、朝廷挪了都城，就南渡
    public static void Check(Empire empire)
    {
        if (empire?.data == null || World.world == null || empire.isRekt() || empire.IsArchived()) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            Checked.Clear();
        }
        double now = World.world.getCurWorldTime();
        if (Checked.TryGetValue(empire.id, out double last) && now >= last && Date.getMonthsSince(last) < 1) return;
        Checked[empire.id] = now;
        Kingdom core = empire.CoreKingdom;
        if (core?.data == null || core.isRekt() || core.capital == null || core.capital.isRekt()) return;
        EmpireData data = empire.data;
        City seat = data.southern_retreat_seat >= 0L ? World.world.cities.get(data.southern_retreat_seat) : null;
        bool seatLost = seat != null && !seat.isRekt() && seat != core.capital &&
                        (seat.kingdom == null || seat.kingdom.GetEmpire() != empire && seat.kingdom != core);
        // 京师还在手里(或迁了都、旧都仍在本朝)：记下眼下坐的都城
        if (!seatLost)
        {
            data.southern_retreat_seat = core.capital.data.id;
            return;
        }
        if (!DynasticCycleSystem.Applies(empire) ||
            InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(core)) != "Huaxia") return;
        int cities = empire.kingdoms_list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt())
            .Sum(kingdom => kingdom.cities?.Count ?? 0);
        bool successor = empire.Emperor != null && !empire.Emperor.isRekt() || core.HasHeir();
        int since = data.southern_retreat_at < 0d ? -1 : YearsSince(data.southern_retreat_at);
        // 京师丢了：先把眼下的都城记成京师，这次南渡不成(城不够、没人承统、刚南渡过)，下次就按新都城算
        data.southern_retreat_seat = core.capital.data.id;
        if (!SouthernRetreatRules.Triggers(true, true, successor, cities, since))
        {
            LogService.LogInfo($"[EmpireCraft][南渡] {empire.GetEmpireName()} 失京师 {seat.GetCityName()}，没成：" +
                               $"{cities} 城、{(successor ? "有" : "没有")}人承统、上次南渡 {since} 年前");
            return;
        }
        data.southern_retreat_at = now;
        MonarchyLegitimacy.Invalidate(empire);
        string text = string.Format(LM.Get("southern_retreat_history"), empire.GetEmpireFullName(), seat.GetCityName(),
            core.capital.GetCityName());
        EventRecorder.Record(empire, text, empire.Emperor, core);
        TranslateHelper.LogEventMessage(text, core);
        LogService.LogInfo($"[EmpireCraft][南渡] {empire.GetEmpireName()} 失京师 {seat.GetCityName()}，朝廷在 {core.capital.GetCityName()}，" +
                           $"{cities} 城，正统 {empire.Legitimacy}，积弊 {DynasticCycleSystem.GetPressure(empire):0}，偏安 {SouthernRetreatRules.ShelterYears} 年");
    }
}

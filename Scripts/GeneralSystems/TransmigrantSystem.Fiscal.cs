using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 澳宋的钱怎么进国库(规则见 AosongFiscalRules)。11-04 三国存档：临高名义年税全图第一(17710，第二名 2751)，
// 民间存款 76 万，国库却常年见底：一座城的月结十几年才轮到一次，税款一次进账几万，几年花光，再空几年，这几年就欠饷逃散。
//   · 每月结算：元老院、大区、殖民地的城都走优先队列(见 CityPopulationSystem.TickFolds)，元老院都城排最前；
//   · 流通券：国库不够发饷时从民间存款筹，账上单记一项，不算经常收入，也不再向百姓举债(见 StateDebtSystem.TryBorrow)。
public static partial class TransmigrantSystem
{
    public static List<City> MonthlySettledCities()
    {
        var cities = new List<City>();
        Kingdom senate = Active();
        if (senate == null) return cities;
        foreach (Kingdom member in NavalRealm(senate))
        {
            if (member?.cities == null) continue;
            foreach (City city in member.cities)
                if (city?.data != null && !city.isRekt()) cities.Add(city);
        }
        // 都城先结，其次元老院的城
        return cities.OrderByDescending(city => city == senate.capital).ThenByDescending(city => city.kingdom == senate).ToList();
    }

    // 澳宋各国不向百姓举债，缺钱走流通券
    public static bool IssuesScrip(Kingdom kingdom) => NavalState(kingdom) != null;

    private static void IssueScrip()
    {
        Kingdom senate = Active();
        TransmigrantState state = Of(senate);
        if (state == null || !CityPopulationSystem.AbstractPopulationEnabled || !TreasurySystem.Enabled(senate)) return;
        List<Kingdom> realm = NavalRealm(senate);
        long wanted = AosongFiscalRules.ScripWanted(senate.GetTreasuryBalance(), realm.Sum(CorpsSystem.MonthlyBill));
        if (wanted <= 0L) return;
        long raised = 0L;
        foreach (City city in MonthlySettledCities().OrderByDescending(Savings))
        {
            CityPopulationData data = CityPopulationSystem.Get(city);
            long take = AosongFiscalRules.ScripFromCity(wanted - raised, PopulationEconomySystem.Savings(city, data));
            if (take <= 0L) continue;
            PopulationEconomySystem.AddSavings(city, data, -take);
            raised += take;
            if (raised >= wanted) break;
        }
        if (raised <= 0L) return;
        senate.AddMoney((int)Math.Min(int.MaxValue, raised), TreasuryCategory.Scrip);
        if (state.scrip_issued <= 0L)
            TranslateHelper.LogEventOnly(string.Format(LM.Get("transmigrant_scrip_history"), senate.GetKingdomFullName()), senate);
        state.scrip_issued += raised;
    }

    private static float Savings(City city) => PopulationEconomySystem.Savings(city, CityPopulationSystem.Get(city));
}

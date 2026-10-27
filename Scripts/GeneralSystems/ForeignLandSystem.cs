using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 异族之地(用户 2026-10-27 截图瑞典帝国横跨欧洲、北非、西亚，维京本族只占 9 城，帝国文化特点"双轨统治"、融合度 1%，两项正统却都是满的：
// "异族入侵如果3代人以内没有完成同化，或者消耗国库使用军队镇压，那么一旦正统值低于50就会全部自立""跨这么多洲的帝国就不可能存在"
// "就算他是双轨制，这帝国也稳定过头了"；问答选"满三代才起算(胡无百年)""加一项异族之地扣正统""AI 自动付驻防军费""按王国法理各自立国、不开战")：
//   · 异族之地：帝国治下(中央连同成员)主流文化与统治文化不同族的城(CultureService.SameKin)；复合帝国的制度文化那一族算照顾到了，
//     不算异族。城里主流文化改成同族(同化)就不再算。每年扫一遍，城上记归本帝国的时间(foreign_since)：本帝国打下的按打下时，
//     成员国的城按成员入帝国时，都没有的从第一次看到起算；
//   · 正统：异族人口(隔海的按 OverseasWeight 倍)占治下人口过 FreeShare 起扣，每多一成扣 PenaltyPerShare/10，至多 MaxPenalty
//     (见 MonarchyLegitimacy)；
//   · 胡无百年：归本帝国满 OverdueYears(三代 90 年)仍未同化的异族之地，正统一跌破 BreakLegitimacy，先拿国库出兵镇压——
//     付一年驻防军费(这些城人口占比 × 一年岁入 × GarrisonShare，至少每城 GarrisonFloor，记战争开支)，付得起就压住一年；
//     付不起就全部自立：都城是这样的城的成员国连国离开帝国，中央直辖的(京城所在的王国法理除外)按王国法理各自立国，都和平离开、不开战；
//     帝国剩下的城不到原来的 FallShare，帝国名号作废(同汗国崩解)。土崩瓦解、分裂进程正走着的等它走完再判。
public static class ForeignLandSystem
{
    public const float OverdueYears = AdministrativeControlSystem.GenerationYears * 3f;
    private const float FreeShare = 0.1f, PenaltyPerShare = 60f, OverseasWeight = 1.5f, GarrisonShare = 0.5f, FallShare = 0.25f;
    private const int MaxPenalty = 40, BreakLegitimacy = 50, GarrisonFloor = 5, NamesShown = 4;

    private static object _world;
    private static double _lastYear = -1d, _lastMonth = -1d;
    // 有满三代仍未同化的异族之地的帝国(年度扫描时记)
    private static readonly HashSet<long> Overdue = new();

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.cities == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            _lastYear = _lastMonth = -1d;
            Overdue.Clear();
        }
        double now = world.getCurWorldTime();
        try
        {
            if (_lastYear < 0d || now < _lastYear || Date.getYearsSince(_lastYear) >= 1)
            {
                _lastYear = now;
                Scan(now);
            }
            if (_lastMonth < 0d || now < _lastMonth || Date.getMonthsSince(_lastMonth) >= 1)
            {
                _lastMonth = now;
                CheckBreakaways(now);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][异族之地] 结算失败: {exception.Message}");
        }
    }

    // 这座城对这个帝国是异族之地：主流文化与统治文化不同族，复合帝国也不是制度文化那一族
    public static bool IsForeign(City city, Empire empire)
    {
        string culture = CultureService.GetMainCulture(city, initialize: false);
        string ruling = CompositeEmpireService.GetRulingCulture(empire);
        if (!CultureService.IsValidCulture(culture) || !CultureService.IsValidCulture(ruling) || CultureService.SameKin(culture, ruling))
            return false;
        if (!CompositeEmpireService.IsComposite(empire)) return true;
        string institutional = CompositeEmpireService.GetInstitutionalCulture(empire);
        return !CultureService.IsValidCulture(institutional) || !CultureService.SameKin(culture, institutional);
    }

    // 正统扣分(年度快照，见 MonarchyLegitimacy)
    public static (string key, int value) LegitimacyItem(Empire empire)
    {
        float share = empire?.data?.foreign_land_share ?? 0f;
        int penalty = (int)Math.Round(Math.Min(MaxPenalty, Math.Max(0f, share - FreeShare) * PenaltyPerShare), MidpointRounding.AwayFromZero);
        return ("mandate_foreign_lands", -penalty);
    }

    #region 年度扫描

    private static void Scan(double now)
    {
        Overdue.Clear();
        var stats = new Dictionary<long, (double total, double foreign)>();
        foreach (City city in World.world.cities.list.ToList())
        {
            if (city?.data == null || city.isRekt() || !Alive(city.kingdom)) continue;
            var data = city.GetOrCreate();
            Empire empire = city.kingdom.GetEmpire();
            bool valid = empire?.data != null && !empire.isRekt() && !empire.IsArchived() && Alive(empire.CoreKingdom);
            bool foreign = valid && IsForeign(city, empire);
            if (!foreign)
            {
                data.foreign_since = -1d;
                data.foreign_realm = -1L;
            }
            else
            {
                Kingdom realm = empire.CoreKingdom;
                if (data.foreign_realm != realm.id || data.foreign_since < 0d || data.foreign_since > now)
                {
                    data.foreign_realm = realm.id;
                    data.foreign_since = Since(city, realm, now);
                }
                if (Date.getYearsSince(data.foreign_since) >= OverdueYears) Overdue.Add(empire.id);
            }
            if (!valid) continue;
            double people = Math.Max(1, city.CountLivingPopulation());
            stats.TryGetValue(empire.id, out var stat);
            stats[empire.id] = (stat.total + people, stat.foreign + (foreign ? people * (data.admin_overseas ? OverseasWeight : 1f) : 0d));
        }
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            if (empire?.data == null) continue;
            empire.data.foreign_land_share = stats.TryGetValue(empire.id, out var stat) && stat.total > 0d
                ? (float)Math.Min(1d, stat.foreign / stat.total)
                : 0f;
        }
        MonarchyLegitimacy.Invalidate();
    }

    // 归本帝国的时间：本帝国打下的按打下时，成员国的城按成员入帝国时，都没有的从现在起算
    private static double Since(City city, Kingdom realm, double now)
    {
        var data = city.GetOrCreate();
        if (data.conquered_by == realm.id && data.conquered_at >= 0d && data.conquered_at <= now) return data.conquered_at;
        Kingdom kingdom = city.kingdom;
        if (kingdom != realm)
        {
            double joined = kingdom.GetTimestampEmpire();
            if (joined >= 0d && joined <= now) return joined;
        }
        return now;
    }

    private static List<City> OverdueCities(Empire empire)
    {
        var result = new List<City>();
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (!Alive(kingdom) || kingdom.cities == null) continue;
            foreach (City city in kingdom.cities.ToList())
            {
                if (city?.data == null || city.isRekt()) continue;
                var data = city.GetOrCreate();
                if (data.foreign_realm == empire.CoreKingdom.id && data.foreign_since >= 0d &&
                    Date.getYearsSince(data.foreign_since) >= OverdueYears && IsForeign(city, empire))
                    result.Add(city);
            }
        }
        return result;
    }

    #endregion

    #region 胡无百年

    private static void CheckBreakaways(double now)
    {
        foreach (long id in Overdue.ToList())
        {
            Empire empire = ModClass.EMPIRE_MANAGER?.get(id);
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || !Alive(empire.CoreKingdom))
            {
                Overdue.Remove(id);
                continue;
            }
            int legitimacy = empire.Legitimacy;
            if (legitimacy >= BreakLegitimacy || Suppressed(empire, now)) continue;
            if (ProvincialCollapseSystem.Active(empire) || SchismSystem.Active(empire)) continue;
            List<City> cities = OverdueCities(empire);
            if (cities.Count == 0)
            {
                Overdue.Remove(id);
                continue;
            }
            if (TrySuppress(empire, cities, legitimacy, now)) continue;
            Break(empire, cities, legitimacy);
            Overdue.Remove(id);
        }
    }

    private static bool Suppressed(Empire empire, double now)
    {
        double at = empire.data.foreign_suppressed_at;
        return at >= 0d && now >= at && Date.getYearsSince(at) < 1;
    }

    // 付一年驻防军费
    private static bool TrySuppress(Empire empire, List<City> cities, int legitimacy, double now)
    {
        Kingdom core = empire.CoreKingdom;
        double total = 0d, held = 0d;
        foreach (Kingdom kingdom in empire.kingdoms_list)
            if (Alive(kingdom) && kingdom.cities != null)
                foreach (City city in kingdom.cities)
                    if (city?.data != null && !city.isRekt()) total += Math.Max(1, city.CountLivingPopulation());
        foreach (City city in cities) held += Math.Max(1, city.CountLivingPopulation());
        double share = total > 0d ? Math.Min(1d, held / total) : 1d;
        int cost = (int)Math.Min(int.MaxValue, Math.Max(cities.Count * (double)GarrisonFloor, YearlyRevenue(core) * share * GarrisonShare));
        if (!TreasurySystem.TrySpend(core, cost, TreasuryCategory.War, discretionary: false)) return false;
        empire.data.foreign_suppressed_at = now;
        string text = string.Format(LM.Get("foreign_lands_suppress_history"), empire.GetEmpireName(), legitimacy, cities.Count, cost);
        empire.RecordHistory(directContent: text);
        TranslateHelper.LogEventOnly(text, core);
        LogService.LogInfo($"[EmpireCraft][异族之地] {empire.GetEmpireName()} 正统{legitimacy}，出兵镇压三代未附的 {cities.Count} 城，耗银 {cost}");
        return true;
    }

    private static int YearlyRevenue(Kingdom kingdom)
    {
        TreasuryReport report = TreasurySystem.Report(kingdom);
        if (report != null && report.months > 0 && report.income > 0)
            return (int)Math.Min(int.MaxValue, report.income * 12L / report.months);
        return Math.Max(0, kingdom.GetMoney()) / 10;
    }

    // 全部自立：都城是三代未附之地的成员国离开，中央直辖的(京城所在的王国法理除外)按王国法理各自立国
    private static void Break(Empire empire, List<City> cities, int legitimacy)
    {
        Kingdom core = empire.CoreKingdom;
        string realm = empire.GetEmpireName();
        int total = UpheavalGuard.Cities(empire);
        var overdue = new HashSet<City>(cities);
        var gone = new List<Kingdom>();
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (member == core || !Alive(member) || member.capital == null || !overdue.Contains(member.capital) ||
                member.isInWarWith(core)) continue;
            member.FinishedSelfPlot();
            empire.leave(member, true, false);
            if (member.IsInEmpire()) continue;
            EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
            gone.Add(member);
        }
        City seat = core.capital;
        KingdomTitle seatTitle = seat?.GetTitle();
        var groups = cities.Where(city => city.kingdom == core && city != seat && (seatTitle == null || city.GetTitle() != seatTitle))
            .GroupBy(city => city.GetTitle()?.id ?? -city.id).ToList();
        foreach (var group in groups)
        {
            List<City> cluster = group.OrderByDescending(city => city.CountLivingPopulation()).ThenBy(city => city.id).ToList();
            Kingdom local = AdministrativeControlSystem.ReleaseToLocal(core, cluster, _ => "independent", out _);
            if (local != null) gone.Add(local);
        }
        if (gone.Count == 0)
        {
            LogService.LogInfo($"[EmpireCraft][异族之地] {realm} 正统{legitimacy}，三代未附的 {cities.Count} 城没有走得了的");
            return;
        }
        int remaining = UpheavalGuard.Cities(empire), taken = gone.Sum(kingdom => kingdom.cities?.Count ?? 0);
        bool fell = remaining < total * FallShare;
        UpheavalGuard.Mark(empire, "异族之地自立", detail: $"走 {gone.Count} 国 {taken} 城，剩 {remaining}/{total} 城");
        string names = string.Join(LM.Get("schism_name_separator"), gone.Take(NamesShown).Select(kingdom => kingdom.GetKingdomName()));
        if (gone.Count > NamesShown) names = string.Format(LM.Get("foreign_lands_break_more"), names, gone.Count);
        string content = string.Format(LM.Get(fell ? "foreign_lands_break_fall_history" : "foreign_lands_break_history"),
            realm, legitimacy, names, remaining);
        empire.RecordHistory(directContent: content);
        TranslateHelper.LogMajor(content, core);
        LogService.LogInfo($"[EmpireCraft][异族之地] {realm} 正统{legitimacy}，国库付不起镇压：{string.Join("、", gone.Select(kingdom => kingdom.GetKingdomName()))}" +
                           $"(共 {gone.Count} 国 {taken} 城)自立，剩 {remaining}/{total} 城{(fell ? $"，不到{FallShare:P0}，帝国名号作废" : "")}");
        if (!fell) return;
        ProvincialCollapseSystem.Absorb(empire, null, "[EmpireCraft][异族之地]");
        ModClass.EMPIRE_MANAGER?.dissolveEmpire(empire);
    }

    #endregion
}

using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 奉诏设的陪京与直隶(用户 2026-11-17 "希望给玩家迁都和设置多个首都和直隶的功能"，走决议天平，见 Decisions.RulerDecrees)：
//   · 陪京：皇帝点本帝国(连成员)手里的一座城为陪京，记在 EmpireData.decreed_jing。不看华夏不华夏、朝廷走到哪个阶段、
//     据不据有天下一半——自动设京的那些条件只管自动的；城还在本帝国手里就一直是京，失了城这道诏令就作废。
//     连京师至多 MaxCapitals 京，奉诏的排在自动挑的前头；京名、宫城、稳定、行政治所都同自动设的一样(见 Arrange)；
//   · 撤陪京：只撤奉诏设的；自动设的随条件自己撤；
//   · 直隶：皇帝点一座陪京设直隶(记在 EmpireData.decreed_zhili_city)，照第二直隶的规矩办(储君就藩，见 MultiCapitalSystem.Zhili)，
//     不必等到内阁阶段；要律令制(直隶这个类别只在律令制的配置里有)。一朝只有一个第二直隶，改点别的城就挪过去。
// 模组设置「多京与直隶」关掉时都不设。
public static partial class MultiCapitalSystem
{
    // 眼下有几座陪京(不含京师)
    public static int AuxiliaryCount(Empire empire) =>
        empire?.data?.jing == null ? 0 : empire.data.jing.Count(record => record != null && !string.IsNullOrEmpty(record.direction));

    private static bool Holds(Empire empire, City city) =>
        city?.data != null && !city.isRekt() && city.kingdom != null && !city.kingdom.isRekt() && city.kingdom.GetEmpire() == empire;

    // 奉诏设的陪京里眼下还作数的(城还在本帝国手里、不是京师)；不作数的从诏令里除掉
    private static List<(EmpireCore core, City city)> DecreedCapitals(Empire empire)
    {
        var result = new List<(EmpireCore core, City city)>();
        List<long> ids = empire?.data?.decreed_jing;
        City main = empire?.CoreKingdom?.capital;
        if (ids == null || ids.Count == 0 || main == null || !RealmNamingSystem.MultiCapitalEnabled) return result;
        EmpireCore own = EmpireCoreManager.Get(empire);
        for (int i = ids.Count - 1; i >= 0; i--)
        {
            City city = World.world?.cities?.get(ids[i]);
            if (!Holds(empire, city))
            {
                ids.RemoveAt(i);
                continue;
            }
            // 迁都迁到了陪京：它成了京师，陪京这一条不再作数
            if (city == main)
            {
                ids.RemoveAt(i);
                continue;
            }
            EmpireCore core = city.GetEmpireCore() ?? own;
            if (core != null) result.Insert(0, (core, city));
        }
        if (result.Count > MaxCapitals - 1) result.RemoveRange(MaxCapitals - 1, result.Count - (MaxCapitals - 1));
        return result;
    }

    // 奉诏的在前、自动挑的在后，同一座城只算一回，连京师至多 MaxCapitals 京
    private static List<(EmpireCore core, City city)> Merge(List<(EmpireCore core, City city)> decreed, List<(EmpireCore core, City city)> picks)
    {
        var merged = new List<(EmpireCore core, City city)>(decreed);
        foreach ((EmpireCore core, City city) pick in picks)
        {
            if (merged.Count >= MaxCapitals - 1) break;
            if (merged.All(entry => entry.city != pick.city)) merged.Add(pick);
        }
        return merged;
    }

    // 奉诏设直隶的那座陪京(要还在奉诏的陪京里、本朝是律令制)；没有返回空
    private static (EmpireCore core, City city) DecreedZhiliSeat(Empire empire, List<(EmpireCore core, City city)> decreed)
    {
        long id = empire?.data?.decreed_zhili_city ?? -1L;
        if (id < 0) return default;
        (EmpireCore core, City city) seat = decreed.FirstOrDefault(entry => entry.city.data.id == id);
        if (seat.city == null || empire.CoreKingdom?.GetRegime()?.type != RegimeType.LvLing)
        {
            // 那座陪京撤了、失了，或改了政体：这道诏令作废
            if (seat.city == null) empire.data.decreed_zhili_city = -1L;
            return default;
        }
        return seat;
    }

    // 可以奉诏设为陪京的城：本帝国(连成员)手里的城，不是京师、还不是京、没被占着、不在代管里；人口多的在前
    public static List<City> DecreeCandidates(Empire empire, int limit)
    {
        var result = new List<City>();
        City main = empire?.CoreKingdom?.capital;
        if (empire?.data == null || main == null || !RealmNamingSystem.MultiCapitalEnabled || AuxiliaryCount(empire) >= MaxCapitals - 1) return result;
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (member?.data == null || member.isRekt() || member.cities == null) continue;
            foreach (City city in member.cities)
            {
                if (!Holds(empire, city) || city == main || IsJing(city)) continue;
                if (city.GetOrCreate().OccupiedStatus?.Count > 0 || WartimeCustodySystem.InCustody(city)) continue;
                result.Add(city);
            }
        }
        return result.OrderByDescending(city => CityPopulationSystem.GetTotal(city)).ThenBy(city => city.data.id).Take(limit).ToList();
    }

    // 奉诏设的、眼下还是京的陪京(撤陪京只撤这些)
    public static List<City> DecreedCities(Empire empire)
    {
        var result = new List<City>();
        if (empire?.data?.decreed_jing == null) return result;
        foreach (long id in empire.data.decreed_jing)
        {
            City city = World.world?.cities?.get(id);
            if (Holds(empire, city) && IsJing(city)) result.Add(city);
        }
        return result;
    }

    // 可以奉诏设直隶的陪京：本朝是律令制；那座陪京在中央直辖或行政区(省)手里，还不是直隶
    public static List<City> ZhiliCandidates(Empire empire)
    {
        var result = new List<City>();
        Kingdom core = empire?.CoreKingdom;
        if (empire?.data == null || core == null || !RealmNamingSystem.MultiCapitalEnabled || core.GetRegime()?.type != RegimeType.LvLing) return result;
        Kingdom current = CurrentZhili(empire);
        foreach (City city in DecreedCities(empire))
        {
            Kingdom holder = city.kingdom;
            if (current != null && holder == current) continue;
            if (holder == core || holder.IsAdministrativeKingdomType() && !IsZhili(holder)) result.Add(city);
        }
        return result;
    }

    // 奉诏设陪京：记下来并当场安排；没排上(四方都占了)就不记，返回 false
    public static bool DecreeJing(Empire empire, City city)
    {
        if (empire?.data == null || !Holds(empire, city) || city == empire.CoreKingdom?.capital || !RealmNamingSystem.MultiCapitalEnabled) return false;
        empire.data.decreed_jing ??= new List<long>();
        if (!empire.data.decreed_jing.Contains(city.data.id)) empire.data.decreed_jing.Add(city.data.id);
        Yearly(empire);
        if (IsJing(city)) return true;
        empire.data.decreed_jing.Remove(city.data.id);
        return false;
    }

    // 撤掉奉诏设的陪京(设在它上面的直隶一并撤)
    public static bool RevokeJing(Empire empire, City city)
    {
        if (empire?.data?.decreed_jing == null || city?.data == null || !empire.data.decreed_jing.Remove(city.data.id)) return false;
        if (empire.data.decreed_zhili_city == city.data.id) empire.data.decreed_zhili_city = -1L;
        Yearly(empire);
        return true;
    }

    // 奉诏设直隶：设在这座陪京；没设成(那片地不在中央或行政区手里)返回 false
    public static bool DecreeZhili(Empire empire, City city)
    {
        if (empire?.data == null || city?.data == null || !ZhiliCandidates(empire).Contains(city)) return false;
        long before = empire.data.decreed_zhili_city;
        empire.data.decreed_zhili_city = city.data.id;
        Yearly(empire);
        Kingdom zhili = CurrentZhili(empire);
        if (zhili != null && city.kingdom == zhili) return true;
        empire.data.decreed_zhili_city = before;
        LogService.LogInfo($"[EmpireCraft][直隶] {empire.GetEmpireFullName()} 奉诏在 {city.GetCityName()} 设直隶没设成");
        return false;
    }
}

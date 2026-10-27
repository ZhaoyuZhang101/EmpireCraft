using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 各文明的本土大教与本土回流(用户 2026-10-27 截图全图只剩基督教、蒙古萨满教，"要符合历史实际的发展"；
// 方案 Docs/plan-religion-spread.md，问答全按推荐)。每年一次，挂在 ReligiousTension 的年度扫描上，有无小人都结算：
//   · 立教：天下(文化配置的天下标识)里有文化研究出礼仪祭祀(二档)，教会、伊斯兰这种一神教还要冶铁(四档)；
//     本天下还没有本土大教(没有城奉本天下的本土宗教)时，在本天下人口最多的城立教，这座城就是圣地——不看城里奉着什么外来宗教。
//     本天下以前立过、如今没人奉的本土宗教在那座城复兴，不另立；宗教类型、名字照创教者的文化(见 ReligionPatch、ReligionFaith)。
//     每年至多一处；科技树关着时按城满 FallbackCityAge 年算；
//   · 回流：本天下的城奉着外来的民族宗教(三教、神道、腾格里、犹太、民间)，每年 ReturnChance 改回本天下的本土大教；
//     外来普世宗教(教会、伊斯兰、法教)不回流，靠本土正教的势能拉锯(见 BackgroundReligionSystem)；别教的圣地不动。
//   跨文明传教的阻力见 ReligionSphere.CrossFactor，入主从俗见 ReligionConversionSystem。
public static class NativeFaithSystem
{
    public const string FoundingTech = "ritual_rites", MonotheismTech = "ironworking";
    public const float ReturnChance = 0.08f;
    public const int FallbackCityAge = 20;

    private static bool Valid(City city) => city?.data != null && !city.isRekt() && city.kingdom?.data != null &&
                                           !city.kingdom.wild && !AncientWarfareCompatibility.OwnsObject(city);

    private static bool Alive(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive();

    public static void YearlyPass()
    {
        if (World.world?.cities == null || ModClass.IS_CLEAR) return;
        var bySphere = new Dictionary<string, List<City>>(StringComparer.Ordinal);
        foreach (City city in World.world.cities.list)
        {
            if (!Valid(city)) continue;
            string sphere = ReligionSphere.SphereOfCity(city);
            if (sphere.Length == 0) continue;
            if (!bySphere.TryGetValue(sphere, out List<City> list)) bySphere[sphere] = list = new List<City>();
            list.Add(city);
        }
        foreach (KeyValuePair<string, List<City>> pair in bySphere.OrderByDescending(pair => pair.Value.Count))
        {
            if (ReligionSphere.OrthodoxOf(pair.Key) != null) continue;
            if (TryFound(pair.Key, pair.Value)) break;
        }
        int returned = 0;
        foreach (KeyValuePair<string, List<City>> pair in bySphere)
        {
            Religion orthodox = ReligionSphere.OrthodoxOf(pair.Key);
            if (orthodox == null) continue;
            foreach (City city in pair.Value)
            {
                if (!city.hasReligion()) continue;
                Religion current = city.religion;
                if (current == orthodox || ReligionFaith.IsUniversal(current) || current.GetCity() == city) continue;
                if (ReligionSphere.SphereOf(current) == pair.Key) continue;
                if (UnityEngine.Random.value >= ReturnChance) continue;
                BackgroundReligionSystem.Convert(city, orthodox, current);
                returned++;
            }
        }
        if (returned > 0) LogService.LogInfo($"[EmpireCraft][宗教] 本年 {returned} 城由外来民族宗教回归本土大教");
    }

    // 本天下到了立教的时代：有文化研究出礼仪祭祀；一神教还要冶铁
    private static bool Ready(List<City> cities, FaithType type)
    {
        if (!TechnologySystem.IsEnabled) return cities.Any(city => city.getAge() >= FallbackCityAge);
        foreach (string culture in cities.Select(city => CultureService.GetMainCulture(city, initialize: false)).Distinct())
        {
            if (!CultureService.IsValidCulture(culture) || !TechnologySystem.HasTech(culture, FoundingTech)) continue;
            if (!ReligionFaith.IsLateFaith(type) || TechnologySystem.HasTech(culture, MonotheismTech)) return true;
        }
        return false;
    }

    private static bool TryFound(string sphere, List<City> cities)
    {
        // 本天下人口最多的城(别教的圣地不算)
        City seat = cities.Where(city => !(city.hasReligion() && city.religion.GetCity() == city))
            .OrderByDescending(city => city.CountLivingPopulation()).FirstOrDefault();
        if (seat == null) return false;
        FaithType type = ReligionFaith.OfCulture(CultureService.GetMainCulture(seat, initialize: false));
        if (!Ready(cities, type)) return false;
        Religion old = seat.hasReligion() ? seat.religion : null;
        Religion dormant = Dormant(sphere);
        if (dormant != null)
        {
            dormant.SetCity(seat);
            BackgroundReligionSystem.Convert(seat, dormant, old);
            Announce(dormant, seat, "religion_revived_log");
            return true;
        }
        Religion founded = Found(seat, old);
        if (founded == null) return false;
        Announce(founded, seat, null);
        return true;
    }

    // 本天下以前立过、如今没有城奉的本土宗教
    private static Religion Dormant(string sphere)
    {
        foreach (Religion religion in World.world.religions.list)
            if (religion?.data != null && !religion.isRekt() && ReligionSphere.IsNative(religion) &&
                ReligionSphere.SphereOf(religion) == sphere) return religion;
        return null;
    }

    private static Religion Found(City city, Religion old)
    {
        Actor founder = Founder(city);
        if (founder == null) return null;
        Religion religion;
        try
        {
            religion = World.world.religions.newReligion(founder, pAddDefaultTraits: true);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宗教] {city.data.name} 立本土宗教失败: {exception.Message}");
            return null;
        }
        if (religion?.data == null) return null;
        founder.setReligion(religion);
        // 圣地就是立教之城(ReligionPatch 按创教者当时所在的城设，人可能不在城里)
        religion.SetCity(city);
        BackgroundReligionSystem.Convert(city, religion, old);
        TranslateHelper.LogEventMessage(string.Format(LM.Get("religion_founded_log"), founder.getName(), city.GetCityName(),
            religion.name), city.kingdom);
        return religion;
    }

    // 立教者：本城城主，没有就从本城人口里补一位(见 CityPopulationSystem.SpawnOfficial)，再没有才是都城的君主
    private static Actor Founder(City city)
    {
        Actor leader = city.leader;
        if (Alive(leader) && leader.hasCulture() && leader.city == city) return leader;
        Actor official = CityPopulationSystem.SpawnOfficial(city, city.kingdom);
        if (Alive(official) && official.hasCulture()) return official;
        Actor king = city.isCapitalCity() ? city.kingdom.king : null;
        return Alive(king) && king.hasCulture() ? king : null;
    }

    private static void Announce(Religion religion, City city, string key)
    {
        if (key == null)
        {
            LogService.LogInfo($"[EmpireCraft][宗教] {city.GetCityName()} 立本土大教 {religion.name}");
            return;
        }
        string text = string.Format(LM.Get(key), religion.name, city.GetCityName());
        TranslateHelper.LogEventMessage(text, city.kingdom);
        ReligionChronicle.Record(religion, ReligionChronicle.KindFounding, text);
        LogService.LogInfo($"[EmpireCraft][宗教] {text}");
    }
}

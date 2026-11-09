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
//   · 立教：天下(文化配置的天下标识)里有文化研究出礼仪祭祀(二档)；
//     本天下还没有本土大教(没有城奉本天下的本土宗教)时，在本天下人口最多的城立教，这座城就是圣地——不看城里奉着什么外来宗教。
//     本天下以前立过、如今没人奉的本土宗教在那座城复兴，不另立；宗教类型、名字照创教者的文化(见 ReligionPatch、ReligionFaith)。
//     每年至多一处；科技树关着时按城满 FallbackCityAge 年算；
//   · 回流：本天下的城奉着外来的民族宗教(三教、神道、腾格里、犹太、民间)，每年 ReturnChance 改回本天下的本土大教；
//     外来普世宗教(教会、伊斯兰、法教)不回流，靠本土正教的势能拉锯(见 BackgroundReligionSystem)；别教的圣地不动。
//   跨文明传教的阻力见 ReligionSphere.CrossFactor，入主从俗见 ReligionConversionSystem。
// 两段式(用户 2026-11-06 选"两段式"："不改的话就会出现在古典时代全世界都信基督")：教会、伊斯兰这种一神普世教的天下(罗马、乌玛)
//   · 先在礼仪祭祀时立"古神"(民间型，名字取 faith_name_oldgods_<文化>/<天下>，如希腊罗马诸神、日耳曼诸神、阿拉伯诸神)；
//   · 古神立满 min_years(60)年、本天下有文化研究出冶铁与文字(ReligionTenets.json 的 revelation.techs)——剧本里科技年历开着的
//     改看史实年份(基督教 30 年、伊斯兰 610 年，到了就不必等满 60 年)——每年 chance(2%)在本天下立"启示宗教"(教会或伊斯兰)：
//     立在本天下城数最少的独立之国的都城(这国君主率国改奉，此后四十年国教势能加倍)，没有就取本天下中等人口的城；
//     立教 grace_years(150)年内不算地方教会(不被并入古神，见 ReligionSphere.IsLocalChurch)，对本天下奉古神的君主另有拉力
//     (见 ReligionConversionSystem)，追上古神后接任正教，古神成了地方教会再并入——"在本天下慢慢取代古神"；
//   · 有小人时原版创教谋划也按这里的时代来(见 AllowsVanillaFounding、PlotAssetPatch)。
public static class NativeFaithSystem
{
    public const string FoundingTech = "ritual_rites", MonotheismTech = "ironworking";
    public const float ReturnChance = 0.08f;
    public const int FallbackCityAge = 20;

    private static bool Valid(City city) => city?.data != null && !city.isRekt() && city.kingdom?.data != null &&
                                           !city.kingdom.wild && !AncientWarfareCompatibility.OwnsObject(city);

    private static bool Alive(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive();

    private static bool Alive(Religion religion) => religion?.data != null && !religion.isRekt();

    public static void YearlyPass()
    {
        if (World.world?.cities == null || ModClass.IS_CLEAR) return;
        Dictionary<string, List<City>> bySphere = CitiesBySphere();
        foreach (KeyValuePair<string, List<City>> pair in bySphere.OrderByDescending(pair => pair.Value.Count))
        {
            if (ReligionSphere.OrthodoxOf(pair.Key) != null) continue;
            if (TryFound(pair.Key, pair.Value)) break;
        }
        TryRevelation(bySphere);
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
        ReligionDiagnostics.Add(ReligionDiagnostics.Returned, returned);
        if (returned > 0) LogService.LogInfo($"[EmpireCraft][宗教] 本年 {returned} 城由外来民族宗教回归本土大教");
    }

    private static Dictionary<string, List<City>> CitiesBySphere()
    {
        var bySphere = new Dictionary<string, List<City>>(StringComparer.Ordinal);
        foreach (City city in World.world.cities.list)
        {
            if (!Valid(city)) continue;
            string sphere = ReligionSphere.SphereOfCity(city);
            if (sphere.Length == 0) continue;
            if (!bySphere.TryGetValue(sphere, out List<City> list)) bySphere[sphere] = list = new List<City>();
            list.Add(city);
        }
        return bySphere;
    }

    // 这个天下立教先立古神(两段式开着、本天下的大教是教会或伊斯兰)
    private static bool OldGodsFirst(FaithType type) => ReligionTenets.Revelation.enabled && ReligionFaith.IsLateFaith(type);

    // 本天下到了立这一类教的时代：有文化研究出礼仪祭祀；一神教还要冶铁与文字(两段式的 revelation.techs)
    private static bool Ready(List<City> cities, FaithType type)
    {
        if (!TechnologySystem.IsEnabled) return cities.Any(city => city.getAge() >= FallbackCityAge);
        List<string> late = LateTechs();
        foreach (string culture in cities.Select(city => CultureService.GetMainCulture(city, initialize: false)).Distinct())
        {
            if (!CultureService.IsValidCulture(culture) || !TechnologySystem.HasTech(culture, FoundingTech)) continue;
            if (!ReligionFaith.IsLateFaith(type) || late.All(tech => TechnologySystem.HasTech(culture, tech))) return true;
        }
        return false;
    }

    private static List<string> LateTechs()
    {
        List<string> techs = ReligionTenets.Revelation.techs;
        return techs != null && techs.Count > 0 ? techs : new List<string> { MonotheismTech };
    }

    private static bool TryFound(string sphere, List<City> cities)
    {
        // 本天下人口最多的城(别教的圣地不算)
        City seat = cities.Where(city => !(city.hasReligion() && city.religion.GetCity() == city))
            .OrderByDescending(city => city.CountLivingPopulation()).FirstOrDefault();
        if (seat == null) return false;
        string culture = CultureService.GetMainCulture(seat, initialize: false);
        FaithType type = ReligionFaith.OfCulture(culture);
        bool oldGods = OldGodsFirst(type);
        if (!Ready(cities, oldGods ? FaithType.Folk : type)) return false;
        Religion old = seat.hasReligion() ? seat.religion : null;
        Religion dormant = Dormant(sphere);
        if (dormant != null)
        {
            dormant.SetCity(seat);
            BackgroundReligionSystem.Convert(seat, dormant, old);
            Announce(dormant, seat, "religion_revived_log");
            return true;
        }
        Religion founded = Found(seat, old, oldGods ? FaithType.Folk : (FaithType?)null, culture, sphere);
        if (founded == null) return false;
        if (oldGods) ReligionDiagnostics.Add(ReligionDiagnostics.OldGods);
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

    // asType：立古神时按民间型立(创教时 ReligionPatch 按文化定成了教会/伊斯兰，这里改回并按古神名库取名)
    private static Religion Found(City city, Religion old, FaithType? asType = null, string culture = null, string sphere = null)
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
        if (asType != null)
        {
            ReligionFaith.Record(religion, asType.Value);
            string name = OldGodsName(culture, sphere);
            if (name != null) religion.data.name = name;
            else if (CultureService.IsValidCulture(culture)) EmpireCraft.Scripts.GamePatches.ReligionPatch.InsertReligionNameTemplate(religion, culture);
            ReligionChronicle.Clear(religion);
            ReligionTenets.Invalidate();
        }
        founder.setReligion(religion);
        // 圣地就是立教之城(ReligionPatch 按创教者当时所在的城设，人可能不在城里)
        religion.SetCity(city);
        BackgroundReligionSystem.Convert(city, religion, old);
        string text = string.Format(LM.Get("religion_founded_log"), founder.getName(), city.GetCityName(), religion.name);
        TranslateHelper.LogEventMessage(text, city.kingdom);
        if (asType != null) ReligionChronicle.Record(religion, ReligionChronicle.KindFounding, text);
        return religion;
    }

    // 古神的名字：本文化(faith_name_oldgods_<文化小写>)，再本天下(faith_name_oldgods_<天下小写>_1..3)
    private static string OldGodsName(string culture, string sphere)
    {
        if (!string.IsNullOrEmpty(culture))
        {
            string key = "faith_name_oldgods_" + culture.ToLowerInvariant();
            string text = LM.Get(key);
            if (!string.IsNullOrEmpty(text) && text != key) return text;
        }
        return string.IsNullOrEmpty(sphere) ? null : ReligionFaith.PickName("oldgods_" + sphere.ToLowerInvariant(), 3);
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

    #region 启示宗教(两段式的后一段)

    // 本天下立了、还在宽限期里的启示宗教(还没成正教；宗教窗口、君主皈依用)
    public static Religion RevealedOf(string sphere)
    {
        if (string.IsNullOrEmpty(sphere) || World.world?.religions == null || !ReligionTenets.Revelation.enabled) return null;
        Religion orthodox = ReligionSphere.OrthodoxOf(sphere);
        if (orthodox == null) return null;
        foreach (Religion religion in World.world.religions.list)
        {
            if (!Alive(religion) || religion == orthodox || religion.GetOrCreate().RevealedAt < 0d) continue;
            if (ReligionSphere.SphereOf(religion) == sphere && ReligionSphere.InRevelationGrace(religion, orthodox)) return religion;
        }
        return null;
    }

    private static void TryRevelation(Dictionary<string, List<City>> bySphere)
    {
        if (!ReligionTenets.Revelation.enabled) return;
        foreach (KeyValuePair<string, List<City>> pair in bySphere.OrderByDescending(pair => pair.Value.Count))
        {
            if (TryReveal(pair.Key, pair.Value)) return;
        }
    }

    private static bool TryReveal(string sphere, List<City> cities)
    {
        Religion orthodox = ReligionSphere.OrthodoxOf(sphere);
        if (orthodox == null) return false;
        City largest = cities.OrderByDescending(city => city.CountLivingPopulation()).FirstOrDefault();
        string culture = CultureService.GetMainCulture(largest, initialize: false);
        FaithType type = ReligionFaith.OfCulture(culture);
        if (!ReligionFaith.IsLateFaith(type) || ReligionFaith.Of(orthodox) == type) return false;
        // 本天下已经有这一类宗教(启示过的、剧本带的、哈里发立的伊斯兰——它记了古神作母宗教)就不再立
        foreach (Religion religion in World.world.religions.list)
            if (Alive(religion) && ReligionSphere.SphereOf(religion) == sphere && ReligionFaith.Of(religion) == type) return false;
        if (!Ready(cities, type)) return false;
        // 剧本(科技年历开着)按史实年份：没到不立，到了就不必再等古神满 min_years 年(117、230 年开局的古神是开局才立的)
        int year = 0;
        bool dated = TechnologySystem.CalendarActive && ReligionTenets.Revelation.calendar_year.TryGetValue(type.ToString(), out year);
        if (dated && TechnologySystem.CalendarYear() < year) return false;
        if (!dated && orthodox.getAge() < ReligionTenets.Revelation.min_years) return false;
        if (UnityEngine.Random.value >= ReligionTenets.Revelation.chance) return false;
        City seat = RevelationSeat(cities);
        if (seat == null) return false;
        Religion old = seat.hasReligion() ? seat.religion : null;
        Religion revealed = Found(seat, old);
        if (revealed == null) return false;
        ReligionFaith.Record(revealed, type);
        ReligionSphere.Record(revealed, culture);
        revealed.GetOrCreate().RevealedAt = World.world.getCurWorldTime();
        ReligionTenets.Invalidate();
        ReligionDiagnostics.Add(ReligionDiagnostics.Revelation);
        Announce(revealed, seat, "religion_revealed_log");
        // 立在一国都城：君主率国改奉(此后四十年国教势能加倍、按皈依冷却)
        Kingdom kingdom = seat.kingdom;
        if (seat.isCapitalCity() && kingdom?.data != null && !kingdom.wild && kingdom.religion != revealed && Alive(kingdom.king) &&
            CaliphateFaith.MayAdopt(kingdom, revealed))
            ReligionConversionSystem.Convert(kingdom, revealed, kingdom.religion, "revelation_ruler_log");
        return true;
    }

    // 启示之地：本天下城数最少的独立之国的都城(别教的圣地不算；小国先奉，像亚美尼亚)，没有就取人口居中的城
    private static City RevelationSeat(List<City> cities)
    {
        City capital = cities.Where(city => city.isCapitalCity() && city.kingdom?.data != null && city.kingdom.GetEmpire() == null &&
                                            !(city.hasReligion() && city.religion.GetCity() == city))
            .OrderBy(city => city.kingdom.cities.Count).ThenByDescending(city => city.CountLivingPopulation()).FirstOrDefault();
        if (capital != null) return capital;
        List<City> ranked = cities.Where(city => !(city.hasReligion() && city.religion.GetCity() == city))
            .OrderByDescending(city => city.CountLivingPopulation()).ToList();
        return ranked.Count == 0 ? null : ranked[ranked.Count / 2];
    }

    #endregion

    // 有小人时原版创教谋划(new_religion)：教会、伊斯兰的天下交给这里的两段式(不许原版直接立出教会)；
    // 别的天下要到了立教的时代(礼仪祭祀)。不是模组文化的照原版
    public static bool AllowsVanillaFounding(Actor founder)
    {
        if (founder?.data == null) return true;
        string culture = CultureService.GetMainCulture(founder.city, initialize: false);
        if (!CultureService.IsValidCulture(culture)) culture = CultureService.GetActorCulture(founder);
        if (!CultureService.IsValidCulture(culture)) return true;
        FaithType type = ReligionFaith.OfCulture(culture);
        if (OldGodsFirst(type)) return false;
        string sphere = ReligionSphere.SphereOfCulture(culture);
        List<City> cities = World.world.cities.list.Where(city => Valid(city) && ReligionSphere.SphereOfCity(city) == sphere).ToList();
        return cities.Count == 0 || Ready(cities, type);
    }
}

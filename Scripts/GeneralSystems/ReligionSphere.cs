using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 一个天下一个正教(用户 2026-10-26 报"宗教太散了，罗马和阿拉伯能有好多个圣地和教皇"，选同天下不另创教、小教并入正教、教宗只认正教)。
// 以前各城各自创教，罗马一系六族、乌玛三族各创各的，每个宗教都有圣地与教宗(哈里发座)：
//   · 宗教的天下：创教时按创教者所在城的主流文化记下(见 ReligionPatch)；旧档没记的按圣地的主流文化、再按创教之国的文化补上；
//     从别的宗教分出去的教会(ParentReligionId：宗教改革另立的国教会、诸侯新教会)随母宗教；
//   · 本土宗教 = 不是从别的宗教分出去的；天下的正教 = 本天下本土宗教里信众(按奉它的城的人口)最多的，现任要被超过两成五才换；
//   · 同天下已有城奉本土宗教时，本天下的城不再创教(背景人口创教、原版创教谋划都拦)；
//   · 传教：本天下的无教之城、奉地方教会的城，正教的势能加倍；无教之城不挨着正教的城也有一份正教势能(见 BackgroundReligionSystem.Pressure)；
//   · 并入(每年)：地方教会(同天下的本土小教)丢了圣地(圣地已不奉它)或只剩 MergeMaxCities 城以下时，整体改奉正教；
//   · 教廷：地方教会没有教宗/哈里发座(PapacySystem.Papacy 返回空)，奉它的帝国也不算有教廷往来(没有加冕、什一税、天课)。
//   分出去的教会不算地方教会，这三条都不管它，宗教改革、诸侯新教会照旧。
public static class ReligionSphere
{
    public const int MergeMaxCities = 3;
    public const float OrthodoxMissionFactor = 2f;
    public const float SphereMissionWeight = 0.3f;
    private const float OrthodoxSwitchMargin = 1.25f;

    // 天下 → 正教 id(不存档，读档后头一次查时重算)
    private static readonly Dictionary<string, long> Orthodox = new(StringComparer.Ordinal);
    private static double _refreshedAt = -1d;

    public static void ResetWorldState()
    {
        Orthodox.Clear();
        _refreshedAt = -1d;
    }

    private static bool Alive(Religion religion) => religion?.data != null && !religion.isRekt();

    public static string SphereOfCulture(string culture) =>
        CultureService.IsValidCulture(culture) ? culture.GetTianxiaId() : "";

    public static string SphereOfCity(City city) =>
        city?.data == null || city.isRekt() ? "" : SphereOfCulture(CultureService.GetMainCulture(city, initialize: false));

    public static string SphereOf(Religion religion) => SphereOf(religion, 0);

    private static string SphereOf(Religion religion, int depth)
    {
        if (religion?.data == null) return "";
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        if (!string.IsNullOrEmpty(data.Sphere)) return data.Sphere;
        string sphere = "";
        if (data.ParentReligionId >= 0 && depth < 8)
        {
            Religion parent = World.world.religions.get(data.ParentReligionId);
            if (parent != null && parent != religion) sphere = SphereOf(parent, depth + 1);
        }
        if (sphere.Length == 0) sphere = SphereOfCulture(OriginCulture(religion));
        if (sphere.Length > 0) data.Sphere = sphere;
        return sphere;
    }

    // 旧档补记用：圣地的主流文化，没有就取创教之国的文化
    public static string OriginCulture(Religion religion)
    {
        if (religion?.data == null) return "";
        City holy = religion.GetCity();
        string culture = holy?.data != null && !holy.isRekt() ? CultureService.GetMainCulture(holy, initialize: false) : "";
        if (CultureService.IsValidCulture(culture)) return culture;
        return CultureService.GetRealmCulture(World.world.kingdoms.get(religion.data.creator_kingdom_id));
    }

    // 创教时记下(见 ReligionPatch)
    public static void Record(Religion religion, string culture)
    {
        string sphere = SphereOfCulture(culture);
        if (religion?.data != null && sphere.Length > 0) religion.GetOrCreate().Sphere = sphere;
    }

    public static bool IsNative(Religion religion) => religion?.data != null && religion.GetOrCreate().ParentReligionId < 0;

    public static Religion OrthodoxOf(string sphere)
    {
        if (string.IsNullOrEmpty(sphere)) return null;
        Refresh(false);
        if (!Orthodox.TryGetValue(sphere, out long id)) return null;
        Religion religion = World.world.religions.get(id);
        return Alive(religion) ? religion : null;
    }

    // 地方教会：同天下的本土宗教，但不是正教
    public static bool IsLocalChurch(Religion religion)
    {
        if (!Alive(religion) || !IsNative(religion)) return false;
        Religion orthodox = OrthodoxOf(SphereOf(religion));
        return orthodox != null && orthodox != religion;
    }

    // 有城奉着本土宗教的天下(这些天下的城不再创教)
    public static HashSet<string> FollowedSpheres(IEnumerable<City> cities)
    {
        var spheres = new HashSet<string>(StringComparer.Ordinal);
        foreach (City city in cities)
        {
            if (city?.data == null || city.isRekt() || !city.hasReligion() || !IsNative(city.religion)) continue;
            string sphere = SphereOf(city.religion);
            if (sphere.Length > 0) spheres.Add(sphere);
        }
        return spheres;
    }

    // 原版创教谋划：创教者的天下已有城奉本土宗教就不创
    public static bool BlocksFounding(Actor founder)
    {
        string culture = CultureService.GetMainCulture(founder?.city, initialize: false);
        if (!CultureService.IsValidCulture(culture)) culture = CultureService.GetActorCulture(founder);
        return OrthodoxOf(SphereOfCulture(culture)) != null;
    }

    private static Dictionary<Religion, float> NativeBelievers()
    {
        var believers = new Dictionary<Religion, float>();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || !city.hasReligion() || !IsNative(city.religion)) continue;
            believers.TryGetValue(city.religion, out float people);
            believers[city.religion] = people + Mathf.Max(1, city.CountLivingPopulation());
        }
        return believers;
    }

    // 每年重算一次各天下的正教
    private static void Refresh(bool force)
    {
        if (World.world?.cities == null || World.world.religions == null || SmoothLoader.isLoading()) return;
        double now = World.world.getCurWorldTime();
        if (!force && _refreshedAt >= 0d && now >= _refreshedAt && Date.getYearsSince(_refreshedAt) < 1) return;
        _refreshedAt = now;
        Dictionary<Religion, float> believers = NativeBelievers();
        var best = new Dictionary<string, (Religion religion, float people)>(StringComparer.Ordinal);
        foreach (KeyValuePair<Religion, float> pair in believers)
        {
            string sphere = SphereOf(pair.Key);
            if (sphere.Length == 0) continue;
            if (!best.TryGetValue(sphere, out var top) || pair.Value > top.people ||
                pair.Value == top.people && pair.Key.data.id < top.religion.data.id)
                best[sphere] = (pair.Key, pair.Value);
        }
        foreach (string sphere in Orthodox.Keys.ToList())
            if (!best.ContainsKey(sphere)) Orthodox.Remove(sphere);
        foreach (KeyValuePair<string, (Religion religion, float people)> pair in best)
        {
            // 现任正教还有人奉着、没被超过两成五就留任，免得教宗年年换
            if (Orthodox.TryGetValue(pair.Key, out long incumbentId) && incumbentId != pair.Value.religion.data.id)
            {
                Religion incumbent = World.world.religions.get(incumbentId);
                if (Alive(incumbent) && believers.TryGetValue(incumbent, out float held) &&
                    pair.Value.people <= held * OrthodoxSwitchMargin) continue;
            }
            Orthodox[pair.Key] = pair.Value.religion.data.id;
        }
    }

    // 圣地毁了：迁到奉本教、人口最多的城(excluded 是正在毁掉的那座)
    public static void RelocateHolyCity(Religion religion, City excluded)
    {
        if (!Alive(religion) || religion.cities == null) return;
        City best = null;
        float most = -1f;
        foreach (City city in religion.cities.ToList())
        {
            if (city?.data == null || city.isRekt() || city == excluded) continue;
            float people = city.CountLivingPopulation();
            if (people <= most) continue;
            most = people;
            best = city;
        }
        if (best != null) religion.SetCity(best);
    }

    #region 并入

    // 每年一次(挂在 ReligiousTension 的年度扫描上，有无小人都结算)
    public static void YearlyPass()
    {
        if (World.world?.cities == null || ModClass.IS_CLEAR) return;
        Refresh(true);
        var cities = new Dictionary<Religion, int>();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || !city.hasReligion()) continue;
            cities.TryGetValue(city.religion, out int count);
            cities[city.religion] = count + 1;
        }
        var candidates = new HashSet<Religion>(cities.Keys);
        foreach (Kingdom kingdom in World.world.kingdoms.list)
            if (kingdom?.data != null && !kingdom.isRekt() && kingdom.hasReligion()) candidates.Add(kingdom.religion);
        foreach (Religion religion in candidates.ToList())
        {
            if (!IsLocalChurch(religion)) continue;
            cities.TryGetValue(religion, out int count);
            City holy = PapacySystem.HolyCity(religion);
            bool holyLost = holy == null || holy.religion != religion;
            if (!holyLost && count > MergeMaxCities) continue;
            Absorb(religion, OrthodoxOf(SphereOf(religion)));
        }
    }

    // 地方教会整体改奉正教：奉它的城(连城中实体)、以它为国教的国与君主、各处还信它的人
    private static void Absorb(Religion local, Religion orthodox)
    {
        if (!Alive(local) || !Alive(orthodox) || local == orthodox) return;
        Kingdom witness = null;
        foreach (City city in World.world.cities.list.ToList())
        {
            if (city?.data == null || city.isRekt() || city.religion != local) continue;
            BackgroundReligionSystem.Convert(city, orthodox, local);
            witness ??= city.kingdom;
        }
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.religion != local) continue;
            kingdom.setReligion(orthodox);
            witness ??= kingdom;
        }
        foreach (Actor actor in World.world.units.ToList())
            if (actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.religion == local) actor.setReligion(orthodox);
        string text = string.Format(LM.Get("religion_merged_log"), local.name, orthodox.name);
        TranslateHelper.LogEventMessage(text, witness);
        ReligionChronicle.Record(orthodox, local, ReligionChronicle.KindConversion, text);
        ReligionExtension.ReligionExtraData data = orthodox.GetOrCreate();
        data.Absorbed ??= new List<ReligionExtension.ReligionEventData>();
        data.Absorbed.Add(new ReligionExtension.ReligionEventData
            { at = World.world.getCurWorldTime(), kind = ReligionChronicle.KindConversion, text = local.name });
        LogService.LogInfo($"[EmpireCraft][宗教] {local.name} 并入本天下正教 {orthodox.name}");
    }

    #endregion
}

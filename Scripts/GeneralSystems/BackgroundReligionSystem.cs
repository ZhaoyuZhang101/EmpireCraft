using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 无小人模式下的宗教(用户 2026-10-20 报"西方文化不创造宗教了，或者说宗教影响力没算上虚拟人口")：
//   原版宗教全靠实体小人——创教谋划要创教者 5 级(君主每年只长 20 经验、城主 10，要在位二三十年)、
//   本文化的实体满 50 个；传教靠小人交谈、读书、生育，城的宗教跟城主、国的宗教跟君主。
//   纯净地图全图只剩几十个实体(连船在内)，文化凑不满 50，创教几乎不发生；偶尔创成了也只有创教者一人信，
//   城不改信，创教者一死宗教就被遗忘(存档里"创立 5、遗忘 3"，17 城只有 3 城有宗教)。
// 这里改按背景人口算(城的宗教 = 城中背景人口所信，与 NoCommonersPatch 的信众统计同一口径)，
// 每年结算一次(挂在 ReligiousTension 的年度扫描上)，只在无小人模式下生效：
//   · 创教：各文明的本土大教按科技时代在本天下最大的城立(见 NativeFaithSystem，有无小人都结算)。以前这里每城每年
//     掷 0.8% 随机创教、还要本城无教，扩散远比创教快，第一个立起来的宗教铺满几个文明，被铺满的再也立不起本土宗教
//     (用户 2026-10-27 截图：全图只剩基督教、蒙古萨满教)。原版创教谋划在无小人模式下停用(见 PlotAssetPatch)；
//   · 传教：每城受国教、邻城的宗教势能(√人口；国教按本城人口算，邻城打 NeighbourWeight 折，圣地再 ×HolyCityFactor)；
//     跨了天下(城的文化天下 ≠ 宗教的天下)的势能打折：普世宗教 ×0.25、民族宗教 ×0.05(ReligionSphere.CrossFactor)，
//     本天下已有本土大教时外来宗教改信、改宗的几率再减半；
//     本天下的无教之城与奉地方教会的城，正教势能 ×OrthodoxMissionFactor，无教之城另有一份正教势能(不必挨着)；
//     势能都乘宗教的传教力、本城惯性乘惰性(见 ReligionFaith)，国教势能按宗教政策放大(见 StateFactor)：
//       无教之城每年以 AdoptChance × 外来势能 / (外来势能 + FolkInertia × 本城势能) 的几率改信，按势能挑一支；
//       有教之城被别教压过时，每年以 ConvertChance × 压过的幅度 改宗；圣地、奉国教的都城不改；
//     改信时城主等本城实体一并改信(原版城主会把城的宗教改回自己的)，没有国教的国家都城改信即立为国教；
//   · 宗教点数(教皇国发动圣战用，原版每有一个小人改信本教 +1，见 ActorPatch.UpdateReligion)：
//     按信众户数每年加 PointsPerHousehold，加上本年改信之城的户数。
public static class BackgroundReligionSystem
{
    // 传教：无教之城改信几率的上限、本城的惯性；有教之城改宗几率的上限
    public const float AdoptChance = 0.15f;
    public const float FolkInertia = 0.5f;
    public const float ConvertChance = 0.04f;
    // 势能：国教按本城人口算一份；邻城打折；圣地加成
    public const float StateWeight = 1f;
    public const float NeighbourWeight = 0.5f;
    public const float HolyCityFactor = 1.5f;
    // 宗教点数：每户信众每年
    public const float PointsPerHousehold = 0.05f;
    // 本天下已有本土大教时，外来宗教改信、改宗的几率倍数
    public const float ForeignChanceFactor = 0.5f;

    public static bool Active => CityPopulationSystem.AbstractPopulationEnabled;

    // 原版创教谋划(new_religion)在无小人模式下停用，改由这里按人口创教
    public static bool ReplacesVanillaPlot(string plotId) => plotId == "new_religion" && Active;

    private static bool Valid(City city) => city?.data != null && !city.isRekt() && city.kingdom?.data != null &&
                                           !city.kingdom.wild && !AncientWarfareCompatibility.OwnsObject(city);

    private static bool Alive(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive();

    #region 年度结算

    public static void YearlyPass()
    {
        if (!Active || World.world?.cities == null || ModClass.IS_CLEAR) return;
        var cities = new List<City>();
        foreach (City city in World.world.cities.list)
            if (Valid(city)) cities.Add(city);
        if (cities.Count == 0) return;

        SyncRulers();
        // 势能按年初的宗教分布算：本年改信的城不当年就往下传
        var faith = new Dictionary<City, Religion>();
        var people = new Dictionary<City, float>();
        var spheres = new Dictionary<City, string>();
        foreach (City city in cities)
        {
            faith[city] = city.hasReligion() ? city.religion : null;
            people[city] = Mathf.Max(1, city.CountLivingPopulation());
            spheres[city] = ReligionSphere.SphereOfCity(city);
        }

        int adopted = 0, converted = 0, crossed = 0;
        // 本年改信之城的户数(宗教点数)
        var gained = new Dictionary<Religion, float>();
        foreach (City city in cities)
        {
            Religion current = faith[city];
            string sphere = spheres[city];
            Dictionary<Religion, float> pressure = Pressure(city, faith, people, sphere);
            if (pressure.Count == 0) continue;
            float own = Mathf.Sqrt(people[city]);
            Religion target;
            if (current == null)
            {
                float outside = pressure.Values.Sum();
                target = Pick(pressure);
                float chance = AdoptChance * outside / (outside + FolkInertia * own);
                if (ReligionSphere.IsForeignToNativeLand(target, sphere)) chance *= ForeignChanceFactor;
                if (UnityEngine.Random.value >= chance) continue;
                adopted++;
            }
            else
            {
                if (Anchored(city, current)) continue;
                pressure.TryGetValue(current, out float support);
                // 本城惯性乘宗教的惰性：多神、萨满易改，犹太教最难(见 ReligionFaith)
                float hold = own * ReligionFaith.Inertia(current) + support;
                KeyValuePair<Religion, float> rival = pressure.Where(pair => pair.Key != current)
                    .OrderByDescending(pair => pair.Value).FirstOrDefault();
                if (rival.Key == null || rival.Value <= hold) continue;
                float chance = ConvertChance * (rival.Value - hold) / (rival.Value + hold);
                if (ReligionSphere.IsForeignToNativeLand(rival.Key, sphere)) chance *= ForeignChanceFactor;
                if (UnityEngine.Random.value >= chance) continue;
                target = rival.Key;
                converted++;
            }
            if (target == null) continue;
            if (ReligionSphere.CrossFactor(target, sphere) < 1f) crossed++;
            Convert(city, target, current);
            gained.TryGetValue(target, out float households);
            gained[target] = households + CityPopulationSystem.FiscalHouseholds(city);
        }

        AccruePoints(cities, gained);
        LogDiagnostics(cities, people, adopted, converted, crossed);
    }

    // 国教势能按宗教政策放大：不宽容的国家改化异教城最力，收人头税的次之，宽容的最慢(以前国教势能恰好等于本城惯性，
    // 没有同教邻城时异教城永远改不过来)；君主皈依后 ConversionPushYears 年内再加倍(见 ReligionConversionSystem)
    private static float StateFactor(Kingdom kingdom)
    {
        float factor = ReligiousTension.PolicyOf(kingdom) switch
        {
            ReligiousTension.Policy.Intolerant => 1.6f,
            ReligiousTension.Policy.PollTax => 1.4f,
            _ => 1.1f
        };
        return ReligionConversionSystem.RecentlyConverted(kingdom) ? factor * 2f : factor;
    }

    // 势能：国教按本城人口算，邻城按各自人口打折，圣地加成；都乘宗教的传教力(见 ReligionFaith)，跨了天下再打折(ReligionSphere.CrossFactor)
    private static Dictionary<Religion, float> Pressure(City city, Dictionary<City, Religion> faith, Dictionary<City, float> people,
        string sphere)
    {
        var pressure = new Dictionary<Religion, float>();
        Religion state = city.kingdom.hasReligion() ? city.kingdom.religion : null;
        if (state != null)
            Add(pressure, state, StateWeight * StateFactor(city.kingdom) * ReligionFaith.Mission(state) *
                                 ReligionSphere.CrossFactor(state, sphere) * Mathf.Sqrt(people[city]));
        foreach (City neighbour in city.neighbours_cities)
        {
            if (neighbour == null || !faith.TryGetValue(neighbour, out Religion religion) || religion == null) continue;
            float weight = NeighbourWeight * ReligionFaith.Mission(religion) * ReligionSphere.CrossFactor(religion, sphere) *
                           Mathf.Sqrt(people[neighbour]);
            if (religion.GetCity() == neighbour) weight *= HolyCityFactor;
            Add(pressure, religion, weight);
        }
        // 本天下的正教：无教之城、奉地方教会的城势能加倍；无教之城不挨着也有一份(分出去的教会、外天下的宗教不加)
        faith.TryGetValue(city, out Religion current);
        Religion orthodox = ReligionSphere.OrthodoxOf(sphere);
        if (orthodox != null && orthodox != current && (current == null || ReligionSphere.IsLocalChurch(current)))
        {
            if (pressure.TryGetValue(orthodox, out float mission))
                pressure[orthodox] = mission * ReligionSphere.OrthodoxMissionFactor;
            if (current == null) Add(pressure, orthodox, ReligionSphere.SphereMissionWeight * Mathf.Sqrt(people[city]));
        }
        return pressure;
    }

    private static void Add(Dictionary<Religion, float> pressure, Religion religion, float weight)
    {
        pressure.TryGetValue(religion, out float current);
        pressure[religion] = current + weight;
    }

    private static Religion Pick(Dictionary<Religion, float> pressure)
    {
        float roll = UnityEngine.Random.value * pressure.Values.Sum();
        foreach (KeyValuePair<Religion, float> pair in pressure)
        {
            roll -= pair.Value;
            if (roll <= 0f) return pair.Key;
        }
        return pressure.Keys.LastOrDefault();
    }

    // 圣地守本教；奉国教的都城随朝廷，不被邻城带走
    private static bool Anchored(City city, Religion current) =>
        current.GetCity() == city || city.isCapitalCity() && city.kingdom.religion == current;

    // 城改信：城主等本城实体一并改信(君主除外，国教由他定)；没有国教的国家都城改信即立为国教
    public static void Convert(City city, Religion religion, Religion old)
    {
        if (city?.data == null || religion?.data == null) return;
        city.setReligion(religion);
        if (city.units != null)
            foreach (Actor actor in city.units.ToList())
            {
                if (!Alive(actor) || actor.isKing() || CityPopulationSystem.IsVehicle(actor)) continue;
                if (actor == city.leader || !actor.hasReligion() || actor.religion == old) actor.setReligion(religion);
            }
        Kingdom kingdom = city.kingdom;
        if (kingdom?.data == null || kingdom.capital != city || kingdom.hasReligion()) return;
        kingdom.setReligion(religion);
        if (Alive(kingdom.king) && !kingdom.king.hasReligion()) kingdom.king.setReligion(religion);
    }

    // 君主与国教：无教的君主随都城之教；没有国教的国家，君主或都城有教即立为国教
    // (原版国教跟君主走，可从人口里补上来的君主以前不带宗教)
    private static void SyncRulers()
    {
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || AncientWarfareCompatibility.Owns(kingdom)) continue;
            City capital = kingdom.capital;
            Religion local = capital?.data != null && !capital.isRekt() && capital.hasReligion() ? capital.religion : null;
            Actor king = kingdom.king;
            if (Alive(king) && !king.hasReligion() && local != null) king.setReligion(local);
            if (kingdom.hasReligion()) continue;
            Religion faith = Alive(king) && king.hasReligion() ? king.religion : local;
            if (faith != null) kingdom.setReligion(faith);
        }
    }

    #endregion

    #region 宗教点数

    // 教皇国(封建、神权至上、圣地即都城)的宗教点数：信众户数 × PointsPerHousehold + 本年改信之城的户数
    private static void AccruePoints(List<City> cities, Dictionary<Religion, float> gained)
    {
        Dictionary<Religion, float> households = null;
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || !kingdom.hasReligion() || kingdom.capital == null) continue;
            Religion religion = kingdom.religion;
            if (religion.GetCity() != kingdom.capital) continue;
            Regime regime = kingdom.GetRegime();
            if (regime == null || regime.type != RegimeType.Feudalism || regime.GetReligionLevel() != ReligionLevel.High) continue;
            if (households == null)
            {
                households = new Dictionary<Religion, float>();
                foreach (City city in cities)
                    if (city.hasReligion()) Add(households, city.religion, CityPopulationSystem.FiscalHouseholds(city));
            }
            households.TryGetValue(religion, out float believers);
            gained.TryGetValue(religion, out float converts);
            regime.religion_point += Mathf.RoundToInt(believers * PointsPerHousehold + converts);
        }
    }

    #endregion

    private static void LogDiagnostics(List<City> cities, Dictionary<City, float> people, int adopted, int converted, int crossed)
    {
        var believers = new Dictionary<Religion, float>();
        var counts = new Dictionary<Religion, int>();
        float total = 0f, faithful = 0f;
        foreach (City city in cities)
        {
            total += people[city];
            if (!city.hasReligion()) continue;
            faithful += people[city];
            Add(believers, city.religion, people[city]);
            counts.TryGetValue(city.religion, out int count);
            counts[city.religion] = count + 1;
        }
        KeyValuePair<Religion, float> top = believers.OrderByDescending(pair => pair.Value).FirstOrDefault();
        string largest = top.Key == null ? "无" : $"{top.Key.name} {counts[top.Key]} 城 {top.Value:0} 人";
        LogService.LogInfo($"[EmpireCraft][宗教诊断] 各城所奉宗教 {believers.Count} 种；" +
                           $"有教之城 {counts.Values.Sum()}/{cities.Count}，信众 {faithful:0}/{total:0} 人；" +
                           $"本年无教之城改信 {adopted}、改宗 {converted}(其中跨天下 {crossed})；最大：{largest}");
    }
}

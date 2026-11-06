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
// 2026-11-06(用户"还有本土宗教……不改的话就会出现在古典时代全世界都信基督"，问答全按推荐；"传播要考虑距离、地形的隔离程度""和通信技术、科技挂钩")：
//   · 本土加成：城奉的是本天下的宗教，本城惯性再 ×(1 + 0.5 + 祖灵护土等)，有本地圣徒的天下另加(ReligionTenets.HomeFactor)；
//   · 本天下正教收复：被外来宗教占着的城，本天下正教的势能也 ×OrthodoxMissionFactor(以前只给无教之城、地方教会，
//     与 Docs/plan-religion-spread.md 第四条"本土大教的正教势能加倍照算、长期拉锯"不符)；
//   · 跨天下改信、改宗的几率一律 ×CrossChanceFactor(0.5)，本天下已有本土大教再 ×ForeignChanceFactor(0.5)；
//   · 通达(ConnectivitySystem，消息口径，按传教一方的本事)：邻城势能 × min(1, 强度/0.6)，跨天下的民族宗教强度再按衰减长度 ×0.6 折；
//     圣地加成只给强度 ≥0.3 的邻城；通达层算好后，只隔无人区的邻城也算邻城(以前隔着就断)；没算好时照旧(原版邻城、不打折)；
//   · 国教势能 ×(0.4 + 0.6 × 政令控制力)：山后、隔无人区的边地不随朝廷改宗(AdministrativeControlSystem)；
//   · 以融合御同化：城奉的宗教从某教一系融过教义的，那一系对这城的势能 ×0.85ⁿ(至多 ×0.55，见 ReligionTenets.Pull)。
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
    // 跨天下改信、改宗(不管本天下有没有本土大教)的几率倍数
    public const float CrossChanceFactor = 0.5f;
    // 国教势能按政令控制力：0.4 + 0.6 × 控制力
    public const float ControlFloor = 0.4f;
    // 通达：强度到这个数算"紧密"不打折；圣地加成要的强度；跨天下的民族宗教衰减长度的倍数
    public const float HolyReachMin = 0.3f, EthnicLengthFactor = 0.6f;

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
                float chance = AdoptChance * outside / (outside + FolkInertia * own) * ForeignChance(target, sphere);
                if (UnityEngine.Random.value >= chance) continue;
                adopted++;
            }
            else
            {
                if (Anchored(city, current)) continue;
                pressure.TryGetValue(current, out float support);
                // 本城惯性乘宗教的惰性：多神、萨满易改，犹太教最难(见 ReligionFaith)；本天下的宗教再乘本土加成(见 ReligionTenets)
                float hold = own * ReligionFaith.Inertia(current) * ReligionTenets.HomeFactor(current, sphere) + support;
                KeyValuePair<Religion, float> rival = pressure.Where(pair => pair.Key != current)
                    .OrderByDescending(pair => pair.Value).FirstOrDefault();
                if (rival.Key == null || rival.Value <= hold) continue;
                float chance = ConvertChance * (rival.Value - hold) / (rival.Value + hold) * ForeignChance(rival.Key, sphere);
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
        ReligionDiagnostics.Add(ReligionDiagnostics.Adopted, adopted);
        ReligionDiagnostics.Add(ReligionDiagnostics.Converted, converted);
        LogDiagnostics(cities, people, adopted, converted, crossed);
    }

    // 跨天下改信、改宗的几率倍数：跨了天下 ×0.5，本天下已有本土大教再 ×0.5
    public static float ForeignChance(Religion religion, string citySphere)
    {
        if (religion?.data == null || string.IsNullOrEmpty(citySphere) || ReligionSphere.SphereOf(religion) == citySphere) return 1f;
        return ReligionSphere.IsForeignToNativeLand(religion, citySphere) ? CrossChanceFactor * ForeignChanceFactor : CrossChanceFactor;
    }

    // 传教够得着的邻城：原版邻城；通达层算好后加上只隔无人区的邻城(只隔着无人区以前就断了，现在按通达打折)
    internal static IEnumerable<City> Contacts(City city)
    {
        if (city?.neighbours_cities != null)
            foreach (City neighbour in city.neighbours_cities)
                if (neighbour != null) yield return neighbour;
        if (!ConnectivitySystem.Ready) yield break;
        foreach (City other in WildernessNeighbours.Of(city))
            if (other != null && (city.neighbours_cities == null || !city.neighbours_cities.Contains(other))) yield return other;
    }

    // 从 from 城传到 to 城的通达(0~1，按传教一方的本事、消息口径)：强度到"紧密"(0.6)以上不打折；
    // 跨天下的民族宗教走不远(衰减长度 ×0.6，即强度 ^(1/0.6))；通达层没算好时为 1(照旧)
    internal static float Reach(City from, City to, Religion religion, string targetSphere)
    {
        float strength = ConnectivitySystem.NeighbourStrength(from, to, ConnectivityMode.Ideas);
        if (strength < 0f) return 1f;
        if (strength <= 0f) return 0f;
        if (religion?.data != null && !ReligionFaith.IsUniversal(religion) && !string.IsNullOrEmpty(targetSphere) &&
            ReligionSphere.SphereOf(religion) != targetSphere)
            strength = Mathf.Pow(strength, 1f / EthnicLengthFactor);
        return Mathf.Clamp01(strength / Mathf.Max(0.05f, ConnectivitySystem.Config.close_strength));
    }

    // 圣地加成只给通达到 HolyReachMin 的邻城(通达层没算好、这两城没算进去时照旧给)
    private static bool HolyReach(City holy, City city)
    {
        float strength = ConnectivitySystem.NeighbourStrength(holy, city, ConnectivityMode.Ideas);
        return strength < 0f || strength >= HolyReachMin;
    }

    // 国教势能按政令控制力打折(还没算过控制力的城不打折)
    public static float ControlFactor(City city) =>
        AdministrativeControlSystem.Known(city) ? ControlFloor + (1f - ControlFloor) * Mathf.Clamp01(AdministrativeControlSystem.Get(city)) : 1f;

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
        // 哈里发国的国教吸引力更高，只管境内(见 CaliphateFaith)
        factor *= CaliphateFaith.StateFactor(kingdom);
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
                                 ReligionSphere.CrossFactor(state, sphere) * Mathf.Sqrt(people[city]) * ControlFactor(city));
        foreach (City neighbour in Contacts(city))
        {
            if (!faith.TryGetValue(neighbour, out Religion religion) || religion == null) continue;
            float reach = Reach(neighbour, city, religion, sphere);
            if (reach <= 0f) continue;
            float weight = NeighbourWeight * ReligionFaith.Mission(religion) * ReligionSphere.CrossFactor(religion, sphere) *
                           Mathf.Sqrt(people[neighbour]) * reach;
            if (religion.GetCity() == neighbour && HolyReach(neighbour, city))
                weight *= HolyCityFactor * ReligionTenets.HolyCityFactor(religion);
            Add(pressure, religion, weight);
        }
        // 本天下的正教：无教之城、奉地方教会的城、被外来宗教占着的城势能加倍；无教之城不挨着也有一份(分出去的教会不加)
        faith.TryGetValue(city, out Religion current);
        Religion orthodox = ReligionSphere.OrthodoxOf(sphere);
        if (orthodox != null && orthodox != current &&
            (current == null || ReligionSphere.IsLocalChurch(current) || ReligionSphere.SphereOf(current) != sphere))
        {
            if (pressure.TryGetValue(orthodox, out float mission))
                pressure[orthodox] = mission * ReligionSphere.OrthodoxMissionFactor;
            if (current == null) Add(pressure, orthodox, ReligionSphere.SphereMissionWeight * Mathf.Sqrt(people[city]));
        }
        // 以融合御同化：本城之教从哪一系融过教义，那一系的势能打折(见 ReligionTenets.Pull)
        if (current != null)
            foreach (Religion rival in pressure.Keys.ToList())
                if (rival != current) pressure[rival] *= ReligionTenets.Pull(current, rival);
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

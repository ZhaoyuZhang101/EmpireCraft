using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 从当前世界导入剧本(用户 10-30 "现在的剧本编辑器可以导入已经设置好行政区的存档嘛"，答复两条路后选"都做")：玩家先在游戏里读好存档，
// 世界地图窗口「自定剧本」卡点「从当前世界导入」，把眼前这个世界写成一个自定剧本文件夹(写法同别的自定剧本，见 Docs/plan-scenario-editor.md)，
// 选中它直接进剧本编辑器接着改。全程只读世界、不改世界；写出来的剧本开局照 WorldMapScenario 的规矩摆城建国：
//   · 地图：世界地图剧本开的局(WorldMapWilderness 存着地图范围与大小)沿用地球地图与那档范围(frame)，城的经纬度按那个范围从格子换算；
//     别的世界(随机图、导入地图开的局)按格子另存一张 map.png(一格一像素，颜色取 WorldMapImporter 的对照色，读回来就是原来的地形；
//     宽高补到区块的整数倍、至少四个区块，补的是深海；图太大的照导入的上限隔几格取一格)，剧本写 map {mode: color}，城的经纬度
//     按左下角为 0、一格 0.1° 记——最小一档大小开局，城就落回原来那一格；
//   · 城：每座文明城一条(编号 "c<城编号>")，名字三种写法同名，大小档按人口(都城与人口前三成记 1 档，其余 2 档；不用 3 档——
//     小图不放 3 档的城，同样大小开局就少了城)，地区记它的王国法理；兼属的王国法理记 share；
//   · 王国法理 = 剧本的地区(编号 "t<法理编号>")：省份名、法理名、文化(法理文化，取不到取辖下城的主流文化)、法理首都；
//     没有法理的城按所在国家并成一块(编号 "u<国家编号>"，名字取国名)——剧本里的城都得有地区，开局时这块另立一个王国法理；
//   · 帝国法理 = cores(编号 "e<核心编号>")：收的王国法理、首都、文化、兼收的王国法理(share)；军阀时期另立政府的临时核心不导；
//     天下：并了几个帝国法理的联合天下、改过名的、被别的帝国法理兼属的，写 tx(编号 "x<主核心编号>")，改过名的另写进 tianxia；
//     自成一个天下又没改名的不写(开局照常自动排)；
//   · 国家 = polities(编号 "k<国家编号>")：每个有城的文明国家一条，帝国的行政区(律令制的道、郡、行省、总督区……，见
//     KingdomExtension.IsAdministrativeKingdomType)不单列，记成帝国那一条的 divisions(名、城，行政区首府排第一，开局 Divide 取它的法理)，
//     它们的城照样列在帝国的 cities 里(开局先归核心王国，进世界后再按 divisions 划出去)；殖民地(宗主是独立的国家)同理记成宗主的 colonies；
//     帝国写 emp、所挂帝国法理 core、美号 grand、年号与是年号第几年；帝国里不是行政区的成员国(封国、附庸)写 of 指帝国；
//     君主(姓、名、年龄、性别)与在世子女、储君写 ruler、kin；
//   · 关系：在打的仗(主攻方 → 主守方，带战争目标)、同盟、和亲、通好、保护、朝贡、附庸、关税同盟、还没淡完的旧谊宿怨，
//     都是 DiplomacyActions.ApplyScenario 认的种类，开局走外交界面同一套判定；行政区、殖民地参与的仗记到帝国、宗主头上；
//   · 开局年份：世界地图剧本开的局 = 剧本年份 + 过了几年(公元前后之间跳过 0 年)，别的世界 = 游戏里的年份；
//     预设剧本开的局另记 base = 那个预设的编号；
//   · 科技与制度：各文化已研究的科技记成 tech(已研究的最高一档) + techSkip(这档以内没研究的)，已施行的制度节点记 inst；
//   · 无人区(与剧本编辑器约定的写法)：永久中立区刷成 paint 的 "n"，挂了法理的刷成 "r:<地区编号>"，格子按无人区每一格的经纬度取
//     ScenarioCells.LandStep 一格(去重)；没挂法理的不写。
// 不导的：长城关隘(格子上的墙描不回折线)、剧情事件(开局后的事)、名将名臣、王朝积弊、宗教；地球地图上玩家改过的地形
// (剧本沿用原样的地球地图)；城里各文化的人口比例(剧本里一个地区一种文化)。
// json 用 JObject 一条条写(不经 WorldMapEra，免得跟着那边加的字段走)，空的不写；写坏了把建了一半的文件夹删掉
public static class ScenarioWorldExport
{
    // 导入地图一边至多几像素(同 WorldMapImporter 的上限)；宽高补到区块(64 格)的整数倍，至少四个区块(导入地图最小四个区块)
    private const int MaxSide = 2048, ChunkTiles = 64, MinSide = 256;
    // 城的大小档：人口前几成记 1 档
    private const float BigShare = 0.3f;
    // 旧谊宿怨淡到这么点就不记了；宗室至多记几个子女
    private const int MinOpinion = 5, MaxKin = 6;

    // 格子 → 经纬度：左下角经纬度与一格几度(取格子正中)
    private sealed class Projection
    {
        public float west, bottom, degrees;

        public void LonLat(int x, int y, out float lon, out float lat)
        {
            lon = west + (x + 0.5f) * degrees;
            lat = bottom + (y + 0.5f) * degrees;
        }
    }

    // 世界里一个导出的国家：本身(polity 的那个国家)、它名下的城(连同行政区、殖民地的)、行政区与殖民地各自的城
    private sealed class Realm
    {
        public Kingdom kingdom;
        public string id;
        public readonly List<City> cities = new();
        public readonly Dictionary<Kingdom, List<City>> divisions = new();
        public readonly Dictionary<Kingdom, List<City>> colonies = new();
    }

    #region 能不能导

    // 读着的世界里有文明城才能导
    public static bool CanExport
    {
        get
        {
            try
            {
                return World.world?.cities?.list != null && World.world.cities.list.Any(Exportable);
            }
            catch
            {
                return false;
            }
        }
    }

    private static bool Exportable(City city) =>
        city?.data != null && !city.isRekt() && city.kingdom?.data != null && !city.kingdom.isRekt() && city.kingdom.isCiv() &&
        city.getTile() != null;

    // 剧本名：世界名，没有就"导入_年月日"；都加"·导入"，免得与预设剧本同名(世界地图开的局，世界名就是剧本名)
    public static string DefaultName()
    {
        string world = Clean(World.world?.map_stats?.name);
        if (string.IsNullOrEmpty(world)) world = string.Format(LM.Get("scenario_export_default_name"), DateTime.Now.ToString("yyyyMMdd"));
        return world + LM.Get("scenario_export_suffix");
    }

    #endregion

    // 写一个自定剧本文件夹；folder 为新剧本的文件夹名(剧本编号 custom/<folder>)，失败时 error 为文本键
    public static bool Export(string name, out string folder, out string error)
    {
        folder = null;
        error = null;
        if (!CanExport)
        {
            error = "scenario_export_no_world";
            return false;
        }
        // 只要时代表(认 base)与地表类型表(对照表)：不看 EnsureLoaded 的结果——上一局是没城的导入地图剧本时城表是空的，它也返回假
        WorldMapData.EnsureLoaded();
        EarthTerrain.EnsureLoaded();
        if (string.IsNullOrWhiteSpace(name)) name = DefaultName();
        string dir = null;
        try
        {
            Directory.CreateDirectory(WorldMapData.CustomFolder);
            string stem = FolderName(name);
            string chosen = stem;
            for (int i = 2; Directory.Exists(Path.Combine(WorldMapData.CustomFolder, chosen)); i++) chosen = stem + "_" + i;
            dir = Path.Combine(WorldMapData.CustomFolder, chosen);
            Directory.CreateDirectory(dir);
            JObject root = Build(chosen, Clean(name), dir, out int cities, out int realms, out int divisions);
            File.WriteAllText(Path.Combine(dir, WorldMapData.CustomFile), root.ToString(Formatting.Indented));
            folder = chosen;
            LogService.LogInfo($"[EmpireCraft][剧本导入] 当前世界存成自定剧本 {dir}：{cities} 城、{realms} 国、{divisions} 个行政区");
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][剧本导入] 导入失败: {exception}");
            error = "scenario_export_failed";
            try
            {
                if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch
            {
                // 删不掉就留着，玩家可以自己删
            }
            return false;
        }
    }

    private static JObject Build(string id, string name, string dir, out int cityCount, out int realmCount, out int divisionCount)
    {
        // 世界地图剧本开的局：存档里的地图范围与大小(拿它会顺手把开局记的史实归属落成法理编号，存档时本来也要落)
        WorldMapWildSave wild = WorldMapWilderness.Save();
        WorldMapFrame frame = wild == null ? null : EarthTerrain.Frames.FirstOrDefault(item => item.id == wild.frame);
        bool earth = frame != null && wild.w > 0 && wild.h > 0 && MapBox.width == wild.w * ChunkTiles && MapBox.height == wild.h * ChunkTiles;
        Projection projection;
        if (earth)
        {
            frame.Bounds(new Vector2Int(wild.w, wild.h), out float bottom, out float degrees);
            projection = new Projection { west = frame.west, bottom = bottom, degrees = degrees };
        }
        else projection = WriteMap(dir);

        List<City> cities = World.world.cities.list.Where(Exportable).OrderBy(city => city.id).ToList();
        var kingdoms = new HashSet<Kingdom>(cities.Select(city => city.kingdom));

        #region 国家：行政区并进帝国、殖民地并进宗主

        bool Province(Kingdom kingdom, out Empire empire)
        {
            empire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
            return empire?.data != null && !empire.isRekt() && empire.CoreKingdom != null && empire.CoreKingdom != kingdom &&
                   kingdoms.Contains(empire.CoreKingdom) && kingdom.IsAdministrativeKingdomType();
        }

        var headOf = new Dictionary<Kingdom, Kingdom>();
        foreach (Kingdom kingdom in kingdoms)
        {
            if (Province(kingdom, out Empire empire))
            {
                headOf[kingdom] = empire.CoreKingdom;
                continue;
            }
            Kingdom metropole = ColonySystem.MetropoleOf(kingdom);
            headOf[kingdom] = metropole != null && metropole != kingdom && kingdoms.Contains(metropole) && !kingdom.IsEmpire() &&
                              !Province(metropole, out _) && ColonySystem.MetropoleOf(metropole) == null
                ? metropole
                : kingdom;
        }
        var realms = new Dictionary<Kingdom, Realm>();
        foreach (City city in cities)
        {
            Kingdom head = headOf[city.kingdom];
            if (!realms.TryGetValue(head, out Realm realm)) realms[head] = realm = new Realm { kingdom = head, id = "k" + head.id };
            realm.cities.Add(city);
            if (city.kingdom == head) continue;
            Dictionary<Kingdom, List<City>> part = Province(city.kingdom, out _) ? realm.divisions : realm.colonies;
            if (!part.TryGetValue(city.kingdom, out List<City> list)) part[city.kingdom] = list = new List<City>();
            list.Add(city);
        }
        // 关系里行政区、殖民地记到帝国、宗主头上
        string RealmId(Kingdom kingdom) =>
            kingdom != null && headOf.TryGetValue(kingdom, out Kingdom head) && realms.TryGetValue(head, out Realm realm) ? realm.id : null;
        // 只认本身就是一条 polity 的国家(朝贡、附庸、同盟……只有独立的国家谈得上)
        string OwnId(Kingdom kingdom) => kingdom != null && realms.TryGetValue(kingdom, out Realm realm) ? realm.id : null;

        #endregion

        #region 城与王国法理

        var people = cities.ToDictionary(city => city, People);
        List<float> sorted = people.Values.OrderByDescending(value => value).ToList();
        float big = sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.FloorToInt(sorted.Count * BigShare), 0, sorted.Count - 1)];
        var regionOf = new Dictionary<KingdomTitle, string>();
        var untitled = new Dictionary<Kingdom, string>();
        var regionCities = new Dictionary<string, List<City>>();
        var cityRegion = new Dictionary<City, string>();
        foreach (City city in cities)
        {
            KingdomTitle title = city.GetTitle();
            string region;
            if (title?.data != null && !title.isRekt())
            {
                if (!regionOf.TryGetValue(title, out region)) regionOf[title] = region = "t" + title.id;
            }
            else if (!untitled.TryGetValue(city.kingdom, out region)) untitled[city.kingdom] = region = "u" + city.kingdom.id;
            cityRegion[city] = region;
            if (!regionCities.TryGetValue(region, out List<City> members)) regionCities[region] = members = new List<City>();
            members.Add(city);
        }
        string CityId(City city) => city != null && cityRegion.ContainsKey(city) ? "c" + city.id : null;

        var cityArray = new JArray();
        foreach (City city in cities)
        {
            projection.LonLat(city.getTile().x, city.getTile().y, out float lon, out float lat);
            var entry = new JObject { ["id"] = CityId(city) };
            Names(entry, Clean(city.GetCityName()));
            entry["x"] = Math.Round(lon, 4);
            entry["y"] = Math.Round(lat, 4);
            entry["r"] = cityRegion[city];
            entry["k"] = city.kingdom.capital == city || people[city] > big ? 1 : 2;
            List<string> shares = Safe(() => DeJureShareSystem.SharedTitles(city), new List<KingdomTitle>())
                .Where(title => title != null && regionOf.ContainsKey(title)).Select(title => regionOf[title])
                .Where(region => region != cityRegion[city]).Distinct().ToList();
            if (shares.Count > 0) entry["share"] = new JArray(shares);
            cityArray.Add(entry);
        }

        // 开局要定科技、制度的文化(各地区与各国的)
        var cultures = new HashSet<string>();
        var titleOf = regionOf.ToDictionary(item => item.Value, item => item.Key);
        var ownerOf = untitled.ToDictionary(item => item.Value, item => item.Key);
        var regionArray = new JArray();
        foreach (KeyValuePair<string, List<City>> pair in regionCities)
        {
            List<City> members = pair.Value;
            KingdomTitle title = titleOf.TryGetValue(pair.Key, out KingdomTitle found) ? found : null;
            Kingdom owner = title == null ? ownerOf[pair.Key] : null;
            City seat = title != null ? title.title_capital : owner.capital;
            if (seat == null || !members.Contains(seat)) seat = members.OrderByDescending(city => people[city]).First();
            string culture = title != null ? Safe(() => CultureService.GetEffectiveTitleCulture(title), "") : "";
            if (!CultureService.IsValidCulture(culture))
                culture = members.Select(city => Safe(() => CultureService.GetMainCulture(city, false), ""))
                    .Where(CultureService.IsValidCulture).GroupBy(key => key).OrderByDescending(group => group.Count())
                    .Select(group => group.Key).FirstOrDefault() ?? CultureService.GetRealmCulture(seat.kingdom);
            string province = title != null ? Clean(title.data.province_name) : Clean(owner.GetKingdomName());
            if (string.IsNullOrEmpty(province)) province = Clean(seat.GetCityName());
            string titleName = title != null ? Clean(title.data.name) : Clean(owner.GetKingdomName());
            var entry = new JObject { ["id"] = pair.Key };
            Names(entry, province);
            if (CultureService.IsValidCulture(culture))
            {
                entry["c"] = culture;
                cultures.Add(culture);
            }
            if (!string.IsNullOrEmpty(titleName) && titleName != province) entry["t"] = titleName;
            entry["cap"] = CityId(seat);
            regionArray.Add(entry);
        }

        #endregion

        #region 帝国法理与天下

        var coreOf = new Dictionary<EmpireCore, string>();
        var coreRegions = new Dictionary<EmpireCore, List<string>>();
        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values.Where(core => core != null && core.warlord_parent_core_id < 0)
                     .OrderBy(core => core.id))
        {
            List<string> regions = EmpireCoreManager.GetTitles(core).Where(regionOf.ContainsKey).Select(title => regionOf[title])
                .Distinct().ToList();
            if (regions.Count == 0) continue;
            coreOf[core] = "e" + core.id;
            coreRegions[core] = regions;
        }
        // 要写 tx 的天下：并了几个帝国法理的、改过名的、被兼属的(兼属天下开局只认写了 tx 的那些)
        var sharedLeads = coreOf.Keys.ToDictionary(core => core,
            core => Safe(() => DeJureShareSystem.SharedLeads(core), new List<EmpireCore>()).Where(lead => lead != null).ToList());
        var namedLeads = new HashSet<EmpireCore>();
        foreach (EmpireCore core in coreOf.Keys)
        {
            EmpireCore lead = TianxiaSystem.Lead(core);
            if (lead != null && (TianxiaSystem.Members(lead).Count(coreOf.ContainsKey) > 1 || !string.IsNullOrWhiteSpace(lead.tianxia_name)))
                namedLeads.Add(lead);
        }
        foreach (EmpireCore lead in sharedLeads.Values.SelectMany(list => list)) namedLeads.Add(TianxiaSystem.Lead(lead) ?? lead);
        var tianxia = new JObject();
        var coreArray = new JArray();
        foreach (KeyValuePair<EmpireCore, string> pair in coreOf)
        {
            EmpireCore core = pair.Key;
            var entry = new JObject { ["id"] = pair.Value };
            Names(entry, Clean(EmpireCoreManager.GetDisplayName(core)));
            Put(entry, "c", CultureService.IsValidCulture(core.default_culture) ? core.default_culture : null);
            Put(entry, "cap", CityId(Safe(() => core.GetCoreCapital(), null)));
            entry["r"] = new JArray(coreRegions[core]);
            List<string> shares = Safe(() => DeJureShareSystem.SharedTitles(core), new List<KingdomTitle>())
                .Where(title => title != null && regionOf.ContainsKey(title)).Select(title => regionOf[title])
                .Except(coreRegions[core]).Distinct().ToList();
            if (shares.Count > 0) entry["share"] = new JArray(shares);
            List<string> also = sharedLeads[core].Select(lead => "x" + (TianxiaSystem.Lead(lead) ?? lead).id).Distinct().ToList();
            if (also.Count > 0) entry["tianxia"] = new JArray(also);
            EmpireCore own = TianxiaSystem.Lead(core);
            if (own != null && namedLeads.Contains(own))
            {
                entry["tx"] = "x" + own.id;
                if (!string.IsNullOrWhiteSpace(own.tianxia_name)) tianxia["x" + own.id] = Clean(own.tianxia_name);
            }
            coreArray.Add(entry);
        }

        #endregion

        #region 国家

        var realmArray = new JArray();
        divisionCount = 0;
        foreach (Realm realm in realms.Values.OrderByDescending(item => item.cities.Count).ThenBy(item => item.kingdom.id))
        {
            Kingdom kingdom = realm.kingdom;
            Empire empire = kingdom.IsEmpire() ? kingdom.GetEmpire() : null;
            string realmName = Clean(empire != null ? empire.GetEmpireName() : kingdom.GetKingdomName());
            if (string.IsNullOrEmpty(realmName)) realmName = Clean(kingdom.data.name);
            City capital = kingdom.capital != null && realm.cities.Contains(kingdom.capital) && kingdom.capital.kingdom == kingdom
                ? kingdom.capital
                : realm.cities.Where(city => city.kingdom == kingdom).OrderByDescending(city => people[city]).FirstOrDefault() ??
                  realm.cities.OrderByDescending(city => people[city]).First();
            var entry = new JObject { ["id"] = realm.id };
            Names(entry, realmName);
            string culture = CultureService.GetRealmCulture(kingdom);
            if (CultureService.IsValidCulture(culture))
            {
                entry["c"] = culture;
                cultures.Add(culture);
            }
            entry["cap"] = CityId(capital);
            entry["cities"] = new JArray(realm.cities.OrderByDescending(city => city == capital).ThenBy(city => city.id).Select(CityId));
            // 帝国里不是行政区的成员国(封国、附庸)：开局入那个帝国
            Empire lord = !kingdom.IsEmpire() && kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
            if (lord?.CoreKingdom != null && lord.CoreKingdom != kingdom && OwnId(lord.CoreKingdom) != null && lord.CoreKingdom.IsEmpire())
                entry["of"] = OwnId(lord.CoreKingdom);
            if (empire?.data != null)
            {
                entry["emp"] = true;
                EmpireCore core = Safe(() => EmpireCoreManager.Get(empire), null);
                if (core != null && coreOf.TryGetValue(core, out string coreId)) entry["core"] = coreId;
                if (empire.data.name_source == RealmNamingSystem.SourceGrandName) entry["grand"] = true;
                if (empire.data.has_year_name && !string.IsNullOrWhiteSpace(empire.data.year_name))
                {
                    entry["era_name"] = Clean(empire.data.year_name);
                    double since = empire.data.newEmperor_timestamp;
                    entry["era_year"] = since >= 0d ? Math.Max(1, Date.getYearsSince(since) + 1) : 1;
                }
            }
            Actor king = kingdom.king;
            JObject ruler = king != null && !king.isRekt() && king.isAlive() ? Person(king, null, false) : null;
            if (ruler != null)
            {
                entry["ruler"] = ruler;
                JArray kin = Safe(() => Kin(kingdom, king), new JArray());
                if (kin.Count > 0) entry["kin"] = kin;
            }
            if (realm.divisions.Count > 0)
            {
                var divisions = new JArray();
                foreach (KeyValuePair<Kingdom, List<City>> part in realm.divisions.OrderBy(item => item.Key.id))
                {
                    string partName = Clean(part.Key.GetKingdomName());
                    if (string.IsNullOrEmpty(partName)) partName = Clean(part.Key.data.name);
                    divisions.Add(new JObject
                    {
                        ["name"] = partName,
                        ["cities"] = new JArray(part.Value.OrderByDescending(city => city == part.Key.capital).ThenBy(city => city.id).Select(CityId))
                    });
                }
                entry["divisions"] = divisions;
                divisionCount += divisions.Count;
            }
            if (realm.colonies.Count > 0)
            {
                var colonies = new JArray();
                foreach (KeyValuePair<Kingdom, List<City>> part in realm.colonies.OrderBy(item => item.Key.id))
                {
                    var colony = new JObject
                    {
                        ["name"] = Clean(part.Key.GetKingdomName()),
                        ["cities"] = new JArray(part.Value.OrderByDescending(city => city == part.Key.capital).ThenBy(city => city.id).Select(CityId))
                    };
                    if (ColonySystem.Of(part.Key)?.kind == ColonyState.Company) colony["company"] = true;
                    colonies.Add(colony);
                }
                entry["colonies"] = colonies;
            }
            realmArray.Add(entry);
        }

        #endregion

        JArray relations = Safe(() => Relations(realms, RealmId, OwnId), new JArray());

        #region 剧本本身

        int elapsed = Math.Max(0, Date.getYearsSince(0d));
        int year = elapsed + 1;
        if (wild != null && wild.year != 0)
        {
            year = wild.year + elapsed;
            // 公元前后之间没有 0 年
            if (wild.year < 0 && year >= 0) year++;
        }
        WorldMapEra preset = wild == null || string.IsNullOrEmpty(wild.era) ? null : WorldMapData.Eras.FirstOrDefault(era => era.id == wild.era);
        cityCount = cities.Count;
        realmCount = realms.Count;
        var root = new JObject { ["id"] = id };
        Names(root, name);
        string description = string.Format(LM.Get("scenario_export_desc"), Clean(World.world.map_stats?.name), year, cityCount, realmCount,
            divisionCount);
        root["dzh"] = description;
        root["dtw"] = description;
        root["den"] = description;
        root["year"] = year == 0 ? 1 : year;
        root["focus"] = preset?.focus ?? "east";
        root["order"] = 100;
        if (preset != null) root["base"] = preset.id;
        if (earth) root["frame"] = frame.id;
        else root["map"] = new JObject { ["mode"] = "color" };
        root["regions"] = regionArray;
        root["cities"] = cityArray;
        if (tianxia.Count > 0) root["tianxia"] = tianxia;
        if (coreArray.Count > 0) root["cores"] = coreArray;
        root["polities"] = realmArray;
        if (relations.Count > 0) root["relations"] = relations;
        Guard("科技与制度", () => Culture(root, cultures.OrderBy(key => key, StringComparer.Ordinal).ToList()));
        List<WorldMapPaint> paint = Safe(() => Wilderness(projection, regionOf), null);
        if (paint is { Count: > 0 }) root["paint"] = JArray.FromObject(paint);

        #endregion

        return root;
    }

    #region 地图

    // 别的世界：按格子另存 map.png(颜色取导入地图的对照色，读回来就是原来那种地形)与对照表；返回格子 → 经纬度(左下角为 0、一格 0.1°，
    // 隔几格取一格的按取样后的格子算)
    private static Projection WriteMap(string dir)
    {
        int width = MapBox.width, height = MapBox.height;
        int shrink = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / (double)MaxSide));
        int cols = Pad(width / shrink), rows = Pad(height / shrink);
        Color32 sea = EarthTerrain.PreviewColorOf("deep_ocean");
        var pixels = new Color32[cols * rows];
        var colors = new Dictionary<(object, object), Color32>();
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            int x = col * shrink, y = row * shrink;
            WorldTile tile = x < width && y < height ? World.world.GetTileSimple(x, y) : null;
            if (tile == null)
            {
                pixels[row * cols + col] = sea;
                continue;
            }
            var key = ((object)tile.main_type, (object)tile.top_type);
            if (!colors.TryGetValue(key, out Color32 color)) colors[key] = color = EarthTerrain.PreviewColorOf(TypeOf(tile));
            pixels[row * cols + col] = color;
        }
        var texture = new Texture2D(cols, rows, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(dir, WorldMapImporter.FileName), ImageConversion.EncodeToPNG(texture));
        }
        finally
        {
            UnityEngine.Object.Destroy(texture);
        }
        WorldMapImporter.WriteLegend(dir);
        return new Projection { west = 0f, bottom = 0f, degrees = WorldMapImporter.Step / shrink };
    }

    private static int Pad(int tiles) => Math.Max(MinSide, (tiles + ChunkTiles - 1) / ChunkTiles * ChunkTiles);

    // 一格的地形换成地球网格的写法(导入地图的对照色只有这几种)：水按深浅，岩浆当山，山巅当山，雪顶的山、丘陵当雪山、雪丘；
    // 平地按高低与地表(草、稀树、丛林、荒漠、冻土、桦、枫、沼泽)，原版别的地表(魔法、糖果、腐化、地狱……)就近归类，田、路、墙看底下的地
    private static string TypeOf(WorldTile tile)
    {
        TileTypeBase type = tile.Type;
        TileTypeBase main = tile.main_type ?? type;
        if (main == null) return "deep_ocean";
        string mainId = main.id ?? "";
        if (main.liquid && !main.lava)
            return mainId.Contains("deep") ? "deep_ocean" : mainId.Contains("close") ? "close_ocean" : "shallow_waters";
        TopTileType top = tile.top_type;
        if (top != null && (top.road || top.farm_field || top.wall)) top = null;
        string topId = top?.id ?? "";
        string biome = (top?.biome_id ?? "") + "|" + topId;
        bool snow = topId.Contains("snow") || topId.Contains("ice");
        if (main.lava || type?.lava == true || main.mountains || main.summit) return snow ? "mountains:snow_block" : "mountains";
        if (mainId.Contains("hills")) return snow ? "hills:snow_hills" : "hills";
        if (main.sand && top == null) return "sand";
        string level = mainId.EndsWith("_high", StringComparison.Ordinal) || topId.EndsWith("_high", StringComparison.Ordinal) ? "high" : "low";
        string kind = biome.Contains("desert") || biome.Contains("infernal") || biome.Contains("wasteland") || biome.Contains("sand") ? "desert"
            : biome.Contains("permafrost") || snow || biome.Contains("frost") || biome.Contains("crystal") ? "permafrost"
            : biome.Contains("jungle") ? "jungle"
            : biome.Contains("swamp") || biome.Contains("corrupt") || biome.Contains("tumor") || biome.Contains("biomass") ? "swamp"
            : biome.Contains("savanna") ? "savanna"
            : biome.Contains("birch") ? "birch"
            : biome.Contains("maple") ? "maple"
            : "grass";
        return "soil_" + level + ":" + kind + "_" + level;
    }

    #endregion

    #region 无人区

    // 永久中立区刷 "n"，挂了法理的刷 "r:<地区编号>"(与剧本编辑器约定)；格子按无人区每一格的经纬度取 LandStep 一格，先到先得
    private static List<WorldMapPaint> Wilderness(Projection projection, Dictionary<KingdomTitle, string> regionOf)
    {
        IReadOnlyList<WildRegion> regions = WildernessSystem.Regions;
        List<TileZone> zones = World.world?.zone_calculator?.zones;
        if (regions == null || regions.Count == 0 || zones == null) return null;
        var zoneById = new Dictionary<int, TileZone>(zones.Count);
        foreach (TileZone zone in zones)
            if (zone != null) zoneById[zone.id] = zone;
        var cells = new Dictionary<int, string>();
        foreach (WildRegion region in regions)
        {
            if (region == null) continue;
            foreach (int zoneId in region.Zones)
            {
                if (!zoneById.TryGetValue(zoneId, out TileZone zone) || zone.city != null || zone.tiles == null) continue;
                string key;
                if (region.Neutral) key = "n";
                else
                {
                    KingdomTitle title = WildernessSystem.TitleOf(zone);
                    if (title == null || !regionOf.TryGetValue(title, out string regionId)) continue;
                    key = "r:" + regionId;
                }
                foreach (WorldTile tile in zone.tiles)
                {
                    if (tile == null) continue;
                    projection.LonLat(tile.x, tile.y, out float lon, out float lat);
                    int cell = ScenarioCells.Key(lon, lat, ScenarioCells.LandStep);
                    if (!cells.ContainsKey(cell)) cells[cell] = key;
                }
            }
        }
        return ScenarioCells.FromMap(cells);
    }

    #endregion

    #region 关系

    // 只记 DiplomacyActions.ApplyScenario 认的、看得准的：
    //   war 在打的仗(主攻 → 主守，行政区、殖民地记到帝国、宗主头上，记到同一国的不要)；alliance 同盟(帝国与帝国成员不结同盟，不记)；
    //   marriage 和亲(二十年内的姻亲)；pact 通好(两个帝国)；protect 保护(神罗的保护约)；tribute 朝贡(a 向帝国 b)；vassal 封建附庸
    //   (a 立 b，b 不在帝国里)；union 关税同盟(b 入 a 的市场)；friend/feud 还没淡完的旧谊宿怨(记眼下淡到的分量)
    private static JArray Relations(Dictionary<Kingdom, Realm> realms, Func<Kingdom, string> realmId, Func<Kingdom, string> ownId)
    {
        var result = new JArray();
        var seen = new HashSet<string>();
        void Add(string a, string b, string kind, bool ordered, string goal = null, int value = 0)
        {
            if (a == null || b == null || a == b) return;
            if (!ordered && string.CompareOrdinal(a, b) > 0) (a, b) = (b, a);
            if (!seen.Add(a + "|" + b + "|" + kind)) return;
            var entry = new JObject { ["a"] = a, ["b"] = b, ["kind"] = kind };
            Put(entry, "goal", goal);
            if (value != 0) entry["value"] = value;
            result.Add(entry);
        }

        Guard("在打的仗", () =>
        {
            foreach (War war in World.world.wars.list.ToList())
            {
                if (war?.data == null || war.hasEnded() || war.isTotalWar()) continue;
                WarGoal goal = WarGoalSystem.Get(war);
                Add(realmId(war.main_attacker), realmId(war.main_defender), "war", true, goal == WarGoal.None ? null : goal.ToString());
            }
        });
        Guard("同盟", () =>
        {
            foreach (Alliance alliance in World.world.alliances.list.ToList())
            {
                List<Kingdom> members = alliance?.kingdoms_list?.Where(kingdom => ownId(kingdom) != null && !kingdom.IsEmpire() && !kingdom.IsInEmpire())
                    .ToList();
                if (members == null) continue;
                for (int i = 1; i < members.Count; i++) Add(ownId(members[0]), ownId(members[i]), "alliance", false);
            }
        });
        foreach (Kingdom kingdom in realms.Keys)
        {
            Kingdom a = kingdom;
            string self = ownId(a);
            Guard("和亲", () =>
            {
                foreach (long other in a.GetOrCreate().marriage_pacts?.Keys.ToList() ?? new List<long>())
                {
                    Kingdom b = World.world.kingdoms.get(other);
                    if (ownId(b) != null && RoyalMarriageSystem.Allied(a, b)) Add(self, ownId(b), "marriage", false);
                }
            });
            Guard("通好", () =>
            {
                Empire empire = a.IsEmpire() ? a.GetEmpire() : null;
                if (empire?.data?.diplomatic_pacts == null) return;
                foreach (long other in empire.data.diplomatic_pacts.Keys.ToList())
                {
                    Kingdom b = ModClass.EMPIRE_MANAGER?.get(other)?.CoreKingdom;
                    if (ownId(b) != null && GreatPowerDiplomacy.Bound(a, b)) Add(self, ownId(b), "pact", false);
                }
            });
            Guard("保护", () =>
            {
                Kingdom protector = World.world.kingdoms.get(a.GetOrCreate().hre_protector_id);
                if (ownId(protector) != null && HolyRomanSystem.ProtectedBy(a, protector)) Add(ownId(protector), self, "protect", true);
            });
            Guard("朝贡", () =>
            {
                Empire lord = a.IsInEmpire() ? null : a.GetTakenAllianceEmpire();
                if (lord?.data != null && !lord.isRekt()) Add(self, ownId(lord.CoreKingdom), "tribute", true);
            });
            Guard("附庸", () =>
            {
                Kingdom lord = a.IsEmpire() || a.IsInEmpire() ? null : FeudalVassalService.GetOverlord(a);
                Add(ownId(lord), self, "vassal", true);
            });
            Guard("关税同盟", () =>
            {
                Kingdom leader = CommodityMarketSystem.UnionOf(a);
                if (leader != a) Add(ownId(leader), self, "union", true);
            });
            Guard("旧谊宿怨", () =>
            {
                foreach (long other in a.GetOrCreate().history_opinion?.Keys.ToList() ?? new List<long>())
                {
                    Kingdom b = World.world.kingdoms.get(other);
                    if (ownId(b) == null) continue;
                    int opinion = DiplomacyActions.HistoryOpinion(a, b);
                    if (Math.Abs(opinion) >= MinOpinion) Add(self, ownId(b), opinion > 0 ? "friend" : "feud", false, null, Math.Abs(opinion));
                }
            });
        }
        return result;
    }

    #endregion

    #region 科技与制度

    // 各文化已研究的科技：tech 记已研究的最高一档，这档以内没研究的记 techSkip(开局照档补齐、跳过这些，恰好是现在研究了的)；
    // 已施行的制度节点记 inst(开局连同前置强制施行)
    private static void Culture(JObject root, List<string> cultures)
    {
        if (cultures.Count == 0) return;
        List<TechNodeConfig> techs = TechnologySystem.Techs?.Where(tech => tech != null && !string.IsNullOrEmpty(tech.id)).ToList() ??
                                     new List<TechNodeConfig>();
        Dictionary<string, CultureInstitutionState> institutions = InstitutionSystem.ExportCultureStates();
        var tech = new JObject();
        var skip = new JObject();
        var inst = new JObject();
        foreach (string culture in cultures)
        {
            List<TechNodeConfig> known = techs.Where(item => TechnologySystem.HasTech(culture, item.id)).ToList();
            int tier = known.Count == 0 ? 0 : known.Max(item => item.tier);
            tech[culture] = tier;
            List<string> missing = techs.Where(item => item.tier <= tier && !TechnologySystem.HasTech(culture, item.id)).Select(item => item.id).ToList();
            if (missing.Count > 0) skip[culture] = new JArray(missing);
            if (institutions.TryGetValue(culture, out CultureInstitutionState state) && state?.enacted_node_ids is { Count: > 0 })
                inst[culture] = new JArray(state.enacted_node_ids.Distinct());
        }
        if (tech.Count > 0) root["tech"] = tech;
        if (skip.Count > 0) root["techSkip"] = skip;
        if (inst.Count > 0) root["inst"] = inst;
    }

    #endregion

    #region 人物

    // 剧本人物：姓、名(只写一种，三种语言同名；没姓的不写姓，开局按本国文化起)、年龄、女的写 sex f；rel 与君主的关系
    private static JObject Person(Actor actor, string rel, bool heir)
    {
        if (actor?.data == null || actor.isRekt()) return null;
        var name = actor.GetModName();
        string family = Clean(name?.familyName), given = Clean(name?.firstName);
        if (string.IsNullOrEmpty(given))
        {
            given = Clean(actor.getName());
            family = "";
        }
        var entry = new JObject();
        Put(entry, "fam", family);
        Put(entry, "giv", given);
        entry["age"] = actor.getAge();
        if (!actor.isSexMale()) entry["sex"] = "f";
        Put(entry, "rel", rel);
        if (heir) entry["heir"] = true;
        return entry;
    }

    // 宗室：君主在世的子女(年长的在前，至多 MaxKin 个，开局认君主作父)，储君不是子女的另记一条(同父母的记兄弟姐妹)
    private static JArray Kin(Kingdom kingdom, Actor king)
    {
        var list = new JArray();
        Actor heir = kingdom.GetHeir();
        var added = new HashSet<Actor>();
        foreach (Actor child in (king.getChildren() ?? Enumerable.Empty<Actor>())
                 .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive()).OrderByDescending(actor => actor.getAge()).Take(MaxKin))
        {
            JObject person = Person(child, child.isSexMale() ? "son" : "daughter", child == heir);
            if (person == null) continue;
            list.Add(person);
            added.Add(child);
        }
        if (heir != null && !added.Contains(heir))
        {
            var parents = new HashSet<Actor>((king.getParents() ?? Enumerable.Empty<Actor>()).Where(parent => parent != null));
            bool sibling = parents.Count > 0 && (heir.getParents() ?? Enumerable.Empty<Actor>()).Any(parents.Contains);
            JObject person = Person(heir, sibling ? heir.isSexMale() ? "brother" : "sister" : null, true);
            if (person != null) list.Add(person);
        }
        return list;
    }

    #endregion

    #region 小工具

    // 城的人口(无小人模式取背景人口，否则取城里的人；取大的)
    private static float People(City city)
    {
        float total = Safe(() => CityPopulationSystem.GetTotal(city), 0f);
        return Math.Max(total, city.getPopulationPeople());
    }

    // 名字里的 "|" 是"简体|英文|繁体"的分隔，换掉
    private static string Clean(string text) => (text ?? "").Replace('|', '/').Trim();

    // 三种写法同名
    private static void Names(JObject entry, string name)
    {
        entry["zh"] = name ?? "";
        entry["tw"] = name ?? "";
        entry["en"] = name ?? "";
    }

    private static void Put(JObject entry, string key, string value)
    {
        if (!string.IsNullOrEmpty(value)) entry[key] = value;
    }

    // 文件夹名：去掉 Windows 文件名里不许有的字符与首尾的点、空格，太长的截短，空的叫 import
    private static string FolderName(string name)
    {
        var chars = (name ?? "").Where(c => c >= ' ' && "<>:\"/\\|?*".IndexOf(c) < 0).ToArray();
        string folder = new string(chars).Trim().Trim('.').Trim();
        if (folder.Length > 40) folder = folder.Substring(0, 40).Trim();
        return string.IsNullOrEmpty(folder) ? "import" : folder;
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            T value = read();
            return value == null ? fallback : value;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][剧本导入] 读取出错，跳过: {exception.Message}");
            return fallback;
        }
    }

    private static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][剧本导入] {what} 没导成: {exception.Message}");
        }
    }

    #endregion
}

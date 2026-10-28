using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using Newtonsoft.Json;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 开局宗教(WorldMaps/religions.json，由 Tools/WorldMap/religion.py 生成)：按时代立教、定圣地、定各城信仰，
// 分出的教会记母宗教(东正教会、什叶派、新教各宗、科普特与亚美尼亚教会)，各国国教随都城。
// 以前剧本不管宗教，开局后按天下各立一教：全欧洲连罗斯一个"使徒教会"，1628 年的天竺是"佛教"、华夏是"道教"
public static class WorldMapReligion
{
    private sealed class Definition
    {
        public string id = "", faith = "", sphere = "", parent = "", split = "";
        public bool caesaro;
        // 名(按年换名)、圣地(按年换城)：[年, 值]
        public List<List<object>> n = new();
        public List<List<object>> holy = new();
    }

    private sealed class FileData
    {
        public List<int> years = new();
        public List<Definition> religions = new();
        public Dictionary<string, List<string>> regions = new();
        public Dictionary<string, Dictionary<string, string>> cities = new();
    }

    public sealed class Place
    {
        public string id, region;
        public int rank;
        public City city;
    }

    private static FileData _file;
    private static bool _loaded;

    private static FileData Load()
    {
        if (_loaded) return _file;
        _loaded = true;
        try
        {
            string path = Path.Combine(WorldMapData.Folder, "religions.json");
            if (File.Exists(path)) _file = JsonConvert.DeserializeObject<FileData>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 宗教表读不出来: {exception.Message}");
        }
        return _file;
    }

    // 按年取值：不晚于开局年的最后一条
    private static string AtYear(List<List<object>> entries, int year)
    {
        string value = null;
        foreach (List<object> entry in entries ?? new List<List<object>>())
        {
            if (entry == null || entry.Count < 2 || Convert.ToInt32(entry[0]) > year) continue;
            value = entry[1]?.ToString();
        }
        return value;
    }

    // 开局年取哪一列：不晚于开局年的最后一个时代
    private static int Column(FileData file, int year)
    {
        int column = 0;
        for (int i = 0; i < file.years.Count; i++)
            if (file.years[i] <= year) column = i;
        return column;
    }

    public static void Apply(int year, List<Place> places, List<(Kingdom kingdom, City capital, Layer.Empire empire)> realms)
    {
        FileData file = Load();
        if (file?.religions == null || file.years == null || file.years.Count == 0) return;
        int column = Column(file, year);
        string yearKey = file.years[column].ToString();
        // 各城本时代的宗教
        var faithOf = new Dictionary<City, string>();
        foreach (Place place in places.Where(place => place.city?.data != null && !place.city.isRekt()))
        {
            string religion = null;
            if (file.cities != null && file.cities.TryGetValue(place.id, out Dictionary<string, string> row))
                row.TryGetValue(yearKey, out religion);
            if (religion == null && place.region != null && file.regions.TryGetValue(place.region, out List<string> columns) &&
                column < columns.Count)
                religion = columns[column];
            if (!string.IsNullOrEmpty(religion)) faithOf[place.city] = religion;
        }
        var byId = file.religions.Where(definition => !string.IsNullOrEmpty(definition.id))
            .GroupBy(definition => definition.id).ToDictionary(group => group.Key, group => group.First());
        var made = new Dictionary<string, Religion>();
        // 母宗教先立(分出的教会要记母宗教编号)
        foreach (string id in faithOf.Values.Distinct().OrderBy(id => byId.TryGetValue(id, out Definition d) && !string.IsNullOrEmpty(d.parent) ? 1 : 0))
        {
            if (!byId.TryGetValue(id, out Definition definition)) continue;
            try
            {
                List<Place> followers = places.Where(place => place.city != null && faithOf.TryGetValue(place.city, out string r) && r == id)
                    .OrderBy(place => place.rank).ToList();
                string holyId = AtYear(definition.holy, year);
                City holy = places.FirstOrDefault(place => place.id == holyId && place.city != null && faithOf.TryGetValue(place.city, out string r) && r == id)?.city
                            ?? followers.FirstOrDefault()?.city;
                Religion religion = Found(definition, holy, year);
                if (religion == null) continue;
                made[id] = religion;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] 立教 {id} 失败: {exception.Message}");
            }
        }
        // 分出的教会：记母宗教，母宗教记已分出(免得又按大分裂、继承之争再分一次)
        double now = World.world.getCurWorldTime();
        foreach ((string id, Religion religion) in made)
        {
            Definition definition = byId[id];
            if (string.IsNullOrEmpty(definition.parent) || !made.TryGetValue(definition.parent, out Religion parent)) continue;
            var data = religion.GetOrCreate();
            data.ParentReligionId = parent.data.id;
            data.SplitAt = now;
            data.Caesaropapist = definition.caesaro;
            var parentData = parent.GetOrCreate();
            if (definition.split == "eastern") parentData.EasternSplit = true;
            if (definition.split == "shia") parentData.ShiaSplit = true;
        }
        // 各城改信
        foreach ((City city, string id) in faithOf)
            if (made.TryGetValue(id, out Religion religion))
                BackgroundReligionSystem.Convert(city, religion, city.religion);
        // 国教随都城(莫卧儿的都城阿格拉奉伊斯兰，治下多是印度教的城)
        foreach ((Kingdom kingdom, City capital, Layer.Empire empire) in realms)
        {
            if (kingdom?.data == null || kingdom.isRekt() || capital?.data == null) continue;
            if (!faithOf.TryGetValue(capital, out string id) || !made.TryGetValue(id, out Religion religion)) continue;
            kingdom.setReligion(religion);
            if (kingdom.king != null && !kingdom.king.isRekt()) kingdom.king.setReligion(religion);
            if (empire != null && empire.CoreKingdom == kingdom) empire.Religion = religion;
        }
        LogService.LogInfo($"[EmpireCraft][世界地图] 开局宗教：{string.Join("、", made.Values.Select(religion => religion.data.name))}");
    }

    private static Religion Found(Definition definition, City holy, int year)
    {
        if (holy?.data == null) return null;
        CityPopulationSystem.EnsureLeader(holy);
        Actor founder = holy.leader;
        if (founder == null || founder.isRekt() || founder.subspecies == null) founder = holy.kingdom?.king;
        if (founder == null || founder.isRekt() || founder.subspecies == null || founder.kingdom == null) return null;
        Religion religion = World.world.religions.newReligion(founder, false);
        if (religion?.data == null) return null;
        string name = WorldMapData.PickParts(AtYear(definition.n, year));
        if (!string.IsNullOrWhiteSpace(name)) religion.data.name = name;
        var data = religion.GetOrCreate();
        if (Enum.TryParse(definition.faith, out FaithType faith)) ReligionFaith.Record(religion, faith);
        if (!string.IsNullOrEmpty(definition.sphere)) data.Sphere = definition.sphere;
        religion.SetCity(holy);
        // 原版造教时按创教者记了一条"某某创立某教"(名字还是原版起的)：剧本里的宗教是古已有之，不记
        ReligionChronicle.Clear(religion);
        return religion;
    }
}

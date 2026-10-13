using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 城市风格(纯净地图城市平面图用，见 PureMapPatch)：每座城记住自己是按哪一系文化的风格建起来的(CityExtraData.map_style)。
//   · 文化变了(被征服、同化、易俗)不会自动改：照旧按原风格建，新街区也是旧风格；
//   · 本国君主走"改建城池"决议(每座要改的城花国库 CostPerCity)，各城才按现文化风格逐年翻新——
//     先拆旧、起工地，再建成新式，新旧混杂，约 RebuildYears 年改完(布局逐个地块改，城墙与都城地标改完才换)。
// 文化系：华夏(华夏、中国、山海)、日本、西欧(西方、法兰克、日耳曼、斯拉夫、精灵)、罗马、维京、阿拉伯(阿拉伯、犹太)、
//   波斯、埃及、印度、阿兹特克、游牧、林地(奥吉布瓦)；其余按华夏的通用图块画。
public static class CityStyleSystem
{
    public const string Plot = "rebuild_city_style";
    private const float RebuildYears = 10f;
    private const int CostPerCity = 120;

    public static string Family(string culture) => culture switch
    {
        "Huaxia" or "China" or "Shanhai" => "huaxia",
        "Japan" => "japan",
        "Western" or "Frankish" or "Germanic" or "Slavonic" or "ElfFancy" => "western",
        "Roma" => "roma",
        "Viking" => "viking",
        "Arab" or "Kosher" => "arab",
        "Persepolis" => "persia",
        "Egypt" => "egypt",
        "India" => "india",
        "Aztec" => "aztec",
        "Youmu" => "nomad",
        "Ojibwe" => "woodland",
        _ => "huaxia"
    };

    public static string CurrentFamily(City city) => Family(TechnologySystem.GetCultureOf(city) ?? "");

    // 这座城定型的风格(第一次问到时按现文化定下)
    public static string Established(City city)
    {
        var data = city.GetOrCreate();
        if (string.IsNullOrEmpty(data.map_style)) data.map_style = CurrentFamily(city);
        return data.map_style;
    }

    // 改建中：目标风格与进度(0~1)；没在改建返回 (null, 0)。改完即定型
    public static (string target, float progress) Rebuild(City city)
    {
        var data = city.GetOrCreate();
        if (string.IsNullOrEmpty(data.map_style_target) || data.map_style_since < 0d) return (null, 0f);
        float progress = Mathf.Clamp01(Date.getYearsSince(data.map_style_since) / RebuildYears);
        if (progress < 1f) return (data.map_style_target, progress);
        data.map_style = data.map_style_target;
        data.map_style_target = "";
        data.map_style_since = -1d;
        return (null, 0f);
    }

    // 风格与现文化不合、又没在改建的城
    private static List<City> Stale(Kingdom kingdom) =>
        kingdom?.cities?.Where(city => city?.data != null && !city.isRekt() &&
                                       string.IsNullOrEmpty(city.GetOrCreate().map_style_target) &&
                                       Established(city) != CurrentFamily(city)).ToList() ?? new List<City>();

    public static bool CanRebuild(Actor actor)
    {
        Kingdom kingdom = actor?.kingdom;
        if (kingdom?.king != actor || kingdom.isRekt()) return false;
        int count = Stale(kingdom).Count;
        return count > 0 && kingdom.GetMoney() >= count * CostPerCity;
    }

    // 决议施行：出钱，各城按现文化风格开工改建
    public static bool Enact(Actor king)
    {
        Kingdom kingdom = king?.kingdom;
        if (!CanRebuild(king)) return false;
        List<City> cities = Stale(kingdom);
        kingdom.SubMoney(cities.Count * CostPerCity, TreasuryCategory.Construction);
        double now = World.world.getCurWorldTime();
        foreach (City city in cities)
        {
            var data = city.GetOrCreate();
            data.map_style_target = CurrentFamily(city);
            data.map_style_since = now;
        }
        string text = string.Format(LM.Get("city_style_rebuild_history"), kingdom.GetKingdomName(), cities.Count);
        EventRecorder.Record(kingdom.GetEmpire(), text, king, kingdom);
        return true;
    }
}

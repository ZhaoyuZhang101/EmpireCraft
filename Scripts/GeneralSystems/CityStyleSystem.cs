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
//   波斯、埃及、印度、阿兹特克、游牧(游牧文化，及制度线属游牧的各文化)、林地(奥吉布瓦)；其余按华夏的通用图块画。
// 游牧的城是蒙古包营地，不修路(见 UrbanFabric、RoadNetwork)；林地(奥吉布瓦)、阿兹特克这类部落制的文化也不修路
// (用户转玩家 2026-10-17 "不止游牧，像奥吉布瓦以及阿兹特克这种明显部落制的，也没必要画路")，城外的村镇照旧有，只用小路连着。
public static class CityStyleSystem
{
    public const string Plot = "rebuild_city_style";
    private const float RebuildYears = 10f;
    private const int CostPerCity = 120;

    public static string Family(string culture) => culture switch
    {
        "Huaxia" or "China" or "Shanhai" => "huaxia",
        "Japan" => "japan",
        "Western" or "Frankish" or "Germanic" or "Slavonic" or "ElfFancy" or "Emperor" or "Corporate" => "western",
        "Roma" => "roma",
        "Viking" => "viking",
        "Arab" or "Kosher" => "arab",
        "Persepolis" => "persia",
        "Egypt" => "egypt",
        "India" => "india",
        "Aztec" => "aztec",
        "Youmu" => "nomad",
        "Ojibwe" => "woodland",
        _ => NomadCulture(culture) ? "nomad" : LineFamily(culture)
    };

    // 别的(自定义)文化按制度线取(以前一律回落成华夏风格)
    private static string LineFamily(string culture) =>
        (string.IsNullOrEmpty(culture) ? "" : InstitutionSystem.GetCultureLine(culture)) switch
        {
            "Roma" => "western",
            "Arabic" => "arab",
            "Japan" => "japan",
            "Youmu" => "woodland",
            _ => "huaxia"
        };

    // 别的(自定义)文化：有游牧特性的住牧帐(以前看游牧一线，没马的阿兹特克式文化也住帐篷)
    private static bool NomadCulture(string culture) =>
        !string.IsNullOrEmpty(culture) && EmpireCraft.Scripts.Data.OnomasticsRule.IsNomadCulture(culture);

    public static bool Nomad(City city) => city?.data != null && !city.isRekt() && Established(city) == "nomad";

    // 游牧营地的人口上限：逐水草而居，一片草场养的人远不如农耕城，住房上限只算 NomadCapacityShare(见 CityPatch.getPopulationMaximum)。
    // 只看城是不是营地：游牧政权占了的农耕城照旧；营地改建成别的风格后也照旧。原有超出的人口按住房超额的规则每年流失超出部分的
    // 四分之一、不再出生，逐年降下来，不会一下子没了。征兵比例照旧(十户制 15%)，人少兵也就少
    public const float NomadCapacityShare = 0.4f;

    // 读档、建图途中不判(那时城市风格还没读进来，免得按眼下文化先定了风格)
    public static float CapacityFactor(City city) =>
        global::Config.game_loaded && !SmoothLoader.isLoading() && Nomad(city) ? NomadCapacityShare : 1f;

    // 不修路的城：游牧、林地、阿兹特克
    public static bool Roadless(City city) =>
        city?.data != null && !city.isRekt() && Established(city) is "nomad" or "woodland" or "aztec";

    public static string CurrentFamily(City city) => Family(TechnologySystem.GetCultureOf(city) ?? "");

    // 这座城定型的风格(第一次问到时按现文化定下)
    public static string Established(City city)
    {
        var data = city.GetOrCreate();
        if (string.IsNullOrEmpty(data.map_style)) data.map_style = CurrentFamily(city);
        // 旧档：以前只认游牧文化本身，制度线属游牧的文化被定成了华夏城池，改回来一次
        if (!data.map_style_nomad_checked)
        {
            data.map_style_nomad_checked = true;
            if (data.map_style == "huaxia" && string.IsNullOrEmpty(data.map_style_target) && CurrentFamily(city) == "nomad")
                data.map_style = "nomad";
        }
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

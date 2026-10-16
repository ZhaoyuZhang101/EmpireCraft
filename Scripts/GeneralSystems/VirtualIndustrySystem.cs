using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 纯净地图模式的虚拟工厂：人物、建筑都是数据，不必靠装了工厂模组才有工厂。工厂建筑资产不存在
// (没装 WarBox 的火药厂、钢铁厂、轻重工厂、船厂、飞机厂)时，按本文化研究到的技术与城市规模折算工厂数
// (见 UrbanEmploymentSystem.CountFactories，影响城市工场岗位、工人阶层与研究)：
//   · 火药 → 火药厂，工业化 → 钢铁厂，摩托化 → 轻工厂与轻型船厂，装甲战 → 重工厂与重型船厂，
//     航空 → 轻型飞机厂，战略航空 → 重型飞机厂；每样算一类，船厂要有码头；
//   · 每 HouseholdsPerFactory 户能开一类，至多开齐已解锁的类数；工厂区里的厂另按等级算(见 FactorySystem.Units)；
//   · 资产在(装了模组)的那几类照旧按实际建筑数，不重复折算。
public static class VirtualIndustrySystem
{
    private const int HouseholdsPerFactory = 80;
    private const float RefreshSeconds = 2f;
    private static readonly HashSet<string> FactoryBuildings = new()
    {
        "gunpowder_mill", "steel_mill", "light_factory", "light_shipyard", "heavy_factory", "heavy_shipyard",
        "light_aircraft_factory", "heavy_aircraft_factory"
    };

    // 本文化已解锁、资产又不存在的工厂类数(其中船厂几类)；每两秒(真实时间)重算
    private static readonly Dictionary<string, (float at, int all, int shipyards)> Unlocked = new();

    public static bool Applies => PureMapMode.Enabled;

    public static int Factories(City city)
    {
        if (!Applies || city?.data == null || city.isRekt()) return 0;
        var (_, all, shipyards) = Count(TechnologySystem.GetCultureOf(city) ?? "");
        if (!city.hasBuildingType("type_docks")) all -= shipyards;
        if (all <= 0) return 0;
        // 工厂要水运与平地：按地利的工业系数(见 CityGeographySystem)
        int capacity = Mathf.FloorToInt(CityPopulationSystem.Households(city) / (float)HouseholdsPerFactory *
                                        CityGeographySystem.Industry(city));
        return Mathf.Clamp(capacity, 0, all) + FactorySystem.Units(city);
    }

    private static (float, int, int) Count(string culture)
    {
        float now = Time.unscaledTime;
        if (Unlocked.TryGetValue(culture, out var cached) && now - cached.at < RefreshSeconds && now >= cached.at) return cached;
        int all = 0, shipyards = 0;
        if (!string.IsNullOrEmpty(culture) && TechnologySystem.IsActiveFor(culture))
            foreach (TechNodeConfig tech in TechnologySystem.Techs)
            {
                if (!TechnologySystem.HasTech(culture, tech.id)) continue;
                foreach (string building in tech.unlock_buildings)
                {
                    if (!IsVirtual(building)) continue;
                    all++;
                    if (building.EndsWith("_shipyard")) shipyards++;
                }
            }
        var entry = (now, all, shipyards);
        Unlocked[culture] = entry;
        return entry;
    }

    // 这座工厂建筑要虚拟(是工厂、资产又不存在)
    public static bool IsVirtual(string building) =>
        FactoryBuildings.Contains(building) && AssetManager.buildings?.get(building) == null;

    // 科技窗口：这项技术在纯净地图下解锁了几类虚拟工厂
    public static int VirtualUnlocks(TechNodeConfig tech) =>
        !Applies || tech?.unlock_buildings == null ? 0 : tech.unlock_buildings.Count(IsVirtual);

    public static void ResetWorldState() => Unlocked.Clear();
}

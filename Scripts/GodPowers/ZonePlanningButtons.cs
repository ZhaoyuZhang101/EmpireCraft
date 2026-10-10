using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 城市规划：工业区、居住区、商业区、军事区、保护区各一个神力(农耕区见 FarmPlanningButton)。
// 点区块划为该用途；已经是该用途的再点一次取消。选中这些神力时地图上按颜色显示各区块的用途。
// 规划决议：点国家领土，把该国的规划决议换成下一个可选的(之后若干年 AI 不改)
public static class ZonePlanningButtons
{
    public static readonly (string id, ZoneUse use)[] Powers =
    {
        ("zone_industry", ZoneUse.Industry),
        ("zone_residential", ZoneUse.Residential),
        ("zone_commerce", ZoneUse.Commerce),
        ("zone_military", ZoneUse.Military),
        ("zone_reserve", ZoneUse.Reserve)
    };

    public static void init()
    {
        foreach ((string id, ZoneUse use) in Powers)
        {
            ZoneUse captured = use;
            AssetManager.powers.add(new GodPower
            {
                id = id,
                name = id,
                click_action = (tile, power) => Toggle(tile, captured)
            });
        }
        AssetManager.powers.add(new GodPower
        {
            id = "plan_policy",
            name = "plan_policy",
            click_action = CyclePolicy
        });
        // 羁縻规划：点帝国内的地方政区，在 自动 → 羁縻 → 直辖 之间切换
        AssetManager.powers.add(new GodPower
        {
            id = "jimi_policy",
            name = "jimi_policy",
            click_action = (tile, power) =>
            {
                WorldTip.showNow(JimiSystem.Cycle(tile?.zone?.city?.kingdom), false, "top", 3f);
                return true;
            }
        });
    }

    private static bool Toggle(WorldTile pTile, ZoneUse use)
    {
        TileZone zone = pTile?.zone;
        City city = zone?.city;
        if (city == null || city.isRekt() || city.isNeutral())
        {
            WorldTip.showNow(LM.Get("farm_planning_no_city"), false, "top", 3f);
            return false;
        }
        if (ZonePlanSystem.IsCenter(city, zone))
        {
            WorldTip.showNow(LM.Get("zone_planning_center"), false, "top", 3f);
            return false;
        }
        bool clear = ZonePlanSystem.Get(city, zone) == use;
        if (!ZonePlanSystem.Set(city, zone, clear ? ZoneUse.None : use))
        {
            WorldTip.showNow(LM.Get("zone_planning_contiguous"), false, "top", 3f);
            return false;
        }
        KingdomFrontLineHelper.HighlightZones(zone, ZonePlanSystem.ColorOf(clear ? ZoneUse.None : use));
        WorldTip.showNow(string.Format(LM.Get(clear ? "zone_planning_removed" : "zone_planning_added"),
            city.GetCityName(), ZonePlanSystem.UseName(use)), false, "top", 3f);
        return true;
    }

    private static bool CyclePolicy(WorldTile pTile, string pPower)
    {
        Kingdom kingdom = pTile?.zone?.city?.kingdom;
        if (kingdom == null || kingdom.wild || kingdom.isRekt())
        {
            WorldTip.showNow(LM.Get("farm_planning_no_city"), false, "top", 3f);
            return false;
        }
        PlanPolicy policy = ZonePlanSystem.CyclePolicy(kingdom);
        WorldTip.showNow(string.Format(LM.Get("plan_policy_adopted"), kingdom.GetKingdomName(),
            ZonePlanSystem.PolicyName(policy)), false, "top", 3f);
        return true;
    }
}

using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 城市规划的其余神力(用途、设施笔刷见 PlanBrushPowers)：
// 规划决议：点国家领土，把该国的规划决议换成下一个可选的(之后若干年 AI 不改)；另起格局；羁縻规划
public static class ZonePlanningButtons
{
    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = "plan_policy",
            name = "plan_policy",
            click_action = CyclePolicy
        });
        // 另起格局：点一座城，城市肌理换一个随机种子(干道、片区街网、市中心、工业区、村镇、开发先后重新随机)
        AssetManager.powers.add(new GodPower
        {
            id = "urban_reseed",
            name = "urban_reseed",
            click_action = Reseed
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

    private static bool Reseed(WorldTile pTile, string pPower)
    {
        City city = pTile?.zone?.city;
        if (city == null || city.isRekt() || city.isNeutral())
        {
            WorldTip.showNow(LM.Get("farm_planning_no_city"), false, "top", 3f);
            return false;
        }
        UrbanFabric.Reseed(city);
        PureMapPatch.Redraw(city);
        WorldTip.showNow(string.Format(LM.Get("urban_reseed_done"), city.GetCityName()), false, "top", 3f);
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

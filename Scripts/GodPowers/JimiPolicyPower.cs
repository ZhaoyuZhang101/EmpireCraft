using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.GodPowers;

// 羁縻规划：点帝国内的地方政区，在 自动 → 羁縻 → 直辖 之间切换(见 JimiSystem)
public static class JimiPolicyPower
{
    public static void Init()
    {
        if (AssetManager.powers.get("jimi_policy") != null) return;
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
}

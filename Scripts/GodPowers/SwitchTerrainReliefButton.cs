using EmpireCraft.Scripts.GamePatches;

namespace EmpireCraft.Scripts.GodPowers;

// 地形浮雕开关(用户 2026-10-15："城市和道路永久显示，power 上给个开关是否开启浮雕")：
// 开着时国色上叠地形明暗(山地压暗、高差打光、掺一点地表本色)；关掉是纯平的国色，城市与道路照常显示。
// 跟着玩家设置保存，换存档不用重开，默认开
public static class SwitchTerrainReliefButton
{
    public const string Option = "switch_terrain_relief";

    public static void init()
    {
        if (!PlayerConfig.dict.ContainsKey(Option))
            PlayerConfig.instance.data.add(new PlayerOptionData(Option) { boolVal = true });
        PureMapPatch.Relief = !PlayerConfig.dict.TryGetValue(Option, out PlayerOptionData option) || option.boolVal;
        AssetManager.powers.add(new GodPower
        {
            id = "terrain_relief",
            name = "terrain_relief",
            toggle_name = Option,
            toggle_action = toggleAction
        });
    }

    // 原版先调这里、再翻转开关值：按翻转后的值处理，整张图重画
    private static void toggleAction(string pPower)
    {
        PureMapPatch.Relief = !PlayerConfig.dict[Option].boolVal;
        World.world?.zone_calculator?.dirtyAndClear();
    }
}

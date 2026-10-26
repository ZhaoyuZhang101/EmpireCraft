using EmpireCraft.Scripts.GamePatches;

namespace EmpireCraft.Scripts.GodPowers;

// 地形浮雕开关(用户 2026-10-15："城市和道路永久显示，power 上给个开关是否开启浮雕")：
// 开着时国色半透明、露出原版地形(用户 2026-10-26 改：以前是把地形明暗画进不透明的色块，见 PureMapPatch.FillAlpha)，
// 并画出城市与道路；关掉是纯平不透明的国色，城市与道路也不画(用户 2026-10-15："地形浮雕要连城市和道路也隐藏")，城市图层照画。
// 跟着玩家设置保存，换存档不用重开，默认开
public static class SwitchTerrainReliefButton
{
    public const string Option = "switch_terrain_relief";

    public static void init()
    {
        // 开关的 OptionAsset 要自己登记：GodPower.option_asset 按 toggle_name 去 options_library 里取，
        // NML 的 CreateToggleButton 只在玩家设置里还没有这一项时才替我们登记，而这里先放了默认开的设置项，
        // 它就跳过了——没有 OptionAsset，换图、读档时原版刷新开关图标(PowerButton.checkToggleIcon)就空引用
        if (!AssetManager.options_library.has(Option))
            AssetManager.options_library.add(new OptionAsset { id = Option, type = OptionType.Bool, default_bool = true });
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

    // 原版先调这里、再翻转开关值：按翻转后的值处理，整张图重画(国色透不透明跟着变)；城市平面图层再显示时自己整张重画
    private static void toggleAction(string pPower)
    {
        PureMapPatch.Relief = !PlayerConfig.dict[Option].boolVal;
        World.world?.zone_calculator?.dirtyAndClear();
    }
}

using NeoModLoader.services;

namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftWorldLawLibrary
{
    public static WorldLawAsset world_law_civ_limit_population_50;
    public static WorldLawAsset world_law_civ_limit_population_20;
    public static WorldLawAsset empirecraft_law_realistic_age;
    public static WorldLawAsset empirecraft_law_prevent_city_destroy;
    public static WorldLawAsset empirecraft_law_ban_empire;
    public static void init()
    {
        LogService.LogInfo("加载帝国世界规则");
        AssetManager.world_laws_library.list.RemoveAll(w => w.id == "world_law_civ_limit_population_100");
        //限制人口100
        AssetManager.world_laws_library.add(WorldLawLibrary.world_law_civ_limit_population_100  = new WorldLawAsset()
        {
            id = "world_law_civ_limit_population_100",
            group_id = "harmony",
            icon_path = "ui/Icons/iconPopulation100",
            on_state_enabled = Population100On,
            default_state = false
        });
        //限制人口50
        AssetManager.world_laws_library.add(world_law_civ_limit_population_50 = new WorldLawAsset()
        {
            id = "world_law_civ_limit_population_50",
            group_id = "harmony",
            icon_path = "ui/Icons/iconPopulation50",
            on_state_enabled = Population50On,
            default_state = false
        });
        //限制人口20
        AssetManager.world_laws_library.add(world_law_civ_limit_population_20= new WorldLawAsset()
        {
            id = "world_law_civ_limit_population_20",
            group_id = "harmony",
            icon_path = "ui/Icons/iconPopulation20",
            on_state_enabled = Population20On,
            default_state = false
        });
        //真实年龄
        AssetManager.world_laws_library.add(empirecraft_law_realistic_age= new WorldLawAsset()
        {
            id = "empirecraft_law_realistic_age",
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/Icons/actor_traits/iconDeathMark",
            default_state = true
        });
        //阻止城市毁灭
        AssetManager.world_laws_library.add(empirecraft_law_prevent_city_destroy= new WorldLawAsset()
        {
            id = "empirecraft_law_prevent_city_destroy",
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconCity",
            default_state = true
        });
        //禁止称帝
        AssetManager.world_laws_library.add(empirecraft_law_ban_empire= new WorldLawAsset()
        {
            id = "empirecraft_law_ban_empire",
            group_id = "EmpireCraftCommonSetting",
            icon_path = "ui/icons/iconKingdom",
            default_state = false
        });
        
    }

    private static void Population20On(PlayerOptionData pOption)
    {
        world_law_civ_limit_population_50.toggle(false);
        WorldLawLibrary.world_law_civ_limit_population_100.toggle(false);
        RefreshLaws();
    }

    private static void Population50On(PlayerOptionData pOption)
    {
        world_law_civ_limit_population_20.toggle(false);
        WorldLawLibrary.world_law_civ_limit_population_100.toggle(false);
        RefreshLaws();
    }

    private static void Population100On(PlayerOptionData pOption)
    {
        world_law_civ_limit_population_20.toggle(false);
        world_law_civ_limit_population_50.toggle(false);
        RefreshLaws();
    }

    public static void RefreshLaws()
    {        
        ScrollWindow window = ScrollWindow.getCurrentWindow();
        WorldLawElement[] law_window = window.transform.parent.GetComponentsInChildren<WorldLawElement>();
        foreach (var law in law_window)
        {
            law.updateStatus();
        }
    }
}
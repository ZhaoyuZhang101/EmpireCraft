namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftWorldLawGroupLibrary
{
    public static void init()
    {
        AssetManager.world_law_groups.add(new WorldLawGroupAsset
        {
            id = "EmpireCraftCommonSetting",
            name = "empirecraft_laws_tab_common_setting",
            color = "#ff552bff"
        });
    }
}
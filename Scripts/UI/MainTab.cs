using System.Collections.Generic;
using EmpireCraft.Scripts.GodPowers;
using EmpireCraft.Scripts.UI.Windows;
using NCMS.Utils;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.utils;

namespace EmpireCraft.Scripts.UI;

internal static class MainTab
{
    public const string KINGDOM_TITLE_GROUP = "kingdom_title_group";
    public const string EMPIRE_GROUP = "empire_layer_group";
    public const string EMPIRE_FUNCTIONS = "empire_function_group";
    public const string PROVINCE_GROUP = "province_group";
    public static PowersTab tab;

    public static void Init()
    {
        // Create a tab with id "Example", title key "tab_example", description key "hotkey_tip_tab_other", and icon "ui/icons/iconSteam".
        // 创建一个id为"Example", 标题key为"set_empire_power", 描述key为"hotkey_tip_tab_other", 图标为"ui/icons/iconSteam"的标签页.
        tab = TabManager.CreateTab("EmpireTab", "empire_tab_name", "empire_tab_description",
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath+"/GameResources/TabEmpire.png"));
        // Set the layout of the tab. The layout is a list of strings, each string is a category. Names of each category are not important.
        // 设置标签页的布局. 布局是一个字符串列表, 每个字符串是一个分类. 每个分类的名字不重要.
        tab.SetLayout(new List<string>()
        {
            KINGDOM_TITLE_GROUP,
            EMPIRE_GROUP,
            EMPIRE_FUNCTIONS,
            PROVINCE_GROUP
        });
        // Add buttons to the tab.
        // 向标签页添加按钮.
        _addButtons();
        _createWindows();
        // Update the layout of the tab.
        // 更新标签页的布局.
        tab.UpdateLayout();
    }

    private static void _createWindows()
    {
        EmpireListWindow.CreateWindow(nameof(EmpireListWindow),
            nameof(EmpireListWindow) + "Title");
        EmpireWindow.CreateWindow(nameof(EmpireWindow),
            nameof(EmpireWindow) + "Title");
        KingdomTitleWindow.CreateWindow(nameof(KingdomTitleWindow),
            nameof(KingdomTitleWindow) + "Title");
        EmpireBeaurauWindow.CreateAndInit(nameof(EmpireBeaurauWindow));
    }

    private static void _addButtons()
    {
        TitleLayerToggle.init();
        PowerButton pb0 = FixFunctions.CreateToggleButton("title_layer",
                 SpriteTextureLoader.getSprite("ui/icons/iconCity"));
        tab.AddPowerButton(KINGDOM_TITLE_GROUP, pb0);

        CreateTitleButton.init();
        tab.AddPowerButton(KINGDOM_TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_title",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TitleCreate.png")));

        AddTitleButton.init();
        tab.AddPowerButton(KINGDOM_TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("add_title",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TitleAdd.png")));

        RemoveTitleButton.init();
        tab.AddPowerButton(KINGDOM_TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_title",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TitleRemove.png")));



        EmpireLayerToggle.init();
        PowerButton pb = FixFunctions.CreateToggleButton("empire_layer",
                 SpriteTextureLoader.getSprite("ui/icons/iconKingdom"));
        tab.AddPowerButton(EMPIRE_GROUP, pb);

        CreateEmpireButton.init();
        tab.AddPowerButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_empire",
                SpriteTextureLoader.getSprite("ui/icons/iconAlliance")));

        EmpireFormButton.init();
        tab.AddPowerButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("empire_form",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/ChineseCrown.png")));

        EmpireEnfeoffButton.init();
        tab.AddPowerButton(EMPIRE_GROUP, PowerButtonCreator.CreateGodPowerButton("empire_enfeoff",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/SplitAllUnderHeaven.png")));


        tab.AddPowerButton(EMPIRE_GROUP, PowerButtonCreator.CreateWindowButton("empire_list", nameof(EmpireListWindow),
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/icon.png")));

        PreventCityDestroyToggle.init();
        PowerButton pb2 = FixFunctions.CreateToggleButton("prevent_city_destroy",
                 SpriteTextureLoader.getSprite("ui/icons/iconCity"));
        tab.AddPowerButton(EMPIRE_FUNCTIONS, pb2);

        ProvinceLayerToggle.init();
        PowerButton pb3 = FixFunctions.CreateToggleButton("province_layer",
                 SpriteTextureLoader.getSprite("ui/icons/iconCity"));
        tab.AddPowerButton(PROVINCE_GROUP, pb3);

        CreateProvinceButton.init();
        tab.AddPowerButton(PROVINCE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_province",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TitleCreate.png")));

        AddProvinceButton.init();
        tab.AddPowerButton(PROVINCE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("add_province",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TitleAdd.png")));

        RemoveProvinceButton.init();
        tab.AddPowerButton(PROVINCE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_province",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TitleRemove.png")));



    }
}
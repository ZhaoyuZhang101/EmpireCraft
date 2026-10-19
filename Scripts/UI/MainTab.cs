using System.Collections.Generic;
using System;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GodPowers;
using EmpireCraft.Scripts.UI.Windows;
using NCMS.Utils;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.services;
using NeoModLoader.utils;
using UnityEngine;

namespace EmpireCraft.Scripts.UI;

internal static class MainTab
{
    public const string KINGDOM_TITLE_GROUP = "kingdom_title_group";
    // 两个标签页，各按类别分组(同原版标签页，组与组之间有分隔线)：
    //   帝国：法理与行政区 | 帝国核心 | 帝国 | 显示与工具
    //   文化与规划：文化理念等图层 | 城市规划 | 理念种子
    public const string TITLE_GROUP = "title_group";
    public const string EMPIRE_CORE_GROUP = "empire_core_group";
    public const string EMPIRE_GROUP = "empire_layer_group";
    public const string CULTURE_GROUP = "culture_ideology_group";
    public const string BUILD_GROUP = "city_building_group";
    public const string IDEOLOGY_SEED_GROUP = "ideology_seed_group";
    public const string CULTURE_BRUSH_GROUP = "culture_brush_group";
    public const string RESOURCE_BRUSH_GROUP = "resource_brush_group";
    public const string DISPLAY_GROUP = "display_tool_group";
    public const string EMPIRE_FUNCTIONS = "empire_function_group";
    public const string PROVINCE_GROUP = "province_group";
    // 列国征伐页(用户 2026-10-17 "感觉可以再分一页了")：军令、附庸、春秋战国
    public const string WAR_GROUP = "war_command_group";
    public const string VASSAL_GROUP = "vassal_group";
    public const string ZHOU_GROUP = "zhou_era_group";
    public static PowersTab tab;
    public static PowersTab cultureTab;
    public static PowersTab warTab;

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
            TITLE_GROUP,
            EMPIRE_CORE_GROUP,
            EMPIRE_GROUP,
            DISPLAY_GROUP,
        });
        cultureTab = TabManager.CreateTab("EmpireCultureTab", "empire_culture_tab_name",
            "empire_culture_tab_description",
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TabCulturePlanning.png"));
        cultureTab.SetLayout(new List<string>()
        {
            CULTURE_GROUP,
            BUILD_GROUP,
            IDEOLOGY_SEED_GROUP,
            CULTURE_BRUSH_GROUP,
            RESOURCE_BRUSH_GROUP,
        });
        warTab = TabManager.CreateTab("EmpireWarTab", "empire_war_tab_name", "empire_war_tab_description",
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/TabWarfare.png"));
        warTab.SetLayout(new List<string>()
        {
            WAR_GROUP,
            VASSAL_GROUP,
            ZHOU_GROUP,
        });
        // Add buttons to the tab.
        // 向标签页添加按钮.
        _addButtons();
        _createWindows();
        // Update the layout of the tab.
        // 更新标签页的布局.
        tab.UpdateLayout();
        cultureTab.UpdateLayout();
        warTab.UpdateLayout();
        // 当前加载器版本的 UpdateLayout 把所有按钮排成一整列，不分组也不画分隔线：
        // 在它排好的基础上按组重新排，组与组之间留空并画分隔线(同原版标签页)
        ApplyGroupedLayout(tab);
        ApplyGroupedLayout(cultureTab);
        ApplyGroupedLayout(warTab);
    }

    private static void _createWindows()
    {
        EmpireListWindow.CreateAndInit(nameof(EmpireListWindow), new Vector2(480f, 380f));
        CultureSpeciesPairWindow.CreateWindow(nameof(CultureSpeciesPairWindow),
            nameof(CultureSpeciesPairWindow) + "Title");
        // 帝国窗口换宽窗口(标题用 "<窗口id> Title" 约定 key)
        EmpireWindow.CreateAndInit(nameof(EmpireWindow), new Vector2(480f, 380f));
        // 法理窗口换宽窗口(标题用 "<窗口id> Title" 约定 key)
        KingdomTitleWindow.CreateAndInit(nameof(KingdomTitleWindow), new Vector2(480f, 380f));
        // Phase 16 加的 CityNameHistoryWindow 当时漏掉了这一步——每一个自定义
        // AutoLayoutWindow<T> 窗口都得在这里调一次 CreateWindow 完成注册（标题文本、
        // WindowLibrary/WindowToolbar 等原版系统需要用到的 WindowAsset 记录都是这一步
        // 建立的），跟有没有单独的顶栏按钮无关。没注册就直接 ScrollWindow.showWindow(...)
        // 打开，原版那些"窗口切换时通知所有监听者"的代码（HoveringBgIconManager.animate、
        // WindowToolbar.toggleShow 等）在自己内部查不到这个窗口 ID 对应的记录，就会各自
        // 抛空引用异常，把窗口内容还没来得及构建的那一步给打断——表现出来就是点了按钮以后
        // 窗口只有个空壳，内容一直是原版兜底的"未找到/开发中"占位页。
        CityNameHistoryWindow.CreateWindow(nameof(CityNameHistoryWindow),
            nameof(CityNameHistoryWindow) + "Title");
        // 官僚管理换成宽窗口：标题用 "<窗口id> Title"(带空格)约定 key，见下面 InstitutionWindow 的说明
        EmpireBeaurauWindow.CreateAndInit(nameof(EmpireBeaurauWindow), new Vector2(480f, 380f));
        ChangeUnitWindow.CreateWindow(nameof(ChangeUnitWindow),
            nameof(ChangeUnitWindow) + "Title");
        SpecificClanWindow.CreateWindow(nameof(SpecificClanWindow),
            nameof(SpecificClanWindow) + "Title");
        // 设置窗口换宽窗口(标题用 "<窗口id> Title" 约定 key)，内容只有两张卡，窗口矮一些
        EmpireSettingWindow.CreateAndInit(nameof(EmpireSettingWindow), new Vector2(480f, 180f));
        // 村镇小窗口(点地图上的镇村打开)
        SettlementWindow.CreateAndInit(nameof(SettlementWindow), new Vector2(480f, 200f));
        // 厂区小窗口(点地图上的厂区打开)
        FactoryWindow.CreateAndInit(nameof(FactoryWindow), new Vector2(480f, 200f));
        MetaColorWindow.CreateWindow(nameof(MetaColorWindow),
            nameof(MetaColorWindow) + "Title");
        // 政体、派系详情改宽窗口(标题用 "<窗口id> Title" 约定 key)
        RegimeWindow.CreateAndInit(nameof(RegimeWindow), new Vector2(480f, 380f));
        SpecificClanListWindow.CreateWindow(nameof(SpecificClanListWindow),
            nameof(SpecificClanListWindow) + "Title");
        FactionDetailWindow.CreateAndInit(nameof(FactionDetailWindow), new Vector2(480f, 380f));
        TraitsSelectWindow.CreateWindow(nameof(TraitsSelectWindow),
            nameof(TraitsSelectWindow) + "Title");
        AddFactionMemberWindow.CreateWindow(nameof(AddFactionMemberWindow),
            nameof(AddFactionMemberWindow) + "Title");
        AddFactionWindow.CreateWindow(nameof(AddFactionWindow),
            nameof(AddFactionWindow) + "Title");
        EmpireHistoryWindow.CreateAndInit(nameof(EmpireHistoryWindow), new Vector2(480f, 380f));
        // 世系图(历代君主窗口左栏打开)
        DynastyLineageWindow.CreateAndInit(nameof(DynastyLineageWindow), new Vector2(480f, 380f));
        EmpireCoreWindow.CreateAndInit(nameof(EmpireCoreWindow), new Vector2(480f, 380f));
        TianxiaWindow.CreateAndInit(nameof(TianxiaWindow), new Vector2(480f, 380f));
        BugReportWindow.CreateWindow(nameof(BugReportWindow),
            "bug_report_window_title");
        PureMapNoticeWindow.CreateWindow(nameof(PureMapNoticeWindow), "pure_map_notice_title");
        TerritoryFontWindow.CreateWindow(nameof(TerritoryFontWindow),
            "territory_font_window_title");
        OnlineUpdateWindow.CreateWindow(nameof(OnlineUpdateWindow),
            "online_update_window_title");
        // InstitutionWindow 换成了 AbstractWideWindow(真宽窗口)，注册方式跟其它
        // AutoLayoutWindow<T> 窗口不一样：CreateAndInit 只认 (窗口id, 尺寸)，标题固定用
        // "<窗口id> Title"(带空格)这个约定 key，不能像 CreateWindow 那样自己传 titleKey。
        InstitutionWindow.CreateAndInit(nameof(InstitutionWindow), new Vector2(480f, 380f));
        // 族谱树是单独的宽窗口，标题固定用 "<窗口id> Title" 约定 key(见上面 InstitutionWindow)。
        SpecificClanTreeWindow.CreateAndInit(nameof(SpecificClanTreeWindow), new Vector2(500f, 400f));
        // 支派(一姓的分支与宗子世系，见 ClanLineageWindow)
        ClanLineageWindow.CreateAndInit(nameof(ClanLineageWindow), new Vector2(480f, 380f));
        // 模组文化窗口（点击文化图层/原版文化链接时打开，见 CultureWindowRedirectPatch）
        CultureInfoWindow.CreateAndInit(nameof(CultureInfoWindow), new Vector2(480f, 380f));
        IdeologyInfoWindow.CreateAndInit(nameof(IdeologyInfoWindow), new Vector2(480f, 380f));
        TechWindow.CreateAndInit(nameof(TechWindow), new Vector2(480f, 380f));
        // 一国的军事(不属于帝国的王国点地图兵牌时、原版王国窗口"军事"页签打开)
        KingdomMilitaryWindow.CreateAndInit(nameof(KingdomMilitaryWindow), new Vector2(480f, 380f));
        // 国情(帝国图层上点不属于帝国的国家时打开)
        KingdomOverviewWindow.CreateAndInit(nameof(KingdomOverviewWindow), new Vector2(480f, 380f));
        CultureBooksWindow.CreateAndInit(nameof(CultureBooksWindow), new Vector2(480f, 380f));
        // 名臣名将名册(见 FamousTalentSystem)
        FamousTalentWindow.CreateAndInit(nameof(FamousTalentWindow), new Vector2(480f, 380f));
        SetWindowIcons();
    }

    // NML 建的窗口默认图标是 neomodloader；原版返回按钮会显示"上一个窗口"的图标，换成贴题的原版图标
    private static void SetWindowIcons()
    {
        var icons = new Dictionary<string, string>
        {
            [nameof(EmpireWindow)] = "iconKings",
            [nameof(EmpireSettingWindow)] = "iconOptions",
            [nameof(SettlementWindow)] = "iconCity",
            [nameof(FactoryWindow)] = "iconCity",
            [nameof(EmpireBeaurauWindow)] = "iconKingdom",
            [nameof(KingdomTitleWindow)] = "iconWorldLaws",
            [nameof(EmpireCoreWindow)] = "iconCity",
            [nameof(EmpireListWindow)] = "iconKingdomList",
            [nameof(EmpireHistoryWindow)] = "iconClock",
            [nameof(DynastyLineageWindow)] = "iconFamily",
            [nameof(ClanLineageWindow)] = "iconFamily",
            [nameof(InstitutionWindow)] = "iconKnowledge",
            [nameof(FactionDetailWindow)] = "iconFamily",
            [nameof(RegimeWindow)] = "iconBooks",
            [nameof(KingdomMilitaryWindow)] = "iconWar",
            [nameof(KingdomOverviewWindow)] = "iconKingdom",
            [nameof(FamousTalentWindow)] = "iconKings",
        };
        foreach (KeyValuePair<string, string> pair in icons)
        {
            WindowAsset asset = AssetManager.window_library.get(pair.Key);
            if (asset == null || SpriteTextureLoader.getSprite("ui/Icons/" + pair.Value) == null) continue;
            asset.icon_path = pair.Value;
        }
    }
    [Hotfixable]
    private static void _addButtons()
    {
        // ---- 法理与行政区：图层与法理、省份的增删，羁縻规划 ----
        PowerButton pb0 = FixFunctions.CreateLayerButton(MetaTypeExtension.KingdomTitle,
                 SpriteTextureLoader.getSprite("ui/icons/iconToolTitleLayer.png"));
        AddButton(TITLE_GROUP, pb0);

        CreateTitleButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_title",
                  SpriteTextureLoader.getSprite("ui/icons/iconToolTitleCreate.png")));

        AddTitleButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("add_title",
                SpriteTextureLoader.getSprite("ui/icons/iconToolTitleAdd.png")));

        RemoveTitleButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_title",
                SpriteTextureLoader.getSprite("ui/icons/iconToolTitleRemove.png")));

        CreateProvinceButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_province",
                SpriteTextureLoader.getSprite("ui/icons/iconToolProvinceCreate.png")));

        AddProvinceButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("add_province",
                SpriteTextureLoader.getSprite("ui/icons/iconToolProvinceAdd.png")));

        RemoveProvinceButton.init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_province",
                SpriteTextureLoader.getSprite("ui/icons/iconToolProvinceRemove.png")));

        JimiPolicyPower.Init();
        AddButton(TITLE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("jimi_policy",
                SpriteTextureLoader.getSprite("ui/icons/godpowers/jimi_policy")));

        // ---- 帝国核心 ----
        CreateEmpireCoreButton.init();
        // 天下图层：按天下给城上色(模式二按各家据有着色)，点城打开天下窗口(见 TianxiaMap)
        AddButton(EMPIRE_CORE_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.Tianxia,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/tianxia_layer"), maxOption: 2));
        AddButton(EMPIRE_CORE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_empire_core",
                SpriteTextureLoader.getSprite("ui/icons/iconToolCoreCreate.png")));

        AddTitleToEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("add_title_to_empire_core",
                SpriteTextureLoader.getSprite("ui/icons/iconToolCoreAddTitle.png")));

        RemoveTitleFromEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_title_from_empire_core",
                SpriteTextureLoader.getSprite("ui/icons/iconToolCoreRemoveTitle.png")));

        DestroyEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("destroy_empire_core",
                SpriteTextureLoader.getSprite("ui/icons/iconToolCoreDestroy.png")));

        // 天下：几个帝国核心并成联合天下、退出(见 TianxiaSystem；图层按钮在本组开头)
        TianxiaPowers.init();
        AddButton(EMPIRE_CORE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("tianxia_join",
                SpriteTextureLoader.getSprite("ui/icons/iconToolTianxiaJoin.png")));
        AddButton(EMPIRE_CORE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("tianxia_leave",
                SpriteTextureLoader.getSprite("ui/icons/iconToolTianxiaLeave.png")));

        // ---- 帝国：图层、称帝与解散、分封、列表、人物建国 ----
        PowerButton pb = FixFunctions.CreateLayerButton(MetaTypeExtension.Empire,
                 SpriteTextureLoader.getSprite("ui/icons/godpowers/empire_layer"));
        AddButton(EMPIRE_GROUP, pb);

        CreateEmpireButton.init();
        AddButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("create_empire",
                SpriteTextureLoader.getSprite("ui/icons/godpowers/create_empire")));

        EmpireFormButton.init();
        AddButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("empire_form",
                SpriteTextureLoader.getSprite("ui/icons/godpowers/empire_form")));


        RemoveEmpireButton.init();
        AddButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("remove_empire",
                SpriteTextureLoader.getSprite("ui/icons/godpowers/remove_empire")));

        EmpireEnfeoffButton.init();
        AddButton(EMPIRE_GROUP, PowerButtonCreator.CreateGodPowerButton("empire_enfeoff",
                SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/SplitAllUnderHeaven.png")));

        // ---- 列国征伐页：军令、附庸、春秋战国 ----
        // 军令：先点一国的城，再点目标城(本国的城固守，交战敌国的城集中攻打，见 CorpsCommandSystem)
        CorpsCommandPower.init();
        AddButton(WAR_GROUP, PowerButtonCreator.CreateGodPowerButton(CorpsCommandPower.Id,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/" + CorpsCommandPower.Id)));

        // 附庸：强制附庸(先点宗主再点目标)与解除附庸
        VassalPowerButtons.init();
        AddButton(VASSAL_GROUP, PowerButtonCreator.CreateGodPowerButton("force_vassal",
            SpriteTextureLoader.getSprite("ui/icons/godpowers/force_vassal")));
        AddButton(VASSAL_GROUP, PowerButtonCreator.CreateGodPowerButton("release_vassal",
            SpriteTextureLoader.getSprite("ui/icons/godpowers/release_vassal")));

        // 春秋战国(周制)：召集会盟、赐胙立霸主、推进时代
        ZhouEraPowers.init();
        AddButton(ZHOU_GROUP, PowerButtonCreator.CreateGodPowerButton("zhou_conference",
            SpriteTextureLoader.getSprite("ui/icons/godpowers/zhou_conference")));
        AddButton(ZHOU_GROUP, PowerButtonCreator.CreateGodPowerButton("zhou_bestow",
            SpriteTextureLoader.getSprite("ui/icons/godpowers/zhou_bestow")));
        AddButton(ZHOU_GROUP, PowerButtonCreator.CreateGodPowerButton("zhou_advance",
            SpriteTextureLoader.getSprite("ui/icons/godpowers/zhou_advance")));

        //帝国势力列表
        var empireListButon = PowerButtonCreator.CreateWindowButton("empire_list", nameof(EmpireListWindow),
            SpriteTextureLoader.getSprite("ui/icons/godpowers/empire_list"));
        AddButton(EMPIRE_GROUP, empireListButon);
        empireListButon._button.OnHover(() =>
        {
            Tooltip.show(empireListButon,"normal", new TooltipData()
            {
                tip_name = "show_empire_list",
                tip_description = "show_empire_list_description"
            });
        });
        empireListButon._button.OnHoverOut(Tooltip.hideTooltip);

        // 名臣名将名册
        var famousTalentsButton = PowerButtonCreator.CreateWindowButton("famous_talents", nameof(FamousTalentWindow),
            SpriteTextureLoader.getSprite("ui/icons/godpowers/famous_talents"));
        AddButton(EMPIRE_GROUP, famousTalentsButton);
        famousTalentsButton._button.OnHover(() =>
        {
            Tooltip.show(famousTalentsButton, "normal", new TooltipData()
            {
                tip_name = "show_famous_talents",
                tip_description = "show_famous_talents_description"
            });
        });
        famousTalentsButton._button.OnHoverOut(Tooltip.hideTooltip);
        // 册封名将、册封名臣(点人物直接封，再点晋一档)
        FamousTalentPowers.init();
        AddButton(EMPIRE_GROUP, PowerButtonCreator.CreateGodPowerButton(FamousTalentPowers.BestowGeneral,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/" + FamousTalentPowers.BestowGeneral)));
        AddButton(EMPIRE_GROUP, PowerButtonCreator.CreateGodPowerButton(FamousTalentPowers.BestowMinister,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/" + FamousTalentPowers.BestowMinister)));

        //宗族列表
        var specificClanListButton = PowerButtonCreator.CreateWindowButton("specific_clan_list",
            nameof(SpecificClanListWindow),
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/ui/specificClanIcon.png"));
        specificClanListButton._button.OnHover(() =>
            {
                Tooltip.show(specificClanListButton,"normal", new TooltipData()
                {
                    tip_name = "show_specific_clan_list",
                    tip_description = "show_specific_clan_list_description"
                });
            });
        specificClanListButton._button.OnHoverOut(Tooltip.hideTooltip);
        AddButton(EMPIRE_GROUP, specificClanListButton);
        
        ActorCreateKingdom.init();
        AddButton(EMPIRE_GROUP,
            PowerButtonCreator.CreateGodPowerButton("actor_create_kingdom",
               SpriteTextureLoader.getSprite("ui/icons/godpowers/actor_create_kingdom")));
        
        // ---- 文化与理念：图层与文化配置 ----
        // 原版文化图层的第二个入口：复用同一个 GodPower/OptionAsset，因此这里与
        // 原版按钮的区域模式、边框和名称开关始终同步。
        PowerButton cultureLayer = FixFunctions.CloneExistingLayerButton(MetaType.Culture,
                 SpriteTextureLoader.getSprite("ui/icons/iconCulture"));
        if (cultureLayer != null) AddButton(CULTURE_GROUP, cultureLayer);

        // 理念图层(独立于原版宗教图层)
        PowerButton ideologyLayer = FixFunctions.CreateLayerButton(MetaTypeExtension.Ideology,
                 SpriteTextureLoader.getSprite("ui/icons/iconBooks"));
        AddButton(CULTURE_GROUP, ideologyLayer);

        // 民族情绪图层
        PowerButton nationLayer = FixFunctions.CreateLayerButton(MetaTypeExtension.Nation,
                 SpriteTextureLoader.getSprite("ui/icons/iconNationLayer"));
        AddButton(CULTURE_GROUP, nationLayer);

        // 财富图层(红到绿：最穷到最富)
        PowerButton wealthLayer = FixFunctions.CreateLayerButton(MetaTypeExtension.Wealth,
                 SpriteTextureLoader.getSprite("ui/icons/godpowers/wealth_layer"), maxOption: 2);
        AddButton(CULTURE_GROUP, wealthLayer);

        // 教化图层(四方文化教化所及)、政令图层(行政控制力，鞭长莫及)
        PowerButton edificationLayer = FixFunctions.CreateLayerButton(MetaTypeExtension.Edification,
                 SpriteTextureLoader.getSprite("ui/icons/godpowers/edification_layer"), maxOption: 2);
        AddButton(CULTURE_GROUP, edificationLayer);
        PowerButton governanceLayer = FixFunctions.CreateLayerButton(MetaTypeExtension.Governance,
                 SpriteTextureLoader.getSprite("ui/icons/godpowers/governance_layer"), maxOption: 2);
        AddButton(CULTURE_GROUP, governanceLayer);

        // 矿产图层(图标用现成的资源图标)
        PowerButton mineralLayer = FixFunctions.CreateLayerButton(MetaTypeExtension.Mineral,
                 SpriteTextureLoader.getSprite("ui/icons/resources/iconRes_coal"));
        AddButton(CULTURE_GROUP, mineralLayer);

        // 城市图层：只显示模拟的城市(纯净地图)
        PowerButton cityViewLayer = FixFunctions.CreateLayerButton(MetaTypeExtension.CityView,
                 SpriteTextureLoader.getSprite("ui/icons/godpowers/city_view"), maxOption: 1);
        AddButton(CULTURE_GROUP, cityViewLayer);

        var cultureConfigButton = PowerButtonCreator.CreateWindowButton("culture_list", nameof(CultureSpeciesPairWindow),
            SpriteTextureLoader.getSprite("ui/icons/iconCulture"));
        AddButton(CULTURE_GROUP, cultureConfigButton);
        cultureConfigButton._button.OnHover(() =>
        {
            Tooltip.show(cultureConfigButton,"normal", new TooltipData()
            {
                tip_name = "show_culture_config",
                tip_description = "show_culture_config_description"
            });
        });
        cultureConfigButton._button.OnHoverOut(Tooltip.hideTooltip);

        // ---- 营建：立村、设镇、毁弃、改隶(见 SettlementPowers)；撤城 ----
        SettlementPowers.Init();
        foreach (string id in SettlementPowers.Ids)
            AddButton(BUILD_GROUP, PowerButtonCreator.CreateGodPowerButton(id, SpriteTextureLoader.getSprite("ui/icons/godpowers/" + id)));
        // 撤城：点一座城，百姓迁往本国最近的城，城市废弃
        CityDissolutionPower.init();
        AddButton(BUILD_GROUP,
            PowerButtonCreator.CreateGodPowerButton("dissolve_city",
                SpriteTextureLoader.getSprite("ui/icons/godpowers/dissolve_city")));

        // ---- 易俗：点城一键改换文化，本国最后一座城连国家制度一并改(见 CityCulturePower) ----
        // 举国 / 举朝易俗开关与复旧法理(见 RealmCulturePowers)放在各文化笔刷前面
        RealmCulturePowers.Init();
        foreach (string toggle in new[] { RealmCulturePowers.Kingdom, RealmCulturePowers.Empire })
            AddButton(CULTURE_BRUSH_GROUP, PowerButtonCreator.CreateToggleButton(toggle,
                SpriteTextureLoader.getSprite("ui/icons/godpowers/" + toggle)));
        AddButton(CULTURE_BRUSH_GROUP, PowerButtonCreator.CreateGodPowerButton(RealmCulturePowers.Restore,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/" + RealmCulturePowers.Restore)));
        foreach ((string powerId, string culture) in CityCulturePower.Init())
        {
            PowerButton brush = PowerButtonCreator.CreateGodPowerButton(powerId, EmpireCraft.Scripts.UI.Components.CultureIcons.Get(culture));
            TipButton brushTip = brush?.GetComponent<TipButton>();
            if (brushTip != null)
            {
                brushTip.textOnClick = powerId;
                brushTip.textOnClickDescription = powerId + "_description";
                brushTip.text_description_2 = "";
                brushTip.type = "normal";
            }
            AddButton(CULTURE_BRUSH_GROUP, brush);
        }

        // ---- 人口、矿藏笔刷(见 CityResourcePowers) ----
        foreach ((string powerId, string icon) in CityResourcePowers.Init())
        {
            PowerButton brush = PowerButtonCreator.CreateGodPowerButton(powerId, SpriteTextureLoader.getSprite(icon));
            TipButton brushTip = brush?.GetComponent<TipButton>();
            if (brushTip != null)
            {
                brushTip.textOnClick = powerId;
                brushTip.textOnClickDescription = powerId + "_description";
                brushTip.text_description_2 = "";
                brushTip.type = "normal";
            }
            AddButton(RESOURCE_BRUSH_GROUP, brush);
        }

        // ---- 理念种子 ----
        IdeologyTraitIcons.Register();
        IdeologySeedPower.Init();
        foreach (GeneralSystems.PartyIdeology ideology in Enum.GetValues(typeof(GeneralSystems.PartyIdeology)))
        {
            // 只按身份归属、不在民间流传的理念(皇权、门阀、外戚、宦官、宗亲，见 IdeologyFamilies.IsIdentityStance)，撒种子没有意义
            if (GeneralSystems.IdeologyFamilies.IsIdentityStance(ideology)) continue;
            string powerId = IdeologySeedPower.Id(ideology);
            PowerButton seedButton = PowerButtonCreator.CreateGodPowerButton(powerId,
                SpriteTextureLoader.getSprite(IdeologyTraitIcons.Path(ideology)));
            // NML 从原版 inspect 按钮克隆 GodPower 按钮。当前游戏版本的预制体自带
            // TipButton，若不覆盖就会一直显示它继承来的 "Normal Tooltip"。
            TipButton tip = seedButton?.GetComponent<TipButton>();
            if (tip != null)
            {
                tip.textOnClick = powerId;
                tip.textOnClickDescription = powerId + "_description";
                tip.text_description_2 = "";
                tip.type = "normal";
            }
            AddButton(IDEOLOGY_SEED_GROUP, seedButton);
        }

        // ---- 显示与工具：显示开关、调试、反馈与更新 ----
        SwitchRealNumButton.init();
        PowerButton pb4 = PowerButtonCreator.CreateToggleButton("real_num",
            SpriteTextureLoader.getSprite("ui/realNumToggle"));
        AddButton(DISPLAY_GROUP, pb4);

        SwitchSimpleNameplateButton.init();
        PowerButton simpleNameplateButton = PowerButtonCreator.CreateToggleButton("simple_nameplate",
            SpriteTextureLoader.getSprite("ui/icons/iconHideUI"));
        AddButton(DISPLAY_GROUP, simpleNameplateButton);

        // 地形浮雕：国色上叠不叠地形明暗
        SwitchTerrainReliefButton.init();
        PowerButton reliefButton = PowerButtonCreator.CreateToggleButton("terrain_relief",
            SpriteTextureLoader.getSprite("ui/icons/godpowers/terrain_relief"));
        AddButton(DISPLAY_GROUP, reliefButton);

        // 领土铭牌字体：自动(传统书体优先) / 游戏字体 / 本机任一字体
        var territoryFontButton = PowerButtonCreator.CreateWindowButton("territory_font_button",
            nameof(TerritoryFontWindow), SpriteTextureLoader.getSprite("ui/icons/godpowers/territory_font"));
        AddButton(DISPLAY_GROUP, territoryFontButton);
        territoryFontButton._button.OnHover(() =>
        {
            Tooltip.show(territoryFontButton, "normal", new TooltipData
            {
                tip_name = "territory_font_button",
                tip_description = "territory_font_button_description"
            });
        });
        territoryFontButton._button.OnHoverOut(Tooltip.hideTooltip);

        // 帝国视图下是否叠加显示同盟
        SwitchEmpireAllianceButton.init();
        PowerButton empireAllianceButton = PowerButtonCreator.CreateToggleButton("empire_show_alliance",
            SpriteTextureLoader.getSprite("plots/icons/plot_alliance_create"));
        AddButton(DISPLAY_GROUP, empireAllianceButton);

        // 原版性能面板：打开原版自带的计时窗口
        VanillaBenchmarkButton.init();
        AddButton(DISPLAY_GROUP,
            PowerButtonCreator.CreateGodPowerButton("vanilla_benchmark",
                SpriteTextureLoader.getSprite("ui/icons/iconHideUI")));

        DebugFrontLineButton.init();
        AddButton(DISPLAY_GROUP,
            PowerButtonCreator.CreateGodPowerButton("debug_frontline",
                SpriteTextureLoader.getSprite("ui/icons/iconWar")));

        var bugReportButton = PowerButtonCreator.CreateWindowButton("bug_report_button", nameof(BugReportWindow),
            GetOriginalBugIcon());
        AddButton(DISPLAY_GROUP, bugReportButton);
        bugReportButton._button.OnHover(() =>
        {
            Tooltip.show(bugReportButton, "normal", new TooltipData
            {
                tip_name = "bug_report_button",
                tip_description = "bug_report_button_description"
            });
        });
        bugReportButton._button.OnHoverOut(Tooltip.hideTooltip);

        Sprite updateIcon = SpriteTextureLoader.getSprite("ui/icons/iconArrowUP") ??
                            SpriteTextureLoader.getSprite("ui/icons/iconSteam");
        var updateButton = PowerButtonCreator.CreateWindowButton(
            "online_update_button",
            nameof(OnlineUpdateWindow),
            updateIcon);
        AddButton(DISPLAY_GROUP, updateButton);
        EmpireCraft.Scripts.UI.Components.UpdateBadge.Attach(updateButton);
        updateButton._button.OnHover(() =>
        {
            Tooltip.show(updateButton, "normal", new TooltipData
            {
                tip_name = "online_update_button",
                tip_description = "online_update_button_description"
            });
        });
        updateButton._button.OnHoverOut(Tooltip.hideTooltip);
    }

    // 各组的按钮(按加入顺序)，用于自行分组排版
    private static readonly List<KeyValuePair<string, PowerButton>> _buttonsInOrder = new();

    private static void AddButton(string group, PowerButton button)
    {
        bool cultureSide = group is CULTURE_GROUP or BUILD_GROUP or IDEOLOGY_SEED_GROUP or CULTURE_BRUSH_GROUP;
        bool warSide = group is WAR_GROUP or VASSAL_GROUP or ZHOU_GROUP;
        (warSide ? warTab : cultureSide ? cultureTab : tab).AddPowerButton(group, button);
        if (button != null) _buttonsInOrder.Add(new KeyValuePair<string, PowerButton>(group, button));
    }

    // 原版 PowersTab.sortButtons 按子物体顺序排版(子物体数量变化时重排)，会覆盖任何手动坐标；
    // 但它认得名字以 "_line" 开头的子物体：遇到就收尾当前列、画一条分隔线再继续。
    // 所以这里按组调整子物体顺序，并在组与组之间插入一条 "_line" 分隔线，交给原版排版
    private static void ApplyGroupedLayout(PowersTab target)
    {
        try
        {
            Transform parent = target.transform;
            int index = int.MaxValue;
            foreach (var pair in _buttonsInOrder)
                if (pair.Value != null && pair.Value.transform.parent == parent)
                    index = Mathf.Min(index, pair.Value.transform.GetSiblingIndex());
            if (index == int.MaxValue) return;
            GameObject template = FindVanillaLine(target);
            string current = null;
            int lineCount = 0;
            foreach (var pair in _buttonsInOrder)
            {
                if (pair.Value == null || pair.Value.transform.parent != parent) continue;
                if (current != null && pair.Key != current)
                {
                    GameObject line = CreateLine(parent, template, lineCount++);
                    line.transform.SetSiblingIndex(index++);
                }
                current = pair.Key;
                pair.Value.transform.SetSiblingIndex(index++);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 标签页 {target?.name} 分组排版失败: {exception.Message}");
        }
    }

    // 从原版标签页里找一条现成的分隔线做模板，外观与原版一致
    private static GameObject FindVanillaLine(PowersTab target)
    {
        foreach (PowersTab other in Resources.FindObjectsOfTypeAll<PowersTab>())
        {
            if (other == null || other == tab || other == cultureTab || other == warTab || other == target) continue;
            foreach (Transform child in other.transform)
                if (child.name.StartsWith("_line")) return child.gameObject;
        }
        return null;
    }

    private static GameObject CreateLine(Transform parent, GameObject template, int number)
    {
        GameObject line;
        if (template != null)
        {
            line = UnityEngine.Object.Instantiate(template, parent);
        }
        else
        {
            line = new GameObject("_line", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            line.transform.SetParent(parent, false);
            var image = line.GetComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.16f, 0.14f, 0.12f, 0.9f);
            image.raycastTarget = false;
            line.GetComponent<RectTransform>().sizeDelta = new Vector2(2f, 48f);
        }
        line.name = $"_line_empirecraft_{number}";
        var lineImage = line.GetComponent<UnityEngine.UI.Image>();
        if (lineImage != null) lineImage.enabled = true;
        line.SetActive(true);
        return line;
    }

    private static Sprite GetOriginalBugIcon()
    {
        Sprite originalIcon = SpriteTextureLoader.getSprite("ui/icons/iconDebug");
        if (originalIcon != null) return originalIcon;

        string[] exactNames = { "iconDebug", "iconBug", "icon_bug", "bug" };
        Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (string name in exactNames)
        {
            foreach (Sprite sprite in sprites)
            {
                if (string.Equals(sprite.name, name, StringComparison.OrdinalIgnoreCase)) return sprite;
            }
        }
        return SpriteTextureLoader.getSprite("ui/icons/iconMainInfo");
    }
}

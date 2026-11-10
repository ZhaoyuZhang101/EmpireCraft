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
    // 五个标签页(用户 2026-11-10 "godpower好乱"后重排)，每页按组排，组与组之间画分隔线(同原版标签页)。
    // 每组先放本组的图层、再放本组的工具；按钮多的笔刷组放在页尾，前面的短组不用滚动就看得到：
    //   法理与帝国：王国法理 | 帝国法理 | 天下 | 帝国 | 行政区 | 显示 | 工具
    //   列国与人物：邦交 | 征伐 | 春秋战国 | 人物 | 穿越
    //   文化与宗教：文化 | 宗教 | 科技 | 易俗
    //   营建：城池 | 村镇 | 厂区 | 道路 | 无人区 | 高原 | 民生 | 矿藏
    //   理念种子：理念图层 | 各派种子
    // 排版只看这里各页的组顺序，跟 _addButtons 里加按钮的先后无关
    // ---- 法理与帝国 ----
    public const string KINGDOM_TITLE_GROUP = "kingdom_title_group";
    public const string EMPIRE_CORE_GROUP = "empire_core_group";
    public const string TIANXIA_GROUP = "tianxia_group";
    public const string EMPIRE_GROUP = "empire_layer_group";
    public const string PROVINCE_GROUP = "province_group";
    public const string DISPLAY_GROUP = "display_tool_group";
    public const string TOOL_GROUP = "debug_tool_group";
    // ---- 列国与人物 ----
    public const string DIPLOMACY_GROUP = "diplomacy_vassal_group";
    public const string WAR_GROUP = "war_command_group";
    public const string ZHOU_GROUP = "zhou_era_group";
    public const string PEOPLE_GROUP = "people_group";
    public const string TRANSMIGRANT_GROUP = "transmigrant_group";
    // ---- 文化与宗教 ----
    public const string CULTURE_GROUP = "culture_ideology_group";
    public const string RELIGION_GROUP = "religion_group";
    public const string TECH_GROUP = "tech_rollback_group";
    public const string CULTURE_BRUSH_GROUP = "culture_brush_group";
    // ---- 营建 ----
    public const string CITY_GROUP = "city_group";
    public const string SETTLEMENT_GROUP = "city_building_group";
    public const string FACTORY_GROUP = "factory_group";
    public const string ROAD_GROUP = "road_group";
    public const string WILD_GROUP = "wilderness_group";
    public const string PLATEAU_GROUP = "plateau_brush_group";
    public const string LIVELIHOOD_GROUP = "livelihood_group";
    public const string RESOURCE_BRUSH_GROUP = "resource_brush_group";
    // ---- 理念种子(用户 2026-11-10 "把理念种子单独列一栏") ----
    public const string IDEOLOGY_GROUP = "ideology_layer_group";
    public const string IDEOLOGY_SEED_GROUP = "ideology_seed_group";

    public static PowersTab tab;
    public static PowersTab warTab;
    public static PowersTab cultureTab;
    public static PowersTab buildTab;
    public static PowersTab ideologyTab;

    // 每页的组顺序；组归哪一页也由这里定
    private static readonly List<(PowersTab tab, string[] groups)> _tabs = new();
    private static readonly Dictionary<string, PowersTab> _tabOfGroup = new();

    public static void Init()
    {
        tab = CreateTab("EmpireTab", "empire_tab_name", "empire_tab_description", "TabEmpire.png",
            KINGDOM_TITLE_GROUP, EMPIRE_CORE_GROUP, TIANXIA_GROUP, EMPIRE_GROUP, PROVINCE_GROUP, DISPLAY_GROUP, TOOL_GROUP);
        warTab = CreateTab("EmpireWarTab", "empire_war_tab_name", "empire_war_tab_description", "TabWarfare.png",
            DIPLOMACY_GROUP, WAR_GROUP, ZHOU_GROUP, PEOPLE_GROUP, TRANSMIGRANT_GROUP);
        cultureTab = CreateTab("EmpireCultureTab", "empire_culture_tab_name", "empire_culture_tab_description", "TabCulturePlanning.png",
            CULTURE_GROUP, RELIGION_GROUP, TECH_GROUP, CULTURE_BRUSH_GROUP);
        buildTab = CreateTab("EmpireBuildTab", "empire_build_tab_name", "empire_build_tab_description", "TabBuild.png",
            CITY_GROUP, SETTLEMENT_GROUP, FACTORY_GROUP, ROAD_GROUP, WILD_GROUP, PLATEAU_GROUP, LIVELIHOOD_GROUP, RESOURCE_BRUSH_GROUP);
        ideologyTab = CreateTab("EmpireIdeologyTab", "empire_ideology_tab_name", "empire_ideology_tab_description", "TabIdeologySeed.png",
            IDEOLOGY_GROUP, IDEOLOGY_SEED_GROUP);
        _addButtons();
        _createWindows();
        // 当前加载器版本的 UpdateLayout 把所有按钮排成一整列，不分组也不画分隔线：
        // 在它排好的基础上按组重新排，组与组之间留空并画分隔线(同原版标签页)
        foreach ((PowersTab powersTab, string[] groups) in _tabs)
        {
            powersTab.UpdateLayout();
            ApplyGroupedLayout(powersTab, groups);
        }
    }

    private static PowersTab CreateTab(string id, string titleKey, string descriptionKey, string iconFile, params string[] groups)
    {
        PowersTab created = TabManager.CreateTab(id, titleKey, descriptionKey,
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/" + iconFile));
        created.SetLayout(new List<string>(groups));
        foreach (string group in groups) _tabOfGroup[group] = created;
        _tabs.Add((created, groups));
        return created;
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
        // 宗族与天下宗族名册：卷册宽窗口(支派、族谱树都并在宗族窗口里)，标题用 "<窗口id> Title" 约定 key
        SpecificClanWindow.CreateAndInit(nameof(SpecificClanWindow), new Vector2(480f, 380f));
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
        SpecificClanListWindow.CreateAndInit(nameof(SpecificClanListWindow), new Vector2(480f, 380f));
        FactionDetailWindow.CreateAndInit(nameof(FactionDetailWindow), new Vector2(480f, 380f));
        TraitsSelectWindow.CreateWindow(nameof(TraitsSelectWindow),
            nameof(TraitsSelectWindow) + "Title");
        AddFactionMemberWindow.CreateWindow(nameof(AddFactionMemberWindow),
            nameof(AddFactionMemberWindow) + "Title");
        AddFactionWindow.CreateAndInit(nameof(AddFactionWindow), new Vector2(480f, 380f));
        EmpireHistoryWindow.CreateAndInit(nameof(EmpireHistoryWindow), new Vector2(480f, 380f));
        // 世系图(历代君主窗口左栏打开)
        DynastyLineageWindow.CreateAndInit(nameof(DynastyLineageWindow), new Vector2(480f, 380f));
        EmpireCoreWindow.CreateAndInit(nameof(EmpireCoreWindow), new Vector2(480f, 380f));
        TianxiaWindow.CreateAndInit(nameof(TianxiaWindow), new Vector2(480f, 380f));
        // 加载器模组列表里「设置」按钮打开的分类设置窗口(见 ModSettingsPatch)
        ModSettingsWindow.CreateAndInit(nameof(ModSettingsWindow), new Vector2(480f, 380f));
        BugReportWindow.CreateWindow(nameof(BugReportWindow),
            "bug_report_window_title");
        PureMapNoticeWindow.CreateWindow(nameof(PureMapNoticeWindow), "pure_map_notice_title");
        // 世界地图(原版新世界窗口的模板按钮打开，见 WorldMapTemplatePatch)
        WorldMapWindow.CreateAndInit(nameof(WorldMapWindow), new Vector2(480f, 380f));
        TerritoryFontWindow.CreateWindow(nameof(TerritoryFontWindow),
            "territory_font_window_title");
        OnlineUpdateWindow.CreateWindow(nameof(OnlineUpdateWindow),
            "online_update_window_title");
        // InstitutionWindow 换成了 AbstractWideWindow(真宽窗口)，注册方式跟其它
        // AutoLayoutWindow<T> 窗口不一样：CreateAndInit 只认 (窗口id, 尺寸)，标题固定用
        // "<窗口id> Title"(带空格)这个约定 key，不能像 CreateWindow 那样自己传 titleKey。
        InstitutionWindow.CreateAndInit(nameof(InstitutionWindow), new Vector2(480f, 380f));
        // 模组文化窗口（点击文化图层/原版文化链接时打开，见 CultureWindowRedirectPatch）
        CultureInfoWindow.CreateAndInit(nameof(CultureInfoWindow), new Vector2(480f, 380f));
        // 宗教窗口(点宗教图层、原版宗教链接时打开，见 ReligionWindowRedirectPatch)与天下宗教名册
        ReligionInfoWindow.CreateAndInit(nameof(ReligionInfoWindow), new Vector2(480f, 380f));
        WorldReligionsWindow.CreateAndInit(nameof(WorldReligionsWindow), new Vector2(480f, 380f));
        // 战争窗口(原版战争链接、战争徽标、战争列表打开，见 WarWindowRedirectPatch)
        WarInfoWindow.CreateAndInit(nameof(WarInfoWindow), new Vector2(480f, 380f));
        IdeologyInfoWindow.CreateAndInit(nameof(IdeologyInfoWindow), new Vector2(480f, 380f));
        TechWindow.CreateAndInit(nameof(TechWindow), new Vector2(480f, 380f));
        // 一国的军事(不属于帝国的王国点地图兵牌时、原版王国窗口"军事"页签打开)
        KingdomMilitaryWindow.CreateAndInit(nameof(KingdomMilitaryWindow), new Vector2(480f, 380f));
        // 国情(帝国图层上点不属于帝国的国家时打开)
        KingdomOverviewWindow.CreateAndInit(nameof(KingdomOverviewWindow), new Vector2(480f, 380f));
        // 一国的律法(国情窗口"去处"打开；帝国治下的国家转到帝国窗口的律法页)
        KingdomLawWindow.CreateAndInit(nameof(KingdomLawWindow), new Vector2(480f, 380f));
        CultureBooksWindow.CreateAndInit(nameof(CultureBooksWindow), new Vector2(480f, 380f));
        // 名臣名将名册(见 FamousTalentSystem)
        FamousTalentWindow.CreateAndInit(nameof(FamousTalentWindow), new Vector2(480f, 380f));
        // 元老院名册(见 TransmigrantSystem)
        SenateWindow.CreateAndInit(nameof(SenateWindow), new Vector2(480f, 380f));
        // 外交(见 DiplomacyLedger、DiplomacyActions)
        DiplomacyWindow.CreateAndInit(nameof(DiplomacyWindow), new Vector2(480f, 380f));
        // 市场汇总(见 CommodityMarketSystem、MarketWindow)
        MarketWindow.CreateAndInit(nameof(MarketWindow), new Vector2(480f, 380f));
        // 无人区(地图上点无人区、营建页「无人区」按钮打开，见 WildernessWindow)；各图层点地块没接的、点在无人区上的交给它
        WildernessWindow.CreateAndInit(nameof(WildernessWindow), new Vector2(480f, 380f));
        WildernessWindow.HookClicks();
        SetWindowIcons();
    }

    // NML 建的窗口默认图标是 neomodloader；原版返回按钮会显示"上一个窗口"的图标，换成贴题的原版图标
    private static void SetWindowIcons()
    {
        var icons = new Dictionary<string, string>
        {
            [nameof(EmpireWindow)] = "iconKings",
            [nameof(WorldMapWindow)] = "iconKingdomZones",
            [nameof(EmpireSettingWindow)] = "iconOptions",
            [nameof(SettlementWindow)] = "iconCity",
            [nameof(FactoryWindow)] = "iconCity",
            [nameof(EmpireBeaurauWindow)] = "iconKingdom",
            [nameof(KingdomTitleWindow)] = "iconWorldLaws",
            [nameof(EmpireCoreWindow)] = "iconCity",
            [nameof(EmpireListWindow)] = "iconKingdomList",
            [nameof(EmpireHistoryWindow)] = "iconClock",
            [nameof(DynastyLineageWindow)] = "iconFamily",
            [nameof(SpecificClanWindow)] = "iconFamily",
            [nameof(SpecificClanListWindow)] = "iconFamilyList",
            [nameof(InstitutionWindow)] = "iconKnowledge",
            [nameof(FactionDetailWindow)] = "iconFamily",
            [nameof(RegimeWindow)] = "iconBooks",
            [nameof(KingdomMilitaryWindow)] = "iconWar",
            [nameof(KingdomOverviewWindow)] = "iconKingdom",
            [nameof(KingdomLawWindow)] = "iconWorldLaws",
            [nameof(FamousTalentWindow)] = "iconKings",
            [nameof(SenateWindow)] = "iconKings",
            [nameof(DiplomacyWindow)] = "iconKingdom",
            [nameof(MarketWindow)] = "iconMoney",
            [nameof(ReligionInfoWindow)] = "iconReligion",
            [nameof(WorldReligionsWindow)] = "iconReligion",
            [nameof(WarInfoWindow)] = "iconWar",
            [nameof(WildernessWindow)] = "iconLand",
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
        _addEmpireButtons();
        _addWarButtons();
        _addCultureButtons();
        _addBuildButtons();
        _addIdeologyButtons();
    }

    // ======== 法理与帝国 ========
    private static void _addEmpireButtons()
    {
        // ---- 王国法理：图层、建立、加入、兼属、移出 ----
        AddButton(KINGDOM_TITLE_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.KingdomTitle,
            SpriteTextureLoader.getSprite("ui/icons/iconToolTitleLayer.png")));
        CreateTitleButton.init();
        AddButton(KINGDOM_TITLE_GROUP, Power("create_title", "ui/icons/iconToolTitleCreate.png"));
        AddTitleButton.init();
        AddButton(KINGDOM_TITLE_GROUP, Power("add_title", "ui/icons/iconToolTitleAdd.png"));
        // 兼属(城另挂一个王国法理、王国法理另挂一个帝国法理、帝国法理另挂一个天下，见 DeJureShareSystem)
        DeJureSharePowers.init();
        AddButton(KINGDOM_TITLE_GROUP, Power(DeJureSharePowers.TitleShare, "ui/icons/iconToolTitleShare.png"));
        RemoveTitleButton.init();
        AddButton(KINGDOM_TITLE_GROUP, Power("remove_title", "ui/icons/iconToolTitleRemove.png"));

        // ---- 帝国法理(帝国核心)：图层(法理图层的帝国法理显示)、建立、加入、兼属、移出、销毁 ----
        CreateEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP, Power(DeJureSharePowers.CoreLayer, "ui/icons/iconToolCoreLayer.png"));
        AddButton(EMPIRE_CORE_GROUP, Power("create_empire_core", "ui/icons/iconToolCoreCreate.png"));
        AddTitleToEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP, Power("add_title_to_empire_core", "ui/icons/iconToolCoreAddTitle.png"));
        AddButton(EMPIRE_CORE_GROUP, Power(DeJureSharePowers.CoreShare, "ui/icons/iconToolCoreShare.png"));
        RemoveTitleFromEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP, Power("remove_title_from_empire_core", "ui/icons/iconToolCoreRemoveTitle.png"));
        DestroyEmpireCoreButton.init();
        AddButton(EMPIRE_CORE_GROUP, Power("destroy_empire_core", "ui/icons/iconToolCoreDestroy.png"));

        // ---- 天下：图层(按天下给城上色，模式二按各家据有着色，点城打开天下窗口，见 TianxiaMap)、并入、兼属、退出 ----
        AddButton(TIANXIA_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.Tianxia,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/tianxia_layer"), maxOption: 2));
        TianxiaPowers.init();
        AddButton(TIANXIA_GROUP, Power("tianxia_join", "ui/icons/iconToolTianxiaJoin.png"));
        AddButton(TIANXIA_GROUP, Power(DeJureSharePowers.TianxiaShare, "ui/icons/iconToolTianxiaShare.png"));
        AddButton(TIANXIA_GROUP, Power("tianxia_leave", "ui/icons/iconToolTianxiaLeave.png"));

        // ---- 帝国：图层与同盟叠加、加入、称帝、移除、分封、帝国列表 ----
        AddButton(EMPIRE_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.Empire,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/empire_layer")));
        // 帝国视图下是否叠加显示同盟
        SwitchEmpireAllianceButton.init();
        AddButton(EMPIRE_GROUP, PowerButtonCreator.CreateToggleButton("empire_show_alliance",
            SpriteTextureLoader.getSprite("plots/icons/plot_alliance_create")));
        CreateEmpireButton.init();
        AddButton(EMPIRE_GROUP, Power("create_empire", "ui/icons/godpowers/create_empire"));
        EmpireFormButton.init();
        AddButton(EMPIRE_GROUP, Power("empire_form", "ui/icons/godpowers/empire_form"));
        RemoveEmpireButton.init();
        AddButton(EMPIRE_GROUP, Power("remove_empire", "ui/icons/godpowers/remove_empire"));
        EmpireEnfeoffButton.init();
        AddButton(EMPIRE_GROUP, PowerButtonCreator.CreateGodPowerButton("empire_enfeoff",
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/SplitAllUnderHeaven.png")));
        AddButton(EMPIRE_GROUP, WindowButton("empire_list", nameof(EmpireListWindow),
            SpriteTextureLoader.getSprite("ui/icons/godpowers/empire_list"), "show_empire_list"));

        // ---- 行政区：政令图层(行政控制力，鞭长莫及)，省份的增删，羁縻规划 ----
        AddButton(PROVINCE_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.Governance,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/governance_layer"), maxOption: 2));
        CreateProvinceButton.init();
        AddButton(PROVINCE_GROUP, Power("create_province", "ui/icons/iconToolProvinceCreate.png"));
        AddProvinceButton.init();
        AddButton(PROVINCE_GROUP, Power("add_province", "ui/icons/iconToolProvinceAdd.png"));
        RemoveProvinceButton.init();
        AddButton(PROVINCE_GROUP, Power("remove_province", "ui/icons/iconToolProvinceRemove.png"));
        JimiPolicyPower.Init();
        AddButton(PROVINCE_GROUP, Power("jimi_policy", "ui/icons/godpowers/jimi_policy"));

        // ---- 显示：人口数字、铭牌、声音 ----
        SwitchRealNumButton.init();
        AddButton(DISPLAY_GROUP, PowerButtonCreator.CreateToggleButton("real_num",
            SpriteTextureLoader.getSprite("ui/realNumToggle")));
        SwitchSimpleNameplateButton.init();
        AddButton(DISPLAY_GROUP, PowerButtonCreator.CreateToggleButton("simple_nameplate",
            SpriteTextureLoader.getSprite("ui/icons/iconHideUI")));
        // 领土铭牌字体：自动(传统书体优先) / 游戏字体 / 本机任一字体
        AddButton(DISPLAY_GROUP, WindowButton("territory_font_button", nameof(TerritoryFontWindow),
            SpriteTextureLoader.getSprite("ui/icons/godpowers/territory_font"), "territory_font_button"));
        // 战声：镜头拉近到交战处时听得到厮杀声(见 BattleSound)
        SwitchBattleSoundButton.init();
        AddButton(DISPLAY_GROUP, PowerButtonCreator.CreateToggleButton("battle_sound",
            SpriteTextureLoader.getSprite("ui/icons/godpowers/battle_sound")));
        // 文明乐：镜头拉近到某一系文明的地盘时放那一系的曲子(见 CivMusic)
        SwitchCivMusicButton.init();
        AddButton(DISPLAY_GROUP, PowerButtonCreator.CreateToggleButton("civ_music",
            SpriteTextureLoader.getSprite("ui/icons/godpowers/civ_music")));

        // ---- 工具：性能面板、战线调试、反馈与更新 ----
        // 原版性能面板：打开原版自带的计时窗口
        VanillaBenchmarkButton.init();
        AddButton(TOOL_GROUP, Power("vanilla_benchmark", "ui/icons/iconClock"));
        DebugFrontLineButton.init();
        AddButton(TOOL_GROUP, Power("debug_frontline", "ui/icons/iconWar"));
        AddButton(TOOL_GROUP, WindowButton("bug_report_button", nameof(BugReportWindow), GetOriginalBugIcon(), "bug_report_button"));
        Sprite updateIcon = SpriteTextureLoader.getSprite("ui/icons/iconArrowUP") ??
                            SpriteTextureLoader.getSprite("ui/icons/iconSteam");
        PowerButton updateButton = WindowButton("online_update_button", nameof(OnlineUpdateWindow), updateIcon, "online_update_button");
        AddButton(TOOL_GROUP, updateButton);
        EmpireCraft.Scripts.UI.Components.UpdateBadge.Attach(updateButton);
    }

    // ======== 列国与人物 ========
    private static void _addWarButtons()
    {
        // ---- 邦交：外交(先点发起国、再点对象国，打开两国的外交界面)，强制附庸(先点宗主再点目标)与解除附庸 ----
        DiplomacyPower.Init();
        AddButton(DIPLOMACY_GROUP, Power(DiplomacyPower.Id, "ui/icons/godpowers/" + DiplomacyPower.Id));
        VassalPowerButtons.init();
        AddButton(DIPLOMACY_GROUP, Power("force_vassal", "ui/icons/godpowers/force_vassal"));
        AddButton(DIPLOMACY_GROUP, Power("release_vassal", "ui/icons/godpowers/release_vassal"));

        // ---- 征伐：太平时期的驻军兵牌；军令(先点一国的城，再点目标城：本国的城固守，交战敌国的城集中攻打，见 CorpsCommandSystem) ----
        SwitchGarrisonButton.init();
        AddButton(WAR_GROUP, PowerButtonCreator.CreateToggleButton("show_garrison",
            SpriteTextureLoader.getSprite("ui/icons/iconWar")));
        CorpsCommandPower.init();
        AddButton(WAR_GROUP, Power(CorpsCommandPower.Id, "ui/icons/godpowers/" + CorpsCommandPower.Id));

        // ---- 春秋战国(周制)：召集会盟、赐胙立霸主、推进时代 ----
        ZhouEraPowers.init();
        AddButton(ZHOU_GROUP, Power("zhou_conference", "ui/icons/godpowers/zhou_conference"));
        AddButton(ZHOU_GROUP, Power("zhou_bestow", "ui/icons/godpowers/zhou_bestow"));
        AddButton(ZHOU_GROUP, Power("zhou_advance", "ui/icons/godpowers/zhou_advance"));

        // ---- 人物：名臣名将名册、册封名将与名臣(点人物直接封，再点晋一档)、宗族列表 ----
        AddButton(PEOPLE_GROUP, WindowButton("famous_talents", nameof(FamousTalentWindow),
            SpriteTextureLoader.getSprite("ui/icons/godpowers/famous_talents"), "show_famous_talents"));
        FamousTalentPowers.init();
        AddButton(PEOPLE_GROUP, Power(FamousTalentPowers.BestowGeneral, "ui/icons/godpowers/" + FamousTalentPowers.BestowGeneral));
        AddButton(PEOPLE_GROUP, Power(FamousTalentPowers.BestowMinister, "ui/icons/godpowers/" + FamousTalentPowers.BestowMinister));
        AddButton(PEOPLE_GROUP, WindowButton("specific_clan_list", nameof(SpecificClanListWindow),
            SpriteLoadUtils.LoadSingleSprite(ModClass._declare.FolderPath + "/GameResources/ui/specificClanIcon.png"),
            "show_specific_clan_list"));

        // ---- 穿越：五百二十一名穿越者连同城、船、枪与物资凭空出现(见 TransmigrantSystem)；元老院名册 ----
        EmpireCraft.Scripts.GeneralSystems.TransmigrantSystem.InitPower();
        string landingId = EmpireCraft.Scripts.GeneralSystems.TransmigrantSystem.PowerId;
        AddButton(TRANSMIGRANT_GROUP, Power(landingId, "ui/icons/godpowers/" + landingId, setTip: true));
        AddButton(TRANSMIGRANT_GROUP, WindowButton("transmigrant_senate", nameof(SenateWindow),
            SpriteTextureLoader.getSprite("ui/icons/godpowers/transmigrant_senate"), "show_transmigrant_senate"));
    }

    // ======== 文化与宗教 ========
    private static void _addCultureButtons()
    {
        // ---- 文化：文化图层、民族情绪图层、教化图层(四方文化教化所及)、文化配置 ----
        // 原版文化图层的第二个入口：复用同一个 GodPower/OptionAsset，因此这里与
        // 原版按钮的区域模式、边框和名称开关始终同步。
        AddButton(CULTURE_GROUP, FixFunctions.CloneExistingLayerButton(MetaType.Culture,
            SpriteTextureLoader.getSprite("ui/icons/iconCulture")));
        AddButton(CULTURE_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.Nation,
            SpriteTextureLoader.getSprite("ui/icons/iconNationLayer")));
        AddButton(CULTURE_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.Edification,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/edification_layer"), maxOption: 2));
        AddButton(CULTURE_GROUP, WindowButton("culture_list", nameof(CultureSpeciesPairWindow),
            SpriteTextureLoader.getSprite("ui/icons/iconCultureList") ?? SpriteTextureLoader.getSprite("ui/icons/iconCulture"),
            "show_culture_config"));

        // ---- 宗教：宗教图层(原版图层，铭牌换成模组文字铭牌，见 EmpireCraftNamePlateLibrary)、天下宗教名册、指定圣地 ----
        AddButton(RELIGION_GROUP, FixFunctions.CloneExistingLayerButton(MetaType.Religion,
            SpriteTextureLoader.getSprite("ui/icons/iconReligion")));
        AddButton(RELIGION_GROUP, WindowButton("world_religions", nameof(WorldReligionsWindow),
            SpriteTextureLoader.getSprite("ui/icons/iconReligionList") ?? SpriteTextureLoader.getSprite("ui/icons/iconReligion"),
            "show_world_religions"));
        // 指定圣地：点一座城，它成为它所奉宗教的圣地(见 HolyCityPower)
        HolyCityPower.init();
        AddButton(RELIGION_GROUP, Power(HolyCityPower.Id, "ui/icons/iconToolHolyCity.png"));

        // ---- 科技：点城退回上一个时代；举世退回原始(点两下确认)，见 TechRollbackPowers ----
        TechRollbackPowers.Init();
        foreach (string powerId in TechRollbackPowers.Ids)
            AddButton(TECH_GROUP, Power(powerId, "ui/icons/godpowers/" + powerId, setTip: true));

        // ---- 易俗：举国 / 举朝易俗开关与复旧法理(见 RealmCulturePowers)，再是各文化笔刷：点城一键改换文化，
        //      本国最后一座城连国家制度一并改(见 CityCulturePower) ----
        RealmCulturePowers.Init();
        foreach (string toggle in new[] { RealmCulturePowers.Kingdom, RealmCulturePowers.Empire })
            AddButton(CULTURE_BRUSH_GROUP, PowerButtonCreator.CreateToggleButton(toggle,
                SpriteTextureLoader.getSprite("ui/icons/godpowers/" + toggle)));
        AddButton(CULTURE_BRUSH_GROUP, Power(RealmCulturePowers.Restore, "ui/icons/godpowers/" + RealmCulturePowers.Restore));
        foreach ((string powerId, string culture) in CityCulturePower.Init())
            AddButton(CULTURE_BRUSH_GROUP, Power(powerId, EmpireCraft.Scripts.UI.Components.CultureIcons.Get(culture), setTip: true));
    }

    // ======== 营建 ========
    private static void _addBuildButtons()
    {
        SettlementPowers.Init();
        var placed = new HashSet<string>();
        PowerButton SettlementButton(string powerId)
        {
            placed.Add(powerId);
            return Power(powerId, "ui/icons/godpowers/" + powerId);
        }
        // ---- 城池：城市图层(只显示模拟的城市，纯净地图)、建城、点小人建国、迁城、营宫殿、城貌、撤城 ----
        AddButton(CITY_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.CityView,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/city_view"), maxOption: 1));
        AddButton(CITY_GROUP, SettlementButton(SettlementPowers.NewCity));
        ActorCreateKingdom.init();
        AddButton(CITY_GROUP, Power("actor_create_kingdom", "ui/icons/godpowers/actor_create_kingdom"));
        foreach (string id in new[] { SettlementPowers.Relocate, SettlementPowers.Palace, SettlementPowers.Restyle })
            AddButton(CITY_GROUP, SettlementButton(id));
        // 撤城：点一座城，百姓迁往本国最近的城，城市废弃
        CityDissolutionPower.init();
        AddButton(CITY_GROUP, Power("dissolve_city", "ui/icons/godpowers/dissolve_city"));

        // ---- 村镇：立村、设镇、筑关、毁弃、改隶(见 SettlementPowers) ----
        foreach (string id in new[] { SettlementPowers.Village, SettlementPowers.Town, SettlementPowers.Fort,
                     SettlementPowers.Raze, SettlementPowers.Assign })
            AddButton(SETTLEMENT_GROUP, SettlementButton(id));

        // ---- 厂区：小、中、大 ----
        foreach (string id in new[] { SettlementPowers.FactorySmall, SettlementPowers.FactoryMedium, SettlementPowers.FactoryLarge })
            AddButton(FACTORY_GROUP, SettlementButton(id));

        // ---- 道路：绘制、擦除(笔刷)，连路(先后点两座城)，见 RoadPowers ----
        RoadPowers.Init();
        foreach (string id in RoadPowers.Ids)
            AddButton(ROAD_GROUP, Power(id, "ui/icons/godpowers/" + id));

        // ---- 无人区：中立区、开发、无人区法理、还给法理，无人区名册(全图各片、改名，见 WildernessWindow) ----
        foreach (string id in new[] { SettlementPowers.WildNeutral, SettlementPowers.WildDevelop,
                     SettlementPowers.WildTitle, SettlementPowers.WildTitleClear })
            AddButton(WILD_GROUP, SettlementButton(id));
        AddButton(WILD_GROUP, WindowButton("wild_window", nameof(WildernessWindow),
            SpriteTextureLoader.getSprite("ui/icons/godpowers/wild_window"), "show_wild_window"));

        // ---- 高原画笔：高寒、黄土、草原三种高原，松手后新画的边上自动起山脊坡地(见 PlateauPowers) ----
        PlateauPowers.Init();
        foreach ((string id, string _, string icon) in PlateauPowers.Brushes)
            AddButton(PLATEAU_GROUP, Power(id, "ui/icons/" + icon, setTip: true));

        // ---- 民生：财富图层(红到绿：最穷到最富)、商品市场图层，人口增减与繁荣笔刷 ----
        // ---- 矿藏：矿产图层，各矿藏笔刷与清除矿藏(见 CityResourcePowers) ----
        AddButton(LIVELIHOOD_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.Wealth,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/wealth_layer"), maxOption: 3));
        AddButton(LIVELIHOOD_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.Market,
            SpriteTextureLoader.getSprite("ui/icons/godpowers/market_layer"), maxOption: 3));
        AddButton(RESOURCE_BRUSH_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.Mineral,
            SpriteTextureLoader.getSprite("ui/icons/resources/iconRes_coal")));
        foreach ((string powerId, string icon) in CityResourcePowers.Init())
        {
            bool livelihood = powerId is CityResourcePowers.PopulationAdd or CityResourcePowers.PopulationRemove or CityResourcePowers.Flourish;
            AddButton(livelihood ? LIVELIHOOD_GROUP : RESOURCE_BRUSH_GROUP, Power(powerId, icon, setTip: true));
        }
        // SettlementPowers 以后新加、上面没分到组的权能先归村镇一组，免得漏掉
        foreach (string id in SettlementPowers.Ids)
            if (!placed.Contains(id)) AddButton(SETTLEMENT_GROUP, SettlementButton(id));
    }

    // ======== 理念种子 ========
    private static void _addIdeologyButtons()
    {
        // 理念图层(独立于原版宗教图层)
        AddButton(IDEOLOGY_GROUP, FixFunctions.CreateLayerButton(MetaTypeExtension.Ideology,
            SpriteTextureLoader.getSprite("ui/icons/iconBooks")));
        IdeologyTraitIcons.Register();
        IdeologySeedPower.Init();
        foreach (GeneralSystems.PartyIdeology ideology in Enum.GetValues(typeof(GeneralSystems.PartyIdeology)))
        {
            // 只按身份归属、不在民间流传的理念(皇权、门阀、外戚、宦官、宗亲，见 IdeologyFamilies.IsIdentityStance)，撒种子没有意义
            if (GeneralSystems.IdeologyFamilies.IsIdentityStance(ideology)) continue;
            string powerId = IdeologySeedPower.Id(ideology);
            AddButton(IDEOLOGY_SEED_GROUP, Power(powerId,
                SpriteTextureLoader.getSprite(IdeologyTraitIcons.Path(ideology)), setTip: true));
        }
    }

    // 各组的按钮(按加入顺序)，用于自行分组排版
    private static readonly List<KeyValuePair<string, PowerButton>> _buttonsInOrder = new();

    private static void AddButton(string group, PowerButton button)
    {
        if (button == null) return;
        if (!_tabOfGroup.TryGetValue(group, out PowersTab target))
        {
            LogService.LogWarning($"[EmpireCraft] 按钮 {button.name} 的组 {group} 没有归到任何标签页");
            return;
        }
        target.AddPowerButton(group, button);
        _buttonsInOrder.Add(new KeyValuePair<string, PowerButton>(group, button));
    }

    private static PowerButton Power(string powerId, string iconPath, bool setTip = false) =>
        Power(powerId, SpriteTextureLoader.getSprite(iconPath), setTip);

    private static PowerButton Power(string powerId, Sprite icon, bool setTip = false)
    {
        PowerButton button = PowerButtonCreator.CreateGodPowerButton(powerId, icon);
        if (setTip) SetTip(button, powerId);
        return button;
    }

    // 打开窗口的按钮；悬停提示用 <tipKey>、<tipKey>_description
    private static PowerButton WindowButton(string id, string windowId, Sprite icon, string tipKey)
    {
        PowerButton button = PowerButtonCreator.CreateWindowButton(id, windowId, icon);
        if (button == null) return null;
        button._button.OnHover(() =>
        {
            Tooltip.show(button, "normal", new TooltipData
            {
                tip_name = tipKey,
                tip_description = tipKey + "_description"
            });
        });
        button._button.OnHoverOut(Tooltip.hideTooltip);
        return button;
    }

    // NML 从原版 inspect 按钮克隆 GodPower 按钮。当前游戏版本的预制体自带
    // TipButton，若不覆盖就会一直显示它继承来的 "Normal Tooltip"：改成这个权能的名字与说明(<id>、<id>_description)
    private static void SetTip(PowerButton button, string powerId)
    {
        TipButton tip = button?.GetComponent<TipButton>();
        if (tip == null) return;
        tip.textOnClick = powerId;
        tip.textOnClickDescription = powerId + "_description";
        tip.text_description_2 = "";
        tip.type = "normal";
    }

    // 原版 PowersTab.sortButtons 按子物体顺序排版(子物体数量变化时重排)，会覆盖任何手动坐标；
    // 但它认得名字以 "_line" 开头的子物体：遇到就收尾当前列、画一条分隔线再继续。
    // 所以这里按本页的组顺序(组内按加入先后)调整子物体顺序，并在组与组之间插入一条 "_line" 分隔线，交给原版排版
    private static void ApplyGroupedLayout(PowersTab target, string[] groups)
    {
        try
        {
            Transform parent = target.transform;
            var buttons = new List<KeyValuePair<string, PowerButton>>();
            int index = int.MaxValue;
            foreach (var pair in _buttonsInOrder)
            {
                if (pair.Value == null || pair.Value.transform.parent != parent) continue;
                buttons.Add(pair);
                index = Mathf.Min(index, pair.Value.transform.GetSiblingIndex());
            }
            if (buttons.Count == 0) return;
            GameObject template = FindVanillaLine(target);
            string current = null;
            int lineCount = 0;
            foreach (string group in groups)
            {
                foreach (var pair in buttons)
                {
                    if (pair.Key != group) continue;
                    if (current != null && current != group)
                    {
                        GameObject line = CreateLine(parent, template, lineCount++);
                        line.transform.SetSiblingIndex(index++);
                    }
                    current = group;
                    pair.Value.transform.SetSiblingIndex(index++);
                }
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
            if (other == null || other == target || _tabs.Exists(entry => entry.tab == other)) continue;
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

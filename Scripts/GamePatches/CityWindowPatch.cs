using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.UI.Components;
using EmpireCraft.Scripts.UI.Windows;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Data;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.GamePatches;
public class CityWindowPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public static CityWindow _window { get; set; }
    public static SimpleButton limitToggle { get; set; }
    public static TextInput limitInput { get; set; }


    public void Initialize()
    {
        new Harmony(nameof(startShowingWindow)).Patch(
            AccessTools.Method(typeof(CityWindow), nameof(CityWindow.startShowingWindow)),
            postfix: new HarmonyMethod(GetType(), nameof(startShowingWindow))
        );
        new Harmony(nameof(showStatsRows)).Patch(
            AccessTools.Method(typeof(CityWindow), nameof(CityWindow.showStatsRows)),
            prefix: new HarmonyMethod(GetType(), nameof(showStatsRows))
        );
    }
    public static bool showStatsRows(CityWindow __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        City metaObject = __instance.meta_object;
        if (metaObject == null)
            return false;
        if (metaObject.kingdom.isNeutral())
            __instance.village_title.setKeyAndUpdate("village_dying");
        else
            __instance.village_title.setKeyAndUpdate("village");
        __instance.tryShowPastNames();
        __instance.showStatRow("founded", (object) metaObject.getFoundedDate(), MetaType.None, -1L, "iconAge", (string) null, (TooltipDataGetter) null);
        __instance.tryToShowActor("founder", metaObject.data.founder_id, metaObject.data.founder_name, pIconPath: "actor_traits/iconStupid");
        __instance.tryShowPastRulers();
        __instance.tryToShowActor("village_statistics_leader", pObject: metaObject.leader, pIconPath: "iconLeaders");
        if (metaObject.hasLeader())
            __instance.showStatRow("ruler_money", (object) metaObject.GetMoney(), "#43FF43", pIconPath: "iconMoney");
        CityValueSnapshot cityValue = metaObject.GetCityStrategicValue();
        string cityValueText = $"{cityValue.Total}/100" +
                               (cityValue.IsIsolated ? $" ({LM.Get("city_value_isolated")})" : "");
        __instance.showStatRow("city_value", cityValueText, "#FFD34E", pIconPath: "iconKings");
        __instance.showStatRow("tax", (object) metaObject.kingdom.GetTaxRate().ToString("0%"), "#43FF43", pIconPath: "kingdom_traits/kingdom_trait_tax_rate_local_low");
        __instance.showStatRow("tribute", (object) metaObject.kingdom.GetTaxRate().ToString("0%"), "#43FF43", pIconPath: "kingdom_traits/kingdom_trait_tax_rate_tribute_high");
        __instance.tryToShowActor("king", pObject: metaObject.kingdom.king, pIconPath: "iconKings");
        __instance.tryToShowMetaSpecies("founder_species", metaObject.getFounderSpecies()?.id);
        CityLandReport land = LandEconomySystem.GetReport(metaObject);
        string landSystem = land.MarketOpen
            ? LM.Get("city_land_system_open")
            : LM.Get("city_land_system_closed");
        __instance.showStatRow("city_land_system", landSystem,
            land.MarketOpen ? "#66D98A" : "#F3C34A", pIconPath: "iconKings");
        __instance.showStatRow("city_household_migration",
            LM.Get(land.MigrationBlocked ? "city_household_migration_blocked" : "city_household_migration_free"),
            land.MigrationBlocked ? "#E66B66" : "#66D98A", pIconPath: "iconChildren");
        __instance.showStatRow("city_landless_population", $"{land.LandlessPopulationRatio:P0}",
            land.LandlessPopulationRatio >= LandEconomySystem.RebellionLandlessThreshold ? "#E66B66" : "#B8C6CC",
            pIconPath: "iconChildren");
        if (CityPopulationSystem.AbstractPopulationEnabled) ShowPopulationRows(__instance, metaObject);
        ShowZoneRows(__instance, metaObject);
        UrbanEmploymentReport employment = UrbanEmploymentSystem.GetReport(metaObject);
        __instance.showStatRow("city_production_stage",
            LM.Get($"urban_production_stage_{employment.Stage}"), "#F3C34A", pIconPath: "iconMoney");
        __instance.showStatRow("city_workshop_employment",
            $"{employment.Employed}/{employment.Capacity}", "#7FD8EA", pIconPath: "iconMoney");
        __instance.showStatRow("city_job_sources",
            string.Format(LM.Get("city_job_sources_format"), employment.Buildings, employment.Merchants,
                employment.RecentVoyages, employment.Factories), "#B8C6CC", pIconPath: "iconMoney");
        __instance.showStatRow("city_worker_population", employment.Workers.ToString(), "#7FD8EA",
            pIconPath: "iconChildren");
        __instance.showStatRow("city_job_seekers", employment.AvailableResidents.ToString(), "#B8C6CC",
            pIconPath: "iconChildren");
        for (int index = 0; index < land.Holders.Count; index++)
        {
            CityLandHolderView holder = land.Holders[index];
            __instance.showStatRow("city_land_owner",
                $"{index + 1}. {holder.Name}  {holder.Share:0.#}%",
                holder.Institutional ? "#F3C34A" : "#7FD8EA", pIconPath: "iconKings");
        }
        return false;
    }
    // 城市规划：各用途的区块数，以及矿场、伐木场的最高等级
    private static void ShowZoneRows(CityWindow window, City city)
    {
        var parts = new global::System.Collections.Generic.List<string>();
        foreach (ZoneUse use in new[] { ZoneUse.Farm, ZoneUse.Industry, ZoneUse.Residential, ZoneUse.Commerce,
                     ZoneUse.Military, ZoneUse.Reserve })
        {
            int count = ZonePlanSystem.Count(city, use);
            if (count > 0) parts.Add($"{ZonePlanSystem.UseName(use)} {count}");
        }
        if (parts.Count > 0)
            window.showStatRow("city_zone_plan", string.Join(" / ", parts), "#B8C6CC", pIconPath: "iconCity");
        int mine = 0, lumber = 0;
        if (city.buildings != null)
            foreach (Building building in city.buildings)
            {
                if (building?.asset == null || building.isUnderConstruction()) continue;
                int tier = IndustryBuildingSystem.Tier(building.asset);
                if (IndustryBuildingSystem.IsMine(building.asset) && tier > mine) mine = tier;
                if (IndustryBuildingSystem.IsLumber(building.asset) && tier > lumber) lumber = tier;
            }
        float herd = CityPopulationSystem.Get(city)?.herd ?? 0f;
        float capacity = AnimalHusbandrySystem.HerdCapacity(city);
        if (capacity > 0f || herd > 0f)
            window.showStatRow("city_herd", string.Format(LM.Get("city_herd_format"), Mathf.RoundToInt(herd),
                Mathf.RoundToInt(capacity)), "#B8E07A", pIconPath: "iconPopulation");
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            int ruins = CityConstructionSystem.CountRuins(city);
            if (ruins > 0)
                window.showStatRow("city_ruins", string.Format(LM.Get("city_ruins_format"), ruins,
                    CityConstructionSystem.LastRuinsCleared.TryGetValue(city, out int cleared) ? cleared : 0),
                    "#B8A27A", pIconPath: "iconMoney");
        }
        var deposits = MineralResourceSystem.Deposits(city);
        if (deposits.Count > 0)
        {
            var names = new global::System.Collections.Generic.List<string>();
            foreach ((string id, bool minable, float remaining, int cooldown) in deposits)
            {
                if (cooldown > 0)
                    names.Add($"<color=#E66B66>{string.Format(LM.Get("deposit_cooldown"), LM.Get(id), cooldown)}</color>");
                else if (minable) names.Add($"{LM.Get(id)} {remaining:P0}");
                else names.Add($"<color=#8A8A8A>{LM.Get(id)}</color>");
            }
            window.showStatRow("city_deposits", string.Join(" ", names), "#E6C27A", pIconPath: "iconMoney");
        }
        float shortage = IdeologyPopulationSystem.GoodsShortage(city);
        if (shortage > 0f)
            window.showStatRow("city_leather_shortage", $"{shortage:P0}", "#E6A166", pIconPath: "iconMoney");
        if (mine > 0 || lumber > 0)
            window.showStatRow("city_industry_tier", string.Format(LM.Get("city_industry_tier_format"), mine, lumber),
                "#E6A166", pIconPath: "iconMoney");
    }

    // 无小人模式的城市人口面板：规模与户数、年增长率、粮食、就业、在外军团，以及阶层、文化、物种构成
    private static void ShowPopulationRows(CityWindow window, City city)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data == null) return;
        int people = city.getPopulationPeople();
        int households = CityPopulationSystem.Households(city);
        window.showStatRow("city_pop_scale", string.Format(LM.Get("city_pop_scale_format"), people, households,
            CityPopulationSystem.PeoplePerSlot(city)), "#F3C34A", pIconPath: "iconPopulation");
        CityPopulationSystem.GrowthFactors factors = CityPopulationSystem.GetGrowthFactors(city, data);
        float growth = factors.BirthRate - factors.DeathRate;
        window.showStatRow("city_pop_growth", $"{growth:+0.0%;-0.0%;0.0%}",
            growth >= 0f ? "#66D98A" : "#E66B66", pIconPath: "iconPopulation");
        string food = string.Format(LM.Get("city_pop_food_format"), factors.FoodPerCapita,
            factors.Famine ? LM.Get("city_pop_famine") : "");
        window.showStatRow("city_pop_food", food, factors.Famine ? "#E66B66" : "#B8C6CC", pIconPath: "iconPopulation");
        if (data.last_workforce > 0f)
            window.showStatRow("city_pop_employment", $"{Mathf.Clamp01(data.last_jobs / data.last_workforce):P0}",
                "#7FD8EA", pIconPath: "iconMoney");
        if (MarketSystem.Blockaded(city))
            window.showStatRow("city_market_status", data.siege_famine_months > 0
                ? string.Format(LM.Get("city_market_siege_famine"), data.siege_famine_months)
                : LM.Get("city_market_blockaded"), "#E66B66", pIconPath: "iconWar");
        else if (city.kingdom != null && city.kingdom.hasEnemies())
            window.showStatRow("city_market_status", LM.Get("city_market_embargo"), "#E6A166", pIconPath: "iconWar");
        window.showStatRow("city_market_prices", string.Format(LM.Get("city_market_prices_format"),
                MarketSystem.Price(city, MarketSystem.Good.Food), MarketSystem.Price(city, MarketSystem.Good.Wood),
                MarketSystem.Price(city, MarketSystem.Good.Stone), MarketSystem.Price(city, MarketSystem.Good.Metal)),
            "#F3C34A", pIconPath: "iconMoney");
        if (data.last_market_bought > 0f || data.last_market_sold > 0f || data.market_sold_month > 0f)
            window.showStatRow("city_market_trade", string.Format(LM.Get("city_market_trade_format"),
                    Mathf.RoundToInt(data.last_market_bought),
                    Mathf.RoundToInt(Mathf.Max(data.last_market_sold, data.market_sold_month))),
                "#7FD8EA", pIconPath: "iconMoney");
        if (data.last_income > 0f)
            window.showStatRow("city_pop_income", Mathf.RoundToInt(data.last_income).ToString(), "#C8E66A",
                pIconPath: "iconMoney");
        if (data.last_tax_income > 0f)
            window.showStatRow("city_pop_tax", Mathf.RoundToInt(data.last_tax_income).ToString(), "#43FF43",
                pIconPath: "iconMoney");
        if (data.levied > 0f)
            window.showStatRow("city_pop_legions", Mathf.RoundToInt(data.levied).ToString(), "#E6A166",
                pIconPath: "iconWar");
        window.showStatRow("city_pop_classes", Top(CityPopulationSystem.GetClassCounts(city),
            socialClass => socialClass.ToTranslate(), 4), "#B8C6CC", pIconPath: "iconKings");
        window.showStatRow("city_pop_cultures", Top(CityPopulationSystem.GetCultureCounts(city),
            culture => string.IsNullOrEmpty(culture) ? "-" : culture.GetCultureTranslate(), 3), "#B8C6CC",
            pIconPath: "iconCulture");
        window.showStatRow("city_pop_species", Top(CityPopulationSystem.GetSpeciesCounts(city),
            species => LM.Get(species), 3), "#B8C6CC", pIconPath: "iconPopulation");
    }

    private static string Top<TKey>(Dictionary<TKey, float> counts, Func<TKey, string> name, int take)
    {
        float total = counts.Values.Sum();
        if (total <= 0f) return "-";
        return string.Join("  ", counts.OrderByDescending(pair => pair.Value).Take(take)
            .Select(pair => $"{name(pair.Key)} {pair.Value / total:P0}"));
    }

    public static void startShowingWindow(CityWindow __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        _window = __instance;
        AddSettingTab();
    }

    public static void AddSettingTab()
    {
        City city = SelectedMetas.selected_city;
        Transform space = _window.tabs.transform.Find("space (1)");
        if (space != null) 
        {
            GameObject.Destroy(space.gameObject);
        }
        if (!_window.tabs._tabs.Any(p => p.name == "city_setting"))
        {
            SimpleWindowTab simpleWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            simpleWindowTab.Setup("city_setting", _window.scroll_window, CreateCitySettingContent());
            if (city.GetMaxPopulationLimitStats())
            {
                limitToggle.Text.text = LM.Get("On");
                limitInput.input.text = city.GetMaxPopulation().ToString();
            }
            else
            {
                limitToggle.Text.text = LM.Get("Off");
                limitInput.input.text = LM.Get("population_limit_text"); 
            }
        }
        else
        {
            if (city.GetMaxPopulationLimitStats())
            {
                limitToggle.Text.text = LM.Get("On");
                limitInput.input.text = city.GetMaxPopulation().ToString();
            }
            else
            {
                limitToggle.Text.text = LM.Get("Off");
                limitInput.input.text = LM.Get("population_limit_text");
            }
        }

    }

    [Hotfixable]
    public static List<Transform> CreateCitySettingContent()
    {
        City city = SelectedMetas.selected_city;
        List<Transform> city_setting_contents = new List<Transform>();

        // 创建标题
        SimpleText title = GameObject.Instantiate(SimpleText.Prefab);
        title.Setup(LM.Get("population_limit_title"), TextAnchor.MiddleCenter, new Vector2(180, 30));
        title.background.enabled = false;
        // 强制标题大小
        var titleLayout = title.gameObject.AddComponent<LayoutElement>();
        titleLayout.minWidth = titleLayout.preferredWidth = 180;
        titleLayout.minHeight = titleLayout.preferredHeight = 30;
        title.transform.localScale = Vector3.one;


        RectTransform hori = UIHelper.CreateHorizontalLayoutContainer("hori", new Vector2(300, 40));
        hori.transform.localScale = Vector3.one;
        var horiLayout = hori.gameObject.AddComponent<LayoutElement>();
        horiLayout.minWidth = horiLayout.preferredWidth = 300;
        horiLayout.minHeight = horiLayout.preferredHeight = 40;
        // 创建开关
        limitToggle = GameObject.Instantiate(SimpleButton.Prefab);

        // 强制开关大小
        var limitToggleLayout = limitToggle.gameObject.AddComponent<LayoutElement>();
        limitToggleLayout.minWidth = limitToggleLayout.preferredWidth = 70;
        limitToggleLayout.minHeight = limitToggleLayout.preferredHeight = 30;
        limitToggle.transform.SetParent(hori.transform);

        // 创建输入框
        limitInput = GameObject.Instantiate(TextInput.Prefab);
        limitInput.Setup(city.GetMaxPopulationLimitStats()? city.GetMaxPopulation().ToString(): LM.Get("population_limit_text"), newValue => InputCityPopLimit(newValue, limitInput));
        limitInput.SetSize(new Vector2(210, 30));
        limitInput.transform.localScale = Vector3.one;
        limitToggle.Setup(() => LimitationToggleSwitch(limitToggle, limitInput), SpriteTextureLoader.getSprite("ui/icons/iconArrowUP"), city.GetMaxPopulationLimitStats() ? LM.Get("On") : LM.Get("Off"), pSize: new Vector2(65, 30));
        // 强制输入框大小
        var inputLayout = limitInput.gameObject.AddComponent<LayoutElement>();
        inputLayout.minWidth = inputLayout.preferredWidth = 210;
        inputLayout.minHeight = inputLayout.preferredHeight = 30;
        limitInput.transform.SetParent(hori.transform);

        city_setting_contents.Add(title.transform);
        city_setting_contents.Add(hori.transform);

        // 城市改名 + 文化地名历史不再直接摊在设置面板里了（之前那版每次点开设置都要
        // 把整份可编辑列表都画出来，用户明确要求挪到单独的窗口）。这里只留一个入口
        // 按钮，点了以后按跟法理窗口"文化地名历史"标签页同一套规范打开的独立窗口
        // （CityNameHistoryWindow：文化标签 + 可编辑输入框 + 删除按钮，一行一条）。
        SimpleText nameHistoryTitle = GameObject.Instantiate(SimpleText.Prefab);
        nameHistoryTitle.Setup(LM.Get("city_setting_name_history_title"), TextAnchor.MiddleCenter, new Vector2(180, 30));
        nameHistoryTitle.background.enabled = false;
        var nameHistoryTitleLayout = nameHistoryTitle.gameObject.AddComponent<LayoutElement>();
        nameHistoryTitleLayout.minWidth = nameHistoryTitleLayout.preferredWidth = 180;
        nameHistoryTitleLayout.minHeight = nameHistoryTitleLayout.preferredHeight = 30;
        nameHistoryTitle.transform.localScale = Vector3.one;

        SimpleButton openNameHistoryButton = GameObject.Instantiate(SimpleButton.Prefab);
        openNameHistoryButton.Setup(OpenCityNameHistoryWindow, SpriteTextureLoader.getSprite("ui/icons/iconCulture"),
            LM.Get("city_setting_open_name_history"), pSize: new Vector2(200, 30));
        openNameHistoryButton.transform.localScale = Vector3.one;
        var openNameHistoryButtonLayout = openNameHistoryButton.gameObject.AddComponent<LayoutElement>();
        openNameHistoryButtonLayout.minWidth = openNameHistoryButtonLayout.preferredWidth = 200;
        openNameHistoryButtonLayout.minHeight = openNameHistoryButtonLayout.preferredHeight = 30;

        city_setting_contents.Add(nameHistoryTitle.transform);
        city_setting_contents.Add(openNameHistoryButton.transform);

        return city_setting_contents;
    }

    public static void OpenCityNameHistoryWindow()
    {
        City city = SelectedMetas.selected_city;
        if (city == null) return;
        ScrollWindow.showWindow(nameof(CityNameHistoryWindow));
    }

    public static void InputCityPopLimit(string pName, TextInput textInput)
    {
        City city = SelectedMetas.selected_city;
        int limitNum = int.TryParse(pName, out int num) ? num : -1;
        if (city == null) return;
        bool is_open = city.GetMaxPopulationLimitStats();
        if (is_open && limitNum > 0)
        {
            city.SetMaxPopulation(limitNum);
            textInput.input.text = limitNum.ToString();
        }
        else
        {
            textInput.input.text = city.GetMaxPopulation().ToString();
        }
    }

    public static void LimitationToggleSwitch(SimpleButton button, TextInput textInput)
    {
        City city = SelectedMetas.selected_city;
        bool limitationStats = city.GetMaxPopulationLimitStats();
        if (limitationStats) 
        {
            button.Text.text = LM.Get("Off");
            city.CloseMaxPopulationLimit();
            textInput.input.text = LM.Get("population_limit_text");
        } else
        {
            button.Text.text = LM.Get("On");
            city.OpenMaxPopulationLimit();
            textInput.input.text = city.GetMaxPopulation().ToString();
        }
    }
}

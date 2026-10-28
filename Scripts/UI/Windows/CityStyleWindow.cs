using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 城池小窗口(用户 2026-10-20 "什么文化风格都可以转换"；2026-10-28 转群友"可以给主城设置一个界面吗……村庄界面……可以直接修改样式那个界面"，
// 要"和农村一样的界面")：纯净地图没开时营建页「城貌」神力点一座城打开(见 SettlementPowers)；纯净地图开着时神力与街道一级点主城城区
// 都改开全屏的城池全图(见 CityPlanScreen)，小窗口里「全城规划」也能切过去。
// 宽窗口 480×200，同村镇小窗口(SettlementWindow)——
//   左「城池」：城名与国家、现在的城貌(手动选定 / 按文化定型 / 改建中)、现文化的风格、随风格走的规矩；
//   右「营建」：人口与住房上限、民居座数与富庶，城郊各生计的村镇份数；繁荣(同神力笔刷刷一下，见 CityConstructionSystem.Flourish)、
//     加减人口(同神力笔刷，见 CityResourcePowers.ChangePopulation)、营宫殿(同神力，五秒内再点一次才营建，见 CityPlacementSystem.BuildPalace)；
//   下「城貌」：各文化系的画法(华夏、日本、西欧、罗马、维京、阿拉伯、波斯、埃及、印度、阿兹特克、毡帐营地、冬营地、林间营地)，
//     点哪个当场换成哪个、记进存档并记作手动选定(改建决议、冬营地判定、水土改从游牧都不再改它)；「照现文化」当场照现文化改过来、
//     交还规矩(见 CityStyleSystem.SetByHand、FollowCulture)。
public class CityStyleWindow : AbstractWideWindow<CityStyleWindow>
{
    private const float CardWidth = (UIHelper.WidePanelWidth - UIHelper.CardGap) / 2f;
    private const float RowWidth = CardWidth - 12f;
    private const int PerRow = 5;
    private static long _selected = -1L;
    private AutoVertLayoutGroup _root;

    public static void Open(City city)
    {
        if (city?.data == null) return;
        _selected = city.data.id;
        ScrollWindow.showWindow(nameof(CityStyleWindow));
    }

    protected override void Init()
    {
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        Draw();
    }

    private static void Line(AutoVertLayoutGroup card, string text, int size = 7, float height = 11f) =>
        card.AddTextIntoVertLayout(text, true, TextAnchor.MiddleLeft, new Vector2(RowWidth, height))
            .UseFixedFontSize(size, HorizontalWrapMode.Overflow);

    private void Draw()
    {
        if (_root == null) return;
        _root.transform.ClearChildren();
        City city = _selected >= 0L ? World.world?.cities?.get(_selected) : null;
        if (city?.data == null || city.isRekt())
        {
            _root.BeginCard(UIHelper.WidePanelWidth).AddTextIntoVertLayout(LM.Get("city_style_none").ColorString("#8FA0A8"), true,
                TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 12f, 14f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
            return;
        }
        CityExtension.CityExtraData data = city.GetOrCreate();
        string family = CityStyleSystem.Established(city);
        (string target, float progress) = CityStyleSystem.Rebuild(city);
        bool winter = PureMapMode.Enabled && family == "nomad" && PureMapPatch.WinterCamp(city);
        string option = CityStyleSystem.OptionOf(city, winter);
        var columns = _root.BeginColumns(UIHelper.WidePanelWidth);

        // 城池
        var info = columns.BeginColumnCard(CardWidth, "city_style_window_city", icons: "ui/icons/iconCity");
        Line(info, city.GetCityName().ColorString("#F2EEE2"), 8, 14f);
        Line(info, (city.kingdom?.GetKingdomName() ?? LM.Get("settlement_unowned")).ColorString("#7FD8EA"));
        Line(info, string.Format(LM.Get(data.map_style_by_hand ? "city_style_now_hand" : "city_style_now_culture"),
            CityStyleSystem.StyleName(option)).ColorString("#65D66E"));
        if (target != null)
            Line(info, string.Format(LM.Get("city_style_rebuilding"), CityStyleSystem.StyleName(target), Mathf.RoundToInt(progress * 100f))
                .ColorString("#E9A85B"));
        Line(info, string.Format(LM.Get("city_style_culture"), CityStyleSystem.StyleName(CityStyleSystem.CurrentFamily(city)))
            .ColorString("#8FA0A8"), 6, 10f);
        string effect = family switch
        {
            "nomad" => "city_style_effect_nomad",
            // 阿兹特克照定居城修路，林地只修土路(用户 2026-10-21 选，见 CityStyleSystem.RoadGradeCap)
            "woodland" => "city_style_effect_dirt_road",
            _ => "city_style_effect_none"
        };
        Line(info, LM.Get(effect).ColorString("#8FA0A8"), 6, 10f);
        if (!PureMapMode.Enabled) Line(info, LM.Get("city_style_need_pure_map").ColorString("#E9A85B"), 6, 10f);
        // 全城规划(用户 2026-10-28 "能不能在宽窗口直接显示这个城镇的形状和完整城市样式, 然后允许玩家随意修改")：
        // 关掉小窗口，打开城池全图(见 CityPlanScreen)；纯净地图下街道级点城区、城貌神力也直接开全图
        info.AddButtonIntoVertLayout("city_window_plan", LM.Get("city_plan_ui_open"), () =>
        {
            City now = World.world?.cities?.get(_selected);
            if (now?.data == null || now.isRekt()) return;
            ScrollWindow.hideAllEvent();
            CityPlanScreen.Open(now);
        }, size: new Vector2(RowWidth, 14f));

        // 营建：人口、住房、富庶、城郊生计，繁荣、加减人口、营宫殿
        var build = columns.BeginColumnCard(CardWidth, "city_window_build", icons: "ui/icons/godpowers/city_flourish");
        Line(build, string.Format(LM.Get("city_window_people"), Mathf.RoundToInt(CityPopulationSystem.GetTotal(city)),
            CityPopulationSystem.Households(city), city.getPopulationMaximum()).ColorString("#65D66E"));
        Line(build, string.Format(LM.Get("city_window_houses"), city.countBuildingsType("type_house", false),
            city.status?.houses_max ?? 0, Mathf.RoundToInt(CityConstructionSystem.Wealth(city) * 100f)).ColorString("#F2EEE2"));
        var trades = new global::System.Collections.Generic.List<string>();
        for (int trade = 0; trade < LivelihoodSystem.Count; trade++)
        {
            float shares = LivelihoodSystem.Shares(city, trade);
            if (shares > 0f) trades.Add(LivelihoodSystem.Name(trade) + " " + shares.ToString("0"));
        }
        Line(build, (trades.Count == 0 ? LM.Get("city_window_trades_none")
            : string.Format(LM.Get("city_window_trades"), string.Join(LM.Get("city_window_trades_separator"), trades))).ColorString("#7FD8EA"));
        var actions = build.BeginHoriGroup(new Vector2(RowWidth, 16f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        float actionWidth = (RowWidth - 3f * 3f) / 4f;
        actions.AddButtonIntoHoriLayout("city_window_flourish", LM.Get("city_flourish"), () =>
        {
            City now = World.world?.cities?.get(_selected);
            if (now == null) return;
            (int built, int upgraded) = CityConstructionSystem.Flourish(now);
            WorldTip.showNow(built + upgraded == 0 ? LM.Get("city_flourish_full")
                : string.Format(LM.Get("city_flourish_done"), built, upgraded), false, "top", 2f);
            Draw();
        }, size: new Vector2(actionWidth, 14f));
        actions.AddButtonIntoHoriLayout("city_window_people_add", LM.Get("city_window_people_add"), () =>
        {
            EmpireCraft.Scripts.GodPowers.CityResourcePowers.ChangePopulation(World.world?.cities?.get(_selected), true);
            Draw();
        }, size: new Vector2(actionWidth, 14f));
        actions.AddButtonIntoHoriLayout("city_window_people_remove", LM.Get("city_window_people_remove"), () =>
        {
            EmpireCraft.Scripts.GodPowers.CityResourcePowers.ChangePopulation(World.world?.cities?.get(_selected), false);
            Draw();
        }, size: new Vector2(actionWidth, 14f));
        actions.AddButtonIntoHoriLayout("city_window_palace", LM.Get("build_palace"), () =>
        {
            City now = World.world?.cities?.get(_selected);
            if (now == null) return;
            string tip = CityPlacementSystem.BuildPalace(now.getTile());
            if (!string.IsNullOrEmpty(tip)) WorldTip.showNow(tip, false, "top", 3f);
            Draw();
        }, size: new Vector2(actionWidth, 14f));
        Line(build, LM.Get("city_window_hint").ColorString("#8FA0A8"), 6, 10f);

        // 城貌(整宽)
        const float Inner = UIHelper.WidePanelWidth - 12f;
        var styles = _root.BeginCard(UIHelper.WidePanelWidth, "city_style_window_styles", icons: "ui/icons/godpowers/restyle_city");
        float buttonWidth = (Inner - (PerRow - 1) * 3f) / PerRow;
        string[] options = CityStyleSystem.ManualStyles;
        for (int i = 0; i <= options.Length; i += PerRow)
        {
            var row = styles.BeginHoriGroup(new Vector2(Inner, 16f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            for (int k = i; k < Mathf.Min(options.Length + 1, i + PerRow); k++)
            {
                // 末尾一个是「照现文化」
                if (k == options.Length)
                {
                    row.AddButtonIntoHoriLayout("city_style_follow", LM.Get("city_style_follow"), () =>
                    {
                        CityStyleSystem.FollowCulture(World.world?.cities?.get(_selected));
                        Draw();
                    }, size: new Vector2(buttonWidth, 14f));
                    continue;
                }
                string choice = options[k];
                string text = CityStyleSystem.StyleName(choice);
                if (choice == option) text = "· " + text + " ·";
                row.AddButtonIntoHoriLayout("city_style_" + choice, text, () =>
                {
                    CityStyleSystem.SetByHand(World.world?.cities?.get(_selected), choice);
                    Draw();
                }, size: new Vector2(buttonWidth, 14f));
            }
        }
        styles.AddTextIntoVertLayout(LM.Get("city_style_window_hint").ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
            new Vector2(Inner, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
    }
}

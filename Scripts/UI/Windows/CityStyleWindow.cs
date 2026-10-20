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

// 城貌小窗口(用户 2026-10-20 "什么文化风格都可以转换")：营建页「城貌」神力点一座城打开(见 SettlementPowers)。
// 宽窗口 480×200、两张半宽卡，同村镇小窗口——
//   左「城池」：城名与国家、现在的城貌(手动选定 / 按文化定型 / 改建中)、现文化的风格、随风格走的规矩；
//   右「城貌」：各文化系的画法(华夏、日本、西欧、罗马、维京、阿拉伯、波斯、埃及、印度、阿兹特克、毡帐营地、冬营地、林间营地)，
//     点哪个当场换成哪个、记进存档并记作手动选定(改建决议、冬营地判定、水土改从游牧都不再改它)；「照现文化」当场照现文化改过来、
//     交还规矩(见 CityStyleSystem.SetByHand、FollowCulture)。
public class CityStyleWindow : AbstractWideWindow<CityStyleWindow>
{
    private const float CardWidth = (UIHelper.WidePanelWidth - UIHelper.CardGap) / 2f;
    private const float RowWidth = CardWidth - 12f;
    private const int PerRow = 3;
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
            "woodland" or "aztec" => "city_style_effect_roadless",
            _ => "city_style_effect_none"
        };
        Line(info, LM.Get(effect).ColorString("#8FA0A8"), 6, 10f);
        if (!PureMapMode.Enabled) Line(info, LM.Get("city_style_need_pure_map").ColorString("#E9A85B"), 6, 10f);

        // 城貌
        var styles = columns.BeginColumnCard(CardWidth, "city_style_window_styles", icons: "ui/icons/godpowers/restyle_city");
        float buttonWidth = (RowWidth - (PerRow - 1) * 3f) / PerRow;
        string[] options = CityStyleSystem.ManualStyles;
        for (int i = 0; i <= options.Length; i += PerRow)
        {
            var row = styles.BeginHoriGroup(new Vector2(RowWidth, 16f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
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
        Line(styles, LM.Get("city_style_window_hint").ColorString("#8FA0A8"), 6, 10f);
    }
}

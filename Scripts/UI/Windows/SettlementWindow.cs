using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 村镇小窗口(用户 2026-10-17 "点击后打开一个很小的窗口, 就像那个帝国设置的界面一样……升级降级, 改名")：
// 拉近到看得见镇村时点一下镇村打开(见 MapLabelOverlay.HandleClick)。宽窗口 480×200、两张半宽卡，同帝国设置——
//   左「概况」：名字(可改)、档位(村 → 镇 → 有墙大镇，升级降级)、约多少人、属哪座城哪国、完好或毁了几年；
//   右「营建」：生计(务农、伐木、采矿、放牧、打鱼，可改，见 LivelihoodSystem)与一年的出产，林场、矿、牧的官营民营(可改)；
//     换个样子(只换这一处的布局种子)、毁弃(完好的毁为废墟，废墟彻底抹去)或重建废墟，改隶与固定城乡格局的说明。
public class SettlementWindow : AbstractWideWindow<SettlementWindow>
{
    private const float CardWidth = (UIHelper.WidePanelWidth - UIHelper.CardGap) / 2f;
    private const float RowWidth = CardWidth - 12f;
    private static int _selected = -1;
    private AutoVertLayoutGroup _root;

    public static void Open(RuralSettlement settlement)
    {
        if (settlement == null) return;
        _selected = settlement.id;
        ScrollWindow.showWindow(nameof(SettlementWindow));
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

    private static SimpleText Line(AutoVertLayoutGroup card, string text, int size = 7, float height = 11f)
    {
        SimpleText label = card.AddTextIntoVertLayout(text, true, TextAnchor.MiddleLeft, new Vector2(RowWidth, height));
        label.UseFixedFontSize(size, HorizontalWrapMode.Overflow);
        return label;
    }

    private void Draw()
    {
        if (_root == null) return;
        _root.transform.ClearChildren();
        RuralSettlement settlement = RuralSettlementSystem.ById(_selected);
        if (settlement == null)
        {
            Line(_root.BeginCard(UIHelper.WidePanelWidth), LM.Get("settlement_none").ColorString("#8FA0A8"), 8, 14f);
            return;
        }
        City city = RuralSettlementSystem.CityOf(settlement);
        bool ruined = settlement.ruined_at >= 0d;
        int tier = RuralSettlementSystem.Tier(settlement);
        var columns = _root.BeginColumns(UIHelper.WidePanelWidth);

        // 概况
        var info = columns.BeginColumnCard(CardWidth, "settlement_window_overview", icons: "ui/icons/iconCity");
        Line(info, LM.Get("settlement_window_name").ColorString("#8FA0A8"), 6, 9f);
        TextInput nameInput = Instantiate(TextInput.Prefab, null);
        nameInput.Setup(RuralSettlementSystem.Label(settlement), text =>
        {
            RuralSettlementSystem.Rename(RuralSettlementSystem.ById(_selected), text);
            Draw();
        });
        nameInput.SetSize(new Vector2(RowWidth, 18f));
        Text inputText = nameInput.input.textComponent;
        if (inputText != null)
        {
            inputText.resizeTextMinSize = 6;
            inputText.resizeTextMaxSize = 10;
        }
        nameInput.input.characterLimit = 8;
        info.AddChild(nameInput.gameObject);
        var tierRow = info.BeginHoriGroup(new Vector2(RowWidth, 16f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        tierRow.AddTextIntoHoriLayout(RuralSettlementSystem.TierName(settlement).ColorString("#F2EEE2"), true, TextAnchor.MiddleLeft,
            new Vector2(RowWidth - 80f, 14f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        if (!ruined)
        {
            if (tier < RuralSettlementSystem.MaxTier)
                tierRow.AddButtonIntoHoriLayout("settlement_upgrade", LM.Get("settlement_upgrade"), () => Change(+1), size: new Vector2(36f, 14f));
            if (tier > 0)
                tierRow.AddButtonIntoHoriLayout("settlement_downgrade", LM.Get("settlement_downgrade"), () => Change(-1), size: new Vector2(36f, 14f));
        }
        Line(info, (ruined
            ? string.Format(LM.Get("settlement_window_ruined"), Mathf.Max(0, Date.getYearsSince(settlement.ruined_at)))
            : settlement.fort
                ? string.Format(LM.Get("fort_window_defense"), (tier + 1) * 30)
                : string.Format(LM.Get("settlement_window_people"), Mathf.RoundToInt(RuralSettlementSystem.PeopleOf(settlement, city))))
            .ColorString(ruined ? "#E9A85B" : "#65D66E"));
        Line(info, (city == null
            ? LM.Get("settlement_unowned")
            : string.Format(LM.Get("settlement_owner"), city.GetCityName(), city.kingdom?.GetKingdomName() ?? "")).ColorString("#7FD8EA"));

        // 营建
        var build = columns.BeginColumnCard(CardWidth, "settlement_window_build", icons: "ui/icons/godpowers/place_town");
        // 生计(关城没有，见 LivelihoodSystem)：做哪一行、几级、一年出多少；林场、矿、牧分官营民营
        int trade = LivelihoodSystem.Of(settlement);
        if (trade >= 0)
        {
            var tradeRow = build.BeginHoriGroup(new Vector2(RowWidth, 16f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            string level = city != null && trade is LivelihoodSystem.Mine or LivelihoodSystem.Forest
                ? string.Format(LM.Get("livelihood_level"), LivelihoodSystem.Level(city, trade)) : "";
            tradeRow.AddTextIntoHoriLayout((string.Format(LM.Get("livelihood_row"), LivelihoodSystem.Name(trade)) + level).ColorString("#F2EEE2"),
                true, TextAnchor.MiddleLeft, new Vector2(RowWidth - 46f, 14f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            tradeRow.AddButtonIntoHoriLayout("settlement_trade", LM.Get("livelihood_change"), () =>
            {
                RuralSettlement now = RuralSettlementSystem.ById(_selected);
                if (now != null) RuralSettlementSystem.SetTrade(now, LivelihoodSystem.Of(now) + 1);
                Draw();
            }, size: new Vector2(42f, 14f));
            Line(build, LivelihoodSystem.Describe(settlement, city).ColorString(ruined ? "#E9A85B" : "#65D66E"));
            if (LivelihoodSystem.IsEnterprise(trade))
            {
                var ownRow = build.BeginHoriGroup(new Vector2(RowWidth, 16f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
                ownRow.AddTextIntoHoriLayout(string.Format(LM.Get("enterprise_row"),
                        LM.Get(settlement.state ? "enterprise_state" : "enterprise_private")).ColorString("#D9B36A"), true,
                    TextAnchor.MiddleLeft, new Vector2(RowWidth - 46f, 14f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
                ownRow.AddButtonIntoHoriLayout("settlement_state", LM.Get(settlement.state ? "enterprise_to_private" : "enterprise_to_state"), () =>
                {
                    RuralSettlement now = RuralSettlementSystem.ById(_selected);
                    if (now != null) RuralSettlementSystem.SetState(now, !now.state);
                    Draw();
                }, size: new Vector2(42f, 14f));
            }
        }
        var buttons = build.BeginHoriGroup(new Vector2(RowWidth, 18f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        buttons.AddButtonIntoHoriLayout("settlement_reroll", LM.Get("settlement_reroll"), () =>
        {
            RuralSettlementSystem.Reroll(RuralSettlementSystem.ById(_selected));
            Draw();
        }, size: new Vector2(64f, 16f));
        if (ruined)
            buttons.AddButtonIntoHoriLayout("settlement_rebuild", LM.Get("settlement_rebuild"), () =>
            {
                RuralSettlementSystem.Rebuild(RuralSettlementSystem.ById(_selected));
                Draw();
            }, size: new Vector2(64f, 16f));
        buttons.AddButtonIntoHoriLayout("settlement_raze", LM.Get(ruined ? "settlement_erase" : "raze_settlement"), () =>
        {
            string tip = RuralSettlementSystem.Raze(RuralSettlementSystem.ById(_selected));
            if (!string.IsNullOrEmpty(tip)) WorldTip.showNow(tip, false, "top", 3f);
            Draw();
        }, size: new Vector2(64f, 16f));
        Line(build, LM.Get("settlement_window_reroll_hint").ColorString("#8FA0A8"), 6, 10f);
        Line(build, LM.Get("settlement_window_assign_hint").ColorString("#8FA0A8"), 6, 10f);
        Line(build, LM.Get(RuralSettlementSystem.Fixed ? "settlement_window_fixed_on" : "settlement_window_fixed_off")
            .ColorString(RuralSettlementSystem.Fixed ? "#F3C34A" : "#8FA0A8"), 6, 10f);
    }

    private void Change(int delta)
    {
        RuralSettlement settlement = RuralSettlementSystem.ById(_selected);
        if (settlement == null) return;
        RuralSettlementSystem.SetTier(settlement, RuralSettlementSystem.Tier(settlement) + delta);
        Draw();
    }
}

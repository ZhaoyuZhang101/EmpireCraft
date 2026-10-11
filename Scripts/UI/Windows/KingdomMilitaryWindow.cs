using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 一国的军事(宽窗口)：左栏国名、君主、都城与战备，右侧是与帝国军团页同一套内容(见 EmpireWindow.BuildMilitaryPage)。
// 不属于帝国的王国点地图兵牌、交战标记时打开；原版王国窗口的"军事"页签也打开它
public class KingdomMilitaryWindow : AbstractWideWindow<KingdomMilitaryWindow>
{
    private const float MainWidth = UIHelper.PageMainWidth;
    private const float HalfWidth = UIHelper.PageColumnWidth;

    private static long _pendingKingdom = -1L;
    private static HashSet<long> _focusCorps;
    private static long _focusCity = -1L;

    private AutoVertLayoutGroup _root;
    private AutoVertLayoutGroup _rail;
    private readonly List<GameObject> _content = new();

    public static void Open(Kingdom kingdom, IEnumerable<long> focusCorps = null, long focusCity = -1L)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return;
        _pendingKingdom = kingdom.id;
        _focusCorps = focusCorps == null ? null : new HashSet<long>(focusCorps);
        _focusCity = focusCity;
        ScrollWindow.showWindow(nameof(KingdomMilitaryWindow));
    }

    protected override void Init()
    {
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
        _rail = UIHelper.CreateWideWindowRail(BackgroundTransform, UIHelper.RailWidth, UIHelper.RailInset);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
        Rebuild();
    }

    public override void OnNormalDisable()
    {
        base.OnNormalDisable();
        _focusCorps = null;
        _focusCity = -1L;
        Clear();
    }

    private void Clear()
    {
        UIHelper.ClearRail(_rail);
        foreach (GameObject item in _content)
            if (item != null) Destroy(item);
        _content.Clear();
    }

    private void Rebuild()
    {
        Clear();
        Kingdom kingdom = World.world.kingdoms.get(_pendingKingdom);
        if (kingdom?.data == null || kingdom.isRekt()) return;
        DrawRail(kingdom);
        var page = _root.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 6));
        UIHelper.FixPanelWidth(page.transform, MainWidth);
        _content.Add(page.gameObject);
        var realms = new List<Kingdom> { kingdom };
        if (!CorpsSystem.Enabled)
        {
            page.AddPageHeader(MainWidth, "kingdom_military");
            EmpireWindow.AddMilitaryDisabledHint(page, MainWidth);
        }
        else
        {
            page.AddPageHeader(MainWidth, "kingdom_military", EmpireWindow.MilitaryChips(realms));
            EmpireWindow.BuildMilitaryPage(page, realms, MainWidth, HalfWidth, _focusCorps, _focusCity);
        }
        StartCoroutine(RefreshScrollLayout());
    }

    // 左栏：国名、国家类别、所属帝国 → 君主、都城、战备、军团与兵力
    private void DrawRail(Kingdom kingdom)
    {
        if (_rail == null) return;
        var identity = _rail.BeginRailCard("FactionFrame_dominate");
        identity.AddRailText(kingdom.GetKingdomFullName().ColorString(kingdom.getColor().color_text), 10, 26f, wrap: true);
        identity.AddRailText(LM.Get(kingdom.GetKingdomTypeDisplayKey()).ColorString("#8FE7FF"), 7, 10f);
        Empire empire = kingdom.GetEmpire();
        if (empire != null)
            identity.AddRailText(empire.GetEmpireFullName().ColorString("#8FA0A8"), 6, 9f);

        var stats = _rail.BeginRailCard();
        float rowWidth = UIHelper.RailWidth - 8f;
        int row = 0;
        if (kingdom.hasKing())
            stats.AddRailRow(rowWidth, LM.Get("village_statistics_king"), kingdom.king.getName(), "#F3C34A", row++);
        if (kingdom.hasCapital())
            stats.AddRailRow(rowWidth, LM.Get("kingdom_statistics_capital"), kingdom.capital.GetCityName(), "#C8C2B4", row++);
        bool war = CorpsSystem.IsAtWar(kingdom);
        stats.AddRailRow(rowWidth, LM.Get("kingdom_military_state"),
            LM.Get(war ? "kingdom_military_war" : "kingdom_military_peace"), war ? "#E8584F" : "#65D66E", row++);
        if (!CorpsSystem.Enabled) return;
        List<Data.CorpsData> corps = CorpsSystem.Of(kingdom);
        stats.AddRailRow(rowWidth, LM.Get("corps_kpi_count"), corps.Count.ToString(), "#F2EEE2", row++);
        stats.AddRailRow(rowWidth, LM.Get("corps_kpi_strength"), CorpsSystem.ShortStrength(corps.Sum(entry => entry.strength)),
            "#65D66E", row++);
        int battles = FrontSystem.Battles.Values.Count(battle =>
            battle.attacker_id == kingdom.id || battle.defender_id == kingdom.id);
        stats.AddRailRow(rowWidth, LM.Get("corps_kpi_battles"), battles.ToString(), battles > 0 ? "#E8584F" : "#8FA0A8", row);
    }

    // 卡片层层套着自适应高度：等一帧、连续重建几次再让滚动区读最终高度
    private IEnumerator RefreshScrollLayout()
    {
        yield return CoroutineHelper.wait_for_next_frame;
        if (ContentTransform == null) yield break;
        var contentRect = ContentTransform.GetComponent<RectTransform>();
        if (contentRect != null)
        {
            for (int pass = 0; pass < 3; pass++)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            float height = LayoutUtility.GetPreferredHeight(contentRect);
            if (height > 0f) contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        ScrollRect scrollRect = GetComponent<ScrollWindow>()?.scrollRect;
        if (scrollRect == null) yield break;
        scrollRect.vertical = true;
        scrollRect.StopMovement();
        scrollRect.verticalNormalizedPosition = 1f;
    }
}

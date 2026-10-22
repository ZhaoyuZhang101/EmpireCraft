using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 亲属与族谱树：
//   · 亲属：本人为中心按辈分分卡(配偶、长辈、同辈、子辈、孙辈、侄辈)，可切父系、母系，可搜亲属；一行三格，点人换成他；
//   · 族谱树：一代一列(父母 → 本人与兄弟姐妹、配偶 → 子女 → 孙辈)，拖拽平移、滚轮缩放，点人以他为中心重画
//     (排布与连线见 SpecificClanGraphView，节点是这里画的小人物卡)。
public partial class SpecificClanWindow
{
    private enum Side { Home, Father, Mother }

    private const float TreeHeight = 300f;
    private Side _side = Side.Home;
    private string _relativeSearch = "";

    private void DrawRelatives(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, LM.Get("clan_page_relatives"), string.Format(LM.Get("clan_relatives_chip"), _identity.name));
        page.AddChipRow(PageWidth,
            (LM.Get("clan_relatives_home"), _side == Side.Home, () => SetSide(Side.Home)),
            (LM.Get("father_relation"), _side == Side.Father, () => SetSide(Side.Father)),
            (LM.Get("mother_relation"), _side == Side.Mother, () => SetSide(Side.Mother)));
        page.AddSearchInput(PageWidth, _relativeSearch, text =>
        {
            string value = (text ?? "").Trim();
            if (value == _relativeSearch) return;
            _relativeSearch = value;
            if (gameObject.activeInHierarchy) StartCoroutine(RefreshNextFrame());
        });

        if (_relativeSearch.Length > 0)
        {
            List<(ClanRelation, PersonalClanIdentity)> found = OverallHelperFunc.SearchPersonalClanIdentityHelper(_relativeSearch,
                SpecificClanManager.FindAllRelations(_identity));
            Section(page, "title_search_result", found, "ui/icons/iconFamily");
            if (found.Count == 0) page.AddEmptyCard(PageWidth, LM.Get("clan_relatives_none"));
            return;
        }

        int shown = 0;
        switch (_side)
        {
            case Side.Home:
                var spouses = new List<(ClanRelation, PersonalClanIdentity)>();
                if (_identity.hasLover()) spouses.Add((ClanRelation.LOV, SpecificClanManager.getPerson(_identity.lover.identity)));
                spouses.AddRange(_identity.concubines.Select(cob => (ClanRelation.COB, SpecificClanManager.getPerson(cob.identity))));
                var elders = new List<(ClanRelation, PersonalClanIdentity)>
                {
                    (ClanRelation.FAT, SpecificClanManager.getPerson(_identity.father)),
                    (ClanRelation.MOM, SpecificClanManager.getPerson(_identity.mother)),
                    (ClanRelation.FIL, SpecificClanManager.getPerson(_identity.father_in_law)),
                    (ClanRelation.MIL, SpecificClanManager.getPerson(_identity.mother_in_law))
                };
                var same = new List<(ClanRelation, PersonalClanIdentity)> { (ClanRelation.SELF, _identity) };
                same.AddRange(SpecificClanManager.GetSiblingsWithRelation(_identity));
                shown += Section(page, "current_parent_generation", elders, "ui/icons/iconArrowUP");
                shown += Section(page, "current_same_generation", same, "ui/icons/iconFamily");
                shown += Section(page, "current_lovers_generation", spouses, "ui/icons/iconFamily");
                shown += Section(page, "current_children_generation", SpecificClanManager.getChildren(_identity), "ui/icons/iconNewFamily");
                shown += Section(page, "current_grand_children_generation", SpecificClanManager.GetGrandChildren(_identity),
                    "ui/icons/iconNewFamily");
                shown += Section(page, "sibling_child_generation", SpecificClanManager.GetSiblingChildGeneration(_identity),
                    "ui/icons/iconNewFamily");
                break;
            case Side.Father:
                shown += Section(page, "father_grand_generation", Pairs(SpecificClanManager.GetFatherGrandGeneration(_identity)),
                    "ui/icons/iconArrowUP");
                shown += Section(page, "father_great_generation", Pairs(SpecificClanManager.GetFatherGreatGeneration(_identity)),
                    "ui/icons/iconArrowUP");
                shown += Section(page, "father_same_generation", Pairs(SpecificClanManager.GetFatherSameGeneration(_identity)),
                    "ui/icons/iconFamily");
                break;
            case Side.Mother:
                shown += Section(page, "mother_grand_generation", Pairs(SpecificClanManager.GetMotherGrandGeneration(_identity)),
                    "ui/icons/iconArrowUP");
                shown += Section(page, "mother_great_generation", Pairs(SpecificClanManager.GetMotherGreatGeneration(_identity)),
                    "ui/icons/iconArrowUP");
                shown += Section(page, "mother_same_generation", Pairs(SpecificClanManager.GetMotherSameGeneration(_identity)),
                    "ui/icons/iconFamily");
                break;
        }
        if (shown == 0) page.AddEmptyCard(PageWidth, LM.Get("clan_relatives_none"));
    }

    private static IEnumerable<(ClanRelation, PersonalClanIdentity)> Pairs(
        IEnumerable<(ClanRelation relation, PersonalClanIdentity pci)> list) =>
        list.Select(item => (item.relation, item.pci));

    // 一辈一张卡：标题 + 人数，人物格一行三个；没人不画
    private int Section(AutoVertLayoutGroup page, string titleKey, IEnumerable<(ClanRelation relation, PersonalClanIdentity person)> people,
        string icon)
    {
        List<(ClanRelation relation, PersonalClanIdentity person)> list = people.Where(item => item.person != null)
            .GroupBy(item => item.person.id).Select(group => group.First()).ToList();
        if (list.Count == 0) return 0;
        var card = page.BeginCard(PageWidth, LM.Get(titleKey), WideKit.Gold, string.Format(LM.Get("clan_kpi_people"), list.Count), icon);
        DrawTiles(card, list);
        return list.Count;
    }

    private void SetSide(Side side)
    {
        if (_side == side) return;
        _side = side;
        Refresh(resetScroll: true);
    }

    // ───────── 族谱树 ─────────
    private void DrawTree(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, LM.Get("clan_page_tree"), string.Format(LM.Get("clan_tree_chip"), _identity.name),
            LM.Get("clan_tree_hint"));
        var card = page.BeginCard(PageWidth);
        SpecificClanGraphView graph = SpecificClanGraphView.Create(card.transform, new Vector2(PageWidth - 12f, TreeHeight));
        graph.Rebuild(_identity, (person, relation, position) => TreeNode(graph.ContentTransform, person, relation, position));
    }

    // 树上的人物卡：像素框(本人金框) + 头像 + 称谓·姓名 / 生卒 / 官职；点人以他为中心重画
    private void TreeNode(Transform parent, PersonalClanIdentity person, ClanRelation relation, Vector2 position)
    {
        float width = SpecificClanGraphView.CardWidth, height = SpecificClanGraphView.CardHeight;
        bool focus = person == _identity;
        var node = new GameObject("ClanNode", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = node.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = position;
        Image frame = node.GetComponent<Image>();
        frame.sprite = UIHelper.SlicedFrame(focus ? "FactionFrame_dominate" : "FactionFrame");
        frame.type = Image.Type.Sliced;
        UIHelper.MakeClickable(node, () => Focus(person));

        var holder = new GameObject("Avatar", typeof(RectTransform));
        var holderRect = holder.GetComponent<RectTransform>();
        holderRect.SetParent(rect, false);
        holderRect.anchorMin = holderRect.anchorMax = holderRect.pivot = new Vector2(0.5f, 0.5f);
        holderRect.sizeDelta = new Vector2(30f, 30f);
        holderRect.anchoredPosition = new Vector2(-width / 2f + 21f, 0f);
        SimpleButton avatar = UIHelper.CreateAvatarView(person.actor_id, () => Focus(person), person.is_alive, person);
        // 头像框建出来是铺满父级再加 30 的拉伸锚点(平时由布局组改成定宽)，这里没有布局组，自己定成 30×30 居中
        var avatarRect = avatar.GetComponent<RectTransform>();
        avatarRect.SetParent(holderRect, false);
        avatarRect.anchorMin = avatarRect.anchorMax = new Vector2(0.5f, 0.5f);
        avatarRect.sizeDelta = new Vector2(30f, 30f);
        avatarRect.anchoredPosition = Vector2.zero;

        float textX = -width / 2f + 39f, textWidth = width - 43f;
        string head = RelationTag(relation, person).ColorString(RelationHex(relation)) + " " +
                      person.name.ColorString(person.is_alive ? WideKit.Value : WideKit.Soft);
        NodeText(rect, head, 7, new Vector2(textX, 11f), textWidth);
        NodeText(rect, Life(person).ColorString(WideKit.Muted), 6, new Vector2(textX, 0f), textWidth);
        NodeText(rect, Post(person).ColorString(OnOffice(person) ? WideKit.Gold : WideKit.Muted), 6, new Vector2(textX, -11f), textWidth);
    }

    private static void NodeText(RectTransform parent, string text, int size, Vector2 leftCenter, float width)
    {
        SimpleText label = Object.Instantiate(SimpleText.Prefab, parent);
        label.Setup(text, TextAnchor.MiddleLeft, new Vector2(width, 10f));
        label.background.enabled = false;
        label.UseFixedFontSize(size, HorizontalWrapMode.Overflow);
        var rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = leftCenter;
        foreach (Graphic graphic in label.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.Compatibility;

// WarBox 总参谋部的帝国适配。
//
// WarBox 每个王国各有一个总参谋部(君主 + 三位城主)，左栏把世界上所有王国平铺成一列。
// 本模组的帝国由中央与一串行政区(省、道、郡、总督区……)组成，行政区不是独立国家：
//   · 中央与各行政区共用一个帝国总参谋部：元首统领，从中央与各行政区的长官、城主中选军事能力最高的三人，
//     加成覆盖中央与各行政区的军队；封国、叛乱中的行政区仍是自己的参谋部；
//   · 左栏把帝国成员收到帝国条目(中央)下，选中该帝国或它的成员时才展开，展开的成员缩进显示。
public static partial class WarBoxCompatibility
{
    private const float StaffCacheSeconds = 3f;
    private const int StaffSize = 4;

    private sealed class EmpireStaffCache
    {
        public Actor Head;
        public float RefreshAt;
        public readonly List<Actor> Members = new(StaffSize);
    }

    private static readonly Dictionary<long, EmpireStaffCache> _empireStaff = new();

    private static Type _staffType;
    private static Type _overlayType;
    private static MethodInfo _getAllKingdoms;
    private static MethodInfo _clearChildren;
    private static MethodInfo _addKingdomButton;
    private static MethodInfo _addWrappedPanel;
    private static FieldInfo _sidebarField;
    private static FieldInfo _targetField;
    private static FieldInfo _rowWidthField;
    private static FieldInfo _sideWidthField;
    private static FieldInfo _currentField;
    private static FieldInfo _fontField;

    private static readonly Color StaffGoldDim = new(0.75f, 0.66f, 0.48f);
    private const float MemberIndent = 10f;

    private static void ApplyGeneralStaffPatches(Harmony harmony)
    {
        _staffType = AccessTools.TypeByName("WarBox.Content.GeneralStaff");
        _overlayType = AccessTools.TypeByName("WarBox.UI.GeneralStaffOverlay");
        if (_staffType == null) return;
        _getAllKingdoms = AccessTools.Method(_staffType, "GetAllKingdoms");
        TryPatch(harmony, "WarBox.Content.GeneralStaff", "GetGeneralStaff", nameof(BeforeGetGeneralStaff));
        if (_overlayType == null) return;
        _clearChildren = AccessTools.Method(_overlayType, "ClearChildren");
        _addKingdomButton = AccessTools.Method(_overlayType, "AddKingdomButton");
        _addWrappedPanel = AccessTools.Method(_overlayType, "AddWrappedPanel");
        _sidebarField = AccessTools.Field(_overlayType, "_sidebar");
        _targetField = AccessTools.Field(_overlayType, "_target");
        _rowWidthField = AccessTools.Field(_overlayType, "_rowWidthActive");
        _sideWidthField = AccessTools.Field(_overlayType, "_sideWidth");
        _currentField = AccessTools.Field(_overlayType, "_current");
        _fontField = AccessTools.Field(_overlayType, "_font");
        if (_getAllKingdoms != null && _clearChildren != null && _addKingdomButton != null &&
            _sidebarField != null && _targetField != null && _rowWidthField != null &&
            _sideWidthField != null && _currentField != null)
            TryPatch(harmony, "WarBox.UI.GeneralStaffOverlay", "RenderSidebar", nameof(BeforeStaffSidebar));
        if (_addWrappedPanel != null)
            TryPatch(harmony, "WarBox.UI.GeneralStaffOverlay", "RenderStaff", nameof(BeforeRenderStaff));
    }

    // 共用帝国总参谋部的帝国：这个王国是帝国中央，或是没有叛乱的行政区
    private static Empire StaffEmpire(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || !kingdom.IsInEmpire()) return null;
        Empire empire = kingdom.GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (empire == null || empire.isRekt() || core == null || core.isRekt()) return null;
        if (kingdom == core) return empire;
        if (!kingdom.IsAdministrativeKingdomType() || kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling())
            return null;
        return empire;
    }

    // 左栏分组用：帝国里的成员(含封国)，叛乱中的不算
    private static Kingdom SidebarCore(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || !kingdom.IsInEmpire()) return null;
        if (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling()) return null;
        Kingdom core = kingdom.GetEmpire()?.CoreKingdom;
        return core == null || core.isRekt() ? null : core;
    }

    private static IEnumerable<Kingdom> StaffMembers(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        yield return core;
        foreach (Kingdom member in empire.kingdoms_list.ToArray())
            if (member != null && member != core && StaffEmpire(member) == empire)
                yield return member;
    }

    public static bool BeforeGetGeneralStaff(Kingdom kingdom, ref List<Actor> __result)
    {
        try
        {
            Empire empire = StaffEmpire(kingdom);
            Actor head = empire?.CoreKingdom?.king;
            if (head == null || !head.isAlive()) return true;
            float now = Time.realtimeSinceStartup;
            long id = empire.id;
            if (_empireStaff.TryGetValue(id, out EmpireStaffCache cached) && cached.Head == head &&
                now < cached.RefreshAt)
            {
                __result = cached.Members;
                return false;
            }
            if (cached == null) _empireStaff[id] = cached = new EmpireStaffCache();
            cached.Head = head;
            cached.RefreshAt = now + StaffCacheSeconds;
            cached.Members.Clear();
            cached.Members.Add(head);

            var candidates = new HashSet<Actor>();
            foreach (Kingdom member in StaffMembers(empire))
            {
                if (member != empire.CoreKingdom && member.king != null && member.king.isAlive())
                    candidates.Add(member.king);
                if (member.cities == null) continue;
                foreach (City city in member.cities)
                {
                    Actor leader = city?.leader;
                    if (leader != null && leader.isAlive() && leader.isCityLeader()) candidates.Add(leader);
                }
            }
            candidates.Remove(head);
            cached.Members.AddRange(candidates.OrderByDescending(actor => actor.warfare).Take(StaffSize - 1));
            __result = cached.Members;
            return false;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] WarBox 帝国总参谋部计算失败: {exception.Message}");
            return true;
        }
    }

    public static bool BeforeStaffSidebar(object __instance)
    {
        try
        {
            var all = _getAllKingdoms.Invoke(null, null) as List<Kingdom>;
            if (all == null || all.Count == 0) return true;

            // 帝国中央 → 成员(行政区在前，封国等在后，各自保持原来的名字顺序)
            var members = new Dictionary<Kingdom, List<Kingdom>>();
            var listed = new HashSet<Kingdom>(all);
            foreach (Kingdom kingdom in all)
            {
                Kingdom core = SidebarCore(kingdom);
                if (core == null || core == kingdom || !listed.Contains(core)) continue;
                if (!members.TryGetValue(core, out List<Kingdom> list)) members[core] = list = new List<Kingdom>();
                list.Add(kingdom);
            }
            foreach (List<Kingdom> list in members.Values)
            {
                List<Kingdom> ordered = list.Where(k => k.IsAdministrativeKingdomType())
                    .Concat(list.Where(k => !k.IsAdministrativeKingdomType())).ToList();
                list.Clear();
                list.AddRange(ordered);
            }

            var current = _currentField.GetValue(__instance) as Kingdom;
            Kingdom expanded = current == null ? null : members.ContainsKey(current) ? current : SidebarCore(current);

            var sidebar = _sidebarField.GetValue(__instance) as RectTransform;
            if (sidebar == null) return true;
            _clearChildren.Invoke(__instance, new object[] { sidebar });
            _targetField.SetValue(__instance, sidebar);
            _rowWidthField.SetValue(__instance, _sideWidthField.GetValue(__instance));
            var font = _fontField?.GetValue(__instance) as Font;

            foreach (Kingdom kingdom in all)
            {
                if (kingdom == null || !kingdom.isAlive()) continue;
                Kingdom core = SidebarCore(kingdom);
                if (core != null && core != kingdom && members.ContainsKey(core)) continue;
                _addKingdomButton.Invoke(__instance, new object[] { kingdom });
                if (!members.TryGetValue(kingdom, out List<Kingdom> list)) continue;
                DecorateEmpireRow(LastRow(sidebar), list.Count, font);
                if (kingdom != expanded) continue;
                foreach (Kingdom member in list)
                {
                    if (member == null || !member.isAlive()) continue;
                    _addKingdomButton.Invoke(__instance, new object[] { member });
                    IndentMemberRow(LastRow(sidebar));
                }
            }
            return false;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] WarBox 总参谋部左栏分组失败: {exception.Message}");
            return true;
        }
    }

    private static RectTransform LastRow(RectTransform sidebar) =>
        sidebar.childCount == 0 ? null : sidebar.GetChild(sidebar.childCount - 1) as RectTransform;

    // 帝国条目：右侧标出"帝国·辖n"，名字让出位置，太长时缩小字号
    private static void DecorateEmpireRow(RectTransform row, int memberCount, Font font)
    {
        if (row == null) return;
        Text label = row.Find("label")?.GetComponent<Text>();
        if (label == null) return;

        var tagObject = new GameObject("empire_tag", typeof(RectTransform));
        tagObject.transform.SetParent(row, false);
        Text tag = tagObject.AddComponent<Text>();
        tag.font = font != null ? font : label.font;
        tag.fontSize = Mathf.Max(6, label.fontSize - 2);
        tag.color = StaffGoldDim;
        tag.alignment = TextAnchor.MiddleRight;
        tag.horizontalOverflow = HorizontalWrapMode.Overflow;
        tag.raycastTarget = false;
        tag.text = string.Format(LM.Get("warbox_staff_empire_tag"), memberCount);
        float tagWidth = Mathf.Ceil(tag.preferredWidth) + 2f;
        var rect = tag.rectTransform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(tagWidth, 0f);
        rect.anchoredPosition = new Vector2(-4f, 0f);

        label.rectTransform.offsetMax = new Vector2(-(tagWidth + 8f), label.rectTransform.offsetMax.y);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.resizeTextMaxSize = label.fontSize;
        label.resizeTextMinSize = 6;
        label.resizeTextForBestFit = true;
    }

    // 展开的成员：整体右移缩进，字小一号
    private static void IndentMemberRow(RectTransform row)
    {
        if (row == null) return;
        foreach (Transform node in row)
        {
            if (node is not RectTransform child) continue;
            if (child.name == "label")
            {
                child.offsetMin += new Vector2(MemberIndent, 0f);
                Text text = child.GetComponent<Text>();
                if (text != null) text.fontSize = Mathf.Max(6, text.fontSize - 1);
            }
            else if (child.name == "banner")
            {
                child.anchoredPosition += new Vector2(MemberIndent, 0f);
            }
        }
    }

    // 参谋部页开头说明这是帝国总参谋部(选中行政区时说明它归帝国统辖)
    public static void BeforeRenderStaff(object __instance, Kingdom kingdom)
    {
        try
        {
            Empire empire = StaffEmpire(kingdom);
            if (empire?.CoreKingdom?.king == null) return;
            string note = kingdom == empire.CoreKingdom
                ? string.Format(LM.Get("warbox_staff_empire_note"), StaffMembers(empire).Count() - 1)
                : string.Format(LM.Get("warbox_staff_member_note"), kingdom.GetKingdomName(),
                    empire.CoreKingdom.GetKingdomName());
            _addWrappedPanel.Invoke(__instance, new object[] { note, 9, StaffGoldDim, 34f, 0.15f });
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] WarBox 帝国总参谋部说明失败: {exception.Message}");
        }
    }
}

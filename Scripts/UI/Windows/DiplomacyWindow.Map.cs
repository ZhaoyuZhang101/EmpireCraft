using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 外交界面的天下简图(用户 2026-11-11 "对了外交界面也可以加这个缩略图模式""这样会很清晰"，看了示意图 Tasks/ui-mockups/diplomacy-map.png
// 后"按这版做")。底图是 RealmMiniMap 按格取样的小图(同发起诉求选目标的那张)，这里只定每国什么颜色、标什么字、点了做什么：
//   · 总览「关系图」：三种上色——关系(本国、同一系、同盟、附属、宗主、有约、交战、无往来)、态度(红到绿，图上写数)、
//     势力(帝国或宗主连同附属一个颜色)；左栏的筛选、搜索同图联动，筛掉的压暗；点一国进两国的双边页；
//   · 双边「两国位置」：本国金、本国一系淡金，对方国色加白框、对方一系压暗的国色，其余压暗；卡头写接不接壤；点别的国家换对方；
//   · 体系图「宗主一系」：按身份上色(宗主、帝国成员、朝贡国、附庸、殖民地)，一系之外压暗；点一国把它设为焦点。
// 指着图上哪一国，图例那一行换成它的名字、态度、关系
public partial class DiplomacyWindow
{
    private const float MapWidth = PageWidth - 12f, MapMaxHeight = 150f;
    private const int MaxLabels = 16;

    // 总览关系图的上色：0 关系、1 态度、2 势力
    private int _mapMode;
    private SimpleText _mapHover;

    private enum Relation
    {
        Self,
        Side,
        Ally,
        Subject,
        Overlord,
        Pact,
        War,
        None
    }

    private struct MapStyle
    {
        public Color32 Color;
        // 画边线时并进哪一国那一组(势力：一系并成一块)；不填 = 自成一组
        public Kingdom Group;
        public bool White, Bold, Clickable;
        public string Label, Hover;
        public Color LabelColor;
    }

    private static readonly Color32 RelSide = new(160, 138, 82, 255), RelAlly = new(79, 174, 98, 255), RelSubject = new(63, 167, 184, 255),
        RelOverlord = new(138, 111, 184, 255), RelPact = new(79, 127, 201, 255), RelWar = new(201, 80, 74, 255),
        RelNone = new(107, 112, 104, 255), RelColony = new(201, 138, 74, 255), RelOther = new(90, 140, 150, 255), MapDim = new(62, 66, 60, 255);

    private static readonly Color LabelGold = new(0.95f, 0.76f, 0.29f), LabelLight = new(0.95f, 0.93f, 0.89f), LabelSoft = new(0.78f, 0.77f, 0.72f);

    private static Color32 RelationColor(Relation relation) => relation switch
    {
        Relation.Self => RealmMiniMap.OwnGold,
        Relation.Side => RelSide,
        Relation.Ally => RelAlly,
        Relation.Subject => RelSubject,
        Relation.Overlord => RelOverlord,
        Relation.Pact => RelPact,
        Relation.War => RelWar,
        _ => RelNone
    };

    // 态度色阶：−80 以下红、0 灰、+80 以上绿
    private static Color32 OpinionColor(int opinion) => opinion >= 0
        ? RealmMiniMap.Mix(RelNone, RelAlly, Mathf.Clamp01(opinion / 80f))
        : RealmMiniMap.Mix(RelNone, RelWar, Mathf.Clamp01(-opinion / 80f));

    // 图例里的一项：字色取图上那块的颜色(提亮些，暗底上看得清)
    private static string Legend(string key, Color32 color, params object[] args)
    {
        Color32 bright = RealmMiniMap.Mix(color, RealmMiniMap.White, 0.3f);
        string text = args.Length == 0 ? LM.Get(key) : string.Format(LM.Get(key), args);
        return text.ColorString($"#{bright.r:X2}{bright.g:X2}{bright.b:X2}");
    }

    private void SetMapMode(int mode)
    {
        if (_mapMode == mode) return;
        _mapMode = mode;
        Refresh();
    }

    // 往卡片里画一张按国上色的简图，下面一行图例(指着图上一国时换成它的说明)。styleOf 给每国的画法；legend 在各国画法算完后取
    private void DrawRealmMap(AutoVertLayoutGroup card, Func<Kingdom, MapStyle> styleOf, Func<string> legend, Action<Kingdom> onClick)
    {
        RealmMiniMap.MapData map;
        try
        {
            map = RealmMiniMap.Snapshot();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][外交] 简化地图没画成: {exception.Message}");
            return;
        }
        int kingdoms = map.Kingdoms.Count;
        var styles = new MapStyle[kingdoms];
        var color = new Color32[kingdoms];
        var group = new int[kingdoms];
        var white = new bool[kingdoms];
        for (int i = 0; i < kingdoms; i++)
        {
            styles[i] = styleOf(map.Kingdoms[i]);
            color[i] = styles[i].Color;
            group[i] = styles[i].Group != null && map.KingdomIndex.TryGetValue(styles[i].Group.id, out int at) ? at : i;
        }
        for (int i = 0; i < kingdoms; i++)
            if (styles[i].White) white[group[i]] = true;
        RealmMiniMap.Painted painted = RealmMiniMap.Paint(map, color, group, white);
        string legendText = legend();
        float scale = Mathf.Min(MapWidth / map.Width, MapMaxHeight / map.Height);
        ClaimTargetMap view = ClaimTargetMap.Create(card.transform, map.Width * scale, map.Height * scale, map.Width, map.Height, painted.Pixels,
            (x, y) =>
            {
                int owner = map.Owner[y * map.Width + x];
                if (owner >= 0 && styles[owner].Clickable) onClick(map.Kingdoms[owner]);
            },
            (x, y) =>
            {
                if (_mapHover?.text == null) return;
                int owner = x < 0 ? -1 : map.Owner[y * map.Width + x];
                string text = owner >= 0 ? styles[owner].Hover : null;
                _mapHover.text.text = string.IsNullOrEmpty(text) ? legendText : text;
            });
        // 图上的名字：加粗的(本国、对方、一系之主)必标，其余按占的格数从大到小取前几个，太小的不标
        int minimum = Mathf.Max(12, painted.Pixels.Length / 260), shown = 0;
        foreach (int i in Enumerable.Range(0, kingdoms).Where(i => painted.Count[i] > 0 && !string.IsNullOrEmpty(styles[i].Label))
                     .OrderByDescending(i => styles[i].Bold).ThenByDescending(i => painted.Count[i]))
        {
            if (!styles[i].Bold && (painted.Count[i] < minimum || shown >= MaxLabels)) continue;
            shown++;
            view.AddLabel(painted.SumX[i] / painted.Count[i], painted.SumY[i] / painted.Count[i], styles[i].Label, styles[i].LabelColor,
                styles[i].Bold ? 7 : 6);
        }
        _mapHover = card.AddParagraph(PageWidth, legendText, 6, WideKit.Soft);
    }

    // 指着一国时的说明：名字 · 态度 · 关系标签 · 点了做什么
    private string HoverText(Kingdom kingdom, List<DiplomacyLedger.Tie> ties, string note, string actionKey)
    {
        var parts = new List<string> { Name(kingdom) };
        if (kingdom != _focus)
        {
            parts.Add(string.Format(LM.Get("diplomacy_map_attitude"), OpinionText(DiplomacyLedger.Opinion(_focus, kingdom))));
            parts.Add(ties == null || ties.Count == 0
                ? LM.Get("diplomacy_map_none").ColorString(WideKit.Muted)
                : string.Join(" · ", ties.Select(tie => tie.text.ColorString(tie.hex))));
        }
        else parts.Add(DiplomacyLedger.Standing(kingdom).ColorString(WideKit.Soft));
        if (!string.IsNullOrEmpty(note)) parts.Add(note.ColorString(WideKit.Muted));
        if (!string.IsNullOrEmpty(actionKey) && kingdom != _focus) parts.Add(LM.Get(actionKey).ColorString(WideKit.Muted));
        return string.Join("  ", parts);
    }

    // 一系的名字：帝国之主写帝国名，别的写国名
    private static string BlocName(Kingdom top)
    {
        Empire empire = top?.GetEmpire();
        return empire != null && !empire.isRekt() && empire.CoreKingdom == top ? empire.GetEmpireName() : top?.GetKingdomName() ?? "";
    }

    #region 总览：关系图

    private void DrawOverviewMap(AutoVertLayoutGroup page)
    {
        var card = page.BeginCard(PageWidth, "diplomacy_map_title", WideKit.Gold, LM.Get("diplomacy_map_hint"), "ui/icons/iconKingdom");
        card.AddChipRow(PageWidth - 12f,
            (LM.Get("diplomacy_map_mode_ties"), _mapMode == 0, () => SetMapMode(0)),
            (LM.Get("diplomacy_map_mode_opinion"), _mapMode == 1, () => SetMapMode(1)),
            (LM.Get("diplomacy_map_mode_bloc"), _mapMode == 2, () => SetMapMode(2)));

        var realms = new HashSet<Kingdom>(DiplomacyLedger.Realms());
        // 左栏筛选、搜索之后还在名单里的；不在的压暗
        var listed = new HashSet<Kingdom>(_rows);
        var dependents = new HashSet<Kingdom>(DiplomacyLedger.Dependents(_focus));
        HashSet<Kingdom> overlords = DiplomacyLedger.Overlords(_focus);
        Empire own = _focus.GetEmpire();
        Kingdom ownTop = DiplomacyLedger.TopOf(_focus);
        // 势力：各系有几国
        var blocSize = new Dictionary<Kingdom, int>();
        if (_mapMode == 2)
            foreach (Kingdom kingdom in realms)
            {
                Kingdom top = DiplomacyLedger.TopOf(kingdom);
                if (top == null) continue;
                blocSize.TryGetValue(top, out int size);
                blocSize[top] = size + 1;
            }
        var used = new HashSet<Relation>();
        bool anyHidden = false;

        Relation RelationOf(Kingdom kingdom, List<DiplomacyLedger.Tie> ties)
        {
            if (kingdom == _focus) return Relation.Self;
            if (_focus.isEnemy(kingdom)) return Relation.War;
            if (overlords.Contains(kingdom)) return Relation.Overlord;
            // 帝国的成员算同一系，别的附属(附庸、殖民地、朝贡国)算附属
            if (dependents.Contains(kingdom)) return own != null && kingdom.GetEmpire() == own ? Relation.Side : Relation.Subject;
            if (DiplomacyLedger.SameBloc(_focus, kingdom)) return Relation.Side;
            if (DiplomacyLedger.Allied(_focus, kingdom)) return Relation.Ally;
            return ties.Count > 0 ? Relation.Pact : Relation.None;
        }

        MapStyle StyleOf(Kingdom kingdom)
        {
            if (!realms.Contains(kingdom)) return new MapStyle { Color = MapDim };
            bool focus = kingdom == _focus, shown = focus || listed.Contains(kingdom);
            List<DiplomacyLedger.Tie> ties = focus ? null : DiplomacyLedger.Ties(_focus, kingdom);
            var style = new MapStyle
            {
                Clickable = !focus,
                Hover = HoverText(kingdom, ties, shown ? null : LM.Get("diplomacy_map_outside"), "diplomacy_map_click_pair")
            };
            if (!shown)
            {
                anyHidden = true;
                style.Color = MapDim;
                return style;
            }
            if (_mapMode == 2)
            {
                Kingdom top = DiplomacyLedger.TopOf(kingdom) ?? kingdom;
                bool ours = top == ownTop;
                Color32 bloc = ours ? RealmMiniMap.OwnGold : RealmMiniMap.KingdomColor(top);
                style.Color = kingdom == top ? bloc : RealmMiniMap.Mix(bloc, RealmMiniMap.Ink, 0.18f);
                style.Group = top;
                style.White = ours;
                if (kingdom == top)
                {
                    blocSize.TryGetValue(top, out int size);
                    style.Label = size > 1 ? $"{BlocName(top)} ·{size}" : BlocName(top);
                    style.LabelColor = ours ? LabelGold : LabelLight;
                    style.Bold = ours;
                }
                if (top != kingdom)
                    style.Hover = HoverText(kingdom, ties, string.Format(LM.Get("diplomacy_map_bloc_hover"), BlocName(top)), "diplomacy_map_click_pair");
                return style;
            }
            Relation relation = RelationOf(kingdom, ties ?? new List<DiplomacyLedger.Tie>());
            used.Add(relation);
            style.Bold = focus;
            if (_mapMode == 1)
            {
                int opinion = focus ? 0 : DiplomacyLedger.Opinion(_focus, kingdom);
                style.Color = focus ? RealmMiniMap.OwnGold : OpinionColor(opinion);
                style.Label = focus ? kingdom.GetKingdomName() : $"{kingdom.GetKingdomName()} {(opinion > 0 ? "+" : "")}{opinion}";
                style.LabelColor = focus ? LabelGold : LabelLight;
                return style;
            }
            style.Color = RelationColor(relation);
            style.Label = kingdom.GetKingdomName();
            style.LabelColor = focus ? LabelGold : relation == Relation.None ? LabelSoft : LabelLight;
            return style;
        }

        string LegendText()
        {
            var items = new List<string> { Legend("diplomacy_map_self", RealmMiniMap.OwnGold) };
            if (_mapMode == 2) items.Add(LM.Get("diplomacy_map_bloc_legend").ColorString(WideKit.Soft));
            else if (_mapMode == 1)
            {
                items.Add(Legend("diplomacy_map_hostile", RelWar));
                items.Add(Legend("diplomacy_map_cool", RelNone));
                items.Add(Legend("diplomacy_map_friendly", RelAlly));
            }
            else
                foreach ((Relation relation, string key) in new[]
                         {
                             (Relation.Side, "diplomacy_map_side"), (Relation.Ally, "diplomacy_map_ally"), (Relation.Subject, "diplomacy_map_subject"),
                             (Relation.Overlord, "diplomacy_map_overlord"), (Relation.Pact, "diplomacy_map_pact"), (Relation.War, "diplomacy_map_war"),
                             (Relation.None, "diplomacy_map_none")
                         })
                    if (used.Contains(relation)) items.Add(Legend(key, RelationColor(relation)));
            if (anyHidden) items.Add(Legend("diplomacy_map_filtered", MapDim));
            if (_mapMode == 1) items.Add(LM.Get("diplomacy_map_opinion_note").ColorString(WideKit.Muted));
            return string.Join("  ", items);
        }

        DrawRealmMap(card, StyleOf, LegendText, kingdom => Go(Page.Bilateral, kingdom));
    }

    #endregion

    #region 双边：两国位置

    private void DrawPairMap(AutoVertLayoutGroup page)
    {
        var realms = new HashSet<Kingdom>(DiplomacyLedger.Realms());
        bool near = DiplomacyLedger.NeighboursOf(_focus).Contains(_other);
        // 两国自己的城挨着的有几座(隔着无人区、经成员国接壤的数不出来，只写接壤)
        int touching = _focus.cities?.Count(city => city?.neighbours_cities != null &&
                                                    city.neighbours_cities.Any(next => next != null && next.kingdom == _other)) ?? 0;
        string border = touching > 0 ? string.Format(LM.Get("diplomacy_map_adjacent"), touching)
            : LM.Get(near ? "diplomacy_map_adjacent_plain" : "diplomacy_map_apart");
        var card = page.BeginCard(PageWidth, "diplomacy_map_pair_title", WideKit.Gold, border, "ui/icons/iconKingdom");
        Color32 theirs = RealmMiniMap.KingdomColor(_other), theirSide = RealmMiniMap.Mix(theirs, MapDim, 0.5f);
        int ourCount = 0, theirCount = 0;
        foreach (Kingdom kingdom in realms)
        {
            if (kingdom == _focus || DiplomacyLedger.SameBloc(_focus, kingdom)) ourCount++;
            else if (kingdom == _other || DiplomacyLedger.SameBloc(_other, kingdom)) theirCount++;
        }
        // 两国同在一系：对方算在本国这一边，对面只有它自己
        if (theirCount == 0) theirCount = 1;

        MapStyle StyleOf(Kingdom kingdom)
        {
            if (!realms.Contains(kingdom)) return new MapStyle { Color = MapDim };
            if (kingdom == _focus)
                return new MapStyle
                {
                    Color = RealmMiniMap.OwnGold, Label = kingdom.GetKingdomName(), LabelColor = LabelGold, Bold = true,
                    Hover = HoverText(kingdom, null, null, null)
                };
            List<DiplomacyLedger.Tie> ties = DiplomacyLedger.Ties(_focus, kingdom);
            if (kingdom == _other)
                return new MapStyle
                {
                    Color = theirs, White = true, Label = kingdom.GetKingdomName(), LabelColor = Color.white, Bold = true,
                    Hover = HoverText(kingdom, ties, null, null)
                };
            var style = new MapStyle { Clickable = true, Hover = HoverText(kingdom, ties, null, "diplomacy_map_click_other"), Color = MapDim };
            if (DiplomacyLedger.SameBloc(_focus, kingdom)) style.Color = RelSide;
            else if (DiplomacyLedger.SameBloc(_other, kingdom)) style.Color = theirSide;
            else return style;
            style.Label = kingdom.GetKingdomName();
            style.LabelColor = LabelSoft;
            return style;
        }

        string LegendText() => string.Join("  ",
            Legend("diplomacy_map_named", RealmMiniMap.OwnGold, _focus.GetKingdomName()),
            Legend("diplomacy_map_bloc_of", RelSide, _focus.GetKingdomName()),
            Legend("diplomacy_map_named", theirs, _other.GetKingdomName()),
            Legend("diplomacy_map_bloc_of", theirSide, _other.GetKingdomName()),
            Legend("diplomacy_map_rest", MapDim),
            string.Format(LM.Get("diplomacy_map_sides_count"), ourCount, theirCount).ColorString(WideKit.Muted));

        DrawRealmMap(card, StyleOf, LegendText, kingdom => Go(Page.Bilateral, kingdom));
    }

    #endregion

    #region 体系图：宗主一系

    private static Color32 RoleColor(string key) => key switch
    {
        "self" => RealmMiniMap.OwnGold,
        "member" => RelSide,
        "tributary" => RelSubject,
        "vassal" => RelOverlord,
        "colony" => RelColony,
        _ => RelOther
    };

    private void DrawSystemMap(AutoVertLayoutGroup page, Kingdom root)
    {
        var realms = new HashSet<Kingdom>(DiplomacyLedger.Realms());
        var roles = new Dictionary<Kingdom, (string key, string role)>();
        foreach ((Kingdom kingdom, int _, string role, string key) in DiplomacyLedger.Tree(root)) roles[kingdom] = (key, role);
        var card = page.BeginCard(PageWidth, "diplomacy_map_system_title", WideKit.Gold,
            string.Format(LM.Get("diplomacy_map_system_count"), BlocName(root), roles.Count), "ui/icons/iconKingdom");
        var used = new List<string>();

        MapStyle StyleOf(Kingdom kingdom)
        {
            if (!realms.Contains(kingdom) && !roles.ContainsKey(kingdom)) return new MapStyle { Color = MapDim };
            List<DiplomacyLedger.Tie> ties = kingdom == _focus ? null : DiplomacyLedger.Ties(_focus, kingdom);
            if (!roles.TryGetValue(kingdom, out (string key, string role) entry))
                return new MapStyle
                {
                    Color = MapDim, Clickable = true,
                    Hover = HoverText(kingdom, ties, LM.Get("diplomacy_map_system_out"), "diplomacy_map_click_focus")
                };
            if (!used.Contains(entry.key)) used.Add(entry.key);
            return new MapStyle
            {
                Color = RoleColor(entry.key), Clickable = kingdom != _focus, White = kingdom == _focus && kingdom != root,
                Label = kingdom.GetKingdomName(), LabelColor = kingdom == root ? LabelGold : kingdom == _focus ? Color.white : LabelLight,
                Bold = kingdom == root || kingdom == _focus,
                Hover = HoverText(kingdom, ties, entry.role, "diplomacy_map_click_focus")
            };
        }

        string LegendText()
        {
            var items = new List<string>();
            foreach (string key in new[] { "self", "member", "tributary", "vassal", "colony", "union", "subject" })
                if (used.Contains(key)) items.Add(Legend(key == "self" ? "diplomacy_map_overlord" : "diplomacy_role_" + key, RoleColor(key)));
            items.Add(Legend("diplomacy_map_system_out", MapDim));
            return string.Join("  ", items);
        }

        DrawRealmMap(card, StyleOf, LegendText, Focus);
    }

    #endregion
}

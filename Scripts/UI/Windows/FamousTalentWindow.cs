using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 名臣名将窗口(「卷册」宽窗口，组件见 WideScrollWindow / WideKit；名册见 FamousTalentSystem)：
//   · 左栏(固定)：名册概览 → 在世 / 青史 / 全部 → 按类别(名将、名臣) → 青史页再按陨落方式 → 按势力 → 排序；
//   · 右栏：页头(页名 · 人数) → 主指标(在世人数、名将·名臣、国柱) → 在世的按势力分节、两栏卡片
//     (势力色条、头像、称号、官职、功业/年龄/成名年数、特质带来的属性，整卡点开此人)；
//     青史收在一张卡里一行一人(称号、势力、卒年、享年、陨落方式、功业)；
//   · 名册为空时显示空状态与成名条件。
// 图标复用名将名臣特质的图标(IconForge 的 TRAITS)。
public class FamousTalentWindow : WideScrollWindow<FamousTalentWindow>
{
    private enum View { Alive, Fallen, All }

    private enum Sort { Realm, Fame, Age }

    private const string TraitIconPath = "ui/icons/actor_traits/";
    private const int RailRealmRows = 8;

    private View _view = View.Alive;
    private Sort _sort = Sort.Realm;
    // -1 = 全部类别
    private int _kind = -1;
    // 空 = 全部势力
    private string _realm = "";
    // -1 = 全部陨落方式
    private int _fall = -1;

    private List<TalentRecord> _alive = new(), _fallen = new(), _shownAlive = new(), _shownFallen = new();
    private List<IGrouping<string, TalentRecord>> _realms = new();

    protected override void OnOpen()
    {
        _realm = "";
        _kind = -1;
        _fall = -1;
    }

    // ───────── 数据(每次重画前取一遍) ─────────
    protected override void Prepare()
    {
        _alive = FamousTalentSystem.Alive().ToList();
        _fallen = FamousTalentSystem.Fallen().ToList();
        IEnumerable<TalentRecord> scope = _view switch
        {
            View.Alive => _alive,
            View.Fallen => _fallen,
            _ => _alive.Concat(_fallen)
        };
        _realms = scope.GroupBy(RealmKey).OrderByDescending(group => group.Count())
            .ThenBy(group => RealmName(group.First())).ToList();
        if (!string.IsNullOrEmpty(_realm) && _realms.All(group => group.Key != _realm)) _realm = "";
        _shownAlive = Filter(_view == View.Fallen ? new List<TalentRecord>() : _alive, false);
        _shownFallen = Filter(_view == View.Alive ? new List<TalentRecord>() : _fallen, true);
    }

    private List<TalentRecord> Filter(IEnumerable<TalentRecord> records, bool fallen)
    {
        IEnumerable<TalentRecord> result = records;
        if (_kind >= 0) result = result.Where(record => (int)record.kind == _kind);
        if (!string.IsNullOrEmpty(_realm)) result = result.Where(record => RealmKey(record) == _realm);
        if (fallen && _fall >= 0) result = result.Where(record => (int)record.fall == _fall);
        return result.ToList();
    }

    private static string RealmName(TalentRecord record) =>
        string.IsNullOrEmpty(record.realm_name) ? LM.Get("talent_no_realm") : record.realm_name;

    private static string RealmKey(TalentRecord record) =>
        record.empire_id >= 0 ? "e" + record.empire_id : "k" + record.kingdom_id;

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        rail.AddRailTitle(LM.Get("talent_window"),
            string.Format(LM.Get("talent_rail_summary"), _alive.Count, _fallen.Count));

        var views = rail.BeginRailMenu();
        views.AddRailOption(null, LM.Get("talent_view_alive"), _alive.Count.ToString(), _view == View.Alive,
            () => SetView(View.Alive), 0);
        views.AddRailOption(null, LM.Get("talent_view_fallen"), _fallen.Count.ToString(), _view == View.Fallen,
            () => SetView(View.Fallen), 1);
        views.AddRailOption(null, LM.Get("talent_view_all"), (_alive.Count + _fallen.Count).ToString(),
            _view == View.All, () => SetView(View.All), 2);

        IEnumerable<TalentRecord> scope = _view switch
        {
            View.Alive => _alive,
            View.Fallen => _fallen,
            _ => _alive.Concat(_fallen)
        };
        List<TalentRecord> scoped = scope.ToList();
        var kinds = rail.BeginRailMenu(LM.Get("talent_by_kind"));
        kinds.AddRailOption(null, LM.Get("talent_all"), scoped.Count.ToString(), _kind < 0, () => SetKind(-1), 0);
        kinds.AddRailOption(Icon("iconTalentGeneralFamous"), LM.Get("talent_kind_general"),
            scoped.Count(record => record.kind == TalentKind.General).ToString(), _kind == (int)TalentKind.General,
            () => SetKind((int)TalentKind.General), 1);
        kinds.AddRailOption(Icon("iconTalentMinisterFamous"), LM.Get("talent_kind_minister"),
            scoped.Count(record => record.kind == TalentKind.Minister).ToString(), _kind == (int)TalentKind.Minister,
            () => SetKind((int)TalentKind.Minister), 2);

        if (_view == View.Fallen)
        {
            var falls = rail.BeginRailMenu(LM.Get("talent_by_fall"));
            falls.AddRailOption(null, LM.Get("talent_all"), _fallen.Count.ToString(), _fall < 0, () => SetFall(-1), 0);
            int index = 1;
            foreach (TalentFall fall in new[] { TalentFall.Battle, TalentFall.Age, TalentFall.Illness, TalentFall.Executed, TalentFall.Other })
            {
                int count = _fallen.Count(record => record.fall == fall);
                if (count == 0 && _fall != (int)fall) continue;
                falls.AddRailOption(null, FamousTalentSystem.FallName(fall), count.ToString(), _fall == (int)fall,
                    () => SetFall((int)fall), index++);
            }
        }

        if (_realms.Count > 1)
        {
            var realms = rail.BeginRailMenu(LM.Get("talent_by_realm"));
            realms.AddRailOption(null, LM.Get("talent_all_realms"), _realms.Sum(group => group.Count()).ToString(),
                string.IsNullOrEmpty(_realm), () => SetRealm(""), 0);
            int index = 1;
            foreach (var group in _realms.Take(RailRealmRows))
            {
                string key = group.Key;
                realms.AddRailOption(null, RealmName(group.First()), group.Count().ToString(), _realm == key,
                    () => SetRealm(key), index++);
            }
        }

        if (_view != View.Fallen)
        {
            var sorts = rail.BeginRailMenu(LM.Get("talent_sort"));
            sorts.AddRailOption(null, LM.Get("talent_sort_realm"), null, _sort == Sort.Realm, () => SetSort(Sort.Realm), 0);
            sorts.AddRailOption(null, LM.Get("talent_sort_fame"), null, _sort == Sort.Fame, () => SetSort(Sort.Fame), 1);
            sorts.AddRailOption(null, LM.Get("talent_sort_age"), null, _sort == Sort.Age, () => SetSort(Sort.Age), 2);
        }
    }

    private void SetView(View view)
    {
        if (_view == view) return;
        _view = view;
        _realm = "";
        _fall = -1;
        Refresh(resetScroll: true);
    }

    private void SetKind(int kind)
    {
        if (_kind == kind) return;
        _kind = kind;
        Refresh(resetScroll: true);
    }

    private void SetFall(int fall)
    {
        if (_fall == fall) return;
        _fall = fall;
        Refresh(resetScroll: true);
    }

    private void SetRealm(string realm)
    {
        if (_realm == realm) return;
        _realm = realm;
        Refresh(resetScroll: true);
    }

    private void SetSort(Sort sort)
    {
        if (_sort == sort) return;
        _sort = sort;
        Refresh(resetScroll: true);
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        string title = _view switch
        {
            View.Alive => LM.Get("talent_view_alive"),
            View.Fallen => LM.Get("talent_view_fallen"),
            _ => LM.Get("talent_window")
        };
        page.AddPageHeader(PageWidth, title,
            string.Format(LM.Get("talent_chip_count"), _shownAlive.Count + _shownFallen.Count));

        if (_shownAlive.Count == 0 && _shownFallen.Count == 0)
        {
            bool fallenOnly = _view == View.Fallen;
            page.AddEmptyCard(PageWidth, fallenOnly ? "talent_empty_fallen" : "talent_empty",
                fallenOnly ? null : LM.Get("talent_empty_hint"));
            return;
        }

        if (_shownAlive.Count > 0)
        {
            page.AddKpiRow(PageWidth,
                (LM.Get("talent_kpi_alive"), _shownAlive.Count.ToString(), WideKit.Green),
                (LM.Get("talent_kpi_kinds"),
                    $"{_shownAlive.Count(record => record.kind == TalentKind.General)} · " +
                    $"{_shownAlive.Count(record => record.kind == TalentKind.Minister)}", WideKit.Cyan),
                (LM.Get("talent_kpi_pillar"),
                    _shownAlive.Count(record => record.rank == TalentRank.Pillar).ToString(), WideKit.Gold));
            DrawAlive(page);
        }
        if (_shownFallen.Count > 0) DrawFallen(page);
    }

    private void DrawAlive(AutoVertLayoutGroup page)
    {
        if (_sort == Sort.Realm)
        {
            foreach (var group in _shownAlive.GroupBy(RealmKey)
                         .OrderByDescending(group => group.Count()).ThenBy(group => RealmName(group.First())))
            {
                TalentRecord first = group.First();
                page.AddSectionHeader(PageWidth,
                    string.Format(LM.Get("talent_section_realm"), RealmName(first), group.Count()),
                    string.IsNullOrEmpty(first.realm_text_color) ? WideKit.Cyan : first.realm_text_color, false);
                page.AddCardGrid(PageWidth,
                    group.OrderByDescending(record => record.rank).ThenByDescending(record => record.fame).ToList(),
                    2, AddAliveCard);
            }
            return;
        }
        List<TalentRecord> ordered = _sort == Sort.Fame
            ? _shownAlive.OrderByDescending(record => record.fame).ToList()
            : _shownAlive.OrderByDescending(record => Age(record)).ToList();
        page.AddCardGrid(PageWidth, ordered, 2, AddAliveCard);
    }

    // 在世者卡：势力色条 → 头像、称号、姓名与档位、官职 → 功业/年龄/成名年数 → 特质属性
    private void AddAliveCard(AutoHoriLayoutGroup row, float width, TalentRecord record)
    {
        Actor actor = FamousTalentSystem.ActorOf(record);
        var card = row.BeginColumnCard(width);
        card.AddColorStrip(width, ParseColor(record.realm_color, Color.gray));
        string caption = FamousTalentSystem.TitleText(record).ColorString(FamousTalentSystem.KindHex(record.kind));
        string name = $"{record.name} {FamousTalentSystem.RankName(record.rank).ColorString(FamousTalentSystem.RankHex(record.rank))}";
        card.AddPersonRow(width, actor, caption, name, FamousTalentSystem.PostText(record));
        card.AddMetricRow(width,
            (LM.Get("talent_metric_fame"), Mathf.RoundToInt(record.fame).ToString(), WideKit.Gold),
            (LM.Get("talent_metric_age"), Age(record).ToString(), WideKit.Value),
            (LM.Get("talent_metric_years"), $"{Mathf.Max(0, Date.getYearsSince(record.founded_at))}{LM.Get("Year")}", WideKit.Cyan));
        string effect = FamousTalentSystem.EffectLine(record);
        if (!string.IsNullOrEmpty(effect))
            card.AddTextIntoVertLayout(effect.ColorString(WideKit.Green), true, TextAnchor.MiddleLeft,
                new Vector2(width - 12f, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        if (actor != null) UIHelper.MakeClickable(card.gameObject, () => UIHelper.actorClick(actor));
    }

    // 青史：一张卡，一行一人
    private void DrawFallen(AutoVertLayoutGroup page)
    {
        var card = page.BeginCard(PageWidth, "talent_fallen_title", WideKit.Muted, _shownFallen.Count.ToString());
        int index = 0;
        foreach (TalentRecord record in _shownFallen.OrderByDescending(record => record.died_at))
        {
            string line = string.Format(LM.Get("talent_fallen_line"), FamousTalentSystem.TitleText(record),
                RealmName(record), HistoryDateFormatter.GetYear(record.died_at), record.died_age,
                FamousTalentSystem.FallName(record.fall), Mathf.RoundToInt(record.fame));
            card.AddListRow(PageWidth, index++, Icon(record.kind == TalentKind.General
                    ? "iconTalentGeneralFamous" : "iconTalentMinisterFamous"),
                $"{record.name} {FamousTalentSystem.RankName(record.rank)}", line, null,
                FamousTalentSystem.RankHex(record.rank), 70f);
        }
    }

    private static int Age(TalentRecord record)
    {
        Actor actor = FamousTalentSystem.ActorOf(record);
        return actor != null ? Mathf.Max(0, Mathf.RoundToInt(actor.getAge())) : record.died_age;
    }

    private static Sprite Icon(string name) => SpriteTextureLoader.getSprite(TraitIconPath + name);

    private static Color ParseColor(string hex, Color fallback) =>
        !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color color) ? color : fallback;
}

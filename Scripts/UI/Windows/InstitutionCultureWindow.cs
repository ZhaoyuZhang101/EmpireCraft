using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 选文化小窗口(用户 2026-10-20)，三种用法：
//   · 浏览(制度窗口左栏)：点一个文化，制度窗口改看它的制度树、文明等级与先进度；改革进度、立宪卡按这个文化城最多的
//     帝国(没有就独立王国)看，只看不改(见 InstitutionWindow 的浏览)；
//   · 改从(制度窗口左栏，神力)：点一个文化，国家当场整个改从此俗(见 RealmCultureSystem.Convert：国家文化、各城百姓与地名、
//     君主宗室与在任官员、法理、政体与制度，不留旧制)；从帝国窗口进来的改整个帝国(核心连同全部成员国)，从王国窗口进来的只改这一国；
//   · 整体转化(文化窗口)：点一个文化，原文化整个改从它(见 RealmCultureSystem.ConvertCulture)，原文化就此消亡。
// 左栏写明是哪种、改的是谁、现在是什么文化；右栏三列文化卡(纹章、名、制度线、文明等级与先进度、本局几国)，整卡可点；选完回原窗口。
public class InstitutionCultureWindow : WideScrollWindow<InstitutionCultureWindow>
{
    public enum Mode { Browse, Convert, Culture }

    private const int Columns = 3;
    private static Mode _mode;
    // 浏览：本国(帝国或王国)的政权；改从：要改的国家、是否连帝国全体
    private static InstitutionRealm _home;
    private static Kingdom _kingdom;
    private static bool _wholeEmpire;
    // 浏览：正在看的文化(空 = 本国文化)；整体转化：要转的文化
    private static string _culture = "";

    private List<string> _cultures = new();
    private Dictionary<string, int> _realms = new(StringComparer.Ordinal);

    // 随制度窗口一起注册(见 InstitutionWindow.Init)；没注册上的话打开时补
    public static void Register()
    {
        if (Instance != null) return;
        CreateAndInit(nameof(InstitutionCultureWindow), new Vector2(480f, 300f));
    }

    public static void OpenBrowse(InstitutionRealm home, string browsing)
    {
        _mode = Mode.Browse;
        _home = home;
        _culture = browsing ?? "";
        Show();
    }

    public static void OpenConvert(Kingdom kingdom, bool wholeEmpire)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return;
        _mode = Mode.Convert;
        _kingdom = kingdom;
        _wholeEmpire = wholeEmpire;
        Show();
    }

    public static void OpenCulture(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return;
        _mode = Mode.Culture;
        _culture = culture;
        Show();
    }

    private static void Show()
    {
        Register();
        ScrollWindow.showWindow(nameof(InstitutionCultureWindow));
    }

    #region 共用：次序与样本政权

    // 浏览、选文化共用的次序：本局有国的文化按文明等级、先进度排前(同制度窗口的文化等级榜)，其余按名
    public static List<string> Ordered()
    {
        List<string> result = InstitutionSystem.GetCultureRankings().Select(item => item.Culture).ToList();
        var seen = new HashSet<string>(result, StringComparer.Ordinal);
        result.AddRange(CultureService.CultureKeys.Where(culture => CultureService.IsValidCulture(culture) && seen.Add(culture))
            .OrderBy(culture => culture.GetCultureTranslate(), StringComparer.Ordinal));
        return result;
    }

    // 浏览别的文化时看哪一国：制度文化是它、城最多的帝国；没有帝国就取独立王国；都没有返回空(只看文化本身)
    public static InstitutionRealm SampleRealm(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return null;
        InstitutionRealm best = null;
        int most = -1;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER ?? Enumerable.Empty<Empire>())
        {
            InstitutionRealm realm = InstitutionRealm.Of(empire);
            if (!Usable(realm) || !string.Equals(realm.Culture, culture, StringComparison.Ordinal)) continue;
            int cities = realm.Scope.Sum(kingdom => kingdom?.cities?.Count ?? 0);
            if (cities <= most) continue;
            best = realm;
            most = cities;
        }
        if (best != null) return best;
        foreach (Kingdom kingdom in World.world?.kingdoms ?? Enumerable.Empty<Kingdom>())
        {
            if (!InstitutionRealm.IsSovereignKingdom(kingdom)) continue;
            InstitutionRealm realm = InstitutionRealm.Of(kingdom);
            if (!Usable(realm) || !string.Equals(realm.Culture, culture, StringComparison.Ordinal)) continue;
            int cities = kingdom.cities?.Count ?? 0;
            if (cities <= most) continue;
            best = realm;
            most = cities;
        }
        return best;
    }

    private static bool Usable(InstitutionRealm realm) =>
        realm != null && realm.IsAlive && realm.Core?.GetRegime() != null;

    // 国家的现俗：征服王朝的核心按统治族群的文化(同 CultureReformSystem.Force)，其余按国家文化
    public static string CurrentCustom(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return "";
        Empire empire = kingdom.GetEmpire();
        bool core = empire?.data != null && empire.CoreKingdom == kingdom && !empire.IsArchived() && !empire.isRekt();
        return core && CompositeEmpireService.IsComposite(empire)
            ? CompositeEmpireService.GetRulingCulture(empire)
            : CultureService.GetRealmCulture(kingdom);
    }

    #endregion

    protected override void Prepare()
    {
        _cultures = Ordered();
        _realms = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Kingdom kingdom in World.world?.kingdoms ?? Enumerable.Empty<Kingdom>())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || (kingdom.cities?.Count ?? 0) == 0) continue;
            string culture = CultureService.GetRealmCulture(kingdom);
            if (!CultureService.IsValidCulture(culture)) continue;
            _realms[culture] = (_realms.TryGetValue(culture, out int count) ? count : 0) + 1;
        }
    }

    // 卡片上标出的那个文化：浏览时是正在看的(没在浏览就是本国的)，改从时是现俗，整体转化时是要转的文化
    private static string Marked() => _mode switch
    {
        Mode.Browse => !string.IsNullOrEmpty(_culture) ? _culture : HomeCulture(),
        Mode.Convert => CurrentCustom(_kingdom),
        _ => _culture
    };

    private static string HomeCulture() => Usable(_home) ? _home.Culture : "";

    private static string TargetName()
    {
        if (_kingdom?.data == null || _kingdom.isRekt()) return "";
        Empire empire = _kingdom.GetEmpire();
        return _wholeEmpire && empire?.data != null && !empire.isRekt() ? empire.GetEmpireName() : _kingdom.GetKingdomName();
    }

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        string marked = Marked();
        string title = _mode switch
        {
            Mode.Browse => LM.Get("institution_culture_browse"),
            Mode.Convert => LM.Get("institution_culture_convert"),
            _ => LM.Get("culture_convert_all")
        };
        string subtitle = _mode switch
        {
            Mode.Browse => _home?.Name ?? "",
            Mode.Convert => TargetName(),
            _ => _culture.GetCultureTranslate()
        };
        rail.AddRailTitle(title, subtitle);

        var card = rail.BeginRailCard();
        float inner = UIHelper.RailWidth - 8f;
        var now = card.BeginHoriGroup(new Vector2(inner, 14f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        if (CultureService.IsValidCulture(marked)) UIHelper.AddLayoutIcon(now.transform, CultureIcons.Get(marked), 12f);
        string label = LM.Get(_mode switch
        {
            Mode.Browse => "institution_culture_now_viewing",
            Mode.Convert => "institution_culture_now_custom",
            _ => "culture_convert_all_from"
        });
        SimpleText nowText = now.AddTextIntoHoriLayout(label.ColorString(WideKit.Muted) +
            (CultureService.IsValidCulture(marked) ? marked.GetCultureTranslate() : LM.Get("label_none")).ColorString(WideKit.Gold),
            true, TextAnchor.MiddleLeft, new Vector2(inner - 15f, 13f));
        nowText.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(nowText);
        string hint = _mode switch
        {
            Mode.Browse => LM.Get("institution_culture_browse_hint"),
            Mode.Convert => string.Format(LM.Get(_wholeEmpire ? "institution_culture_convert_tip_empire"
                : "institution_culture_convert_tip_kingdom"), TargetName()),
            _ => string.Format(LM.Get("culture_convert_all_tip"), _culture.GetCultureTranslate())
        };
        // 说明按长短给高(英文、整体转化的长)
        card.AddRailText(hint.ColorString("#9FB0B6"), 6, _mode switch { Mode.Browse => 54f, Mode.Convert => 70f, _ => 88f },
            wrap: true);

        bool browsing = _mode == Mode.Browse && !string.IsNullOrEmpty(_culture) &&
                        !string.Equals(_culture, HomeCulture(), StringComparison.Ordinal);
        if (browsing) rail.AddRailButton("institution_culture_home", LM.Get("institution_culture_home"), () => Pick(""));
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, LM.Get("institution_culture_pick"),
            string.Format(LM.Get("institution_culture_count"), _cultures.Count));
        string marked = Marked();
        page.AddCardGrid(PageWidth, _cultures, Columns, (row, width, culture) => DrawCulture(row, width, culture, marked));
    }

    // 文化卡：纹章 + 名 / 制度线 / 文明等级 · 先进度 / 本局几国(标出本国、浏览中、现俗)
    private void DrawCulture(AutoHoriLayoutGroup row, float width, string culture, string marked)
    {
        var card = row.BeginColumnCard(width);
        float inner = width - 12f;
        bool mark = string.Equals(culture, marked, StringComparison.Ordinal);
        var head = card.BeginHoriGroup(new Vector2(inner, 14f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        UIHelper.AddLayoutIcon(head.transform, CultureIcons.Get(culture), 12f);
        SimpleText name = head.AddTextIntoHoriLayout(culture.GetCultureTranslate().ColorString(mark ? WideKit.Gold : WideKit.Value),
            true, TextAnchor.MiddleLeft, new Vector2(inner - 15f, 13f));
        name.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(name);

        InstitutionCultureRanking ranking = InstitutionSystem.GetCultureRanking(culture);
        Line(card, inner, (LM.Get(InstitutionDefinitionRegistry.GetLineNameKey(ranking.Line)) + LM.Get("institution_line_label"))
            .ColorString(WideKit.Cyan));
        Line(card, inner, $"{ranking.LevelName} · {LM.Get("institution_advancement")} {ranking.Advancement}".ColorString(WideKit.Soft));
        int realms = _realms.TryGetValue(culture, out int count) ? count : 0;
        string footer = (realms > 0
            ? string.Format(LM.Get("institution_culture_realms"), realms)
            : LM.Get("institution_culture_no_realm")).ColorString(WideKit.Muted);
        if (mark) footer += "  " + LM.Get(_mode switch
        {
            Mode.Browse => string.IsNullOrEmpty(_culture) ? "institution_culture_tag_home" : "institution_culture_tag_browsing",
            Mode.Convert => "institution_culture_tag_current",
            _ => "culture_convert_all_tag"
        }).ColorString(WideKit.Orange);
        Line(card, inner, footer);
        UIHelper.MakeClickable(card.gameObject, () => Pick(culture));
    }

    private static void Line(AutoVertLayoutGroup card, float width, string text)
    {
        SimpleText line = card.AddTextIntoVertLayout(text, true, TextAnchor.MiddleLeft, new Vector2(width, 10f));
        line.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(line);
    }

    // ───────── 选定 ─────────
    private void Pick(string culture)
    {
        switch (_mode)
        {
            case Mode.Browse:
                InstitutionWindow.BrowseCulture(culture);
                Back();
                return;
            case Mode.Convert:
                if (!ConvertRealm(culture)) return;
                InstitutionWindow.BrowseCulture("");
                Back();
                return;
            default:
                if (string.Equals(culture, _culture, StringComparison.Ordinal))
                {
                    WorldTip.showNow(string.Format(LM.Get("culture_convert_all_same"), culture.GetCultureTranslate()), false, "top", 3f);
                    return;
                }
                (int kingdoms, int cities) = RealmCultureSystem.ConvertCulture(_culture, culture);
                WorldTip.showNow(string.Format(LM.Get("culture_convert_all_done"), _culture.GetCultureTranslate(),
                    culture.GetCultureTranslate(), kingdoms, cities), false, "top", 4f);
                // 原文化就此消亡：文化窗口改看转成的文化
                CultureInfoWindow.SelectCulture(culture);
                Back();
                return;
        }
    }

    // 改从：从帝国窗口进来的改整个帝国，从王国窗口进来的只改这一国
    private static bool ConvertRealm(string culture)
    {
        Kingdom kingdom = _kingdom;
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || !CultureService.IsValidCulture(culture)) return false;
        Empire empire = kingdom.GetEmpire();
        bool whole = _wholeEmpire && empire?.data != null && !empire.isRekt() && !empire.IsArchived();
        string before = CurrentCustom(kingdom);
        (int kingdoms, int cities) = RealmCultureSystem.Convert(kingdom, culture, whole);
        if (kingdoms == 0)
        {
            WorldTip.showNow(string.Format(LM.Get("institution_culture_convert_failed"), TargetName()), false, "top", 3f);
            return false;
        }
        // 国家本已是这个文化(只把各城、成员国、旧制统一过来)：易俗改制那条日志不会记，这里补记一笔
        if (string.Equals(before, culture, StringComparison.Ordinal))
        {
            bool core = empire?.data != null && empire.CoreKingdom == kingdom && !empire.isRekt();
            Actor king = kingdom.king != null && !kingdom.king.isRekt() && kingdom.king.isAlive() ? kingdom.king : null;
            TranslateHelper.LogCultureReform(kingdom, core ? empire : null, king, culture, null);
        }
        string name = culture.GetCultureTranslate();
        WorldTip.showNow(whole
            ? string.Format(LM.Get("realm_culture_empire_done"), empire.GetEmpireName(), name, kingdoms, cities)
            : string.Format(LM.Get("realm_culture_kingdom_done"), kingdom.GetKingdomName(), name, cities), false, "top", 4f);
        return true;
    }

    // 回到打开本窗口的那个窗口(同返回键)；没有历史就直接开回去
    private static void Back()
    {
        if (WindowHistory.canReturnWindowBack())
        {
            WindowHistory.clickBack();
            return;
        }
        ScrollWindow.showWindow(_mode == Mode.Culture ? nameof(CultureInfoWindow) : nameof(InstitutionWindow));
    }
}

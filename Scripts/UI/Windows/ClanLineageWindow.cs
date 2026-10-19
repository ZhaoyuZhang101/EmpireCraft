using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 支派窗口(用户 2026-10-19："需要一个族谱分支系统，能查看这一支姓氏是怎么一步步传承下来的"，问答选"支派树加宗子世系")：
//   · 左栏：这一姓的支派树——始祖宗族往下各支(按分出先后，缩进表示层级)，右侧是在世人数，已绝嗣的置灰；点一支看它；
//   · 右栏：页头(支名 · 分支缘由 · 立支之年) → 主指标(开支祖、世代、在世、族人) → 源流(出自哪一支、谁的儿子、
//     哪年因何分出；从本支再分出去的各支，可点) → 宗子世系(一世开支祖到当代宗子逐代的名字、生卒、最高名号，
//     哪一代分出了哪一支)；点人打开他的宗族窗口。
// 宗子世系从当代族长(没有就取本支辈分最低的男丁，整支绝嗣取最后一代)顺着父亲往上追到开支祖；
// 族谱瘦身删去的先人追不上时，开支祖与断处之间标"中间失传"。
public class ClanLineageWindow : WideScrollWindow<ClanLineageWindow>
{
    private const int MaxRailBranches = 40, MaxDepthIndent = 4, MaxLineRows = 60;

    private static SpecificClan _pending;
    private SpecificClan _selected;
    private SpecificClan _root;
    // 支派树：按 DFS 顺序的(支, 层级)
    private readonly List<(SpecificClan clan, int depth)> _tree = new();
    private readonly Dictionary<long, List<SpecificClan>> _children = new();
    private readonly Dictionary<long, int> _living = new();
    private List<PersonalClanIdentity> _line = new();
    private bool _lineBroken;

    public static void ShowFor(SpecificClan clan)
    {
        if (clan == null) return;
        _pending = clan;
        ScrollWindow.showWindow(nameof(ClanLineageWindow));
    }

    protected override void OnOpen()
    {
        if (_pending != null) _selected = _pending;
        _pending = null;
    }

    // ───────── 数据 ─────────
    protected override void Prepare()
    {
        _tree.Clear();
        _children.Clear();
        _living.Clear();
        _line = new List<PersonalClanIdentity>();
        _lineBroken = false;
        if (_selected == null) return;
        _root = _selected.GetRootClan();
        foreach (SpecificClan clan in SpecificClanManager._specificClans.ToList())
        {
            if (clan == null || clan.parent_clan_id < 0 || clan.GetRootClan() != _root) continue;
            if (!_children.TryGetValue(clan.parent_clan_id, out List<SpecificClan> list))
                _children[clan.parent_clan_id] = list = new List<SpecificClan>();
            list.Add(clan);
        }
        foreach (List<SpecificClan> list in _children.Values)
            list.Sort((a, b) => a.established_timestamp.CompareTo(b.established_timestamp));
        Walk(_root, 0, new HashSet<long>());
        _line = MainLine(_selected, out _lineBroken);
    }

    private void Walk(SpecificClan clan, int depth, HashSet<long> seen)
    {
        if (clan == null || !seen.Add(clan.id)) return;
        _tree.Add((clan, depth));
        _living[clan.id] = clan.LivingPeople.Count;
        if (!_children.TryGetValue(clan.id, out List<SpecificClan> list)) return;
        foreach (SpecificClan child in list) Walk(child, depth + 1, seen);
    }

    // 宗子世系：从当代宗子往上追父亲到开支祖
    private static List<PersonalClanIdentity> MainLine(SpecificClan clan, out bool broken)
    {
        broken = false;
        var line = new List<PersonalClanIdentity>();
        PersonalClanIdentity founder = SpecificClanManager.getPerson(clan.founder);
        PersonalClanIdentity end = Heir(clan);
        var seen = new HashSet<long>();
        for (PersonalClanIdentity person = end; person != null && seen.Add(person.id) && line.Count < 400;)
        {
            line.Add(person);
            if (founder != null && person.id == founder.id) break;
            PersonalClanIdentity father = person.father > 0 ? SpecificClanManager.getPerson(person.father) : null;
            if (father == null || father.specific_clan_id != clan.id && (founder == null || father.id != founder.id)) break;
            person = father;
        }
        line.Reverse();
        if (founder != null && (line.Count == 0 || line[0].id != founder.id))
        {
            broken = line.Count > 0;
            line.Insert(0, founder);
        }
        return line;
    }

    // 当代宗子：在世的原版族长；没有就取辈分最低的在世男丁(年长的先)；整支绝嗣取辈分最低、最后去世的人
    private static PersonalClanIdentity Heir(SpecificClan clan)
    {
        List<PersonalClanIdentity> living = clan.LivingPeople;
        PersonalClanIdentity chief = living.Select(person => person._actor?.clan?.getChief())
            .Where(actor => actor != null && !actor.isRekt() && actor.GetSpecificClan() == clan)
            .Select(actor => actor.GetPersonalIdentity()).FirstOrDefault(person => person != null);
        if (chief != null) return chief;
        PersonalClanIdentity male = living.Where(person => person.sex == ActorSex.Male && !person.is_concubine)
            .OrderByDescending(person => person.generation).ThenByDescending(person => person.age).FirstOrDefault();
        if (male != null) return male;
        return clan.SnapshotPeople().Where(person => person != null && !person.is_concubine)
            .OrderByDescending(person => person.generation).ThenByDescending(person => person.deathday ?? "")
            .FirstOrDefault();
    }

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        if (_root == null)
        {
            rail.AddRailTitle(LM.Get("clan_lineage_window"));
            return;
        }
        int living = _living.Values.Sum();
        rail.AddRailTitle(_root.SurnameLine(), string.Format(LM.Get("clan_lineage_rail_summary"), _tree.Count, living));
        var menu = rail.BeginRailMenu(LM.Get("clan_lineage_branches"));
        int index = 0;
        foreach ((SpecificClan clan, int depth) in _tree.Take(MaxRailBranches))
        {
            int count = _living.TryGetValue(clan.id, out int n) ? n : 0;
            string indent = new string('　', Mathf.Min(depth, MaxDepthIndent)) + (depth > 0 ? "└" : "");
            string name = indent + BranchName(clan);
            if (count == 0) name = name.ColorString(WideKit.Muted);
            SpecificClan target = clan;
            menu.AddRailOption(null, name, count.ToString(), clan == _selected, () => Select(target), index++);
        }
        if (_tree.Count > MaxRailBranches)
            menu.AddRailText(string.Format(LM.Get("clan_lineage_more"), _tree.Count - MaxRailBranches).ColorString(WideKit.Muted), 6, 9f);
    }

    private void Select(SpecificClan clan)
    {
        _selected = clan;
        Refresh(resetScroll: true);
    }

    // 支名：始祖宗族写"本宗"，分支写分支称号
    private string BranchName(SpecificClan clan) =>
        clan.parent_clan_id < 0 || string.IsNullOrWhiteSpace(clan.branch_label)
            ? string.Format(LM.Get("clan_lineage_main"), clan.name)
            : clan.branch_label;

    private static string Reason(SpecificClan clan) =>
        string.IsNullOrEmpty(clan.branch_reason) ? "" : LM.Get("clan_branch_reason_" + clan.branch_reason);

    private static string Year(double time) =>
        time < 0d ? "?" : string.Format(LM.Get("clan_lineage_year"), Date.getYear(time));

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        if (_selected == null)
        {
            page.AddEmptyCard(PageWidth, LM.Get("clan_lineage_window"));
            return;
        }
        bool isRoot = _selected.parent_clan_id < 0;
        page.AddPageHeader(PageWidth, _selected.GetDisplayName(), isRoot ? LM.Get("clan_lineage_root") : Reason(_selected),
            Year(_selected.established_timestamp));

        PersonalClanIdentity founder = SpecificClanManager.getPerson(_selected.founder);
        int living = _living.TryGetValue(_selected.id, out int n) ? n : _selected.LivingPeople.Count;
        int generations = _line.Count == 0 ? 0 : Generation(_line[_line.Count - 1], founder, _line.Count);
        page.AddKpiRow(PageWidth,
            (LM.Get("clan_lineage_founder"), founder?.name ?? "?", WideKit.Gold),
            (LM.Get("clan_lineage_generations"), generations.ToString(), WideKit.Cyan),
            (LM.Get("clan_lineage_living"), living.ToString(), living > 0 ? WideKit.Green : WideKit.Red),
            (LM.Get("clan_lineage_members"), _selected.SnapshotPeople().Length.ToString(), WideKit.Value));

        DrawOrigin(page, founder, isRoot);
        DrawMainLine(page, founder);
    }

    private void DrawOrigin(AutoVertLayoutGroup page, PersonalClanIdentity founder, bool isRoot)
    {
        var card = page.BeginCard(PageWidth, LM.Get("clan_lineage_origin"));
        string text;
        if (isRoot)
            text = string.Format(LM.Get("clan_lineage_origin_root"), Year(_selected.established_timestamp), founder?.name ?? "?");
        else
        {
            SpecificClan parent = SpecificClanManager.Get(_selected.parent_clan_id);
            PersonalClanIdentity father = founder?.father > 0 ? SpecificClanManager.getPerson(founder.father) : null;
            text = string.Format(LM.Get("clan_lineage_origin_branch"), parent != null ? BranchName(parent) : "?",
                Year(_selected.established_timestamp), Reason(_selected), founder?.name ?? "?",
                father != null ? string.Format(LM.Get("clan_lineage_son_of"), father.name) : "");
        }
        card.AddTextIntoVertLayout(text.ColorString(WideKit.Soft), true, TextAnchor.UpperLeft, new Vector2(PageWidth - 12f, 22f))
            .UseFixedFontSize(7, HorizontalWrapMode.Wrap);
        if (!isRoot && SpecificClanManager.Get(_selected.parent_clan_id) is SpecificClan up)
            card.AddListRow(PageWidth, 0, null, LM.Get("clan_lineage_parent"), BranchName(up), () => Select(up), WideKit.Cyan, 60f);

        if (!_children.TryGetValue(_selected.id, out List<SpecificClan> subs) || subs.Count == 0) return;
        card.AddSectionHeader(PageWidth - 12f, string.Format(LM.Get("clan_lineage_sub_branches"), subs.Count), WideKit.Cyan, false);
        int index = 0;
        foreach (SpecificClan sub in subs)
        {
            PersonalClanIdentity subFounder = SpecificClanManager.getPerson(sub.founder);
            int count = _living.TryGetValue(sub.id, out int alive) ? alive : 0;
            string detail = string.Format(LM.Get("clan_lineage_sub_detail"), Year(sub.established_timestamp), Reason(sub),
                subFounder?.name ?? "?", count);
            SpecificClan target = sub;
            card.AddListRow(PageWidth, index++, null, BranchName(sub), detail, () => Select(target),
                count > 0 ? WideKit.Soft : WideKit.Muted, 90f);
        }
    }

    private void DrawMainLine(AutoVertLayoutGroup page, PersonalClanIdentity founder)
    {
        var card = page.BeginCard(PageWidth, LM.Get("clan_lineage_main_line"), WideKit.Gold,
            _line.Count == 0 ? null : string.Format(LM.Get("clan_lineage_generations_value"), _line.Count));
        if (_line.Count == 0)
        {
            card.AddTextIntoVertLayout(LM.Get("clan_lineage_no_line").ColorString(WideKit.Muted), true, TextAnchor.MiddleLeft,
                new Vector2(PageWidth - 12f, 14f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            return;
        }
        // 从本支分出去的各支，按开支祖的父亲挂在世系上
        var splits = new Dictionary<long, List<SpecificClan>>();
        if (_children.TryGetValue(_selected.id, out List<SpecificClan> subs))
            foreach (SpecificClan sub in subs)
            {
                PersonalClanIdentity subFounder = SpecificClanManager.getPerson(sub.founder);
                if (subFounder == null || subFounder.father <= 0) continue;
                if (!splits.TryGetValue(subFounder.father, out List<SpecificClan> list)) splits[subFounder.father] = list = new List<SpecificClan>();
                list.Add(sub);
            }
        int index = 0;
        IEnumerable<PersonalClanIdentity> shown = _line.Count > MaxLineRows
            ? _line.Take(1).Concat(_line.Skip(_line.Count - (MaxLineRows - 1)))
            : _line;
        PersonalClanIdentity previous = null;
        foreach (PersonalClanIdentity person in shown)
        {
            if (previous != null && (_lineBroken && previous == founder || _line.IndexOf(person) - _line.IndexOf(previous) > 1))
                card.AddListRow(PageWidth, index++, null, "…", LM.Get("clan_lineage_gap"), null, WideKit.Muted, 40f);
            previous = person;
            int generation = Generation(person, founder, _line.IndexOf(person) + 1);
            string title = string.Format(LM.Get("clan_lineage_generation"), generation, person.name);
            PersonalClanIdentity target = person;
            card.AddListRow(PageWidth, index++, null, title, Detail(person), () => OpenPerson(target),
                person.is_alive ? WideKit.Gold : WideKit.Soft, 96f);
            if (!splits.TryGetValue(person.id, out List<SpecificClan> branches)) continue;
            foreach (SpecificClan branch in branches)
            {
                PersonalClanIdentity subFounder = SpecificClanManager.getPerson(branch.founder);
                SpecificClan go = branch;
                card.AddListRow(PageWidth, index++, null, "　↳ " + BranchName(branch),
                    string.Format(LM.Get("clan_lineage_split_here"), subFounder?.name ?? "?", Year(branch.established_timestamp),
                        Reason(branch)), () => Select(go), WideKit.Cyan, 96f);
            }
        }
    }

    // 第几世：有辈分记录按与开支祖的辈分差，没有按世系里的序号
    private static int Generation(PersonalClanIdentity person, PersonalClanIdentity founder, int fallback) =>
        founder != null && person.generation > 0 && founder.generation > 0 && person.generation >= founder.generation
            ? person.generation - founder.generation + 1
            : fallback;

    // 生卒 · 最高名号(谥号、王号、封号、爵位、官职、功名，取最先有的)
    private static string Detail(PersonalClanIdentity person)
    {
        string life = person.is_alive
            ? string.Format(LM.Get("clan_lineage_life_alive"), person.birthday ?? "?", person.age)
            : string.Format(LM.Get("clan_lineage_life_dead"), person.birthday ?? "?", person.deathday ?? "?",
                person.recordedAge >= 0 ? person.recordedAge : person.age);
        string rank = new[]
        {
            person.posthumous_name, person.prince_title, person.ownedTitleNames?.FirstOrDefault(),
            UIHelper.LocalizeOrRaw(person.PeeragesLevel), person.fullOfficeName, person.officeName,
            UIHelper.LocalizeOrRaw(person.educationLevel)
        }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrEmpty(rank) ? life : life + " · " + rank;
    }

    private static void OpenPerson(PersonalClanIdentity person)
    {
        if (person == null) return;
        SpecificClanWindow.OpenIdentity = person;
        ScrollWindow.showWindow(nameof(SpecificClanWindow));
    }
}

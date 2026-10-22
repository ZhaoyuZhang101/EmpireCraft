using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 支派(原支派窗口并进来，用户 2026-10-19 要"族谱分支系统，能查看这一支姓氏是怎么一步步传承下来的")：
//   支派树(始祖宗族往下各支，按分出先后，缩进表示层级，右边在世人数，绝嗣的置灰；点一支看它) →
//   选中那支的主指标(开支祖、世代、在世、族人) → 源流(出自哪一支、谁的儿子、哪年因何分出；从本支再分出去的各支) →
//   宗子世系(一世开支祖到当代宗子逐代的名字、生卒、最高名号，哪一代分出了哪一支)。
// 宗子世系从当代族长(没有就取本支辈分最低的男丁，整支绝嗣取最后一代)顺着父亲往上追到开支祖；
// 族谱瘦身删去的先人追不上时，开支祖与断处之间标"中间失传"。
public partial class SpecificClanWindow
{
    private const int MaxTreeBranches = 40, MaxDepthIndent = 4, MaxLineRows = 60;

    // 支派页选中的支；空 = 当前宗族
    private SpecificClan _branch;
    private SpecificClan _root;
    // 支派树：按 DFS 顺序的(支, 层级)
    private readonly List<(SpecificClan clan, int depth)> _tree = new();
    private readonly Dictionary<long, List<SpecificClan>> _children = new();
    private readonly Dictionary<long, int> _branchLiving = new();
    private List<PersonalClanIdentity> _line = new();
    private bool _lineBroken;

    private SpecificClan Selected => _branch ?? _sc;

    // 左栏菜单上的支数：本宗往下一共几支(含本宗)
    private int BranchCount()
    {
        SpecificClan root = _sc?.GetRootClan();
        return root == null ? 0 : SpecificClanManager._specificClans.Count(clan => clan != null && clan.GetRootClan() == root);
    }

    private void PrepareBranches()
    {
        _tree.Clear();
        _children.Clear();
        _branchLiving.Clear();
        _line = new List<PersonalClanIdentity>();
        _lineBroken = false;
        if (Selected == null) return;
        _root = Selected.GetRootClan();
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
        _line = MainLine(Selected, out _lineBroken);
    }

    private void Walk(SpecificClan clan, int depth, HashSet<long> seen)
    {
        if (clan == null || !seen.Add(clan.id)) return;
        _tree.Add((clan, depth));
        _branchLiving[clan.id] = clan.LivingPeople.Count;
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
    internal static PersonalClanIdentity Heir(SpecificClan clan)
    {
        List<PersonalClanIdentity> living = clan.LivingPeople;
        PersonalClanIdentity chief = living.Select(person => person._actor?.clan?.getChief())
            .Where(actor => actor != null && !actor.isRekt() && actor.GetSpecificClan() == clan)
            .Select(actor => actor.GetPersonalIdentity()).FirstOrDefault(person => person != null);
        if (chief != null) return chief;
        PersonalClanIdentity male = living.Where(person => person.sex == ActorSex.Male && !person.is_concubine)
            .OrderByDescending(person => person.generation).ThenByDescending(person => person.age).FirstOrDefault();
        if (male != null) return male;
        return living.Where(person => !person.is_concubine).OrderByDescending(person => person.age).FirstOrDefault() ??
               clan.SnapshotPeople().Where(person => person != null && !person.is_concubine)
                   .OrderByDescending(person => person.generation).ThenByDescending(person => person.deathday ?? "")
                   .FirstOrDefault();
    }

    // 支名：始祖宗族写"本宗"，分支写分支称号
    private static string BranchName(SpecificClan clan) =>
        clan.parent_clan_id < 0 || string.IsNullOrWhiteSpace(clan.branch_label)
            ? string.Format(LM.Get("clan_lineage_main"), clan.name)
            : clan.branch_label;

    private static string Reason(SpecificClan clan) =>
        string.IsNullOrEmpty(clan.branch_reason) ? "" : LM.Get("clan_branch_reason_" + clan.branch_reason);

    private void SelectBranch(SpecificClan clan)
    {
        _branch = clan == _sc ? null : clan;
        Refresh(resetScroll: true);
    }

    private void DrawBranches(AutoVertLayoutGroup page)
    {
        SpecificClan selected = Selected;
        if (selected == null || _root == null)
        {
            page.AddEmptyCard(PageWidth, LM.Get("clan_lineage_window"));
            return;
        }
        page.AddPageHeader(PageWidth, LM.Get("clan_page_branches"), _root.SurnameLine(),
            string.Format(LM.Get("clan_lineage_rail_summary"), _tree.Count, _branchLiving.Values.Sum()));

        var tree = page.BeginCard(PageWidth, LM.Get("clan_lineage_branches"), WideKit.Gold, null, "ui/icons/iconFamily");
        int index = 0;
        foreach ((SpecificClan clan, int depth) in _tree.Take(MaxTreeBranches))
        {
            int count = _branchLiving.TryGetValue(clan.id, out int n) ? n : 0;
            string indent = new string('　', Mathf.Min(depth, MaxDepthIndent)) + (depth > 0 ? "└" : "");
            string detail = (depth == 0 ? LM.Get("clan_lineage_root") : Reason(clan)) + " · " + Year(clan.established_timestamp) +
                            " · " + (count > 0 ? string.Format(LM.Get("clan_kpi_people"), count) : LM.Get("clan_branch_extinct"));
            SpecificClan target = clan;
            tree.AddListRow(PageWidth, index++, null, indent + BranchName(clan), detail, () => SelectBranch(target),
                clan == selected ? WideKit.Gold : count > 0 ? WideKit.Soft : WideKit.Muted, 120f);
        }
        if (_tree.Count > MaxTreeBranches)
            tree.AddParagraph(PageWidth, string.Format(LM.Get("clan_lineage_more"), _tree.Count - MaxTreeBranches), 6, WideKit.Muted);

        bool isRoot = selected.parent_clan_id < 0;
        page.AddSectionHeader(PageWidth, selected.GetDisplayName(), WideKit.Gold, false, "ui/icons/iconFamily");
        PersonalClanIdentity founder = SpecificClanManager.getPerson(selected.founder);
        int living = _branchLiving.TryGetValue(selected.id, out int alive) ? alive : selected.LivingPeople.Count;
        int generations = _line.Count == 0 ? 0 : LineGeneration(_line[_line.Count - 1], founder, _line.Count);
        page.AddKpiRow(PageWidth,
            (LM.Get("clan_lineage_founder"), founder?.name ?? "?", WideKit.Gold),
            (LM.Get("clan_lineage_generations"), generations.ToString(), WideKit.Cyan),
            (LM.Get("clan_lineage_living"), living.ToString(), living > 0 ? WideKit.Green : WideKit.Red),
            (LM.Get("clan_lineage_members"), selected.SnapshotPeople().Length.ToString(), WideKit.Value));
        if (selected != _sc && Heir(selected) is PersonalClanIdentity heir)
            page.AddButtonRow(PageWidth + 12f, ("clan_branch_open", LM.Get("clan_branch_open"), () => Focus(heir, Page.Overview)));

        DrawOrigin(page, selected, founder, isRoot);
        DrawMainLine(page, selected, founder);
    }

    private void DrawOrigin(AutoVertLayoutGroup page, SpecificClan selected, PersonalClanIdentity founder, bool isRoot)
    {
        var card = page.BeginCard(PageWidth, LM.Get("clan_lineage_origin"));
        string text;
        if (isRoot)
            text = string.Format(LM.Get("clan_lineage_origin_root"), Year(selected.established_timestamp), founder?.name ?? "?");
        else
        {
            SpecificClan parent = SpecificClanManager.Get(selected.parent_clan_id);
            PersonalClanIdentity father = founder?.father > 0 ? SpecificClanManager.getPerson(founder.father) : null;
            text = string.Format(LM.Get("clan_lineage_origin_branch"), parent != null ? BranchName(parent) : "?",
                Year(selected.established_timestamp), Reason(selected), founder?.name ?? "?",
                father != null ? string.Format(LM.Get("clan_lineage_son_of"), father.name) : "");
        }
        card.AddParagraph(PageWidth, text, 7, WideKit.Soft);
        if (!isRoot && SpecificClanManager.Get(selected.parent_clan_id) is SpecificClan up)
            card.AddListRow(PageWidth, 0, null, LM.Get("clan_lineage_parent"), BranchName(up), () => SelectBranch(up), WideKit.Cyan, 60f);

        if (!_children.TryGetValue(selected.id, out List<SpecificClan> subs) || subs.Count == 0) return;
        card.AddSectionHeader(PageWidth - 12f, string.Format(LM.Get("clan_lineage_sub_branches"), subs.Count), WideKit.Cyan, false);
        int index = 0;
        foreach (SpecificClan sub in subs)
        {
            PersonalClanIdentity subFounder = SpecificClanManager.getPerson(sub.founder);
            int count = _branchLiving.TryGetValue(sub.id, out int alive) ? alive : 0;
            string detail = string.Format(LM.Get("clan_lineage_sub_detail"), Year(sub.established_timestamp), Reason(sub),
                subFounder?.name ?? "?", count);
            SpecificClan target = sub;
            card.AddListRow(PageWidth, index++, null, BranchName(sub), detail, () => SelectBranch(target),
                count > 0 ? WideKit.Soft : WideKit.Muted, 90f);
        }
    }

    private void DrawMainLine(AutoVertLayoutGroup page, SpecificClan selected, PersonalClanIdentity founder)
    {
        var card = page.BeginCard(PageWidth, LM.Get("clan_lineage_main_line"), WideKit.Gold,
            _line.Count == 0 ? null : string.Format(LM.Get("clan_lineage_generations_value"), _line.Count));
        if (_line.Count == 0)
        {
            card.AddParagraph(PageWidth, LM.Get("clan_lineage_no_line"), 7, WideKit.Muted);
            return;
        }
        // 从本支分出去的各支，按开支祖的父亲挂在世系上
        var splits = new Dictionary<long, List<SpecificClan>>();
        if (_children.TryGetValue(selected.id, out List<SpecificClan> subs))
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
            int generation = LineGeneration(person, founder, _line.IndexOf(person) + 1);
            string title = string.Format(LM.Get("clan_lineage_generation"), generation, person.name);
            PersonalClanIdentity target = person;
            card.AddListRow(PageWidth, index++, null, title, LineDetail(person), () => Focus(target, Page.Biography),
                person.is_alive ? WideKit.Gold : WideKit.Soft, 96f);
            if (!splits.TryGetValue(person.id, out List<SpecificClan> branches)) continue;
            foreach (SpecificClan branch in branches)
            {
                PersonalClanIdentity subFounder = SpecificClanManager.getPerson(branch.founder);
                SpecificClan go = branch;
                card.AddListRow(PageWidth, index++, null, "　↳ " + BranchName(branch),
                    string.Format(LM.Get("clan_lineage_split_here"), subFounder?.name ?? "?", Year(branch.established_timestamp),
                        Reason(branch)), () => SelectBranch(go), WideKit.Cyan, 96f);
            }
        }
    }

    // 第几世：有辈分记录按与开支祖的辈分差，没有按世系里的序号
    private static int LineGeneration(PersonalClanIdentity person, PersonalClanIdentity founder, int fallback) =>
        founder != null && person.generation > 0 && founder.generation > 0 && person.generation >= founder.generation
            ? person.generation - founder.generation + 1
            : fallback;

    // 生卒 · 最高名号(谥号、王号、封号、爵位、官职、功名，取最先有的)
    private static string LineDetail(PersonalClanIdentity person)
    {
        string rank = new[]
        {
            person.posthumous_name, person.prince_title, person.ownedTitleNames?.FirstOrDefault(),
            UIHelper.LocalizeOrRaw(person.PeeragesLevel), person.fullOfficeName, person.officeName,
            UIHelper.LocalizeOrRaw(person.educationLevel)
        }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrEmpty(rank) ? Life(person) : Life(person) + " · " + rank;
    }
}

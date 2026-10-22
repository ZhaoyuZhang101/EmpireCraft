using System;
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

// 族人名录：全族一张名单，按在世/已故/居官/全部筛、按声望/辈分/年龄排，可搜名字、官职、所在城；每页 20 人，点人看生平
public partial class SpecificClanWindow
{
    private enum MemberFilter { Living, Dead, Office, All }
    private enum MemberSort { Renown, Generation, Age }

    private const int MembersPerPage = 20;
    private MemberFilter _memberFilter = MemberFilter.Living;
    private MemberSort _memberSort = MemberSort.Renown;
    private string _memberSearch = "";
    private int _memberPage;

    private void DrawMembers(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, LM.Get("clan_page_members"),
            string.Format(LM.Get("clan_members_chip"), UIHelper.ShortCount(_living.Count), UIHelper.ShortCount(_people.Count)));
        page.AddChipRow(PageWidth,
            (LM.Get("clan_members_living"), _memberFilter == MemberFilter.Living, () => SetMemberFilter(MemberFilter.Living)),
            (LM.Get("clan_members_dead"), _memberFilter == MemberFilter.Dead, () => SetMemberFilter(MemberFilter.Dead)),
            (LM.Get("clan_members_office"), _memberFilter == MemberFilter.Office, () => SetMemberFilter(MemberFilter.Office)),
            (LM.Get("clan_members_all"), _memberFilter == MemberFilter.All, () => SetMemberFilter(MemberFilter.All)));
        var sorts = page.AddChipRow(PageWidth);
        sorts.AddTextIntoHoriLayout(LM.Get("clan_members_sort").ColorString(WideKit.Muted), true, TextAnchor.MiddleLeft,
            new Vector2(30f, 13f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        sorts.AddChip(LM.Get("clan_members_sort_renown"), _memberSort == MemberSort.Renown, () => SetMemberSort(MemberSort.Renown));
        sorts.AddChip(LM.Get("clan_members_sort_generation"), _memberSort == MemberSort.Generation,
            () => SetMemberSort(MemberSort.Generation));
        sorts.AddChip(LM.Get("clan_members_sort_age"), _memberSort == MemberSort.Age, () => SetMemberSort(MemberSort.Age));
        page.AddSearchInput(PageWidth, _memberSearch, text =>
        {
            string value = (text ?? "").Trim();
            if (value == _memberSearch) return;
            _memberSearch = value;
            _memberPage = 0;
            if (gameObject.activeInHierarchy) StartCoroutine(RefreshNextFrame());
        });

        List<PersonalClanIdentity> list = SortedMembers(FilteredMembers()).ToList();
        int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)MembersPerPage));
        _memberPage = Mathf.Clamp(_memberPage, 0, pages - 1);
        var card = page.BeginCard(PageWidth, LM.Get(_memberSearch.Length > 0 ? "title_search_result" : "clan_members_table"),
            WideKit.Gold, string.Format(LM.Get("clan_kpi_people"), list.Count), "ui/icons/iconPopulation");
        if (list.Count == 0)
            card.AddParagraph(PageWidth, LM.Get("clan_members_none"), 7, WideKit.Muted);
        int index = 0;
        foreach (PersonalClanIdentity person in list.Skip(_memberPage * MembersPerPage).Take(MembersPerPage))
        {
            PersonalClanIdentity target = person;
            string detail = string.Join(" · ", new[]
            {
                string.Format(LM.Get("clan_generation"), GenerationOf(person)), Life(person), Post(person), CityName(person)
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
            card.AddListRow(PageWidth, index++, null, person.name, detail, () => Focus(target, Page.Biography),
                person == _identity ? WideKit.Gold : !person.is_alive ? WideKit.Muted : OnOffice(person) ? WideKit.Gold : WideKit.Value,
                58f);
        }
        page.AddPager(PageWidth, _memberPage, pages, LM.Get("clan_pager"), target =>
        {
            _memberPage = target;
            Refresh(resetScroll: true);
        });
    }

    private IEnumerable<PersonalClanIdentity> FilteredMembers()
    {
        IEnumerable<PersonalClanIdentity> list = _memberFilter switch
        {
            MemberFilter.Living => _people.Where(person => person.is_alive),
            MemberFilter.Dead => _people.Where(person => !person.is_alive),
            MemberFilter.Office => _people.Where(OnOffice),
            _ => _people
        };
        if (_memberSearch.Length == 0) return list;
        return list.Where(person => Matches(person, _memberSearch));
    }

    private static bool Matches(PersonalClanIdentity person, string text) =>
        new[] { person.name, person.officeName, person.fullOfficeName, person.posthumous_name, person.prince_title, CityName(person) }
            .Any(value => !string.IsNullOrEmpty(value) && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);

    private IEnumerable<PersonalClanIdentity> SortedMembers(IEnumerable<PersonalClanIdentity> list) => _memberSort switch
    {
        MemberSort.Generation => list.OrderBy(person => person.generation).ThenByDescending(person => person.age),
        MemberSort.Age => list.OrderByDescending(person => person.is_alive).ThenByDescending(person => person.age),
        _ => list.OrderByDescending(person => person.is_alive).ThenByDescending(OnOffice).ThenByDescending(Renown)
            .ThenBy(person => person.generation)
    };

    private void SetMemberFilter(MemberFilter filter)
    {
        if (_memberFilter == filter) return;
        _memberFilter = filter;
        _memberPage = 0;
        Refresh(resetScroll: true);
    }

    private void SetMemberSort(MemberSort sort)
    {
        if (_memberSort == sort) return;
        _memberSort = sort;
        _memberPage = 0;
        Refresh(resetScroll: true);
    }
}

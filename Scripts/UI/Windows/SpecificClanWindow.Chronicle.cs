using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 宗族大事记(用户 2026-10-22 选)：一族的编年，新的在上，按年分组。
//   · 宗族一级：立族/分出、再分出各支、列为世家、门第升降一等、家道中落(clan_history，见 GentrySystem)、绝嗣；
//   · 族人一级：族人生平里的大事(登基、受封、入阁、拜相、权臣、政变、废立、绝罚、死因有记载的……)、名列名将名臣；
//   筛：全部 / 宗族 / 族人。点族人那条换成他、看生平。
public partial class SpecificClanWindow
{
    private enum ChronicleFilter { All, Clan, People }

    private const int ChronicleLimit = 120;

    // 族人生平里算大事的(没标 major 的老记录也认)
    private static readonly HashSet<string> MajorEvents = new()
    {
        "personal_history_became_emperor", "personal_history_peerage_granted", "personal_history_peerage_inherited",
        "personal_history_joined_cabinet", "personal_history_became_cabinet_leader", "personal_history_tributary_title_granted",
        "personal_history_powerful_minister_usurpation", "personal_history_powerful_minister_nine_bestowments",
        "personal_history_powerful_minister_duke", "personal_history_powerful_minister_controls_court",
        "personal_history_king_of_romans", "personal_history_installed_by_empress_dowager", "personal_history_imperial_ban",
        "personal_history_impeached", "personal_history_excommunicated", "personal_history_empress_dowager_deposed_emperor",
        "personal_history_deposed_by_powerful_minister", "personal_history_deposed_by_empress_dowager",
        "personal_history_coup_leader", "personal_history_coup_deposed", "personal_history_became_chief_elector",
        "personal_history_became_faction_leader", "personal_history_war_commander", "personal_history_culture_reform_surname"
    };

    private ChronicleFilter _chronicleFilter = ChronicleFilter.All;

    private sealed class ChronicleEntry
    {
        public double time;
        public string date;
        public string text;
        public string hex;
        public PersonalClanIdentity person;
        public bool clan;
    }

    private void DrawChronicle(AutoVertLayoutGroup page)
    {
        List<ChronicleEntry> all = BuildChronicle();
        int clanCount = all.Count(entry => entry.clan);
        page.AddPageHeader(PageWidth, LM.Get("clan_page_chronicle"), string.Format(LM.Get("clan_chronicle_chip"), all.Count, clanCount));
        page.AddChipRow(PageWidth,
            (LM.Get("clan_chronicle_all"), _chronicleFilter == ChronicleFilter.All, () => SetChronicleFilter(ChronicleFilter.All)),
            (LM.Get("clan_chronicle_clan"), _chronicleFilter == ChronicleFilter.Clan, () => SetChronicleFilter(ChronicleFilter.Clan)),
            (LM.Get("clan_chronicle_people"), _chronicleFilter == ChronicleFilter.People,
                () => SetChronicleFilter(ChronicleFilter.People)));

        List<ChronicleEntry> shown = all.Where(entry => _chronicleFilter == ChronicleFilter.All ||
                                                        entry.clan == (_chronicleFilter == ChronicleFilter.Clan)).ToList();
        var card = page.BeginCard(PageWidth, LM.Get("clan_chronicle_card"), WideKit.Gold,
            string.Format(LM.Get("clan_chronicle_count"), shown.Count), "ui/icons/iconWorldLog");
        if (shown.Count == 0)
        {
            card.AddParagraph(PageWidth, LM.Get("clan_chronicle_empty"), 7, WideKit.Muted);
            return;
        }
        int index = 0;
        foreach (IGrouping<string, ChronicleEntry> year in shown.Take(ChronicleLimit)
                     .GroupBy(entry => HistoryDateFormatter.GetYear(entry.time, entry.date)))
        {
            card.AddTextIntoVertLayout(year.Key.ColorString(WideKit.Gold), true, TextAnchor.MiddleLeft,
                new Vector2(PageWidth - 12f, 12f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
            foreach (ChronicleEntry entry in year)
            {
                PersonalClanIdentity person = entry.person;
                card.AddTimelineRow(PageWidth, index++, HistoryDateFormatter.GetMonthDay(entry.time, entry.date), entry.text,
                    entry.hex, person == null ? null : () => Focus(person, Page.Biography));
            }
        }
        if (shown.Count > ChronicleLimit)
            card.AddParagraph(PageWidth, string.Format(LM.Get("clan_chronicle_more"), shown.Count - ChronicleLimit), 6, WideKit.Muted);
    }

    private void SetChronicleFilter(ChronicleFilter filter)
    {
        if (_chronicleFilter == filter) return;
        _chronicleFilter = filter;
        Refresh(resetScroll: true);
    }

    private List<ChronicleEntry> BuildChronicle()
    {
        var entries = new List<ChronicleEntry>();
        void Clan(double time, string text, string hex, PersonalClanIdentity person = null)
        {
            if (time < 0d || string.IsNullOrWhiteSpace(text)) return;
            entries.Add(new ChronicleEntry { time = time, text = text, hex = hex, person = person, clan = true });
        }

        PersonalClanIdentity founder = SpecificClanManager.getPerson(_sc.founder);
        SpecificClan parent = _sc.parent_clan_id >= 0 ? SpecificClanManager.Get(_sc.parent_clan_id) : null;
        Clan(_sc.established_timestamp, parent == null
            ? string.Format(LM.Get("clan_chronicle_founded"), founder?.name ?? "?", _sc.GetDisplayName())
            : string.Format(LM.Get("clan_chronicle_branched"), founder?.name ?? "?", BranchName(parent), Reason(_sc)),
            WideKit.Gold, founder);
        foreach (SpecificClan sub in SpecificClanManager._specificClans.ToList()
                     .Where(clan => clan != null && clan.parent_clan_id == _sc.id))
        {
            PersonalClanIdentity subFounder = SpecificClanManager.getPerson(sub.founder);
            Clan(sub.established_timestamp, string.Format(LM.Get("clan_chronicle_sub_branch"), subFounder?.name ?? "?",
                BranchName(sub), Reason(sub)), "#C9A7E8", subFounder);
        }
        foreach (PersonalHistoryRecord record in _sc.clan_history ?? new List<PersonalHistoryRecord>())
            if (record != null)
                entries.Add(new ChronicleEntry
                {
                    time = record.timestamp, date = record.date, text = record.content, hex = WideKit.Orange, clan = true
                });
        if (_sc.extinct_since >= 0d && _living.Count == 0)
            Clan(_sc.extinct_since, string.Format(LM.Get("clan_chronicle_extinct"), _sc.GetDisplayName()), WideKit.Red);

        foreach (PersonalClanIdentity person in _people)
        {
            if (person.personal_history != null)
                foreach (PersonalHistoryRecord record in person.personal_history)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.content) ||
                        !record.major && !MajorEvents.Contains(record.event_key ?? "")) continue;
                    entries.Add(new ChronicleEntry
                    {
                        time = record.timestamp, date = record.date, person = person, hex = WideKit.Soft,
                        text = string.Format(LM.Get("clan_chronicle_person"), person.name, record.content)
                    });
                }
            TalentRecord talent = person.actor_id > 0 ? FamousTalentSystem.OfActorId(person.actor_id) : null;
            if (talent != null && talent.founded_at >= 0d)
                entries.Add(new ChronicleEntry
                {
                    time = talent.founded_at, person = person, hex = FamousTalentSystem.KindHex(talent.kind),
                    text = string.Format(LM.Get("clan_chronicle_talent"), person.name,
                        FamousTalentSystem.KindName(talent.kind), FamousTalentSystem.RankName(talent.rank))
                });
        }
        return entries.OrderByDescending(entry => entry.time).ToList();
    }
}

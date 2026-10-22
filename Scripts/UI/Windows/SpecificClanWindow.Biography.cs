using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.Events;

namespace EmpireCraft.Scripts.UI.Windows;

// 生平列传与相关记事：
//   · 生平列传：人物名牌(头像、姓名、名将名臣称号、生卒 · 籍贯 · 官职 · 爵位) → 列传(BiographySystem.Compose) |
//     恩怨与志向(恩主、门生、宿敌各一行，点人换成他；志向及成败；名将名臣的类别等第与称号) → 编年(按年分组，
//     涉及别人的那条点了换成他，帝国大事那条点了打开帝国史书)；
//   · 相关记事：别人生平里提到此人的记录，新的在上，点了换成记录的主人。
public partial class SpecificClanWindow
{
    private void DrawBiography(AutoVertLayoutGroup page)
    {
        PersonalClanIdentity person = _identity;
        person.BackfillCurrentEraNameHistory();
        person.BackfillChildBirthHistoryNames();
        DrawHero(page, person);

        string biography = BiographySystem.Compose(person);
        bool hasTies = person.ties != null && person.ties.Count > 0 || !string.IsNullOrEmpty(person.ambition) ||
                       FamousTalentSystem.OfActorId(person.actor_id) != null;
        if (!string.IsNullOrEmpty(biography) || hasTies)
        {
            var columns = page.BeginColumns(PageWidth);
            float width = !string.IsNullOrEmpty(biography) && hasTies ? UIHelper.PageColumnWidth : PageWidth;
            if (!string.IsNullOrEmpty(biography))
                columns.BeginColumnCard(width, LM.Get("biography_heading"), WideKit.Gold, null, "ui/icons/iconWorldInfo")
                    .AddParagraph(width, biography, 7, WideKit.Soft);
            if (hasTies)
                DrawTies(columns.BeginColumnCard(width, LM.Get("clan_card_ties"), WideKit.Gold, null, "ui/icons/iconFamily"), width,
                    person);
        }

        List<PersonalHistoryRecord> records = (person.personal_history ?? new List<PersonalHistoryRecord>())
            .Where(record => record != null).OrderBy(record => record.timestamp).ToList();
        var card = page.BeginCard(PageWidth, LM.Get("clan_card_annals"), WideKit.Gold,
            string.Format(LM.Get("clan_annals_count"), records.Count, records.Count(record => record.major)), "ui/icons/iconClock");
        if (records.Count == 0)
        {
            card.AddParagraph(PageWidth, LM.Get("personal_history_empty"), 7, WideKit.Muted);
            return;
        }
        int index = 0;
        foreach (IGrouping<string, PersonalHistoryRecord> year in records.GroupBy(record =>
                     HistoryDateFormatter.GetYear(record.timestamp, record.date)))
        {
            card.AddTextIntoVertLayout(year.Key.ColorString(WideKit.Gold), true, TextAnchor.MiddleLeft,
                new Vector2(PageWidth - 12f, 12f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
            foreach (PersonalHistoryRecord record in year)
            {
                string content = record.content ?? "";
                if (!string.IsNullOrWhiteSpace(record.kingdom_name)) content += " · " + record.kingdom_name;
                UnityAction history = HistoryAction(record);
                UnityAction other = PersonAction(record);
                card.AddTimelineRow(PageWidth, index++, HistoryDateFormatter.GetMonthDay(record.timestamp, record.date), content,
                    record.major ? WideKit.Value : WideKit.Soft, other ?? history,
                    extra: other != null ? history : null, extraIcon: SpriteTextureLoader.getSprite("ui/iconHistory"));
            }
        }
    }

    // 名牌：头像 + 姓名(名将名臣标签) + 一行身份
    private void DrawHero(AutoVertLayoutGroup page, PersonalClanIdentity person)
    {
        var card = page.BeginCard(PageWidth);
        float inner = PageWidth - 12f;
        var row = card.BeginHoriGroup(new Vector2(inner, 34f), TextAnchor.MiddleLeft, 5, new RectOffset(0, 0, 0, 0));
        row.AddIdentityAvatar(person, null, 32f);
        float textWidth = inner - 37f;
        var lines = row.BeginVertGroup(new Vector2(textWidth, 34f), pSpacing: 1, pAlignment: TextAnchor.MiddleLeft,
            pPadding: new RectOffset(0, 0, 0, 0));
        TalentRecord talent = FamousTalentSystem.OfActorId(person.actor_id);
        string title = person.name.ColorString(person.is_alive ? WideKit.Gold : WideKit.Soft);
        if (talent != null)
            title += "  " + (FamousTalentSystem.KindName(talent.kind) + " · " + FamousTalentSystem.RankName(talent.rank))
                .ColorString(FamousTalentSystem.KindHex(talent.kind));
        var name = lines.AddTextIntoVertLayout(title, true, TextAnchor.MiddleLeft, new Vector2(textWidth, 15f));
        name.UseFixedFontSize(11, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(name);
        string where = person.is_virtual ? person.birthplace : CityName(person);
        string peerage = UIHelper.LocalizeOrRaw(person.PeeragesLevel);
        string detail = string.Join(" · ", new[]
        {
            string.Format(LM.Get("clan_generation"), GenerationOf(person)), Life(person), where, Post(person), peerage,
            person.ownedTitleNames?.FirstOrDefault()
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var line = lines.AddTextIntoVertLayout(detail.ColorString(WideKit.Soft), true, TextAnchor.MiddleLeft, new Vector2(textWidth, 10f));
        line.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(line);
    }

    // 恩怨与志向：恩主/门生/宿敌一人一行(点了换成他)，志向，名将名臣称号
    private void DrawTies(AutoVertLayoutGroup card, float width, PersonalClanIdentity person)
    {
        int index = 0;
        foreach (PersonalTie tie in person.ties ?? new List<PersonalTie>())
        {
            PersonalClanIdentity other = SpecificClanManager.getPerson(tie?.other ?? -1L);
            if (tie == null || other == null) continue;
            (string label, string hex) = tie.kind switch
            {
                1 => (LM.Get("clan_tie_patron"), WideKit.Green),
                2 => (LM.Get("clan_tie_protege"), WideKit.Green),
                _ => (LM.Get("clan_tie_rival"), WideKit.Red)
            };
            string value = tie.kind == 3 && !string.IsNullOrEmpty(tie.cause)
                ? other.name + " · " + LM.Get("tie_cause_" + tie.cause)
                : other.name;
            card.AddValueRow(width, index++, label, value, hex, () => Focus(other), 36f);
        }
        if (!string.IsNullOrEmpty(person.ambition))
            card.AddValueRow(width, index++, LM.Get("clan_tie_ambition"),
                LM.Get("ambition_" + person.ambition) + (person.ambition_done ? " · " + LM.Get("clan_ambition_done")
                    : person.is_alive ? "" : " · " + LM.Get("clan_ambition_failed")),
                person.ambition_done ? WideKit.Green : person.is_alive ? WideKit.Gold : WideKit.Muted, null, 36f);
        TalentRecord talent = FamousTalentSystem.OfActorId(person.actor_id);
        if (talent != null)
        {
            string text = FamousTalentSystem.TitleText(talent);
            card.AddValueRow(width, index++, FamousTalentSystem.KindName(talent.kind),
                string.IsNullOrEmpty(text) ? FamousTalentSystem.RankName(talent.rank) : text,
                FamousTalentSystem.RankHex(talent.rank), () => ScrollWindow.showWindow(nameof(FamousTalentWindow)), 36f);
        }
        if (index == 0) card.AddParagraph(width, LM.Get("clan_ties_none"), 6, WideKit.Muted);
    }

    // 编年一条涉及别人：点了换成他
    private UnityAction PersonAction(PersonalHistoryRecord record)
    {
        PersonalClanIdentity related = record.related_personal_identity_id > 0
            ? SpecificClanManager.getPerson(record.related_personal_identity_id)
            : record.related_actor_id > 0 ? World.world.units.get(record.related_actor_id)?.GetPersonalIdentity() : null;
        return related != null && related != _identity && related._specificClan != null ? () => Focus(related) : null;
    }

    // 编年一条是帝国大事：打开帝国史书
    private static UnityAction HistoryAction(PersonalHistoryRecord record)
    {
        Empire empire = record.empire_id > 0 ? ModClass.EMPIRE_MANAGER.get(record.empire_id) : null;
        if (empire?.data == null || empire.IsArchived()) return null;
        EmpireCraftHistory history = empire.data.currentHistory ?? empire.data.history?.LastOrDefault();
        if (history == null) return null;
        return () =>
        {
            EmpireCraftMetaTypeLibrary.selected_empire = empire;
            ConfigData.CURRENT_SELECTED_HISTORY = history;
            ScrollWindow.showWindow(nameof(EmpireHistoryWindow));
        };
    }

    private void DrawRelated(AutoVertLayoutGroup page)
    {
        _identity.BackfillRelatedHistoryRecords();
        List<PersonalHistoryRecord> records = (_identity.related_history_records ?? new List<PersonalHistoryRecord>())
            .Where(record => record != null).OrderByDescending(record => record.timestamp).ToList();
        page.AddPageHeader(PageWidth, LM.Get("clan_page_related"), string.Format(LM.Get("clan_relatives_chip"), _identity.name),
            string.Format(LM.Get("clan_chronicle_count"), records.Count));
        var card = page.BeginCard(PageWidth, LM.Get("clan_page_related"), WideKit.Gold, null, "ui/icons/iconWorldLog");
        if (records.Count == 0)
        {
            card.AddParagraph(PageWidth, LM.Get("related_history_empty"), 7, WideKit.Muted);
            return;
        }
        int index = 0;
        foreach (PersonalHistoryRecord record in records)
        {
            PersonalClanIdentity owner = record.owner_personal_identity_id > 0
                ? SpecificClanManager.getPerson(record.owner_personal_identity_id)
                : null;
            string ownerName = string.IsNullOrWhiteSpace(owner?.name) ? LM.Get("related_history_owner_unknown") : owner.name;
            string date = HistoryDateFormatter.GetYear(record.timestamp, record.date) + " " +
                          HistoryDateFormatter.GetMonthDay(record.timestamp, record.date);
            card.AddTimelineRow(PageWidth, index++, date,
                string.Format(LM.Get("clan_chronicle_person"), ownerName, record.content ?? ""), WideKit.Soft,
                owner == null ? null : () => Focus(owner, Page.Biography), 60f);
        }
    }
}

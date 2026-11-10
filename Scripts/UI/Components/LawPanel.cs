using System.Collections.Generic;
using System;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 律法页(帝国窗口的「律法」页签与独立王国的律法窗口共用，见 EmpireWindow.Law、KingdomLawWindow)，
// 用户 2026-11 "律法系统感觉很不完善"：界面上看不到本国有哪些律条、谁有前科、最近判了什么案。按论处的主体
// (帝国之内是帝国核心国，见 EmpireLawSystem.RealmKingdom)画：
//   · 执法：宽严(悬停看构成与后果) · 司法(悬停看构成) · 在押人数 · 上次大赦；八议赎刑、死刑、刑制；司法官与在任者(点人打开)；
//   · 现行律条：按门类分组，律名(悬停看律文)、现行刑罚(时代、政体改过的写"杖刑→监禁"，悬停看缘由)、十恶标记；
//   · 近来卷宗：全境新的在前，时间、人犯(点开在世的人，已故的打开生平)、罪名、结果、刑罚，悬停看案由与量刑说明；
//   · 在押之人：罪名、余下刑期(帝国之内注明押在哪一国)。
// 未发觉的罪行(GetPendingCrimes)是秘密，不在这里显示。人物生平里的刑狱一栏见 RenderPerson
public static class LawPanel
{
    private const float CardPadding = 12f;
    private const int MaxCaseRows = 30;
    private const int MaxPrisonerRows = 30;

    // 页头摘要：执法与否、现行律条数
    public static string[] Chips(Kingdom kingdom)
    {
        Kingdom realm = EmpireLawSystem.RealmKingdom(kingdom);
        int laws = realm == null ? 0 : EmpireLawSystem.GetEffectiveLaws(realm).Count;
        return new[]
        {
            LM.Get(EmpireLawSystem.Enabled ? "law_chip_on" : "law_chip_off"),
            string.Format(LM.Get("law_chip_laws"), laws)
        };
    }

    public static void Render(AutoVertLayoutGroup page, Kingdom kingdom, float width)
    {
        Kingdom realm = EmpireLawSystem.RealmKingdom(kingdom);
        if (page == null) return;
        if (realm == null)
        {
            page.AddEmptyCard(width, "kov_gone");
            return;
        }
        if (!EmpireLawSystem.Enabled)
        {
            string hint = LM.Get("law_off_hint");
            float height = Mathf.Max(10f, UIHelper.EstimateWrappedLines(hint, 7, width - 4f) * 9f + 2f);
            page.AddTextIntoVertLayout(hint.ColorString(WideKit.Orange), true, TextAnchor.UpperLeft, new Vector2(width - 4f, height))
                .UseFixedFontSize(7, HorizontalWrapMode.Wrap);
        }
        PenalView view = PenalView.Of(realm);
        List<EffectiveLawInfo> laws = EmpireLawSystem.GetEffectiveLaws(realm);
        List<PersonalClanIdentity> prisoners = RealmPrisoners(realm);
        DrawOverview(page, realm, width, view, laws, prisoners.Count);
        DrawLaws(page, realm, width, view, laws);
        DrawCases(page, realm, width);
        DrawPrisoners(page, realm, width, prisoners);
    }

    #region 执法

    private static void DrawOverview(AutoVertLayoutGroup page, Kingdom realm, float width, PenalView view,
        List<EffectiveLawInfo> laws, int prisonerCount)
    {
        var card = page.BeginCard(width, "law_card_overview", WideKit.Gold, null, "ui/icons/iconWorldLaws", "ui/icons/iconBooks");
        LawStanceInfo stance = EmpireLawSystem.GetLawStance(realm, true);
        JudicialStrengthInfo judicial = EmpireLawSystem.GetJudicialStrength(realm, true);

        // 第一排：宽严 · 司法 · 在押 · 上次大赦
        AutoHoriLayoutGroup first = BeginMetricRow(card, width);
        float cell = (width - CardPadding) / 4f;
        // 格子窄：格里只写宽严，数值写在悬停标题里
        string stanceName = EmpireLawSystem.GetStanceName(stance.Tier);
        Tip(MetricCell(first, cell, LM.Get("law_kpi_stance"), stanceName, StanceHex(stance.Tier)), $"law_stance_{realm.id}",
            string.Format(LM.Get("law_tip_stance_title"), $"{stanceName} {Signed(stance.Severity)}"), StanceTip(stance));
        Tip(MetricCell(first, cell, LM.Get("law_kpi_judicial"), $"×{judicial.Factor:0.00}",
                judicial.Factor >= 1.05f ? WideKit.Green : judicial.Factor < 0.85f ? WideKit.Orange : WideKit.Value),
            $"law_judicial_{realm.id}", string.Format(LM.Get("law_tip_judicial_title"), $"×{judicial.Factor:0.00}"), JudicialTip(judicial));
        MetricCell(first, cell, LM.Get("law_kpi_prisoners"), string.Format(LM.Get("law_people_count"), prisonerCount),
            prisonerCount > 0 ? WideKit.Orange : WideKit.Muted);
        double amnesty = EmpireLawSystem.GetLastAmnesty(realm);
        bool amnestied = amnesty >= 0d && World.world != null && World.world.getCurWorldTime() >= amnesty;
        int amnestyYears = amnestied ? Date.getYearsSince(amnesty) : 0;
        string amnestyValue = !amnestied ? LM.Get("law_amnesty_never")
            : amnestyYears <= 0 ? LM.Get("law_this_year") : string.Format(LM.Get("law_years_ago"), amnestyYears);
        Tip(MetricCell(first, cell, LM.Get("law_kpi_amnesty"), amnestyValue, amnestied ? WideKit.Gold : WideKit.Muted),
            $"law_amnesty_{realm.id}", LM.Get("law_kpi_amnesty"),
            (amnestied ? Date.getDate(amnesty) + "\n" : "") + LM.Get("law_tip_amnesty_note"));

        // 第二排：八议赎刑 · 死刑 · 刑制(悬停看本国律条因时代、政体改了哪些)
        AutoHoriLayoutGroup second = BeginMetricRow(card, width);
        cell = (width - CardPadding) / 3f;
        Tip(MetricCell(second, cell, LM.Get("law_kpi_privilege"), LM.Get(view.Modern ? "law_privilege_off" : "law_privilege_on"),
                view.Modern ? WideKit.Muted : WideKit.Gold),
            $"law_privilege_{realm.id}", LM.Get("law_kpi_privilege"), LM.Get(view.Modern ? "law_tip_privilege_modern" : "law_tip_privilege"));
        string deathTip = view.AbolishDeath
            ? string.Format(LM.Get("law_tip_death_abolished"), (EmpireLawSystem.Eras ?? EmpireLawEraConfig.Defaults()).LifeImprisonmentYears)
            : view.Modern ? LM.Get("law_tip_death_kept_modern") : "";
        Tip(MetricCell(second, cell, LM.Get("law_kpi_death"), LM.Get(view.AbolishDeath ? "law_death_abolished" : "law_death_kept"),
            view.AbolishDeath ? WideKit.Green : WideKit.Red), $"law_death_{realm.id}", LM.Get("law_kpi_death"), deathTip);
        string culture = TechnologySystem.GetCultureOf(realm);
        string era = string.IsNullOrEmpty(culture) ? "" : TechnologySystem.GetEraName(culture);
        string penal = view.Modern ? LM.Get("law_penal_modern") : string.IsNullOrWhiteSpace(era) ? LM.Get("law_penal_classic") : era;
        List<string> adaptations = laws.SelectMany(item => item.AdaptationNotes ?? new List<string>()).Distinct()
            .Select(EmpireLawSystem.FormatNote).Where(text => !string.IsNullOrEmpty(text)).ToList();
        Tip(MetricCell(second, cell, LM.Get("law_kpi_penal"), penal, view.Modern ? WideKit.Cyan : WideKit.Value),
            $"law_penal_{realm.id}", LM.Get("law_kpi_penal"),
            adaptations.Count > 0 ? string.Join("\n", adaptations) : LM.Get("law_penal_none_adapted"));

        // 司法官与在任者(没有司法官的国家由君主亲断)
        if (judicial.HasJudicialOffices)
        {
            int index = 0;
            foreach (JudicialOfficeInfo office in judicial.Offices)
            {
                Actor holder = office.Holder;
                string name = holder != null ? holder.getName() : LM.Get("law_office_vacant");
                string hex = holder == null ? WideKit.Muted : office.Score >= 0.7f ? WideKit.Green : office.Score >= 0.45f ? WideKit.Value
                    : WideKit.Orange;
                card.AddListRow(width, index++, null, office.Office?.GetName() ?? "?", name.ColorString(hex),
                    holder == null ? null : () => OpenActor(holder), WideKit.Soft, 96f);
            }
        }
        else
        {
            Actor ruler = EmpireLawSystem.RealmRuler(realm);
            string label = judicial.Breakdown.Count > 0 ? judicial.Breakdown[0].Label : LM.Get("law_judicial_none");
            card.AddListRow(width, 0, null, LM.Get("law_judge_label"), label, ruler == null ? null : () => OpenActor(ruler),
                WideKit.Soft, 96f);
        }
    }

    // 宽严的悬停：各项构成(加减)，再写后果(犯罪多少、量刑加减一等的机会、罚金刑期、恩赦)
    private static string StanceTip(LawStanceInfo stance)
    {
        var lines = stance.Breakdown.Select(factor => $"{factor.Label}  {Signed(factor.Value)}").ToList();
        if (lines.Count == 0) lines.Add(LM.Get("law_tip_stance_none"));
        lines.Add("");
        lines.Add(string.Format(LM.Get("law_tip_crime_rate"), stance.CrimeRateFactor.ToString("0.00")));
        // 宽严在量刑天平上的砝码：严苛压罪责、宽仁压情面(见 SentencingScale)
        if (stance.Severity > 0.001f)
            lines.Add(string.Format(LM.Get("law_tip_shift_harsh"), SentencingScale.HarshFactor(stance.Severity).ToString("0.00")));
        else if (stance.Severity < -0.001f)
            lines.Add(string.Format(LM.Get("law_tip_shift_lenient"), SentencingScale.LenientFactor(stance.Severity).ToString("0.00")));
        lines.Add(string.Format(LM.Get("law_tip_fine_prison"), stance.FineFactor.ToString("0.00"), stance.PrisonFactor.ToString("0.00")));
        if (stance.AmnestyChance > 0f) lines.Add(string.Format(LM.Get("law_tip_amnesty_chance"), $"{stance.AmnestyChance:P0}"));
        if (stance.Tier == LawStanceTier.严苛) lines.Add(LM.Get("law_tip_harsh_note"));
        return string.Join("\n", lines);
    }

    private static string JudicialTip(JudicialStrengthInfo judicial)
    {
        var lines = judicial.Breakdown.Select(factor => $"{factor.Label}  ×{factor.Value:0.00}").ToList();
        lines.Add(LM.Get("law_tip_judicial_note"));
        return string.Join("\n", lines);
    }

    private static string StanceHex(LawStanceTier tier) =>
        tier == LawStanceTier.严苛 ? WideKit.Red : tier == LawStanceTier.宽仁 ? WideKit.Green : WideKit.Value;

    private static string Signed(float value) => (value >= 0f ? "+" : "") + value.ToString("0.00");

    #endregion

    #region 现行律条

    private static void DrawLaws(AutoVertLayoutGroup page, Kingdom realm, float width, PenalView view, List<EffectiveLawInfo> laws)
    {
        var card = page.BeginCard(width, "law_card_laws", WideKit.Gold,
            string.Format(LM.Get("law_laws_header"), laws.Count, laws.Count(item => item.Grave)), "ui/icons/iconBooks", "ui/icons/iconWorldLaws");
        if (laws.Count == 0)
        {
            card.AddParagraph(width, LM.Get("law_laws_none"), 7, WideKit.Muted);
            return;
        }
        const float NameWidth = 76f, GraveWidth = 22f;
        AddColumnsRow(card, width, -1, (LM.Get("law_col_law"), null, NameWidth, TextAnchor.MiddleLeft),
            (LM.Get("law_col_punishment"), null, 0f, TextAnchor.MiddleLeft), (LM.Get("law_col_grave"), null, GraveWidth, TextAnchor.MiddleRight));
        int index = 0;
        foreach (IGrouping<LawCategory, EffectiveLawInfo> group in laws.GroupBy(item => item.Category).OrderBy(entry => (int)entry.Key))
        {
            card.AddTextIntoVertLayout(CategoryName(group.Key).ColorString(WideKit.Cyan), true, TextAnchor.MiddleLeft,
                new Vector2(width - CardPadding, 11f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            foreach (EffectiveLawInfo law in group.OrderBy(item => (int)item.Type))
            {
                List<(PunishmentLevel before, PunishmentLevel after, List<string> notes)> changes;
                string punishments = PunishmentsText(law, view, out changes);
                var row = AddColumnsRow(card, width, index++, (law.Name, WideKit.Value, NameWidth, TextAnchor.MiddleLeft),
                    (punishments, WideKit.Value, 0f, TextAnchor.MiddleLeft),
                    (law.Grave ? LM.Get("law_grave_mark") : "", WideKit.Red, GraveWidth, TextAnchor.MiddleRight));
                Tip(row.gameObject, $"law_row_{realm.id}_{(int)law.Type}",
                    law.Grave ? $"{law.Name}  {LM.Get("law_grave_mark").ColorString(WideKit.Red)}" : law.Name, LawTip(law, changes));
            }
        }
    }

    // 律名悬停：律文；改过的写原律与每一处改动的缘由；十恶、株连
    private static string LawTip(EffectiveLawInfo law, List<(PunishmentLevel before, PunishmentLevel after, List<string> notes)> changes)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(law.Description)) lines.Add(law.Description);
        if (changes.Count > 0)
        {
            lines.Add(string.Format(LM.Get("law_tip_original"), EmpireLawSystem.GetPunishmentsText(law.BasePunishments)));
            foreach ((PunishmentLevel before, PunishmentLevel after, List<string> notes) in changes)
                lines.Add(string.Format(LM.Get("law_tip_change"), EmpireLawSystem.GetPunishmentName(before), EmpireLawSystem.GetPunishmentName(after),
                    string.Join(Separator, notes.Select(EmpireLawSystem.FormatNote).Where(text => !string.IsNullOrEmpty(text)))));
        }
        else if (law.AdaptationNotes != null && law.AdaptationNotes.Count > 0)
        {
            // 对不上逐项的改法(规则改了)：只写原律与说明
            lines.Add(string.Format(LM.Get("law_tip_original"), EmpireLawSystem.GetPunishmentsText(law.BasePunishments)));
            lines.AddRange(law.AdaptationNotes.Select(EmpireLawSystem.FormatNote).Where(text => !string.IsNullOrEmpty(text)));
        }
        if (law.Grave) lines.Add(LM.Get("law_tip_grave"));
        if (law.Law?.AffectDescendants == true) lines.Add(LM.Get("law_tip_descendants"));
        return string.Join("\n", lines);
    }

    // 现行刑罚：照律施行的照写；时代、政体改过的写"原刑→今刑"(原刑灰、今刑橙，致死的红)。
    // 逐项改法与 EmpireLawSystem 的 Adapt 同一套规则(EmpireLawEras)；对不上现行刑罚时退回"原律 → 现行"整串
    private static string PunishmentsText(EffectiveLawInfo law, PenalView view,
        out List<(PunishmentLevel before, PunishmentLevel after, List<string> notes)> changes)
    {
        changes = new List<(PunishmentLevel before, PunishmentLevel after, List<string> notes)>();
        var mapped = new List<(PunishmentLevel before, PunishmentLevel after, List<string> notes)>();
        foreach (PunishmentLevel punishment in law.BasePunishments ?? new List<PunishmentLevel>())
        {
            if (punishment == PunishmentLevel.无罪) continue;
            var notes = new List<string>();
            mapped.Add((punishment, Adapt(punishment, view, notes), notes));
        }
        var effective = new HashSet<PunishmentLevel>((law.EffectivePunishments ?? new List<PunishmentLevel>())
            .Where(punishment => punishment != PunishmentLevel.无罪));
        if (mapped.Count == 0) return LM.Get("law_outcome_no_punishment");
        if (!effective.SetEquals(mapped.Select(item => item.after).Where(punishment => punishment != PunishmentLevel.无罪)))
            return EmpireLawSystem.GetPunishmentsText(law.BasePunishments).ColorString(WideKit.Muted) + " → ".ColorString(WideKit.Muted) +
                   EmpireLawSystem.GetPunishmentsText(law.EffectivePunishments).ColorString(WideKit.Orange);
        var targets = new HashSet<PunishmentLevel>(mapped.Where(item => item.before != item.after).Select(item => item.after));
        var parts = new List<string>();
        foreach ((PunishmentLevel before, PunishmentLevel after, List<string> notes) in mapped)
        {
            string part;
            if (before == after)
            {
                // 别的刑罚改成了它：只在改动那一处写
                if (targets.Contains(after)) continue;
                part = EmpireLawSystem.GetPunishmentName(after).ColorString(IsLethal(after) ? WideKit.Red : WideKit.Value);
            }
            else
            {
                changes.Add((before, after, notes));
                part = EmpireLawSystem.GetPunishmentName(before).ColorString(WideKit.Muted) + "→".ColorString(WideKit.Muted) +
                       (after == PunishmentLevel.无罪 ? LM.Get("law_outcome_no_punishment") : EmpireLawSystem.GetPunishmentName(after))
                       .ColorString(IsLethal(after) ? WideKit.Red : WideKit.Orange);
            }
            if (!parts.Contains(part)) parts.Add(part);
        }
        return string.Join(Separator, parts);
    }

    private static string CategoryName(LawCategory category)
    {
        string key = "law_category_" + category;
        return LM.Has(key) ? LM.Get(key) : category.ToString();
    }

    #endregion

    #region 近来卷宗

    private static void DrawCases(AutoVertLayoutGroup page, Kingdom realm, float width)
    {
        List<LawCaseRecord> cases = EmpireLawSystem.GetRealmRecentCases(realm, MaxCaseRows);
        var card = page.BeginCard(width, "law_card_cases", WideKit.Gold, string.Format(LM.Get("law_cases_header"), cases.Count),
            "ui/icons/iconWorldLog", "ui/icons/iconClock");
        if (cases.Count == 0)
        {
            card.AddParagraph(width, LM.Get("law_cases_none"), 7, WideKit.Muted);
            return;
        }
        const float DateWidth = 40f, PersonWidth = 62f, CrimeWidth = 58f, OutcomeWidth = 34f;
        AddColumnsRow(card, width, -1, (LM.Get("law_col_date"), null, DateWidth, TextAnchor.MiddleLeft),
            (LM.Get("law_col_person"), null, PersonWidth, TextAnchor.MiddleLeft), (LM.Get("law_col_crime"), null, CrimeWidth, TextAnchor.MiddleLeft),
            (LM.Get("law_col_outcome"), null, OutcomeWidth, TextAnchor.MiddleLeft), (LM.Get("law_col_sentence"), null, 0f, TextAnchor.MiddleLeft));
        for (int i = 0; i < cases.Count; i++)
        {
            LawCaseRecord record = cases[i];
            if (record == null) continue;
            bool amnesty = record.outcome == LawCaseOutcome.大赦;
            string crime = record.Crime is LawType type ? EmpireLawSystem.GetLawName(type) : "—";
            string sentence = amnesty
                ? string.Format(LM.Get("law_note_amnesty_released"), record.count)
                : EmpireLawSystem.GetPunishmentsText(record.punishments);
            UnityAction open = PersonAction(record.actor_id, record.identity_id);
            var row = AddColumnsRow(card, width, i, (YearText(record.timestamp, record.date), WideKit.Cyan, DateWidth, TextAnchor.MiddleLeft),
                (record.actor_name, open != null ? WideKit.Gold : WideKit.Soft, PersonWidth, TextAnchor.MiddleLeft),
                (crime, record.Crime is LawType grave && EmpireLawSystem.IsGraveCrime(grave) ? WideKit.Red : WideKit.Value, CrimeWidth,
                    TextAnchor.MiddleLeft),
                (EmpireLawSystem.GetOutcomeName(record.outcome), OutcomeHex(record.outcome), OutcomeWidth, TextAnchor.MiddleLeft),
                (sentence, SentenceHex(record.punishments, amnesty), 0f, TextAnchor.MiddleLeft));
            if (open != null) UIHelper.MakeClickable(row.gameObject, open);
            var tip = new List<string> { FullDate(record.timestamp, record.date) };
            tip.Add(string.Format(LM.Get("empire_law_outcome_detail"), EmpireLawSystem.GetOutcomeName(record.outcome), sentence));
            string notes = EmpireLawSystem.GetCaseNoteText(record);
            if (!string.IsNullOrEmpty(notes)) tip.Add(notes);
            // 量刑天平分两段：罪责、情面各列砝码(用户 2026-11-10 选"卷宗悬停分两栏")
            string scale = CaseScaleText(record);
            if (!string.IsNullOrEmpty(scale)) tip.Add(scale);
            Tip(row.gameObject, $"law_case_{realm.id}_{i}", amnesty ? LM.Get("law_outcome_大赦") : $"{record.actor_name} · {crime}",
                string.Join("\n", tip.Where(line => !string.IsNullOrEmpty(line))));
        }
    }

    private static string OutcomeHex(LawCaseOutcome outcome) => outcome switch
    {
        LawCaseOutcome.减等 => WideKit.Green,
        LawCaseOutcome.开释 => WideKit.Green,
        LawCaseOutcome.赎罪 => WideKit.Cyan,
        LawCaseOutcome.大赦 => WideKit.Gold,
        LawCaseOutcome.从重 => WideKit.Red,
        _ => WideKit.Value
    };

    private static string SentenceHex(List<PunishmentLevel> punishments, bool amnesty) =>
        amnesty ? WideKit.Gold : punishments != null && punishments.Any(IsLethal) ? WideKit.Red : WideKit.Soft;

    #endregion

    #region 在押之人

    private static void DrawPrisoners(AutoVertLayoutGroup page, Kingdom realm, float width, List<PersonalClanIdentity> prisoners)
    {
        var card = page.BeginCard(width, "law_card_prisoners", WideKit.Gold, string.Format(LM.Get("law_people_count"), prisoners.Count),
            "ui/icons/iconWorldLaws", "ui/icons/iconBooks");
        if (prisoners.Count == 0)
        {
            card.AddParagraph(width, LM.Get("law_prisoners_none"), 7, WideKit.Muted);
            return;
        }
        bool empire = EmpireLawSystem.RealmEmpire(realm) != null;
        for (int i = 0; i < prisoners.Count && i < MaxPrisonerRows; i++)
        {
            PersonalClanIdentity person = prisoners[i];
            LawImprisonment imprisonment = person.law_imprisonment;
            string crime = imprisonment != null && imprisonment.law_type >= 0 ? EmpireLawSystem.GetLawName((LawType)imprisonment.law_type) : "?";
            string detail = string.Format(LM.Get("law_prisoner_detail"), crime, YearsLeft(imprisonment));
            if (empire)
            {
                Kingdom held = imprisonment != null ? World.world.kingdoms.get(imprisonment.kingdom_id) : null;
                if (held?.data != null && !held.isRekt() && held != realm) detail += " · " + held.GetKingdomName();
            }
            if (imprisonment?.grave == true) detail += " · " + LM.Get("law_prisoner_grave").ColorString(WideKit.Red);
            UnityAction open = PersonAction(person.actor_id, person.id);
            card.AddListRow(width, i, null, person.name, detail, open, open != null ? WideKit.Gold : WideKit.Soft, 80f);
        }
        if (prisoners.Count > MaxPrisonerRows)
            card.AddTextIntoVertLayout(string.Format(LM.Get("law_more"), prisoners.Count - MaxPrisonerRows).ColorString(WideKit.Muted),
                true, TextAnchor.MiddleLeft, new Vector2(width - CardPadding, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
    }

    // 全境在押的人：本国与(帝国之内)各成员国的狱中，新下狱的在前
    public static List<PersonalClanIdentity> RealmPrisoners(Kingdom realm)
    {
        var result = new List<PersonalClanIdentity>();
        if (realm?.data == null || realm.isRekt()) return result;
        var seen = new HashSet<long>();
        var scope = new List<Kingdom> { realm };
        Empire empire = EmpireLawSystem.RealmEmpire(realm);
        if (empire?.kingdoms_list != null)
            scope.AddRange(empire.kingdoms_list.Where(member => member?.data != null && !member.isRekt() && member != realm));
        foreach (Kingdom kingdom in scope)
        foreach (PersonalClanIdentity person in EmpireLawSystem.GetPrisoners(kingdom))
            if (person != null && seen.Add(person.id)) result.Add(person);
        return result.OrderByDescending(person => person.law_imprisonment?.since ?? -1d).ToList();
    }

    // 还要关几年(与 EmpireLawSystem.GetImprisonmentYearsLeft 同算法，按族谱身份算)
    public static int YearsLeft(LawImprisonment imprisonment) =>
        imprisonment == null ? 0 : Mathf.Max(1, imprisonment.years - Date.getYearsSince(imprisonment.since));

    #endregion

    #region 人物的刑狱

    // 人物生平里的刑狱一栏：在押写"在押，还有 N 年"(罪名、押在哪国)，下面是前科(时间、国、罪名、刑罚，悬停看结果)。
    // 没有前科、也不在押的人不画
    public static void RenderPerson(AutoVertLayoutGroup page, PersonalClanIdentity person, float width)
    {
        if (page == null || person == null) return;
        List<LawConvictionRecord> convictions = EmpireLawSystem.GetConvictions(person);
        LawImprisonment imprisonment = person.is_alive && EmpireLawSystem.IsImprisoned(person) ? person.law_imprisonment : null;
        if (convictions.Count == 0 && imprisonment == null) return;
        var card = page.BeginCard(width, "law_person_card", WideKit.Orange,
            imprisonment != null ? LM.Get("law_person_imprisoned_tag") : string.Format(LM.Get("law_person_convictions"), convictions.Count),
            "ui/icons/iconWorldLaws", "ui/icons/iconBooks");
        if (imprisonment != null)
        {
            var parts = new List<string> { string.Format(LM.Get("law_person_imprisoned"), YearsLeft(imprisonment)).ColorString(WideKit.Orange) };
            if (imprisonment.law_type >= 0) parts.Add(EmpireLawSystem.GetLawName((LawType)imprisonment.law_type).ColorString(WideKit.Value));
            Kingdom held = World.world?.kingdoms?.get(imprisonment.kingdom_id);
            if (held?.data != null && !held.isRekt()) parts.Add(held.GetKingdomName().ColorString(WideKit.Soft));
            if (imprisonment.grave) parts.Add(LM.Get("law_prisoner_grave").ColorString(WideKit.Red));
            var line = card.AddTextIntoVertLayout(string.Join(" · ".ColorString(WideKit.Muted), parts), true, TextAnchor.MiddleLeft,
                new Vector2(width - CardPadding, 12f));
            line.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(line);
        }
        if (convictions.Count == 0) return;
        const float DateWidth = 40f, RealmWidth = 66f, CrimeWidth = 66f;
        AddColumnsRow(card, width, -1, (LM.Get("law_col_date"), null, DateWidth, TextAnchor.MiddleLeft),
            (LM.Get("law_col_realm"), null, RealmWidth, TextAnchor.MiddleLeft), (LM.Get("law_col_crime"), null, CrimeWidth, TextAnchor.MiddleLeft),
            (LM.Get("law_col_sentence"), null, 0f, TextAnchor.MiddleLeft));
        for (int i = 0; i < convictions.Count; i++)
        {
            LawConvictionRecord record = convictions[i];
            if (record == null) continue;
            string crime = record.Crime is LawType type ? EmpireLawSystem.GetLawName(type) : "—";
            string sentence = EmpireLawSystem.GetPunishmentsText(record.punishments);
            Kingdom kingdom = World.world?.kingdoms?.get(record.kingdom_id);
            string realmName = kingdom?.data != null && !kingdom.isRekt() ? kingdom.GetKingdomName() : record.kingdom_name;
            var row = AddColumnsRow(card, width, i, (YearText(record.timestamp, record.date), WideKit.Cyan, DateWidth, TextAnchor.MiddleLeft),
                (realmName, WideKit.Soft, RealmWidth, TextAnchor.MiddleLeft),
                (crime, record.Crime is LawType grave && EmpireLawSystem.IsGraveCrime(grave) ? WideKit.Red : WideKit.Value, CrimeWidth,
                    TextAnchor.MiddleLeft),
                (sentence, SentenceHex(record.punishments, false), 0f, TextAnchor.MiddleLeft));
            Tip(row.gameObject, $"law_conviction_{person.id}_{i}", crime,
                FullDate(record.timestamp, record.date) + "\n" +
                string.Format(LM.Get("empire_law_outcome_detail"), EmpireLawSystem.GetOutcomeName(record.outcome), sentence));
        }
    }

    #endregion

    #region 量刑天平

    private const int MaxScaleRows = 24;

    // 人物生平的「量刑天平」卡(用户 2026-11-10 选"人物页加天平卡")：在世、受律法管的人才画。
    // 页头写"若论罪：减三等"；指标行罪责 · 情面 · 天平；两栏表列两边的砝码；末尾写十恶、暴君、宗人府的规矩。
    // 预览按寻常罪名(不是十恶)、未发之罪的赃额、没有告发者算(见 EmpireLawSystem.GetScale)
    public static void RenderScale(AutoVertLayoutGroup page, PersonalClanIdentity person, float width, Action refresh = null)
    {
        Actor actor = person?._actor;
        if (page == null || actor?.data == null || actor.isRekt() || !actor.isAlive() || !EmpireLawSystem.Enabled) return;
        Kingdom kingdom = actor.kingdom;
        if (kingdom?.data == null || kingdom.isRekt() || EmpireLawSystem.RealmKingdom(kingdom) == null) return;
        if (EmpireLawSystem.RealmRuler(kingdom) == actor)
        {
            page.BeginCard(width, "law_scale_card", WideKit.Gold, LM.Get("law_scale_ruler_exempt"), "ui/icons/iconWorldLaws")
                .AddParagraph(width, LM.Get("law_scale_ruler_note"), 7, WideKit.Muted);
            return;
        }
        SentencingScaleInfo info = EmpireLawSystem.GetScale(actor, kingdom);
        var card = page.BeginCard(width, "law_scale_card", WideKit.Gold,
            string.Format(LM.Get("law_scale_if_tried"), EmpireLawSystem.ShiftText(info.Shift)), "ui/icons/iconWorldLaws");
        DrawScaleBody(card, width, info.Guilt, info.Mercy, info.GuiltWeight, info.MercyWeight, info.Shift, info.MercyIgnored);
        // 名望(影响力砝码的来源)：现名望、每年按身份记多少(见 RenownSystem)
        var parts = new List<(string key, int value)>();
        int income = RenownSystem.Income(actor, parts);
        string sources = parts.Count == 0 ? LM.Get("renown_income_none")
            : string.Join(Separator, parts.Select(part => $"{LM.Get(part.key)} {part.value}"));
        card.AddParagraph(width, string.Format(LM.Get("renown_income_line"), actor.renown, income, sources), 6, WideKit.Soft);
        var rules = new List<string>();
        if (info.Tyrant) rules.Add(LM.Get("law_scale_rule_tyrant"));
        if (info.Royal) rules.Add(LM.Get(info.Heir ? "law_scale_rule_heir" : "law_scale_rule_royal"));
        rules.Add(LM.Get("law_scale_rule_general"));
        card.AddParagraph(width, string.Join("\n", rules), 6, WideKit.Muted);
        RenderDivine(page, actor, width, refresh);
    }

    private static bool _addingCrime;

    // 神力添加(用户 2026-11-10 "量刑玩家也要能添加，比如添加罪名、添加保释条件什么的"，见 EmpireLawSystem.Divine)：
    // 添罪名、保释、自定砝码、当场开审；点完 refresh 重画
    private static void RenderDivine(AutoVertLayoutGroup page, Actor actor, float width, Action refresh)
    {
        List<CrimeRecord> pending = EmpireLawSystem.PendingCrimes(actor);
        var card = page.BeginCard(width, "law_divine_title", WideKit.Gold,
            string.Format(LM.Get("law_divine_pending"), pending.Count), "ui/icons/iconWorldLaws");
        card.AddParagraph(width, LM.Get("law_divine_hint"), 6, WideKit.Muted);
        if (pending.Count > 0)
            card.AddParagraph(width, string.Format(LM.Get("law_divine_pending_list"), string.Join(Separator, pending.Select(record =>
                EmpireLawSystem.GetLawName((LawType)record.law_type) + (record.amount > 0 ? $"({record.amount})" : "")))), 6, WideKit.Soft);
        card.AddActionRow(width - CardPadding,
            (LM.Get("law_divine_add_crime"), _addingCrime ? WideKit.Gold : WideKit.Cyan, true, () =>
            {
                _addingCrime = !_addingCrime;
                refresh?.Invoke();
            }),
            (string.Format(LM.Get("law_divine_trial"), pending.Count), WideKit.Red, pending.Count > 0, () =>
            {
                EmpireLawSystem.GodTrial(actor);
                refresh?.Invoke();
            }));
        if (_addingCrime)
        {
            card.AddParagraph(width, LM.Get("law_divine_crime_pick"), 6, WideKit.Muted);
            List<LawType> crimes = EmpireLawSystem.AddableCrimes(actor);
            for (int start = 0; start < crimes.Count; start += 6)
                card.AddChipRow(width - CardPadding, crimes.Skip(start).Take(6).Select(type =>
                    (EmpireLawSystem.GetLawName(type), pending.Any(record => record.law_type == (int)type), (UnityAction)(() =>
                    {
                        EmpireLawSystem.GodAddCrime(actor, type);
                        refresh?.Invoke();
                    }))).ToArray());
        }

        // 保释
        LawBail bail = actor.GetPersonalIdentity()?.law_bail;
        string state = bail == null ? LM.Get("law_divine_bail_none") : bail.revoked ? LM.Get("law_divine_bail_revoked")
            : string.Join(Separator, bail.conditions.Select(condition => BailText(actor, bail, condition)));
        card.AddValueRow(width, 0, LM.Get("law_divine_bail"), state, bail?.revoked == true ? WideKit.Red : WideKit.Soft);
        var bailChips = EmpireLawSystem.BailConditions.Select(condition =>
        {
            bool on = bail?.conditions?.Contains(condition) == true;
            string reason = on ? null : EmpireLawSystem.CannotBail(actor, condition);
            string label = BailText(actor, bail, condition) + (reason == null ? "" : $"({LM.Get(reason)})");
            return (label, on, (UnityAction)(() =>
            {
                EmpireLawSystem.GodBail(actor, condition);
                refresh?.Invoke();
            }));
        }).ToList();
        if (bail != null)
            bailChips.Add((LM.Get("law_bail_revoke"), false, (UnityAction)(() =>
            {
                EmpireLawSystem.GodRevokeBail(actor);
                refresh?.Invoke();
            })));
        card.AddChipRow(width - CardPadding, bailChips.ToArray());

        // 自定砝码：点一下换一档(×1.25 → ×1.5 → ×2 → 去掉)
        card.AddParagraph(width, LM.Get("law_divine_custom"), 6, WideKit.Muted);
        card.AddChipRow(width - CardPadding, CustomChips(actor, EmpireLawSystem.CustomGuiltKeys, true, refresh));
        card.AddChipRow(width - CardPadding, CustomChips(actor, EmpireLawSystem.CustomMercyKeys, false, refresh));
    }

    private static (string label, bool on, UnityAction action)[] CustomChips(Actor actor, string[] keys, bool guilt, Action refresh) =>
        keys.Select(key =>
        {
            float current = EmpireLawSystem.CustomWeight(actor, key);
            bool on = Mathf.Abs(current - 1f) > 0.001f;
            string label = LM.Get("law_custom_" + key) + (on ? $" ×{current:0.##}" : "");
            return (label, on, (UnityAction)(() =>
            {
                float[] tiers = EmpireLawSystem.CustomFactors;
                int index = Array.FindIndex(tiers, tier => Mathf.Abs(tier - current) < 0.001f);
                // 没加的上第一档，已到最高档的再点去掉(同一档再点即去掉)
                float next = index < 0 ? tiers[0] : index + 1 < tiers.Length ? tiers[index + 1] : current;
                EmpireLawSystem.GodCustomWeight(actor, key, guilt, next);
                refresh?.Invoke();
            }));
        }).ToArray();

    private static string BailText(Actor actor, LawBail bail, string condition) => condition switch
    {
        "bond" => string.Format(LM.Get("law_bail_bond"), bail?.bond > 0 ? bail.bond : EmpireLawSystem.BailBond),
        "guarantor" => string.Format(LM.Get("law_bail_guarantor"), bail != null && bail.guarantor >= 0
            ? SpecificClanManager.getPerson(bail.guarantor)?.name ?? ""
            : EmpireLawSystem.FindGuarantor(actor)?.name ?? ""),
        _ => LM.Get("law_bail_" + condition)
    };

    // 指标行(罪责 · 情面 · 天平) + 两栏表(左罪责、右情面，一行一个砝码)
    private static void DrawScaleBody(AutoVertLayoutGroup card, float width, List<LawFactor> guilt, List<LawFactor> mercy,
        float guiltWeight, float mercyWeight, int shift, bool mercyIgnored)
    {
        AutoHoriLayoutGroup metrics = BeginMetricRow(card, width);
        float cell = (width - CardPadding) / 3f;
        MetricCell(metrics, cell, LM.Get("law_scale_guilt"), $"×{guiltWeight:0.00}", guiltWeight > 1.001f ? WideKit.Red : WideKit.Value);
        MetricCell(metrics, cell, LM.Get("law_scale_mercy"),
            mercyIgnored && mercy.Count > 0 ? $"×{mercyWeight:0.00} {LM.Get("law_scale_tip_ignored")}" : $"×{mercyWeight:0.00}",
            mercyIgnored ? WideKit.Muted : mercyWeight > 1.001f ? WideKit.Green : WideKit.Value);
        MetricCell(metrics, cell, LM.Get("law_scale_verdict"), EmpireLawSystem.ShiftText(shift),
            shift > 0 ? WideKit.Red : shift < 0 ? WideKit.Green : WideKit.Value);
        int rows = Mathf.Max(guilt.Count, mercy.Count);
        if (rows == 0)
        {
            card.AddParagraph(width, LM.Get("law_scale_no_weights"), 7, WideKit.Muted);
            return;
        }
        float half = (width - CardPadding - 6f) / 2f;
        const float ValueWidth = 30f;
        AddColumnsRow(card, width, -1, (LM.Get("law_scale_guilt"), null, half - ValueWidth, TextAnchor.MiddleLeft),
            ("", null, ValueWidth, TextAnchor.MiddleRight), (LM.Get("law_scale_mercy"), null, half - ValueWidth, TextAnchor.MiddleLeft),
            ("", null, 0f, TextAnchor.MiddleRight));
        for (int i = 0; i < rows; i++)
        {
            LawFactor left = i < guilt.Count ? guilt[i] : null;
            LawFactor right = i < mercy.Count ? mercy[i] : null;
            AddColumnsRow(card, width, i,
                (left?.Label ?? "", WideKit.Soft, half - ValueWidth, TextAnchor.MiddleLeft),
                (left != null ? $"×{left.Value:0.00}" : "", left != null && left.Value > 1f ? WideKit.Red : WideKit.Green, ValueWidth,
                    TextAnchor.MiddleRight),
                (right?.Label ?? "", mercyIgnored ? WideKit.Muted : WideKit.Soft, half - ValueWidth, TextAnchor.MiddleLeft),
                (right != null ? $"×{right.Value:0.00}" : "", mercyIgnored ? WideKit.Muted : right != null && right.Value < 1f ? WideKit.Red : WideKit.Green,
                    0f, TextAnchor.MiddleRight));
        }
    }

    // 律法的「量刑天平」页(用户 2026-11-10 选"律法页加天平分页")：本朝要人——储君、宗室、藩王、在朝的官、宠臣、有爵封的——
    // 各自若论寻常罪名的天平读数，情面最重(最动不得)的在前；点人打开，悬停看两边的砝码
    public static void RenderScaleBoard(AutoVertLayoutGroup page, Kingdom kingdom, float width)
    {
        Kingdom realm = EmpireLawSystem.RealmKingdom(kingdom);
        if (page == null) return;
        if (realm == null)
        {
            page.AddEmptyCard(width, "kov_gone");
            return;
        }
        var intro = page.BeginCard(width, "law_scale_board", WideKit.Gold, null, "ui/icons/iconWorldLaws");
        intro.AddParagraph(width, LM.Get("law_scale_board_intro"), 7, WideKit.Soft);
        intro.AddParagraph(width, LM.Get("law_scale_rule_general") + "\n" + LM.Get("law_scale_rule_royal") + "\n" +
                                  LM.Get("law_scale_rule_heir"), 6, WideKit.Muted);

        List<(Actor actor, string role, SentencingScaleInfo info)> people = ScaleBoardPeople(realm);
        var card = page.BeginCard(width, "law_scale_board_people", WideKit.Gold,
            string.Format(LM.Get("law_people_count"), people.Count), "ui/icons/iconPopulation");
        if (people.Count == 0)
        {
            card.AddParagraph(width, LM.Get("law_scale_board_none"), 7, WideKit.Muted);
            return;
        }
        const float RoleWidth = 46f, WeightWidth = 38f, VerdictWidth = 40f;
        AddColumnsRow(card, width, -1, (LM.Get("law_col_person"), null, 0f, TextAnchor.MiddleLeft),
            (LM.Get("law_scale_role"), null, RoleWidth, TextAnchor.MiddleLeft),
            (LM.Get("law_scale_guilt"), null, WeightWidth, TextAnchor.MiddleRight),
            (LM.Get("law_scale_mercy"), null, WeightWidth, TextAnchor.MiddleRight),
            (LM.Get("law_scale_verdict"), null, VerdictWidth, TextAnchor.MiddleRight));
        for (int i = 0; i < people.Count; i++)
        {
            (Actor actor, string role, SentencingScaleInfo info) = people[i];
            var row = AddColumnsRow(card, width, i, (actor.getName(), WideKit.Gold, 0f, TextAnchor.MiddleLeft),
                (role, WideKit.Soft, RoleWidth, TextAnchor.MiddleLeft),
                ($"×{info.GuiltWeight:0.00}", info.GuiltWeight > 1.001f ? WideKit.Red : WideKit.Value, WeightWidth, TextAnchor.MiddleRight),
                ($"×{info.MercyWeight:0.00}", info.MercyIgnored ? WideKit.Muted : info.MercyWeight > 1.001f ? WideKit.Green : WideKit.Value,
                    WeightWidth, TextAnchor.MiddleRight),
                (EmpireLawSystem.ShiftText(info.Shift), info.Shift > 0 ? WideKit.Red : info.Shift < 0 ? WideKit.Green : WideKit.Value,
                    VerdictWidth, TextAnchor.MiddleRight));
            Actor captured = actor;
            UIHelper.MakeClickable(row.gameObject, () => OpenActor(captured));
            Tip(row.gameObject, $"law_scale_row_{realm.id}_{i}", $"{actor.getName()} · {role}",
                ScaleTipText(info.Guilt, info.Mercy, info.GuiltWeight, info.MercyWeight, info.Shift, info.MercyIgnored));
        }
    }

    // 要人：本朝(帝国连同成员国)在世的成年人里，储君、宗室、藩王与成员国君主、在任官员、城主、宠臣、有爵封的；君主本人不论
    private static List<(Actor actor, string role, SentencingScaleInfo info)> ScaleBoardPeople(Kingdom realm)
    {
        var result = new List<(Actor actor, string role, SentencingScaleInfo info)>();
        if (World.world?.units == null) return result;
        Empire empire = EmpireLawSystem.RealmEmpire(realm);
        var kingdoms = new HashSet<Kingdom> { realm };
        if (empire?.kingdoms_list != null)
            foreach (Kingdom member in empire.kingdoms_list)
                if (member?.data != null && !member.isRekt()) kingdoms.Add(member);
        Actor ruler = EmpireLawSystem.RealmRuler(realm);
        Actor heir = realm.HasHeir() ? realm.GetHeir() : null;
        Actor favourite = empire != null ? FavouriteSystem.Of(empire) : null;
        foreach (Actor actor in World.world.units)
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult() || actor == ruler || actor.IsWarMachine()) continue;
            if (actor.kingdom == null || !kingdoms.Contains(actor.kingdom)) continue;
            bool royal = actor == heir || EmpireLawSystem.IsRoyalKin(actor, realm);
            string role = actor == heir ? LM.Get("law_scale_heir")
                : actor == favourite ? LM.Get("law_scale_role_favourite")
                : actor.isKing() ? LM.Get("law_scale_role_king")
                : royal ? LM.Get("law_scale_royal_kin")
                : actor.IsOnOffice() ? LM.Get("law_scale_role_official")
                : actor.isCityLeader() ? LM.Get("law_scale_role_city")
                : actor.HasTitle() || actor.GetPeeragesLevel() != PeeragesLevel.peerages_6 ? LM.Get("law_scale_role_noble")
                : null;
            if (role == null) continue;
            result.Add((actor, role, EmpireLawSystem.GetScale(actor, actor.kingdom)));
        }
        return result.OrderByDescending(item => item.info.MercyWeight / Mathf.Max(0.01f, item.info.GuiltWeight))
            .ThenByDescending(item => item.actor.renown).Take(MaxScaleRows).ToList();
    }

    // 悬停：两边的砝码各一段，末行天平
    private static string ScaleTipText(List<LawFactor> guilt, List<LawFactor> mercy, float guiltWeight, float mercyWeight, int shift,
        bool mercyIgnored)
    {
        var lines = new List<string>
        {
            string.Format(LM.Get("law_scale_tip_guilt"), guiltWeight.ToString("0.00"),
                guilt.Count == 0 ? "—" : EmpireLawSystem.WeightsText(guilt)),
            string.Format(LM.Get("law_scale_tip_mercy"), mercyWeight.ToString("0.00"),
                mercy.Count == 0 ? "—" : EmpireLawSystem.WeightsText(mercy)) + (mercyIgnored && mercy.Count > 0 ? " " + LM.Get("law_scale_tip_ignored") : ""),
            string.Format(LM.Get("law_scale_tip_verdict"), EmpireLawSystem.ShiftText(shift))
        };
        return string.Join("\n", lines);
    }

    // 卷宗悬停里的天平(存在卷宗上的砝码，见 EmpireLawSystem.FinishCase)；没过天平的旧卷宗为空
    private static string CaseScaleText(LawCaseRecord record)
    {
        if (record == null || (record.scale_guilt == null || record.scale_guilt.Count == 0) &&
            (record.scale_mercy == null || record.scale_mercy.Count == 0)) return null;
        return ScaleTipText(EmpireLawSystem.ScaleFactors(record.scale_guilt), EmpireLawSystem.ScaleFactors(record.scale_mercy),
            record.scale_guilt_weight, record.scale_mercy_weight, record.scale_shift, record.scale_mercy_ignored);
    }

    #endregion

    #region 部件与小工具

    // 本国的时代与政体(与 EmpireLawSystem 的 PenalRegime 同口径)
    private struct PenalView
    {
        public Kingdom Realm;
        public int EraTier;
        public bool Modern;
        public bool AbolishDeath;

        public static PenalView Of(Kingdom realm)
        {
            bool modern = EmpireLawSystem.IsModernLaw(realm);
            return new PenalView
            {
                Realm = realm,
                EraTier = EmpireLawSystem.GetEraTier(realm),
                Modern = modern,
                AbolishDeath = modern && !EmpireLawSystem.KeepsDeathPenalty(realm)
            };
        }
    }

    // 一种刑罚按时代、政体改成什么：直接问律法核心(同一套 EmpireLawEras 规则)；notes 收集用到的说明键
    private static PunishmentLevel Adapt(PunishmentLevel punishment, PenalView view, List<string> notes) =>
        EmpireLawSystem.AdaptPunishment(view.Realm, punishment, notes);

    private static bool IsLethal(PunishmentLevel punishment) =>
        punishment == PunishmentLevel.死刑 || punishment == PunishmentLevel.夷三族;

    private static string Separator => LM.Has("law_list_separator") ? LM.Get("law_list_separator") : ", ";

    // 表格里只写年(格子窄)，悬停写全日期
    private static string YearText(double timestamp, string date) =>
        timestamp >= 0d ? string.Format(LM.Get("clan_lineage_year"), Date.getYear(timestamp)) : date ?? "";

    private static string FullDate(double timestamp, string date) =>
        !string.IsNullOrEmpty(date) ? date : timestamp >= 0d ? Date.getDate(timestamp) : "";

    // 点人：在世的打开人物窗口，已故的(有族谱)打开生平列传
    private static UnityAction PersonAction(long actorId, long identityId)
    {
        Actor actor = actorId >= 0L ? World.world?.units?.get(actorId) : null;
        if (actor?.data != null && !actor.isRekt() && actor.isAlive()) return () => OpenActor(actor);
        PersonalClanIdentity identity = identityId >= 0L ? SpecificClanManager.getPerson(identityId) : null;
        if (identity?._specificClan == null) return null;
        return () => SpecificClanWindow.ShowFor(identity, SpecificClanWindow.Page.Biography);
    }

    private static void OpenActor(Actor actor)
    {
        if (actor?.data == null || actor.isRekt() || !actor.isAlive()) return;
        UIHelper.actorClick(actor);
    }

    // 指标行(同 WideKit.AddMetricRow)：深色内嵌底，格子另行加(要给格子挂悬停)
    private static AutoHoriLayoutGroup BeginMetricRow(AutoVertLayoutGroup card, float cardWidth)
    {
        float inner = cardWidth - CardPadding;
        var row = card.BeginHoriGroup(new Vector2(inner, 22f), TextAnchor.MiddleLeft, 0, new RectOffset(0, 0, 0, 0));
        UIHelper.AddInsetBackground(row, new Vector2(inner, 22f));
        return row;
    }

    // 一格指标，返回这一格(挂悬停用)
    private static GameObject MetricCell(AutoHoriLayoutGroup row, float width, string label, string value, string hex)
    {
        UIHelper.AddMetricCell(row, width, label, value, hex);
        return row.transform.GetChild(row.transform.childCount - 1).gameObject;
    }

    // 悬停说明：没有图形的组补一张透明图接收指针
    private static void Tip(GameObject target, string key, string title, string body)
    {
        if (target == null || string.IsNullOrWhiteSpace(body)) return;
        if (target.GetComponent<Graphic>() == null)
        {
            Image hitArea = target.AddComponent<Image>();
            hitArea.color = Color.clear;
        }
        UIHelper.AttachTextTooltip(target, key, title, body);
    }

    // 表格行：各列定宽(宽度 ≤ 0 的一列占余下的宽度)，左对齐、不折行(过长悬停滚动)；index 为 -1 是表头(灰色小字、不垫底)，否则隔行垫底
    private static AutoHoriLayoutGroup AddColumnsRow(AutoVertLayoutGroup card, float cardWidth, int index,
        params (string text, string hex, float width, TextAnchor anchor)[] cells)
    {
        bool header = index < 0;
        float inner = cardWidth - CardPadding;
        float height = header ? 11f : 14f;
        var row = card.BeginHoriGroup(new Vector2(inner, height), TextAnchor.MiddleLeft, 2, new RectOffset(3, 3, 0, 0));
        if (!header && index % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(inner, height));
        float fixedWidth = cells.Where(cell => cell.width > 0f).Sum(cell => cell.width);
        float rest = Mathf.Max(10f, inner - 6f - 2f * Mathf.Max(0, cells.Length - 1) - fixedWidth);
        foreach ((string text, string hex, float width, TextAnchor anchor) in cells)
        {
            string color = header ? WideKit.Muted : string.IsNullOrEmpty(hex) ? WideKit.Value : hex;
            var label = row.AddTextIntoHoriLayout((text ?? "").ColorString(color), true, anchor,
                new Vector2(width > 0f ? width : rest, 12f));
            label.UseFixedFontSize(header ? 6 : 7, HorizontalWrapMode.Overflow);
            if (!header) HoverMarqueeText.Attach(label);
        }
        return row;
    }

    #endregion
}

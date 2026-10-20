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
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 帝国议会页(封建制帝国，见 HolyRomanSystem)：
    //   页头 → 主指标(帝国权威、家族领地、皇族届数、上次议会、帝国禁令) → 两栏(三院席位 | 皇位：帝位、储君、加冕、帝国改革、诸侯主权)
    //   → 两栏(帝国权威：刻度与近来收支 | 皇权所费：各事项的门槛与花费) → 两栏(诸侯纷争：私战或诉讼 | 留空)
    //   → 议会纪事 → 选举让步 → 诸侯同盟 → 按钮(召开议会、提请或解除禁令、颁行帝国改革、册立自由城市、典押自由城市、封立主教领)
    public partial class EmpireWindow
    {
        public void ShowDiet(WindowMetaTab pArg0)
        {
            Clear();
            if (!HolyRomanSystem.HasDiet(_empire))
            {
                var none = BeginPage("empire_diet");
                none.AddEmptyCard(MainWidth, "diet_none_title", LM.Get("diet_none_hint"));
                StartCoroutine(RefreshScrollLayout(resetToTop: true));
                return;
            }
            HolyRomanState state = HolyRomanSystem.State(_empire);
            bool elective = HolyRomanSystem.IsElective(_empire);
            var page = BeginPage("empire_diet", LM.Get(elective ? "diet_elective_vote" : "diet_elective_hereditary"),
                CrownStatus(out _));

            float share = HolyRomanSystem.HouseShare(_empire);
            string last = state.last_diet_at < 0d ? LM.Get("diet_never")
                : string.Format(LM.Get("diet_years_ago"), Date.getYearsSince(state.last_diet_at));
            string ban = state.banned_kingdom_id >= 0 ? state.banned_name : LM.Get("label_none");
            int authority = HolyRomanSystem.Authority(_empire);
            page.AddKpiRow(MainWidth,
                (LM.Get("diet_authority"), $"{authority}·{HolyRomanSystem.AuthorityTierText(authority)}",
                    WideKit.Grade(authority, 60, 30)),
                (LM.Get("diet_house"), $"{share * 100f:0}%", WideKit.Grade(share * 100f, 25, 10)),
                (LM.Get("diet_terms"), state.house_terms.ToString(), WideKit.Gold),
                (LM.Get("diet_last"), last, WideKit.Cyan),
                (LM.Get("diet_ban"), ban, state.banned_kingdom_id >= 0 ? WideKit.Red : WideKit.Muted));

            var columns = page.BeginColumns(MainWidth);
            List<HolyRomanSystem.DietVoter> voters = HolyRomanSystem.Voters(_empire);
            var colleges = columns.BeginColumnCard(HalfWidth, "diet_colleges_title", WideKit.Gold, null, "TabDiet");
            int row = 0;
            foreach ((int college, string key) in new[]
                     {
                         (HolyRomanSystem.CollegeElectors, "diet_college_electors"),
                         (HolyRomanSystem.CollegePrinces, "diet_college_princes"),
                         (HolyRomanSystem.CollegeCities, "diet_college_cities")
                     })
            {
                List<HolyRomanSystem.DietVoter> seats = voters.Where(voter => voter.college == college).ToList();
                string names = seats.Count == 0 ? LM.Get("label_none")
                    : string.Join("、", seats.Select(voter => college == HolyRomanSystem.CollegeElectors
                        ? voter.person?.getName() ?? voter.realm.GetKingdomName()
                        : voter.realm.GetKingdomName()));
                colleges.AddListRow(HalfWidth, row++, null,
                    $"{LM.Get(key)} {string.Format(LM.Get("diet_seats"), seats.Count)}", names, titleWidth: 92f);
            }
            List<Kingdom> church = HolyRomanSystem.Members(_empire).Where(HolyRomanSystem.IsChurchEstate).ToList();
            colleges.AddListRow(HalfWidth, row, null, LM.Get("diet_church_princes"), Names(church), titleWidth: 92f);

            var crown = columns.BeginColumnCard(HalfWidth, "diet_crown_title", WideKit.Cyan);
            Actor heir = HolyRomanSystem.KingOfRomans(_empire);
            crown.AddListRow(HalfWidth, 0, null, LM.Get("diet_elective"),
                LM.Get(elective ? "diet_elective_vote" : "diet_elective_hereditary"), titleWidth: 64f);
            if (elective)
                crown.AddListRow(HalfWidth, 1, null, LM.Get("diet_heir"), heir?.getName() ?? LM.Get("label_none"), titleWidth: 64f);
            crown.AddListRow(HalfWidth, 2, null, LM.Get("diet_coronation"), CrownStatus(out string crownHex), titleWidth: 64f,
                titleHex: crownHex);
            string reform = HolyRomanSystem.ReformEnacted(_empire)
                ? string.Format(LM.Get("diet_reform_on"), HolyRomanSystem.CircleCount(_empire), state.court_rulings)
                : HolyRomanSystem.ReformNodeEnacted(_empire)
                    ? string.Format(LM.Get("diet_reform_pending"), authority, HolyRomanSystem.ReformThreshold)
                    : LM.Get("diet_reform_off");
            crown.AddListRow(HalfWidth, 3, null, LM.Get("diet_reform"), reform, titleWidth: 64f);
            crown.AddListRow(HalfWidth, 4, null, LM.Get("diet_sovereignty"),
                state.sovereign_princes ? state.peace_name : LM.Get("label_none"), titleWidth: 64f);
            War imperialWar = HolyRomanSystem.ImperialWar(_empire);
            if (imperialWar != null)
                crown.AddListRow(HalfWidth, 5, null, LM.Get("diet_imperial_war"), imperialWar.data.name, titleWidth: 64f);

            DrawAuthority(page, state, authority, elective);
            DrawDisputes(page, state);

            List<DietRecord> records = (state.diets ?? new List<DietRecord>()).AsEnumerable().Reverse().Take(8).ToList();
            var log = page.BeginCard(MainWidth, "diet_records_title", WideKit.Gold, records.Count.ToString());
            if (records.Count == 0)
                log.AddListRow(MainWidth, 0, null, LM.Get("label_none"), LM.Get("diet_records_none"), titleWidth: 150f);
            for (int i = 0; i < records.Count; i++)
            {
                DietRecord record = records[i];
                string detail = string.Format(LM.Get("diet_record_detail"), record.electors_yes, record.electors_no,
                    record.princes_yes, record.princes_no, record.cities_yes, record.cities_no, record.outcome);
                log.AddListRow(MainWidth, i, null, HolyRomanSystem.AgendaText(record.agenda, record.subject), detail,
                    titleWidth: 150f, titleHex: record.passed ? WideKit.Green : WideKit.Red);
            }

            if (elective || state.concessions?.Count > 0)
            {
                List<string> concessions = (state.concessions ?? new List<string>()).AsEnumerable().Reverse().Take(6).ToList();
                var deals = page.BeginCard(MainWidth, "diet_concessions_title", WideKit.Orange, concessions.Count.ToString());
                if (concessions.Count == 0) deals.AddListRow(MainWidth, 0, null, LM.Get("label_none"), "", titleWidth: 150f);
                for (int i = 0; i < concessions.Count; i++)
                    deals.AddListRow(MainWidth, i, null, (i + 1).ToString(), concessions[i], titleWidth: 30f);
            }

            List<Kingdom> league = HolyRomanSystem.LeagueMembers(_empire);
            if (state.league_leader_id >= 0 && league.Count > 0)
            {
                Kingdom head = World.world.kingdoms.get(state.league_leader_id);
                var leagueCard = page.BeginCard(MainWidth, "diet_league_title", WideKit.Red, state.league_name);
                leagueCard.AddListRow(MainWidth, 0, null, state.league_name,
                    string.Format(LM.Get("diet_league_detail"), head?.GetKingdomName() ?? LM.Get("label_none"), league.Count,
                        LM.Get(HolyRomanSystem.LeagueWar(_empire) != null ? "diet_league_at_war" : "diet_league_peace")),
                    titleWidth: 150f);
                leagueCard.AddListRow(MainWidth, 1, null, LM.Get("diet_college_princes"), Names(league), titleWidth: 150f);
            }

            var actions = page.BeginCard(MainWidth, "diet_actions_title", WideKit.Muted);
            actions.AddButtonRow(MainWidth,
                ("diet_act_convene", LM.Get("diet_act_convene"), () => { if (HolyRomanSystem.ActConvene(_empire) != null) ShowDiet(pArg0); }),
                ("diet_act_ban", LM.Get(state.banned_kingdom_id >= 0 ? "diet_act_lift" : "diet_act_ban"), () =>
                {
                    bool done = state.banned_kingdom_id >= 0 ? HolyRomanSystem.ActLiftBan(_empire) : HolyRomanSystem.ActProposeBan(_empire) != null;
                    if (done) ShowDiet(pArg0);
                }),
                ("diet_act_reform", LM.Get("diet_act_reform"), () =>
                {
                    if (HolyRomanSystem.ActEnactReform(_empire) != null) ShowDiet(pArg0);
                    else ActionLibrary.showWhisperTip(HolyRomanSystem.ReformEnacted(_empire) ? "diet_reform_done_tip" : "diet_reform_need_node_tip");
                }));
            actions.AddButtonRow(MainWidth,
                ("diet_act_charter", LM.Get("diet_act_charter"), () => { if (HolyRomanSystem.ActCharterFreeCity(_empire) != null) ShowDiet(pArg0); }),
                ("diet_act_pawn", LM.Get("diet_act_pawn"), () => { if (HolyRomanSystem.PawnFreeCity(_empire)) ShowDiet(pArg0); }),
                ("diet_act_church", LM.Get("diet_act_church"), () => { if (HolyRomanSystem.ActEndowChurch(_empire) != null) ShowDiet(pArg0); }));
            StartCoroutine(RefreshScrollLayout(resetToTop: true));
        }

        // 帝国权威一排两栏：左"帝国权威"(刻度 + 最近六笔收支，涨绿跌红)，右"皇权所费"(各事项门槛与花费，够的金色、不够灰)
        private void DrawAuthority(AutoVertLayoutGroup page, HolyRomanState state, int authority, bool elective)
        {
            float inner = HalfWidth - 12f;
            var columns = page.BeginColumns(MainWidth);
            string grade = WideKit.Grade(authority, 60, 30);
            var left = columns.BeginColumnCard(HalfWidth, "diet_authority_title", WideKit.Gold,
                $"{authority} · {HolyRomanSystem.AuthorityTierText(authority)}");
            Color barColor = ColorUtility.TryParseHtmlString(grade, out Color parsed) ? parsed : Color.white;
            UIHelper.AddBarRow(left, inner, LM.Get("diet_authority").ColorString(WideKit.Muted), $"{authority}/100",
                authority / 100f, barColor);
            List<AuthorityEntry> entries = (state.authority_log ?? new List<AuthorityEntry>()).AsEnumerable().Reverse().Take(6).ToList();
            if (entries.Count == 0)
                left.AddListRow(HalfWidth, 0, null, LM.Get("label_none"), LM.Get("diet_authority_none"), titleWidth: 30f);
            for (int i = 0; i < entries.Count; i++)
            {
                AuthorityEntry entry = entries[i];
                string ago = string.Format(LM.Get("diet_years_ago"), Date.getYearsSince(entry.at));
                left.AddListRow(HalfWidth, i, null, entry.delta > 0 ? $"+{entry.delta}" : entry.delta.ToString(),
                    $"{entry.text} · {ago}", titleWidth: 22f, titleHex: entry.delta > 0 ? WideKit.Green : WideKit.Red);
            }

            var right = columns.BeginColumnCard(HalfWidth, "diet_spend_title", WideKit.Cyan);
            var costs = new List<(string label, int threshold, int cost)>
            {
                (LM.Get("diet_act_reform"), HolyRomanSystem.ReformThreshold, HolyRomanSystem.ReformCost),
                (LM.Get("颁布帝国治安令"), HolyRomanSystem.PeaceThreshold, HolyRomanSystem.PeaceCost),
                (LM.Get("diet_act_ban"), HolyRomanSystem.BanThreshold, HolyRomanSystem.BanCost),
                (LM.Get("确认诸侯特权"), 0, HolyRomanSystem.PrivilegeCost)
            };
            if (elective) costs.Add((LM.Get("恢复世袭皇权"), HolyRomanSystem.HereditaryThreshold, HolyRomanSystem.HereditaryCost));
            for (int i = 0; i < costs.Count; i++)
            {
                (string label, int threshold, int cost) = costs[i];
                string detail = threshold > 0 ? string.Format(LM.Get("diet_spend_detail"), threshold, cost)
                    : string.Format(LM.Get("diet_spend_free"), cost);
                right.AddListRow(HalfWidth, i, null, label, detail, titleWidth: 70f,
                    titleHex: authority >= threshold ? WideKit.Gold : WideKit.Muted);
            }
            right.AddListRow(HalfWidth, costs.Count, null, "", LM.Get("diet_spend_note"), titleWidth: 1f);
        }

        // 诸侯纷争一排两栏：左"诸侯纷争"——进行中的私战(帝国改革后只有违禁私战，红字)、待裁的诉讼，下接最近四起结果
        private void DrawDisputes(AutoVertLayoutGroup page, HolyRomanState state)
        {
            var columns = page.BeginColumns(MainWidth);
            bool reformed = HolyRomanSystem.ReformEnacted(_empire);
            var card = columns.BeginColumnCard(HalfWidth, "diet_feud_title", WideKit.Orange,
                string.Format(LM.Get("diet_feud_header"), state.private_war_count, state.court_rulings));
            int row = 0;
            foreach (PrivateWarRecord record in (state.private_wars ?? new List<PrivateWarRecord>()).Where(item => item.ended_at < 0d))
                card.AddListRow(HalfWidth, row++, null, $"{record.attacker_name}↔{record.defender_name}",
                    string.Format(LM.Get(record.illegal ? "diet_feud_illegal_row" : "diet_feud_row"), record.city_names,
                        Date.getYearsSince(record.at) + 1), titleWidth: 80f, titleHex: record.illegal ? WideKit.Red : WideKit.Orange);
            foreach (LawsuitRecord suit in state.lawsuits ?? new List<LawsuitRecord>())
                card.AddListRow(HalfWidth, row++, null, string.Format(LM.Get("diet_lawsuit_title"), suit.plaintiff_name, suit.defendant_name),
                    string.Format(LM.Get("diet_lawsuit_row"), suit.city_name, Date.getYearsSince(suit.filed_at)) +
                    (suit.bribed ? LM.Get("diet_lawsuit_bribed") : ""), titleWidth: 80f, titleHex: WideKit.Cyan);
            if (row == 0)
                card.AddListRow(HalfWidth, row++, null, LM.Get("diet_feud_quiet"),
                    LM.Get(reformed ? "diet_feud_quiet_reformed" : "diet_feud_quiet_hint"), titleWidth: 50f, titleHex: WideKit.Green);
            foreach (string text in (state.court_log ?? new List<string>()).AsEnumerable().Reverse().Take(4))
                card.AddListRow(HalfWidth, row++, null, "", text, titleWidth: 1f);
        }

        // 加冕状态：已加冕 / 远征教廷途中 / 当选未加冕(原型"德意志国王")
        private string CrownStatus(out string hex)
        {
            if (PapacySystem.IsCrowned(_empire) || PapacySystem.IsHolySee(_empire))
            {
                hex = WideKit.Green;
                return LM.Get("diet_crowned");
            }
            if (HolyRomanSystem.RomzugUnderway(_empire))
            {
                hex = WideKit.Cyan;
                return LM.Get("diet_romzug");
            }
            hex = WideKit.Orange;
            return LM.Get("diet_uncrowned");
        }
    }
}

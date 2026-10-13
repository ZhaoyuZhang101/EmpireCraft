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
    //   页头 → 主指标(家族领地、皇族届数、上次议会、帝国禁令) → 两栏(三院席位 | 皇位：帝位、储君、加冕、帝国改革、诸侯主权)
    //   → 议会纪事 → 选举让步 → 诸侯同盟 → 按钮(召开议会、提请或解除禁令、册立自由城市、典押自由城市、封立主教领)
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
            page.AddKpiRow(MainWidth,
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
                : LM.Get("diet_reform_off");
            crown.AddListRow(HalfWidth, 3, null, LM.Get("diet_reform"), reform, titleWidth: 64f);
            crown.AddListRow(HalfWidth, 4, null, LM.Get("diet_sovereignty"),
                state.sovereign_princes ? state.peace_name : LM.Get("label_none"), titleWidth: 64f);
            War imperialWar = HolyRomanSystem.ImperialWar(_empire);
            if (imperialWar != null)
                crown.AddListRow(HalfWidth, 5, null, LM.Get("diet_imperial_war"), imperialWar.data.name, titleWidth: 64f);

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
                ("diet_act_charter", LM.Get("diet_act_charter"), () => { if (HolyRomanSystem.ActCharterFreeCity(_empire) != null) ShowDiet(pArg0); }));
            actions.AddButtonRow(MainWidth,
                ("diet_act_pawn", LM.Get("diet_act_pawn"), () => { if (HolyRomanSystem.PawnFreeCity(_empire)) ShowDiet(pArg0); }),
                ("diet_act_church", LM.Get("diet_act_church"), () => { if (HolyRomanSystem.ActEndowChurch(_empire) != null) ShowDiet(pArg0); }));
            StartCoroutine(RefreshScrollLayout(resetToTop: true));
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

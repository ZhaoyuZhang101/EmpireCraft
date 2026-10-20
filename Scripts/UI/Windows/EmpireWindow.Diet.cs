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

namespace EmpireCraft.Scripts.UI.Windows
{
    // 帝国议会页(封建制帝国，见 HolyRomanSystem)：
    //   页头(帝位虚悬时标代理) → 主指标(帝国权威、家族领地、皇族届数、上次议会、帝国禁令)
    //   → 两栏(三院席位 | 皇位：帝位、储君、加冕、帝国改革、诸侯主权、帝国战争、会选、对立国王、帝国代理)
    //   → 两栏(帝国权威：刻度、近来收支、各事项所费 | 选帝侯：三教会席 + 四世俗席，授予/褫夺) → 两栏(诸侯纷争：私战或诉讼 | 帝国直属：请愿、陪臣化、典押)
    //   → 帝国存续(只在权威低于 25 或已有同盟离去时) → 议会纪事 → 历次会选 → 选举让步 → 诸侯同盟 → 按钮(召开议会、提请或解除禁令、颁行帝国改革、册立自由城市、典押自由城市、封立主教领)
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
            var chips = new List<string> { LM.Get(elective ? "diet_elective_vote" : "diet_elective_hereditary"), CrownStatus(out _) };
            if (state.interregnum_since >= 0d) chips.Add(string.Format(LM.Get("diet_interregnum_chip"), state.vicar_name));
            var page = BeginPage("empire_diet", chips.ToArray());

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
            if (elective) DrawElectionRows(crown, state);

            DrawAuthority(page, state, authority, elective, pArg0);
            DrawDisputes(page, state, pArg0);
            DrawSurvival(page, state, authority, pArg0);

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

            if (elective)
            {
                List<ElectionRecord> elections = (state.elections ?? new List<ElectionRecord>()).AsEnumerable().Reverse().Take(6).ToList();
                var history = page.BeginCard(MainWidth, "diet_elections_title", WideKit.Cyan, elections.Count.ToString());
                if (elections.Count == 0)
                    history.AddListRow(MainWidth, 0, null, LM.Get("label_none"), LM.Get("diet_elections_none"), titleWidth: 150f);
                for (int i = 0; i < elections.Count; i++)
                {
                    ElectionRecord record = elections[i];
                    history.AddListRow(MainWidth, i, null,
                        $"{string.Format(LM.Get("diet_years_ago"), Date.getYearsSince(record.at))} · {record.winner}",
                        $"{record.outcome} · {record.tally}", titleWidth: 150f, titleHex: WideKit.Gold);
                }
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

        // 帝国权威一排两栏：左"帝国权威"(刻度 + 最近六笔收支，涨绿跌红，下接各事项所费)，右"选帝侯"(七席，标题条上授予、褫夺)
        private void DrawAuthority(AutoVertLayoutGroup page, HolyRomanState state, int authority, bool elective, WindowMetaTab tab)
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

            // 所费(门槛/花)：够门槛的金色、不够的灰
            var costs = new List<(string label, int threshold, int cost)>
            {
                (LM.Get("diet_cost_reform"), HolyRomanSystem.ReformThreshold, HolyRomanSystem.ReformCost),
                (LM.Get("diet_cost_peace"), HolyRomanSystem.PeaceThreshold, HolyRomanSystem.PeaceCost),
                (LM.Get("diet_cost_ban"), HolyRomanSystem.BanThreshold, HolyRomanSystem.BanCost),
                (LM.Get("diet_cost_privilege"), 0, HolyRomanSystem.PrivilegeCost),
                (LM.Get("diet_cost_grant"), HolyRomanSystem.ElectorGrantThreshold, HolyRomanSystem.ElectorGrantCost),
                (LM.Get("diet_cost_strip"), 0, HolyRomanSystem.ElectorStripCost)
            };
            if (elective) costs.Add((LM.Get("diet_cost_hereditary"), HolyRomanSystem.HereditaryThreshold, HolyRomanSystem.HereditaryCost));
            string costLine = string.Join("  ", costs.Select(item =>
                $"{item.label} {(item.threshold > 0 ? item.threshold.ToString() : "-")}/{item.cost}"
                    .ColorString(authority >= item.threshold ? WideKit.Gold : WideKit.Muted)));
            left.AddListRow(HalfWidth, entries.Count == 0 ? 1 : entries.Count, null, LM.Get("diet_spend_title"), costLine,
                titleWidth: 40f, titleHex: WideKit.Cyan);

            DrawElectors(columns, tab);
        }

        // 选帝侯七席：三教会席(随主教领与宗教等级而定) + 四世俗席(绑定领国：在座金、停权橙、出缺灰)；标题条上"授予""褫夺"
        private void DrawElectors(AutoHoriLayoutGroup columns, WindowMetaTab tab)
        {
            List<Actor> church = HolyRomanSystem.ChurchElectorsView(_empire);
            List<(Kingdom realm, Actor holder, int status)> seats = HolyRomanSystem.SecularSeatsView(_empire);
            int seated = church.Count + seats.Count(seat => seat.status == 0);
            var card = columns.BeginColumnCard(HalfWidth);
            var buttons = new List<(string id, string label, global::System.Action action)>();
            if (HolyRomanSystem.HasVacantSeat(_empire))
                buttons.Add(("diet_elector_grant", LM.Get("diet_elector_grant"), () => HolyRomanSystem.ActGrantElector(_empire)));
            if (HolyRomanSystem.BannedHoldsSeat(_empire))
                buttons.Add(("diet_elector_strip", LM.Get("diet_elector_strip"), () => HolyRomanSystem.ActStripElector(_empire)));
            float ButtonWidth(string label) => Mathf.Max(26f, UIHelper.EstimateTextWidth(label, 7) + 10f);
            var bar = UIHelper.AddCardTitle(card, HalfWidth, LM.Get("diet_electors_title"), WideKit.Gold,
                string.Format(LM.Get("diet_electors_header"), seated), buttons.Sum(entry => ButtonWidth(entry.label) + 3f));
            foreach ((string id, string label, global::System.Action action) in buttons)
            {
                global::System.Action run = action;
                bar.AddButtonIntoHoriLayout(id, label, () =>
                {
                    run();
                    ShowDiet(tab);
                }, size: new Vector2(ButtonWidth(label), 12f));
            }
            int row = 0;
            for (int i = 0; i < HolyRomanSystem.ChurchSeats; i++)
            {
                Actor holder = i < church.Count ? church[i] : null;
                card.AddListRow(HalfWidth, row++, null, LM.Get("diet_elector_church"),
                    holder == null ? LM.Get("diet_elector_vacant")
                        : $"{holder.getName()} · {holder.kingdom?.GetKingdomName() ?? ""}",
                    titleWidth: 40f, titleHex: holder == null ? WideKit.Muted : WideKit.Gold);
            }
            foreach ((Kingdom realm, Actor holder, int status) in seats)
            {
                string detail = realm == null ? LM.Get("diet_elector_vacant")
                    : $"{realm.GetKingdomName()} · {holder?.getName() ?? LM.Get("label_none")}" +
                      (status == 1 ? " · " + LM.Get("diet_elector_suspended") : "");
                card.AddListRow(HalfWidth, row++, null, LM.Get("diet_elector_secular"), detail, titleWidth: 40f,
                    titleHex: status == 0 ? WideKit.Gold : status == 1 ? WideKit.Orange : WideKit.Muted);
            }
            HolyRomanState state = HolyRomanSystem.State(_empire);
            string last = state.elector_log?.LastOrDefault();
            card.AddListRow(HalfWidth, row, null, "", string.IsNullOrEmpty(last) ? LM.Get("diet_elector_note") : last, titleWidth: 1f);
        }

        // 帝国存续(整宽，只在权威低于 25 或已有同盟离去时)：式微年数、已离去的同盟与保护人
        private void DrawSurvival(AutoVertLayoutGroup page, HolyRomanState state, int authority, WindowMetaTab tab)
        {
            List<string> departed = state.departed_log ?? new List<string>();
            if (authority >= HolyRomanSystem.LowRecover && departed.Count == 0) return;
            var card = page.BeginCard(MainWidth, "diet_survival_title", WideKit.Red, $"{authority} · {HolyRomanSystem.AuthorityTierText(authority)}");
            int row = 0;
            string low = state.authority_low_since >= 0d
                ? string.Format(LM.Get("diet_survival_low"), HolyRomanSystem.LowAuthority, Date.getYearsSince(state.authority_low_since),
                    HolyRomanSystem.ProtectorLowYears)
                : string.Format(LM.Get("diet_survival_recovering"), HolyRomanSystem.LowAuthority);
            card.AddListRow(MainWidth, row++, null, LM.Get("diet_survival_waning"), low, titleWidth: 80f, titleHex: WideKit.Orange);
            foreach (string text in departed.AsEnumerable().Reverse().Take(3))
                card.AddListRow(MainWidth, row++, null, LM.Get("diet_survival_departed"), text, titleWidth: 80f, titleHex: WideKit.Red);
            if (!HolyRomanSystem.DissolutionEnabled)
            {
                card.AddListRow(MainWidth, row, null, LM.Get("diet_survival_abdication"), LM.Get("diet_survival_disabled"), titleWidth: 80f);
                return;
            }
            string blocker = HolyRomanSystem.AbdicationBlocker(_empire, state);
            card.AddListRow(MainWidth, row, null, LM.Get("diet_survival_abdication"), blocker ?? LM.Get("diet_survival_abdication_ready"),
                titleWidth: 80f, titleHex: blocker == null ? WideKit.Red : WideKit.Muted);
            // 权威不到 10：皇帝退位(点两下才生效)
            if (authority >= HolyRomanSystem.CollapseAuthority) return;
            bool armed = _abdicationArmed == _empire.data.id;
            card.AddButtonRow(MainWidth, ("diet_act_abdicate", LM.Get(armed ? "diet_act_abdicate_confirm" : "diet_act_abdicate"), () =>
            {
                if (_abdicationArmed != _empire.data.id)
                {
                    _abdicationArmed = _empire.data.id;
                    ShowDiet(tab);
                    return;
                }
                _abdicationArmed = -1L;
                if (HolyRomanSystem.ActAbdicate(_empire)) ScrollWindow.hideAllEvent();
            }));
        }

        // 皇帝退位按钮已点过一下的帝国(再点一下才生效)
        private long _abdicationArmed = -1L;

        // 皇位卡的会选三行：会选(拉票中时各候选眼下的票，否则上次结果)、对立国王(有时)、帝国代理(帝位虚悬时)
        private void DrawElectionRows(AutoVertLayoutGroup crown, HolyRomanState state)
        {
            List<(Actor candidate, int votes)> standing = HolyRomanSystem.CampaignStanding(_empire);
            ElectionRecord last = state.elections?.LastOrDefault();
            string election = standing.Count > 0
                ? string.Format(LM.Get("diet_campaign"), string.Join(" · ", standing.Select(item => $"{item.candidate.getName()} {item.votes}")))
                : last == null ? LM.Get("label_none") : $"{last.winner} · {last.outcome}";
            crown.AddListRow(HalfWidth, 6, null, LM.Get("diet_election"), election, titleWidth: 64f);
            if (state.anti_king_id >= 0)
            {
                War war = HolyRomanSystem.ContestWar(_empire);
                crown.AddListRow(HalfWidth, 7, null, LM.Get("diet_anti_king"),
                    war == null ? state.anti_king_name : $"{state.anti_king_name} · {war.data.name}", titleWidth: 64f, titleHex: WideKit.Red);
            }
            if (state.interregnum_since >= 0d)
                crown.AddListRow(HalfWidth, 8, null, LM.Get("diet_vicar"),
                    string.Format(LM.Get("diet_vicar_detail"), state.vicar_name, Date.getYearsSince(state.interregnum_since)),
                    titleWidth: 64f, titleHex: WideKit.Orange);
        }

        // 诸侯纷争一排两栏：左"诸侯纷争"——进行中的私战(帝国改革后只有违禁私战，红字)、待裁的诉讼，下接最近四起结果
        private void DrawDisputes(AutoVertLayoutGroup page, HolyRomanState state, WindowMetaTab tab)
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
            DrawImmediacy(columns, state, tab);
        }

        // 帝国直属：待决的请愿、近年的陪臣化与受阻、典押中的自由城市；标题条上"维护"(驳回请愿)、"赎回"(最早一笔典押)
        private void DrawImmediacy(AutoHoriLayoutGroup columns, HolyRomanState state, WindowMetaTab tab)
        {
            (Kingdom lord, Kingdom small) = HolyRomanSystem.PendingPetition(_empire);
            List<PawnRecord> pawns = state.pawns ?? new List<PawnRecord>();
            List<MediatizationRecord> history = (state.mediatizations ?? new List<MediatizationRecord>()).AsEnumerable().Reverse().Take(4).ToList();
            var card = columns.BeginColumnCard(HalfWidth);
            var buttons = new List<(string id, string label, global::System.Action action)>();
            if (lord != null)
                buttons.Add(("diet_immediacy_defend", LM.Get("diet_immediacy_defend"), () => HolyRomanSystem.ActDefendImmediacy(_empire)));
            if (pawns.Count > 0)
                buttons.Add(("diet_pawn_redeem", LM.Get("diet_pawn_redeem"), () =>
                {
                    if (!HolyRomanSystem.ActRedeem(_empire)) ActionLibrary.showWhisperTip("diet_pawn_redeem_tip");
                }));
            float ButtonWidth(string label) => Mathf.Max(26f, UIHelper.EstimateTextWidth(label, 7) + 10f);
            int done = state.mediatizations?.Count(record => !record.blocked) ?? 0;
            int blocked = state.mediatizations?.Count(record => record.blocked) ?? 0;
            var bar = UIHelper.AddCardTitle(card, HalfWidth, LM.Get("diet_immediacy_title"), WideKit.Cyan,
                string.Format(LM.Get("diet_immediacy_header"), done, blocked), buttons.Sum(entry => ButtonWidth(entry.label) + 3f));
            foreach ((string id, string label, global::System.Action action) in buttons)
            {
                global::System.Action run = action;
                bar.AddButtonIntoHoriLayout(id, label, () =>
                {
                    run();
                    ShowDiet(tab);
                }, size: new Vector2(ButtonWidth(label), 12f));
            }
            int row = 0;
            if (lord != null)
                card.AddListRow(HalfWidth, row++, null, LM.Get("diet_petition"),
                    string.Format(LM.Get("diet_petition_detail"), lord.GetKingdomName(), small.GetKingdomName()), titleWidth: 40f,
                    titleHex: WideKit.Orange);
            foreach (MediatizationRecord record in history)
                card.AddListRow(HalfWidth, row++, null, LM.Get("diet_mediatize_route_" + record.route),
                    string.Format(LM.Get(record.blocked ? "diet_mediatize_blocked" : "diet_mediatize_done"), record.small, record.lord,
                        Date.getYearsSince(record.at)), titleWidth: 40f, titleHex: record.blocked ? WideKit.Green : WideKit.Red);
            foreach (PawnRecord pawn in pawns)
                card.AddListRow(HalfWidth, row++, null, LM.Get("diet_pawn"),
                    string.Format(LM.Get("diet_pawn_detail"), pawn.city_name, pawn.holder_name, MoneyDisplay.Format((long)(pawn.price * 1.2f)),
                        Mathf.Max(0, 30 - Date.getYearsSince(pawn.at))), titleWidth: 40f, titleHex: WideKit.Gold);
            if (row == 0) card.AddListRow(HalfWidth, row, null, LM.Get("diet_feud_quiet"), LM.Get("diet_immediacy_none"), titleWidth: 50f,
                titleHex: WideKit.Green);
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

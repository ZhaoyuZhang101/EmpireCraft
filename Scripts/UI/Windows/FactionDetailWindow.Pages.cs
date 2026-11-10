using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 派系窗口右侧各页按「卷册」排版重做(用户 2026-11-11 "派系界面真的要优化一下了""之前说的提议的砝码和天平呢"
// "玩家也能自己主动发起提议"；"ui尽量多用卡片分区""仿照游戏里面的效果"，示意图 Tasks/ui-mockups/faction-window-cards.png)：
// 成员(概况/在朝/在野)、诉求(现况/清单 → 选中一条拆成总览、为何要它、谁得谁失、天平、赞成反对、准奏砝码、交换、往期、处置)、
// 制度立场两栏、社会基础(力量/倚重/各阶层)、党务(政党)/派务(朝堂派系：本钱、结盟、人情账、各议题立场分卡)。
// 以前每页是零散的文字与按钮，换页时党务卡也清不掉
public partial class FactionDetailWindow
{
    private const float Pad = 12f;
    private static bool _addingClaim;
    // 诉求页清单里点中的那条(换了派系就重选)
    private static TemporaryFactionType? _selectedClaim;
    private static string _selectedFor;

    private Empire CurrentEmpire => _faction?.Empire ?? _kingdom?.GetEmpire();
    private static float Half => (ContentWidth - UIHelper.CardGap) / 2f;

    // 页面骨架：一个竖排的组记进 _groups(Clear 时一并销毁)，顶上页眉
    private AutoVertLayoutGroup BeginContent(string title, params string[] chips)
    {
        var page = _root.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 6));
        _groups.Add(page.gameObject);
        page.AddPageHeader(ContentWidth, title, chips);
        return page;
    }

    private static void Tip(GameObject target, string key, string title, string body)
    {
        if (target == null || string.IsNullOrWhiteSpace(body)) return;
        if (target.GetComponent<Graphic>() == null) target.AddComponent<Image>().color = Color.clear;
        UIHelper.AttachTextTooltip(target, key, title, body);
    }

    // 卡里的小节：左青字，右灰字
    private static void SubHeading(AutoVertLayoutGroup card, float width, string text, string right = null)
    {
        float inner = width - Pad;
        var row = card.BeginHoriGroup(new Vector2(inner, 12f), TextAnchor.MiddleLeft, 2, new RectOffset(2, 2, 0, 0));
        row.AddTextIntoHoriLayout((text ?? "").ColorString(WideKit.Cyan), true, TextAnchor.MiddleLeft, new Vector2(inner * 0.5f, 11f))
            .UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        row.AddTextIntoHoriLayout((right ?? "").ColorString(WideKit.Muted), true, TextAnchor.MiddleRight, new Vector2(inner * 0.5f - 8f, 11f))
            .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
    }

    private static string Signed(float value) => value >= 0f ? $"+{value:0.#}" : $"{value:0.#}";
    private static string Percent(float share) => $"{Mathf.RoundToInt(share * 100f)}%";
    private static string ChanceHex(float chance) => chance >= 0.6f ? WideKit.Green : chance >= 0.3f ? WideKit.Gold : WideKit.Red;

    #region 成员

    // 成员页：概况一张卡(人数、在朝、绩效、名望)，在朝、在野各一张卡
    public IEnumerator ShowMembers(WindowMetaTab window = null)
    {
        Clear();
        InitialTopPart();
        if (_faction == null) yield break;
        Actor leader = _faction.GetLeader();
        List<Actor> members = _faction.AllMembers.Where(actor => actor.isAlive())
            .OrderByDescending(actor => actor == leader).ThenByDescending(actor => actor.renown).ToList();
        var page = BeginContent("faction_page_members", string.Format(LM.Get("faction_members_count"), members.Count));
        List<Actor> inOffice = members.Where(actor => actor.IsOnOffice()).ToList();
        List<Actor> idle = members.Where(actor => !actor.IsOnOffice()).ToList();
        float performance = members.Sum(actor => actor.HasOfficeIdentity() ? (float)actor.GetIdentity().TotalPerformance : 0f);
        float renown = members.Sum(actor => (float)actor.renown);
        var overview = page.BeginCard(ContentWidth, "faction_card_member_overview", WideKit.Gold, null, "ui/icons/iconPopulation");
        overview.AddMetricRow(ContentWidth,
            (LM.Get("label_member_count"), members.Count.ToString(), WideKit.Value),
            (LM.Get("faction_members_in_office"), inOffice.Count.ToString(), WideKit.Gold),
            (LM.Get("faction_members_perf_total"), $"{performance:0}", WideKit.Cyan),
            (LM.Get("faction_members_renown_total"), $"{renown:0}", WideKit.Orange));
        if (members.Count == 0) overview.AddParagraph(ContentWidth, LM.Get("faction_members_none"), 7, WideKit.Muted);
        DrawMemberCard(page, "faction_card_members_office", inOffice, leader, "ui/icons/iconKingdom");
        DrawMemberCard(page, "faction_card_members_idle", idle, leader, "ui/icons/iconPopulation");
        page.AddActionRow(ContentWidth, (LM.Get("faction_add_member"), WideKit.Cyan, true, () =>
        {
            if (_faction.Empire != null && !_faction.Empire.isRekt()) ScrollWindow.showWindow(nameof(AddFactionMemberWindow));
        }));
    }

    private void DrawMemberCard(AutoVertLayoutGroup page, string key, List<Actor> list, Actor leader, string icon)
    {
        if (list.Count == 0) return;
        var card = page.BeginCard(ContentWidth, string.Format(LM.Get(key), list.Count), WideKit.Gold, LM.Get("faction_members_sort_hint"), icon);
        for (int i = 0; i < list.Count; i++) DrawMember(card, list[i], leader, i);
    }

    // 一名成员：头像(点开人物) + 姓名、官职、绩效、名望、品级 + 加减绩效、立为领袖、移出
    private void DrawMember(AutoVertLayoutGroup card, Actor actor, Actor leader, int index)
    {
        float inner = ContentWidth - Pad;
        var row = card.BeginHoriGroup(new Vector2(inner, 32f), TextAnchor.MiddleLeft, 4, new RectOffset(2, 2, 0, 0));
        if (index % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(inner, 32f));
        row.AddActorViewIntoHoriLayout(actor);
        OfficeIdentity identity = actor.HasOfficeIdentity() ? actor.GetIdentity() : null;
        const float buttons = 4 * 34f + 3 * 4f;
        float textWidth = inner - 36f - buttons - 12f;
        var lines = row.BeginVertGroup(new Vector2(textWidth, 30f), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
            pPadding: new RectOffset(0, 0, 0, 0));
        string name = actor.getName() + (actor == leader ? " " + LM.Get("faction_member_leader").ColorString(WideKit.Orange) : "");
        var nameText = lines.AddTextIntoVertLayout(name.ColorString(WideKit.Gold), true, TextAnchor.MiddleLeft, new Vector2(textWidth, 12f));
        nameText.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        string office = actor.IsOnOffice() ? actor.GetOffice().GetOfficeName() : LM.Get("label_none");
        string rank = identity == null ? "" : $"{OfficialRankSystem.PanelLabel(actor)} {identity.GetHonoraryOfficialName()}";
        string detail = string.Format(LM.Get("faction_member_detail"), office, (int)(identity?.TotalPerformance ?? 0), actor.renown, rank);
        var detailText = lines.AddTextIntoVertLayout(detail.ColorString(WideKit.Soft), true, TextAnchor.MiddleLeft, new Vector2(textWidth, 10f));
        detailText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(detailText);
        if (identity != null)
        {
            row.AddButtonIntoHoriLayout("faction_member_perf_up", LM.Get("faction_member_perf_up"), () =>
            {
                identity.TotalPerformance += 100;
                actor.JudgeOfficeLevel();
                StartCoroutine(ShowMembers());
            }, size: new Vector2(34f, 12f));
            row.AddButtonIntoHoriLayout("faction_member_perf_down", LM.Get("faction_member_perf_down"), () =>
            {
                identity.TotalPerformance = Math.Max(0, identity.TotalPerformance - 100);
                actor.JudgeOfficeLevel();
                StartCoroutine(ShowMembers());
            }, size: new Vector2(34f, 12f));
        }
        if (actor != leader)
            row.AddButtonIntoHoriLayout("faction_member_lead", LM.Get("faction_member_lead"), () =>
            {
                _faction.SetLeader(actor);
                StartCoroutine(ShowMembers());
            }, size: new Vector2(34f, 12f));
        row.AddButtonIntoHoriLayout("faction_member_remove", LM.Get("faction_member_remove"), () =>
        {
            _faction.RemoveMember(actor);
            StartCoroutine(ShowMembers());
        }, size: new Vector2(34f, 12f));
    }

    #endregion

    #region 诉求

    // 诉求页：现况卡(朝中正议什么、本派往期成败) → 清单卡(点一条) → 选中那条拆成几张卡：总览、为何要它、谁得谁失、
    // 天平(票拟/廷议/准奏或议会表决)、赞成/反对(各派主因、换来的票、神意改的表态)、准奏砝码(神意砝码)、谈成的交换、往期、处置(开关与发起)
    public void ShowClaims(WindowMetaTab window = null)
    {
        Clear();
        InitialTopPart();
        Empire empire = CurrentEmpire;
        if (_faction == null) return;
        string title = _faction.IsParty
            ? string.Format(LM.Get("agenda_party_title"), PartySystem.GetIdeologyName(_faction.Ideology))
            : LM.Get("label_all_claims");
        CourtState court = empire != null && CourtSystem.Applies(empire) ? CourtSystem.State(empire) : null;
        CourtFactionState data = court != null ? CourtSystem.FactionState(empire, _faction) : null;
        bool parliament = court == null && empire != null && ParliamentBillSystem.Applies(empire);
        var page = BeginContent(title, data == null ? new string[0]
            : new[] { string.Format(LM.Get("faction_claims_chips"), Mathf.RoundToInt(data.capital), Mathf.RoundToInt(data.favor), Mathf.RoundToInt(data.grievance)) });

        ClaimAgendaContext context = ClaimAgendaSystem.BuildContext(empire);
        var entries = (_faction.TemporaryFactions ?? new List<TemporaryFaction>()).Where(tf => tf != null)
            .Select(tf => (claim: tf, view: ClaimAgendaSystem.Evaluate(context, _faction, tf)))
            .OrderByDescending(entry => entry.claim.IsStarted()).ThenByDescending(entry => entry.view.CanPropose)
            .ThenByDescending(entry => entry.view.Score).ToList();
        if (_selectedFor != _faction.GetID() || entries.All(entry => entry.claim.type != _selectedClaim))
        {
            _selectedFor = _faction.GetID();
            _selectedClaim = entries.Count == 0 ? null : entries[0].claim.type;
        }

        DrawClaimNow(page, empire, court, entries, parliament);
        DrawClaimList(page, empire, court, entries, parliament);
        foreach (var entry in entries.Where(entry => entry.claim.type == _selectedClaim).Take(1))
            DrawClaimDetail(page, empire, court, context, entry.claim, entry.view, parliament);

        page.AddActionRow(ContentWidth, (LM.Get(_addingClaim ? "faction_claims_add_close" : "faction_claims_add"),
            _addingClaim ? WideKit.Gold : WideKit.Cyan, true, () =>
            {
                _addingClaim = !_addingClaim;
                ShowClaims();
            }));
        if (_addingClaim) DrawClaimPicker(page, empire);
    }

    private bool IsCurrent(CourtMemorial memorial, TemporaryFaction claim) =>
        memorial != null && claim != null && memorial.claim == claim.type.ToString() && memorial.proposer == _faction.GetID();

    // 一条诉求眼下的状态(清单与总览卡共用)
    private (string text, string hex) ClaimState(Empire empire, CourtState court, TemporaryFaction claim, ClaimAgendaView agenda, bool parliament)
    {
        if (IsCurrent(court?.memorial, claim))
            return (string.Format(LM.Get("faction_claim_state_stage"), LM.Get(CourtSystem.StageKey(empire, Mathf.Clamp(court.memorial.stage, 0, 4)))), WideKit.Cyan);
        if (!claim.Active) return (LM.Get("faction_claim_state_inactive"), WideKit.Muted);
        if (claim.IsStarted()) return (LM.Get("faction_claim_state_running"), WideKit.Cyan);
        if (claim.CountDown > 0) return (string.Format(LM.Get("faction_claim_state_cooldown"), claim.CountDown), WideKit.Muted);
        if (!agenda.CanPropose) return (LM.Get("faction_claim_state_blocked"), WideKit.Orange);
        return (LM.Get(parliament ? "faction_claim_state_eligible_bill" : "faction_claim_state_eligible"), WideKit.Gold);
    }

    // 若由本派上奏的天平读数：本派这条正在议就用当时的奏议(谈成的交换、神意改的表态与砝码都算)，否则按眼下的局面预估
    private sealed class ClaimOutlook
    {
        public bool Current;
        public CourtMemorial Memorial;
        public List<CourtScaleFactor> Draft;
        public float DraftPass;
        public List<(FixedFaction faction, int stance, float weight)> Stances;
        public float Support;
        public List<CourtScaleFactor> Approval;
        public float Chance;
    }

    private ClaimOutlook Outlook(Empire empire, CourtState court, TemporaryFaction claim)
    {
        CourtMemorial live = court?.memorial;
        bool current = IsCurrent(live, claim);
        var temp = new CourtMemorial { claim = claim.type.ToString(), proposer = _faction.GetID() };
        if (current)
        {
            temp.stage = live.stage;
            temp.bid = live.bid;
            temp.reviewed = live.reviewed;
            temp.drafted_by_ally = live.drafted_by_ally;
            temp.deals = live.deals;
            temp.stance_overrides = live.stance_overrides;
            temp.god_factors = live.god_factors;
        }
        var outlook = new ClaimOutlook { Current = current, Memorial = current ? live : temp };
        outlook.Draft = CourtSystem.DraftOutlook(empire, temp, claim, _faction);
        outlook.DraftPass = outlook.Draft.Aggregate(1f, (product, factor) => product * factor.value);
        outlook.Stances = CourtSystem.DebateStances(empire, claim, _faction, current ? live : null);
        outlook.Support = current && live.support >= 0f ? live.support : CourtSystem.SupportShare(outlook.Stances);
        temp.support = outlook.Support;
        outlook.Approval = CourtSystem.ApprovalFactors(empire, temp, claim.type, _faction);
        outlook.Chance = CourtSystem.Chance(outlook.Approval);
        return outlook;
    }

    // 现况：朝中正议哪条、走到哪一步；本派诉求几条、能上奏几条、往期成败
    private void DrawClaimNow(AutoVertLayoutGroup page, Empire empire, CourtState court,
        List<(TemporaryFaction claim, ClaimAgendaView view)> entries, bool parliament)
    {
        int eligible = entries.Count(entry => entry.claim.Active && !entry.claim.IsStarted() && entry.claim.CountDown <= 0 && entry.view.CanPropose);
        string header;
        if (court != null)
        {
            List<CourtMemorialRecord> mine = (court.history ?? new List<CourtMemorialRecord>()).Where(record => record.proposer == _faction.GetID()).ToList();
            int won = mine.Count(ClaimScalePanel.Succeeded);
            header = string.Format(LM.Get("faction_claims_now_header"), entries.Count, eligible, won, mine.Count - won);
        }
        else header = string.Format(LM.Get(parliament ? "faction_claims_now_header_bill" : "faction_claims_now_header_plain"), entries.Count, eligible);
        var card = page.BeginCard(ContentWidth, "faction_card_claim_now", WideKit.Gold, header, "ui/icons/iconKingdom");
        float label = (ContentWidth - Pad) * 0.18f;
        if (court?.memorial != null)
        {
            CourtMemorial memorial = court.memorial;
            FixedFaction proposer = CourtSystem.FindFaction(empire, memorial.proposer);
            string who = proposer == _faction ? LM.Get("faction_claims_now_self") : proposer?.Name ?? "";
            string value = string.Format(LM.Get("faction_claims_now_value"), CourtSystem.IssueName(memorial.claim, empire), who,
                LM.Get(CourtSystem.StageKey(empire, Mathf.Clamp(memorial.stage, 0, 4))));
            if (memorial.support >= 0f)
                value += " · " + string.Format(LM.Get("faction_claims_now_support"), Mathf.RoundToInt(memorial.support), CourtSystem.DebateThreshold);
            card.AddValueRow(ContentWidth, 0, LM.Get("faction_claims_now_label"), value, proposer == _faction ? WideKit.Cyan : WideKit.Soft, labelWidth: label);
        }
        else if (court != null)
            card.AddValueRow(ContentWidth, 0, LM.Get("faction_claims_now_label"), LM.Get("faction_claims_now_idle"), WideKit.Muted, labelWidth: label);
        else card.AddParagraph(ContentWidth, LM.Get(parliament ? "faction_claims_now_parliament" : "faction_claims_now_plain"), 6, WideKit.Muted);
    }

    // 清单：独特议题、公共议题两节；每行 状态 · 议程分 · 准奏率(议会制为表决能否通过)，点一行在下面展开
    private void DrawClaimList(AutoVertLayoutGroup page, Empire empire, CourtState court,
        List<(TemporaryFaction claim, ClaimAgendaView view)> entries, bool parliament)
    {
        var card = page.BeginCard(ContentWidth, "faction_card_claim_list", WideKit.Gold,
            LM.Get(court != null ? "faction_claims_list_hint" : parliament ? "faction_claims_list_hint_bill" : "faction_claims_list_hint_plain"), "ui/icons/iconBooks");
        if (entries.Count == 0)
        {
            card.AddParagraph(ContentWidth, LM.Get("faction_claims_none"), 7, WideKit.Muted);
            return;
        }
        float nameWidth = (ContentWidth - Pad) * 0.4f;
        foreach ((string key, bool isPublic) in new[] { ("court_unique_issues", false), ("court_public_issues", true) })
        {
            var group = entries.Where(entry => CourtIssues.IsPublic(_faction, entry.claim.type) == isPublic).ToList();
            if (group.Count == 0) continue;
            SubHeading(card, ContentWidth, string.Format(LM.Get("faction_claims_group"), LM.Get(key), group.Count));
            for (int i = 0; i < group.Count; i++)
            {
                (TemporaryFaction claim, ClaimAgendaView view) = group[i];
                bool selected = claim.type == _selectedClaim;
                (string state, string stateHex) = ClaimState(empire, court, claim, view, parliament);
                string name = TranslateHelper.GetTemporaryFactionClaimText(claim.type, empire);
                if (selected) name = ("› " + name).ColorString(WideKit.Gold);
                string odds = "—", oddsHex = WideKit.Muted;
                if (court != null)
                {
                    float chance = Outlook(empire, court, claim).Chance;
                    odds = Percent(chance);
                    oddsHex = ChanceHex(chance);
                }
                else if (parliament)
                {
                    bool passed = ParliamentBillSystem.Count(empire, claim, _faction).Passed;
                    odds = LM.Get(passed ? "faction_claim_bill_pass" : "faction_claim_bill_fail");
                    oddsHex = passed ? WideKit.Green : WideKit.Red;
                }
                var row = card.AddTableRow(ContentWidth, i, null, name, nameWidth, (state, stateHex), ($"{view.Score:0}", WideKit.Soft), (odds, oddsHex));
                TemporaryFactionType type = claim.type;
                UIHelper.MakeClickable(row.gameObject, () =>
                {
                    _selectedClaim = type;
                    ShowClaims();
                });
            }
        }
    }

    private void DrawClaimDetail(AutoVertLayoutGroup page, Empire empire, CourtState court, ClaimAgendaContext context,
        TemporaryFaction claim, ClaimAgendaView agenda, bool parliament)
    {
        float half = Half;
        // 总览
        (string state, string stateHex) = ClaimState(empire, court, claim, agenda, parliament);
        if (!agenda.CanPropose && !string.IsNullOrEmpty(agenda.Blocker) && !claim.IsStarted() && !IsCurrent(court?.memorial, claim))
            state += ": " + (LM.Has(agenda.Blocker) ? LM.Get(agenda.Blocker) : agenda.Blocker);
        var card = page.BeginCard(ContentWidth, TranslateHelper.GetTemporaryFactionClaimText(claim.type, empire),
            stateHex == WideKit.Muted ? WideKit.Muted : WideKit.Gold, state, "ui/icons/iconKingdom");
        float stance = CourtIssues.Stance(_faction, claim);
        int level = CourtIssues.StanceLevel(stance);
        card.AddMetricRow(ContentWidth,
            (LM.Get("faction_claim_score"), $"{agenda.Score:0}", agenda.CanPropose ? WideKit.Gold : WideKit.Muted),
            (LM.Get("faction_claim_support"), $"{agenda.Support:0}% / {agenda.Opposition:0}%", agenda.Support >= agenda.Opposition ? WideKit.Green : WideKit.Red),
            (LM.Get("faction_claim_stance"), $"{CourtSystem.StanceName(level)} {stance:0}", level > 0 ? WideKit.Green : level < 0 ? WideKit.Red : WideKit.Muted),
            (LM.Get("faction_claim_progress"), $"{claim.progress:0}/{claim.progressMax:0}", claim.IsStarted() ? WideKit.Cyan : WideKit.Value),
            (LM.Get("label_budget"), claim.Budget.ToString(), WideKit.Value));
        string technology = string.IsNullOrEmpty(agenda.TechnologyName) ? LM.Get("agenda_no_technology") : agenda.TechnologyName;
        string extra = _faction.IsParty
            ? string.Format(LM.Get("faction_claim_party_line"), agenda.PopulationShare,
                context?.ParliamentSeats > 0 ? $"{agenda.LegislativeShare:0}%" : LM.Get("agenda_no_parliament"), agenda.Urgency, agenda.IdeologyStage, technology)
            : string.Format(LM.Get("faction_claim_tech_line"), technology, agenda.Urgency);
        if (claim.type == TemporaryFactionType.提高福利 && empire?.data?.constitutional_economy != null)
        {
            var welfare = empire.data.constitutional_economy;
            extra += "  " + string.Format(LM.Get("agenda_welfare_line"), welfare.welfare_level,
                ConstitutionalEconomySystem.GetWelfareAnnualCost(empire, welfare.welfare_level),
                LM.Get(welfare.welfare_funded ? "agenda_welfare_funded" : "agenda_welfare_unfunded"));
        }
        card.AddParagraph(ContentWidth, extra, 6, WideKit.Muted);

        // 为何要它 | 谁得谁失
        var reasons = page.BeginColumns(ContentWidth);
        var why = reasons.BeginColumnCard(half, LM.Get("faction_card_claim_why"), WideKit.Gold,
            string.Format(LM.Get("faction_claim_why_header"), Signed(stance)));
        List<(string key, string arg, float value)> parts = CourtIssues.Explain(_faction, claim);
        if (parts.Count == 0) why.AddParagraph(half, LM.Get("stance_reason_indifferent"), 7, WideKit.Muted);
        for (int i = 0; i < parts.Count; i++)
            why.AddValueRow(half, i, ClaimScalePanel.PartLabel(parts[i]), Signed(parts[i].value),
                parts[i].value >= 0f ? WideKit.Green : WideKit.Red, labelWidth: (half - Pad) * 0.74f);
        var interests = reasons.BeginColumnCard(half, LM.Get("faction_card_claim_interests"), WideKit.Gold);
        var effects = Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>()
            .Select(socialClass => (socialClass, effect: FactionClassSystem.GetClaimClassEffect(claim.type, socialClass)))
            .Where(item => Mathf.Abs(item.effect) >= 1f).OrderByDescending(item => item.effect).ToList();
        if (effects.Count == 0) interests.AddParagraph(half, LM.Get("faction_claim_no_interests"), 7, WideKit.Muted);
        for (int i = 0; i < effects.Count; i++)
            interests.AddValueRow(half, i, effects[i].socialClass.ToTranslate(), Signed(effects[i].effect),
                effects[i].effect > 0f ? WideKit.Green : WideKit.Red, labelWidth: (half - Pad) * 0.6f);

        if (court != null) DrawCourtScale(page, empire, court, claim);
        else if (parliament) DrawBillScale(page, empire, claim);

        // 往期 | 处置(朝堂)；处置(其余)
        if (court != null)
        {
            var bottom = page.BeginColumns(ContentWidth);
            List<CourtMemorialRecord> records = (court.history ?? new List<CourtMemorialRecord>())
                .Where(record => record.claim == claim.type.ToString() && record.proposer == _faction.GetID())
                .OrderByDescending(record => record.closed).ToList();
            int won = records.Count(ClaimScalePanel.Succeeded);
            var past = bottom.BeginColumnCard(half, LM.Get("faction_card_claim_history"), WideKit.Gold,
                records.Count == 0 ? null : string.Format(LM.Get("faction_claim_history_header"), won, records.Count - won));
            if (records.Count == 0) past.AddParagraph(half, LM.Get("faction_claim_history_none"), 6, WideKit.Muted);
            for (int i = 0; i < Math.Min(4, records.Count); i++)
            {
                CourtMemorialRecord record = records[i];
                int years = Date.getYearsSince(record.closed);
                string when = years <= 0 ? LM.Get("court_log_this_year") : string.Format(LM.Get("court_log_years_ago"), years);
                string kind = ClaimScalePanel.OutcomeKind(record.outcome);
                past.AddValueRow(half, i, when, LM.Get("claim_scale_outcome_" + kind),
                    ClaimScalePanel.Succeeded(record) ? WideKit.Green : kind == "void" ? WideKit.Muted : WideKit.Red, labelWidth: (half - Pad) * 0.4f);
            }
            DrawClaimActions(bottom.BeginColumnCard(half, LM.Get("faction_card_claim_actions"), WideKit.Gold), half, empire, claim, false);
        }
        else DrawClaimActions(page.BeginCard(ContentWidth, "faction_card_claim_actions", WideKit.Gold), ContentWidth, empire, claim, parliament);
    }

    // 朝堂的天平：三格(票拟把握、廷议赞成率、准奏率) → 赞成 | 反对(含观望) → 准奏砝码(神意砝码) → 谈成的交换
    private void DrawCourtScale(AutoVertLayoutGroup page, Empire empire, CourtState court, TemporaryFaction claim)
    {
        float half = Half;
        ClaimOutlook outlook = Outlook(empire, court, claim);
        int stage = outlook.Current ? outlook.Memorial.stage : 0;
        bool editable = outlook.Current && CourtSystem.CanEditDebate(empire, out _);
        var scale = page.BeginCard(ContentWidth, outlook.Current ? "faction_card_claim_scale_current" : "faction_card_claim_scale", WideKit.Cyan,
            LM.Get("faction_claim_scale_hint"), "ui/icons/iconKingdom");
        bool drafted = stage >= 2, debated = stage >= 3;
        bool debateOk = outlook.Support >= CourtSystem.DebateThreshold;
        var gates = scale.AddGateRow(ContentWidth,
            (LM.Get(CourtSystem.StageKey(empire, 1)), "", drafted ? true : (bool?)null,
                drafted ? LM.Get("faction_gate_passed") : Percent(outlook.DraftPass),
                drafted || outlook.DraftPass >= 0.99f ? WideKit.Green : WideKit.Orange, drafted ? 1f : outlook.DraftPass),
            (LM.Get(CourtSystem.StageKey(empire, 2)), string.Format(LM.Get("faction_gate_threshold"), CourtSystem.DebateThreshold), debateOk || debated,
                $"{Mathf.RoundToInt(outlook.Support)}%", debateOk ? WideKit.Green : WideKit.Red, outlook.Support / 100f),
            (LM.Get(CourtSystem.StageKey(empire, 3)), "", null, Percent(outlook.Chance), ChanceHex(outlook.Chance), outlook.Chance));
        if (gates.transform.childCount >= 3)
        {
            Tip(gates.transform.GetChild(0).gameObject, "faction_claim_tip_draft", LM.Get(CourtSystem.StageKey(empire, 1)),
                string.Join("\n", outlook.Draft.Select(factor => $"{FactorText(factor)} {Mathf.RoundToInt(factor.value * 100f)}%")));
            Tip(gates.transform.GetChild(1).gameObject, "faction_claim_tip_debate", LM.Get(CourtSystem.StageKey(empire, 2)),
                string.Join("\n", outlook.Stances.OrderByDescending(item => item.stance).ThenByDescending(item => item.weight).Select(item =>
                    $"{item.faction.Name} {CourtSystem.StanceName(item.stance)} {item.weight:0.#} · {StanceCause(outlook, claim, item.faction, item.stance)}")));
            Tip(gates.transform.GetChild(2).gameObject, "faction_claim_tip_decide", LM.Get(CourtSystem.StageKey(empire, 3)),
                string.Join("\n", outlook.Approval.Where(factor => Mathf.Abs(factor.value) >= 0.005f || factor.key == "court_scale_base")
                    .Select(factor => $"{FactorText(factor)} {(factor.value >= 0 ? "+" : "")}{Mathf.RoundToInt(factor.value * 100f)}")));
        }

        // 赞成 | 反对(观望的列在反对卡下面)；本派这条正在议、还没批红时，点别派一行按神意改表态
        var pro = outlook.Stances.Where(item => item.stance > 0).OrderByDescending(item => item.weight).ToList();
        var con = outlook.Stances.Where(item => item.stance < 0).OrderByDescending(item => item.weight).ToList();
        var neutral = outlook.Stances.Where(item => item.stance == 0).OrderByDescending(item => item.weight).ToList();
        var sides = page.BeginColumns(ContentWidth);
        var proCard = sides.BeginColumnCard(half, string.Format(LM.Get("faction_card_claim_pro"), pro.Count), WideKit.Green,
            string.Format(LM.Get("faction_claim_weight_header"), pro.Sum(item => item.weight)));
        DrawSideRows(proCard, half, empire, outlook, claim, pro, WideKit.Green, editable, 0);
        if (pro.Count == 0) proCard.AddParagraph(half, LM.Get("label_none"), 7, WideKit.Muted);
        var conCard = sides.BeginColumnCard(half, string.Format(LM.Get("faction_card_claim_con"), con.Count), WideKit.Red,
            string.Format(LM.Get("faction_claim_weight_header"), con.Sum(item => item.weight)));
        DrawSideRows(conCard, half, empire, outlook, claim, con, WideKit.Red, editable, 0);
        if (con.Count == 0) conCard.AddParagraph(half, LM.Get("label_none"), 7, WideKit.Muted);
        if (neutral.Count > 0)
        {
            SubHeading(conCard, half, string.Format(LM.Get("faction_claim_neutral_heading"), neutral.Count));
            DrawSideRows(conCard, half, empire, outlook, claim, neutral, WideKit.Muted, editable, con.Count);
        }
        if (editable) proCard.AddParagraph(half, LM.Get("faction_claim_edit_hint"), 6, WideKit.Gold);

        // 准奏砝码：各项加减；正在议的可加神意砝码(点一下换一档)
        var weights = page.BeginCard(ContentWidth, "faction_card_claim_factors", WideKit.Gold,
            string.Format(LM.Get("faction_claim_factors_header"), Percent(outlook.Chance)), "ui/icons/iconKingdom");
        int index = 0;
        foreach (CourtScaleFactor factor in outlook.Approval.Where(factor => Mathf.Abs(factor.value) >= 0.005f || factor.key == "court_scale_base"))
            weights.AddValueRow(ContentWidth, index++, FactorText(factor), $"{(factor.value >= 0 ? "+" : "")}{Mathf.RoundToInt(factor.value * 100f)}",
                factor.key.StartsWith("court_scale_divine_") ? WideKit.Gold : factor.value >= 0f ? WideKit.Green : WideKit.Red,
                labelWidth: (ContentWidth - Pad) * 0.72f);
        if (editable)
        {
            CourtMemorial live = outlook.Memorial;
            weights.AddChipRow(ContentWidth - Pad, CourtSystem.DivinePositive.Concat(CourtSystem.DivineNegative).Select(key =>
            {
                int value = CourtSystem.DivineApproval(live, key);
                string label = LM.Get("court_scale_divine_" + key) + (value != 0 ? $" {(value > 0 ? "+" : "")}{value}" : "");
                return (label, value != 0, (UnityAction)(() =>
                {
                    CourtSystem.CycleDivineApproval(empire, key);
                    ShowClaims();
                }));
            }).ToArray());
        }

        // 谈成的交换(廷议前，谁拿什么换了表态)
        List<CourtDeal> deals = outlook.Current ? outlook.Memorial.deals ?? new List<CourtDeal>() : new List<CourtDeal>();
        if (deals.Count == 0) return;
        var dealCard = page.BeginCard(ContentWidth, "faction_card_claim_deals", WideKit.Gold,
            string.Format(LM.Get("faction_claim_deals_count"), deals.Count), "ui/icons/iconKingdom");
        for (int i = 0; i < deals.Count; i++)
        {
            CourtDeal deal = deals[i];
            dealCard.AddValueRow(ContentWidth, i, string.Format(LM.Get("faction_claim_deal_label"), deal.partner_name,
                    CourtSystem.StanceName(deal.before), CourtSystem.StanceName(deal.after)), ClaimScalePanel.DealTerms(deal),
                deal.kind == "reneged" ? WideKit.Red : WideKit.Gold, labelWidth: (ContentWidth - Pad) * 0.42f);
        }
    }

    // 一边的各派：名字 · 主因 · 权重；可改时点了按神意换表态
    private void DrawSideRows(AutoVertLayoutGroup card, float width, Empire empire, ClaimOutlook outlook, TemporaryFaction claim,
        List<(FixedFaction faction, int stance, float weight)> list, string hex, bool editable, int offset)
    {
        for (int i = 0; i < list.Count; i++)
        {
            (FixedFaction faction, int stance, float weight) = list[i];
            string id = faction.GetID();
            UnityAction action = null;
            if (editable && faction != _faction)
                action = () =>
                {
                    CourtSystem.CycleStance(empire, id);
                    ShowClaims();
                };
            card.AddValueRow(width, offset + i, faction.Name, $"{StanceCause(outlook, claim, faction, stance)} · {weight:0.#}", hex, action,
                labelWidth: (width - Pad) * 0.36f);
        }
    }

    // 一派为什么这么表态：上奏一派 / 神意改的 / 交换换来的 / 立场来由里与表态同向的最大一项
    private string StanceCause(ClaimOutlook outlook, TemporaryFaction claim, FixedFaction faction, int stance)
    {
        if (faction == _faction) return LM.Get("faction_claim_cause_proposer");
        if (outlook.Current && CourtSystem.StanceOverridden(outlook.Memorial, faction.GetID())) return LM.Get("stance_reason_divine");
        CourtDeal deal = outlook.Current ? CourtSystem.DealOf(outlook.Memorial, faction) : null;
        if (deal != null && deal.after == stance) return string.Format(LM.Get("faction_claim_cause_deal"), ClaimScalePanel.DealTerms(deal));
        return MainReason(faction, claim, stance);
    }

    // 议会的天平：赞成/反对/弃权三格(席位) → 赞成的党 | 反对的党(含弃权)
    private void DrawBillScale(AutoVertLayoutGroup page, Empire empire, TemporaryFaction claim)
    {
        float half = Half;
        ParliamentBillSystem.Tally tally = ParliamentBillSystem.Count(empire, claim, _faction);
        float total = Math.Max(1, tally.Yes + tally.No + tally.Abstain);
        var scale = page.BeginCard(ContentWidth, "faction_card_claim_bill", WideKit.Cyan,
            LM.Get(tally.Passed ? "claim_scale_bill_would_pass" : "claim_scale_bill_would_fail"), "ui/icons/iconKingdom");
        scale.AddGateRow(ContentWidth,
            (LM.Get("claim_scale_vote_yes"), "", tally.Passed, tally.Yes.ToString(), WideKit.Green, tally.Yes / total),
            (LM.Get("claim_scale_vote_no"), "", null, tally.No.ToString(), WideKit.Red, tally.No / total),
            (LM.Get("claim_scale_vote_abstain"), "", null, tally.Abstain.ToString(), WideKit.Muted, tally.Abstain / total));
        var yes = tally.Parties.Where(item => item.vote > 0).OrderByDescending(item => item.seats).ToList();
        var no = tally.Parties.Where(item => item.vote < 0).OrderByDescending(item => item.seats).ToList();
        var abstain = tally.Parties.Where(item => item.vote == 0).OrderByDescending(item => item.seats).ToList();
        var sides = page.BeginColumns(ContentWidth);
        void Rows(AutoVertLayoutGroup card, List<(FixedFaction party, int seats, int vote, float stance)> list, string hex)
        {
            for (int i = 0; i < list.Count; i++)
                card.AddValueRow(half, i, list[i].party.Name, $"{MainReason(list[i].party, claim, list[i].vote)} · {list[i].seats}", hex,
                    labelWidth: (half - Pad) * 0.36f);
        }
        var yesCard = sides.BeginColumnCard(half, string.Format(LM.Get("faction_card_claim_pro_bill"), yes.Count), WideKit.Green,
            string.Format(LM.Get("faction_claim_seats_header"), tally.Yes));
        Rows(yesCard, yes, WideKit.Green);
        if (yes.Count == 0) yesCard.AddParagraph(half, LM.Get("label_none"), 7, WideKit.Muted);
        var noCard = sides.BeginColumnCard(half, string.Format(LM.Get("faction_card_claim_con_bill"), no.Count), WideKit.Red,
            string.Format(LM.Get("faction_claim_seats_header"), tally.No));
        Rows(noCard, no, WideKit.Red);
        if (no.Count == 0) noCard.AddParagraph(half, LM.Get("label_none"), 7, WideKit.Muted);
        if (abstain.Count > 0)
        {
            SubHeading(noCard, half, string.Format(LM.Get("faction_claim_abstain_heading"), abstain.Count));
            Rows(noCard, abstain, WideKit.Muted);
        }
    }

    // 处置：启用、地方可推、隐藏、作为谋划；发起(神力替本派上奏/提案)、移除
    private void DrawClaimActions(AutoVertLayoutGroup card, float width, Empire empire, TemporaryFaction claim, bool parliament)
    {
        var chips = new List<(string label, bool on, UnityAction action)>
        {
            (LM.Get("faction_claim_active"), claim.Active, () => { claim.Active = !claim.Active; ShowClaims(); }),
            (LM.Get("faction_claim_local"), claim.canBePushByLocal, () => { claim.canBePushByLocal = !claim.canBePushByLocal; ShowClaims(); }),
            (LM.Get("faction_claim_hide"), claim.Hide, () => { claim.Hide = !claim.Hide; ShowClaims(); })
        };
        if (!_faction.IsParty)
            chips.Add((LM.Get("faction_claim_plot"), claim.ShowAsPlot, () =>
            {
                claim.ShowAsPlot = !claim.ShowAsPlot;
                if ((_kingdom?.king?.plot?.name ?? "") == claim.type.ToString()) _kingdom?.king?.plot?.setAlive(false);
                if (claim.IsStarted()) claim.End();
                ShowClaims();
            }));
        // 半宽的卡一行放两个
        int perRow = width < ContentWidth - 1f ? 2 : chips.Count;
        for (int start = 0; start < chips.Count; start += perRow)
            card.AddChipRow(width - Pad, chips.Skip(start).Take(perRow).ToArray());

        string blocked = CourtSystem.CannotGodPropose(empire, _faction, claim);
        card.AddActionRow(width - Pad,
            (LM.Get(parliament ? "faction_claim_propose_bill" : "faction_claim_propose"), WideKit.Green, blocked == null, () =>
            {
                if (CourtSystem.GodPropose(empire, _faction, claim)) ShowClaims();
            }),
            (LM.Get("faction_claim_remove"), WideKit.Red, true, () =>
            {
                _faction.TemporaryFactions.Remove(claim);
                _faction.TemporaryFactionTypesRecord?.Remove(claim.type);
                ShowClaims();
            }));
        if (blocked != null && !claim.IsStarted())
            card.AddParagraph(width, string.Format(LM.Get("faction_claim_cannot_propose"), LM.Has(blocked) ? LM.Get(blocked) : blocked), 6, WideKit.Muted);
    }

    private static string FactorText(CourtScaleFactor factor)
    {
        string format = LM.Get(factor.key);
        if (string.IsNullOrEmpty(factor.arg)) return format;
        try
        {
            return string.Format(format, factor.arg.Split('|').Cast<object>().ToArray());
        }
        catch (FormatException)
        {
            return format;
        }
    }

    // 一派表态的主因(立场来由里与表态同向的最大一项)
    private static string MainReason(FixedFaction faction, TemporaryFaction claim, int stance)
    {
        (string key, string arg, float value) main = CourtIssues.Explain(faction, claim)
            .FirstOrDefault(part => stance == 0 || Math.Sign(part.value) == Math.Sign(stance));
        return main.key == null ? LM.Get("stance_reason_indifferent") : ClaimScalePanel.PartLabel(main);
    }

    // 添加诉求：按派系类型列出各类诉求，点了加进本派
    private void DrawClaimPicker(AutoVertLayoutGroup page, Empire empire)
    {
        var card = page.BeginCard(ContentWidth, "faction_card_claim_picker", WideKit.Cyan, LM.Get("faction_claims_picker_hint"));
        var owned = new HashSet<TemporaryFactionType>(_faction.TemporaryFactionTypesRecord ?? new List<TemporaryFactionType>());
        var all = FactionManager.FactionConfig.Values.SelectMany(list => list).Concat(CourtIssues.PublicIssues).Distinct()
            .OrderBy(type => (int)type).ToList();
        for (int start = 0; start < all.Count; start += 6)
            card.AddChipRow(ContentWidth - Pad, all.Skip(start).Take(6).Select(type =>
                (TranslateHelper.GetTemporaryFactionClaimText(type, empire), owned.Contains(type), (UnityAction)(() =>
                {
                    _faction.TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
                    if (!_faction.TemporaryFactionTypesRecord.Contains(type)) _faction.TemporaryFactionTypesRecord.Add(type);
                    _faction.TemporaryFactions = _faction.ConvertToObjectFromFactionType();
                    _selectedClaim = type;
                    ShowClaims();
                }))).ToArray());
    }

    #endregion

    #region 制度立场

    public void ShowInstitutionStance(WindowMetaTab window = null)
    {
        Clear();
        InitialTopPart();
        Empire empire = CurrentEmpire;
        if (empire == null || _faction == null) return;
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        (List<InstitutionNodeConfig> supports, List<InstitutionNodeConfig> opposes) = InstitutionSystem.GetFactionInstitutionStance(culture, _faction.Type);
        Dictionary<string, InstitutionNodeView> views = InstitutionSystem.BuildLineView(empire).ToDictionary(view => view.Node.id, view => view);
        var page = BeginContent("institution_faction_stance_title");
        InstitutionReformState active = empire.data.institution_state?.active_reform;
        if (active != null)
        {
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(active.node_id);
            FixedFaction sponsor = InstitutionSystem.GetReformSponsor(empire, active);
            string role = sponsor == _faction ? LM.Get("institution_faction_role_sponsor")
                : node?.politics.support_factions.ContainsKey(_faction.Type) == true ? LM.Get("institution_faction_role_supporter")
                : node?.politics.oppose_factions.ContainsKey(_faction.Type) == true ? LM.Get("institution_faction_role_opponent")
                : LM.Get("institution_faction_role_neutral");
            var reform = page.BeginCard(ContentWidth, "faction_card_reform", WideKit.Gold, InstitutionSystem.GetNodeName(empire, node), "ui/icons/iconBooks");
            reform.AddMetricRow(ContentWidth, (LM.Get("faction_reform_role"), role, WideKit.Gold),
                (LM.Get("faction_reform_progress"), $"{active.progress:0.#}%", WideKit.Cyan),
                (LM.Get("faction_reform_radical"), $"{active.radicalism:0}%", WideKit.Orange));
        }
        var columns = page.BeginColumns(ContentWidth);
        DrawStanceColumn(columns, Half, "institution_faction_supports", supports, views, WideKit.Green);
        DrawStanceColumn(columns, Half, "institution_faction_opposes", opposes, views, WideKit.Red);
    }

    private static void DrawStanceColumn(AutoHoriLayoutGroup columns, float width, string title, List<InstitutionNodeConfig> nodes,
        Dictionary<string, InstitutionNodeView> views, string hex)
    {
        nodes ??= new List<InstitutionNodeConfig>();
        var card = columns.BeginColumnCard(width, LM.Get(title), hex, string.Format(LM.Get("faction_claims_count"), nodes.Count));
        if (nodes.Count == 0) card.AddParagraph(width, LM.Get("institution_faction_no_stance"), 7, WideKit.Muted);
        for (int i = 0; i < nodes.Count; i++)
        {
            InstitutionNodeStatus status = views.TryGetValue(nodes[i].id, out InstitutionNodeView view) ? view.Status : InstitutionNodeStatus.Locked;
            string statusKey = status switch
            {
                InstitutionNodeStatus.Enacted => "institution_status_enacted",
                InstitutionNodeStatus.Reforming => "institution_status_reforming",
                InstitutionNodeStatus.Available => "institution_status_available",
                InstitutionNodeStatus.Forceable => "institution_status_force_available",
                InstitutionNodeStatus.Superseded => "institution_status_superseded",
                _ => "institution_status_locked"
            };
            string statusHex = status == InstitutionNodeStatus.Enacted ? WideKit.Green : status == InstitutionNodeStatus.Reforming ? WideKit.Cyan
                : status is InstitutionNodeStatus.Available or InstitutionNodeStatus.Forceable ? WideKit.Gold : WideKit.Muted;
            card.AddValueRow(width, i, InstitutionSystem.GetNodeName(nodes[i], view?.Culture), LM.Get(statusKey), statusHex, labelWidth: (width - Pad) * 0.62f);
        }
    }

    #endregion

    #region 社会基础

    // 社会基础：力量 | 最倚重的阶层 两栏，下面各阶层表(悬停看近来恩怨与本派哪些诉求在拉拢它)
    public void ShowClassBase(WindowMetaTab window = null)
    {
        Clear();
        InitialTopPart();
        Empire empire = CurrentEmpire;
        if (empire == null || _faction == null) return;
        List<FixedFaction> factions = empire.CoreKingdom?.GetRegime()?.GetPlayerFactions() ?? new List<FixedFaction>();
        FactionClassSystem.EnsureEmpireProfiles(empire, factions);
        Dictionary<SocialClass, float> population = InstitutionSystem.BuildClassShares(empire);
        _faction.ClassPower = population.Sum(pair => pair.Value * _faction.ClassSupport[pair.Key]);
        List<SocialClass> classes = Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>()
            .Where(socialClass => population.TryGetValue(socialClass, out float share) && share > 0f)
            .OrderByDescending(socialClass => population[socialClass] * _faction.ClassSupport[socialClass]).ToList();
        var page = BeginContent("faction_class_foundation_title");

        var top = page.BeginColumns(ContentWidth);
        var summary = top.BeginColumnCard(Half, LM.Get("faction_card_class_summary"), WideKit.Gold);
        summary.AddMetricRow(Half, (LM.Get("faction_rail_class_power"), $"{_faction.ClassPower:0.#}%", WideKit.Cyan),
            (LM.Get("label_central_ratio"), $"{_faction.CentralRatio}%", WideKit.Gold));
        summary.AddParagraph(Half, LM.Get("faction_class_model_hint"), 6, WideKit.Muted);
        var pillars = top.BeginColumnCard(Half, LM.Get("faction_card_class_top"), WideKit.Gold);
        if (classes.Count == 0) pillars.AddParagraph(Half, LM.Get("label_none"), 7, WideKit.Muted);
        for (int i = 0; i < Math.Min(4, classes.Count); i++)
            pillars.AddValueRow(Half, i, classes[i].ToTranslate(),
                $"{population[classes[i]] * _faction.ClassSupport[classes[i]]:0.#}%", WideKit.Cyan);

        var card = page.BeginCard(ContentWidth, "faction_card_class_table", WideKit.Gold, null, "ui/icons/iconFamily");
        const float NameWidth = 70f;
        card.AddTableRow(ContentWidth, -1, null, LM.Get("faction_class_col_class"), NameWidth, (LM.Get("faction_class_col_population"), null),
            (LM.Get("faction_class_col_affinity"), null), (LM.Get("faction_class_col_favor"), null), (LM.Get("faction_class_col_support"), null),
            (LM.Get("faction_class_col_power"), null));
        for (int i = 0; i < classes.Count; i++)
        {
            SocialClass socialClass = classes[i];
            float share = population[socialClass] * 100f;
            float affinity = _faction.ClassAffinities[socialClass];
            float favor = _faction.ClassFavor[socialClass];
            float support = _faction.ClassSupport[socialClass];
            var row = card.AddTableRow(ContentWidth, i, null, $"{socialClass.ToTranslate()} · {LM.Get(FactionClassSystem.GetStratumKey(socialClass))}", NameWidth,
                ($"{share:0.#}%", null),
                (AffinityText(affinity), affinity >= 20f ? WideKit.Green : affinity <= -20f ? WideKit.Red : WideKit.Soft),
                ($"{favor:0}", favor >= 60f ? WideKit.Green : favor <= 40f ? WideKit.Red : WideKit.Gold),
                ($"{support:0.#}%", null),
                ($"{share * support / 100f:0.#}%", WideKit.Cyan));
            List<string> courting = (_faction.TemporaryFactionTypesRecord ?? new List<TemporaryFactionType>())
                .Select(claim => (claim, effect: FactionClassSystem.GetClaimClassEffect(claim, socialClass)))
                .Where(item => item.effect > 0f).OrderByDescending(item => item.effect).Take(3)
                .Select(item => $"{TranslateHelper.GetTemporaryFactionClaimText(item.claim, empire)} +{item.effect:0}").ToList();
            Tip(row.gameObject, $"faction_class_{socialClass}", socialClass.ToTranslate(),
                string.Format(LM.Get("faction_class_recent_source"), FactionClassSystem.GetFavorSourceText(_faction, socialClass)) + "\n" +
                string.Format(LM.Get("faction_class_courting_options"), courting.Count == 0 ? LM.Get("faction_class_no_courting_option") : string.Join(" · ", courting)));
        }
    }

    private static string AffinityText(float affinity) => LM.Get(affinity >= 60f ? "faction_class_affinity_core"
        : affinity >= 20f ? "faction_class_affinity_friendly" : affinity > -20f ? "faction_class_affinity_neutral"
        : affinity > -60f ? "faction_class_affinity_distant" : "faction_class_affinity_hostile");

    #endregion

    #region 党务 / 派务

    public void ShowPartyAffairs(WindowMetaTab window = null)
    {
        Clear();
        InitialTopPart();
        Empire empire = CurrentEmpire;
        if (_faction == null) return;
        var page = BeginContent(_faction.IsParty ? "party_affairs_title" : "faction_affairs_title");
        try
        {
            if (_faction.IsParty) PartyAffairsPanel.Render(page, empire, _faction, ContentWidth);
            else DrawCourtAffairs(page, empire);
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogError($"派系窗口派务页绘制失败({_faction?.Name}): {exception}");
        }
    }

    // 朝堂派系的派务：政治本钱(资本、圣眷、怨望，神力可加减) → 结盟与席位 | 人情账 → 赞成的议题 | 反对的议题 → 观望的议题
    private void DrawCourtAffairs(AutoVertLayoutGroup page, Empire empire)
    {
        if (empire == null || !CourtSystem.Applies(empire))
        {
            page.AddEmptyCard(ContentWidth, "faction_affairs_title", LM.Get("faction_affairs_no_court"));
            return;
        }
        float half = Half;
        CourtFactionState data = CourtSystem.FactionState(empire, _faction);
        var capital = page.BeginCard(ContentWidth, "faction_card_court_capital", WideKit.Gold,
            CourtSystem.IsBoycotting(data) ? LM.Get("court_boycotting") : null, "ui/icons/iconKingdom");
        capital.AddMetricRow(ContentWidth,
            (LM.Get("court_col_capital"), $"{data.capital:0}", WideKit.Gold),
            (LM.Get("court_col_favor"), $"{data.favor:0}", data.favor >= 60f ? WideKit.Green : data.favor <= 30f ? WideKit.Red : WideKit.Value),
            (LM.Get("court_col_grievance"), $"{data.grievance:0}", data.grievance >= 60f ? WideKit.Red : WideKit.Value));
        void Adjust(string field, float delta)
        {
            CourtSystem.AdjustFaction(empire, _faction, field, delta);
            ShowPartyAffairs();
        }
        capital.AddActionRow(ContentWidth - Pad,
            (LM.Get("faction_affairs_capital_up"), WideKit.Green, true, () => Adjust("capital", 10f)),
            (LM.Get("faction_affairs_capital_down"), WideKit.Red, true, () => Adjust("capital", -10f)),
            (LM.Get("faction_affairs_favor_up"), WideKit.Green, true, () => Adjust("favor", 10f)),
            (LM.Get("faction_affairs_favor_down"), WideKit.Red, true, () => Adjust("favor", -10f)),
            (LM.Get("faction_affairs_grievance_up"), WideKit.Red, true, () => Adjust("grievance", 10f)),
            (LM.Get("faction_affairs_grievance_down"), WideKit.Green, true, () => Adjust("grievance", -10f)));

        var middle = page.BeginColumns(ContentWidth);
        var allyCard = middle.BeginColumnCard(half, LM.Get("faction_card_court_ally"), WideKit.Cyan);
        FixedFaction ally = CourtSystem.FindFaction(empire, data.ally);
        int seats = CourtSystem.Council(empire).Count(actor => actor.GetFaction() == _faction);
        string action = string.IsNullOrEmpty(data.last_action) ? LM.Get("label_none") : LM.Get("court_action_" + data.last_action);
        if (!string.IsNullOrEmpty(data.last_action_detail)) action += " " + data.last_action_detail;
        allyCard.AddValueRow(half, 0, LM.Get("faction_affairs_ally"), ally?.Name ?? LM.Get("label_none"), WideKit.Cyan);
        allyCard.AddValueRow(half, 1, LM.Get("faction_affairs_seats"), seats.ToString(), WideKit.Value);
        allyCard.AddValueRow(half, 2, LM.Get("faction_affairs_action"), action, WideKit.Soft);
        var ledger = middle.BeginColumnCard(half, LM.Get("faction_card_court_ledger"), WideKit.Orange);
        string owes = string.Join("、", data.owed.Where(pair => pair.Value > 0).Select(pair => $"{CourtSystem.FindFaction(empire, pair.Key)?.Name ?? pair.Key} {pair.Value}"));
        string owedBy = string.Join("、", CourtSystem.Factions(empire).Where(other => other != _faction)
            .Select(other => (other, count: CourtSystem.FactionState(empire, other).owed.TryGetValue(_faction.GetID(), out int count) ? count : 0))
            .Where(item => item.count > 0).Select(item => $"{item.other.Name} {item.count}"));
        ledger.AddValueRow(half, 0, LM.Get("faction_affairs_owes"), owes.Length == 0 ? LM.Get("label_none") : owes, WideKit.Orange);
        ledger.AddValueRow(half, 1, LM.Get("faction_affairs_owed_by"), owedBy.Length == 0 ? LM.Get("label_none") : owedBy, WideKit.Green);
        ledger.AddValueRow(half, 2, LM.Get("faction_affairs_reneged"), data.reneged.ToString(), data.reneged > 0 ? WideKit.Red : WideKit.Muted);

        // 各议题立场按赞成 / 反对 / 观望分卡(独特议题与公共议题一起排)
        var issues = CourtIssues.UniqueIssues(_faction).Concat(CourtIssues.PublicIssuesFor(_faction)).Distinct()
            .Select(issue => (issue, score: CourtIssues.Stance(_faction, issue))).ToList();
        var support = issues.Where(item => CourtIssues.StanceLevel(item.score) > 0).OrderByDescending(item => item.score).ToList();
        var oppose = issues.Where(item => CourtIssues.StanceLevel(item.score) < 0).OrderBy(item => item.score).ToList();
        var neutral = issues.Where(item => CourtIssues.StanceLevel(item.score) == 0).ToList();
        var stances = page.BeginColumns(ContentWidth);
        DrawIssueColumn(stances, half, empire, "faction_card_issue_support", support, WideKit.Green);
        DrawIssueColumn(stances, half, empire, "faction_card_issue_oppose", oppose, WideKit.Red);
        if (neutral.Count == 0) return;
        var rest = page.BeginCard(ContentWidth, string.Format(LM.Get("faction_card_issue_neutral"), neutral.Count), WideKit.Muted);
        rest.AddParagraph(ContentWidth, string.Join("、", neutral.Select(item => TranslateHelper.GetTemporaryFactionClaimText(item.issue, empire))), 7, WideKit.Soft);
    }

    private static void DrawIssueColumn(AutoHoriLayoutGroup columns, float width, Empire empire, string key,
        List<(TemporaryFactionType issue, float score)> list, string hex)
    {
        var card = columns.BeginColumnCard(width, string.Format(LM.Get(key), list.Count), hex);
        if (list.Count == 0) card.AddParagraph(width, LM.Get("label_none"), 7, WideKit.Muted);
        for (int i = 0; i < list.Count; i++)
            card.AddValueRow(width, i, TranslateHelper.GetTemporaryFactionClaimText(list[i].issue, empire),
                $"{CourtSystem.StanceName(CourtIssues.StanceLevel(list[i].score))} {Signed(list[i].score)}", hex, labelWidth: (width - Pad) * 0.5f);
    }

    #endregion
}

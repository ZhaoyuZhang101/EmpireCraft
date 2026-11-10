using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 廷议前的利益交换(用户 2026-11-10 看诉求天平："开始前的利益交换呢""玩家也可以自定义""就像战争一样"；
// 问答筹码全选：政治资本、首领钱财、人情(日后回投)、结盟与让官)。照议和台(PeaceTable)：
//   · 票拟递上去后、廷议之前一个月，上奏一派私下串联(Bargain)：
//       先收人情——许过诺、欠着本派一次的派系这回投赞成还债；它对这议题强烈反对(立场 ≤ HardOpposition)就背约，欠账一笔勾销、
//       信用记一笔，本派怨望 +5；
//       再换票——廷议赞成率不到 DealTargetShare 就挨家开价：观望的换成赞成、反对的换成弃权(强烈反对的不卖)，至多 MaxAiDeals 家；
//   · 对方肯不肯 = 所得 − 改投的代价 + 亲疏(Evaluate)：所得按筹码折价(资本 1 点一分、钱 20 两一分、人情、结盟、让官)，
//     改投的代价按它对议题的立场，亲疏看两派阶层是否相近、上奏一派的信用、是否盟友；
//   · 交换台(诉求天平页)：神替上奏一派对某一家开价，加撤筹码、撮合(凑出对方肯的最省的一份)、成交(对方肯、本派付得起)、
//     强令成交(不看意愿、不付代价，记作神力)；成交了的也能撤下(资本、人情退回，结盟拆散；给出去的钱和官收不回)；
//   · 换来的表态记在奏议里(CourtMemorial.deals)，廷议照它算(DebateStances)；已廷议过的改了交换，当场重算赞成率。
public static partial class CourtSystem
{
    public const float DealTargetShare = 60f;
    public const int MaxPromises = 2;
    public const int WithdrawYears = 5;
    private const int MaxAiDeals = 3;
    private const float HardOpposition = -20f;
    public static readonly int[] CapitalTiers = { 5, 10, 20 };
    public static readonly int[] MoneyTiers = { 100, 300, 600 };

    public sealed class DealOpinion
    {
        public int gain, loss, trust;
        public readonly List<(string key, string arg, int value)> items = new();
        public int Total => gain - loss + trust;
        public bool Accepts => Total >= 0;
    }

    // 神在交换台上拟的条件(不进存档)：帝国 → 对方、筹码、要它改投成什么
    public sealed class DealDraft
    {
        public string partner = "";
        public readonly List<CourtTerm> terms = new();
        public int after = 1;
    }

    private static readonly Dictionary<long, DealDraft> Drafts = new();

    public static DealDraft Draft(Empire empire, string partner)
    {
        long id = empire?.getID() ?? -1L;
        if (!Drafts.TryGetValue(id, out DealDraft draft) || draft.partner != partner)
            Drafts[id] = draft = new DealDraft { partner = partner ?? "" };
        return draft;
    }

    public static void ClearDraft(Empire empire) => Drafts.Remove(empire?.getID() ?? -1L);

    #region 表态

    // 不算交换的表态(DebateStances 的底子)
    public static int BaseStance(Empire empire, TemporaryFaction claim, FixedFaction proposer, FixedFaction faction)
    {
        if (faction == proposer) return 1;
        CourtFactionState data = FactionState(empire, faction);
        if (IsBoycotting(data)) return -1;
        int stance = CourtIssues.StanceLevel(faction, claim);
        if (faction.GetID() == FactionState(empire, proposer).ally && stance == 0) stance = 1;
        // 学说朋党与它投靠的阶级派系互相声援(见 CliqueSystem.Patron)
        if (stance == 0 && CliqueSystem.Patronage(empire, faction, proposer)) stance = 1;
        return stance;
    }

    // 已成交换换来的表态(没有交换为 null)
    public static CourtDeal DealOf(CourtMemorial memorial, FixedFaction faction) =>
        faction == null ? null : memorial?.deals?.LastOrDefault(deal => deal.partner == faction.GetID() && deal.kind != "reneged");

    // 已廷议过的：改了交换当场重算表态与赞成率
    private static void Retally(Empire empire, CourtMemorial memorial, TemporaryFaction claim, FixedFaction proposer)
    {
        if (memorial == null || memorial.stage < 2) return;
        List<(FixedFaction faction, int stance, float weight)> stances = DebateStances(empire, claim, proposer, memorial);
        memorial.stances.Clear();
        memorial.weights.Clear();
        foreach ((FixedFaction faction, int stance, float weight) in stances)
        {
            memorial.stances[faction.GetID()] = stance;
            memorial.weights[faction.GetID()] = weight;
        }
        memorial.support = SupportShare(stances);
    }

    #endregion

    #region 估价

    // 对方肯不肯：所得 − 改投的代价 + 亲疏
    public static DealOpinion Evaluate(Empire empire, CourtMemorial memorial, TemporaryFaction claim, FixedFaction proposer,
        FixedFaction partner, IEnumerable<CourtTerm> terms, int after)
    {
        var opinion = new DealOpinion();
        if (claim == null || proposer == null || partner == null) return opinion;
        int before = BaseStance(empire, claim, proposer, partner);
        float score = CourtIssues.Stance(partner, claim);
        int loss = SwitchCost(before, after, score);
        opinion.loss = loss;
        opinion.items.Add(("deal_item_switch", $"{StanceName(before)}→{StanceName(after)}|{Mathf.RoundToInt(score)}", -loss));
        foreach (CourtTerm term in terms ?? Enumerable.Empty<CourtTerm>())
        {
            int value = TermValue(empire, proposer, partner, term);
            opinion.gain += value;
            opinion.items.Add(("deal_item_" + term.kind, TermArg(term), value));
        }
        CourtFactionState mine = FactionState(empire, proposer);
        int similarity = Mathf.RoundToInt(Similarity(proposer, partner) * 8f);
        int credit = -6 * mine.reneged;
        int allied = mine.ally == partner.GetID() ? 10 : 0;
        opinion.trust = similarity + credit + allied;
        if (similarity != 0) opinion.items.Add(("deal_item_similarity", "", similarity));
        if (credit != 0) opinion.items.Add(("deal_item_credit", mine.reneged.ToString(), credit));
        if (allied != 0) opinion.items.Add(("deal_item_allied", "", allied));
        return opinion;
    }

    // 改投的代价：观望→赞成 4 起，反对→弃权 6 起，反对→赞成 12 起，越反对越贵；强烈反对的另加 30(几乎不卖)
    public static int SwitchCost(int before, int after, float score)
    {
        if (after <= before) return 0;
        float against = Mathf.Max(0f, -score);
        float cost = before >= 0 ? 4f + against
            : after == 0 ? 6f + Mathf.Max(0f, against - 5f) * 0.8f
            : 12f + against * 1.4f;
        if (score <= HardOpposition) cost += 30f;
        return Mathf.RoundToInt(cost);
    }

    public static int TermValue(Empire empire, FixedFaction proposer, FixedFaction partner, CourtTerm term) => term.kind switch
    {
        "capital" => term.amount,
        "money" => Mathf.RoundToInt(term.amount / MoneyPerPoint),
        // 人情：日后它上奏时多一家赞成，值上奏一派廷议权重的三成，另加 8
        "promise" => 8 + Mathf.RoundToInt(Weight(empire, proposer) * 0.3f),
        "ally" => 12 + Mathf.RoundToInt(Mathf.Max(0f, Similarity(proposer, partner)) * 15f),
        "office" => 10 + 3 * (8 - OfficeTier(term.office)),
        // 撤回本派一条对方反对的诉求：对方越反对它越值钱(用户 2026-11-11 "然后玩家能选择提议开启，然后通过加入代价和条件交换来达到党派自己的目的""当然ai自己也可以")
        "withdraw" => 6 + Mathf.RoundToInt(Mathf.Max(0f, -CourtIssues.Stance(partner, (TemporaryFactionType)term.amount))),
        _ => 0
    };

    private static float Weight(Empire empire, FixedFaction faction) =>
        Weights(empire, new List<FixedFaction> { faction }).TryGetValue(faction, out float weight) ? weight : 0f;

    private static int OfficeTier(long officeId)
    {
        Actor holder = OfficeManager.Offices.TryGetValue(officeId, out OfficeObject office) ? office.GetActor() : null;
        OfficialRank rank = holder == null ? default : OfficialRankSystem.Of(holder);
        return holder != null && rank.Shown ? Mathf.Clamp(rank.Tier, 0, 8) : 6;
    }

    private static string TermArg(CourtTerm term) => term.kind switch
    {
        "office" => OfficeManager.Offices.TryGetValue(term.office, out OfficeObject office) ? office.GetName() : "",
        "withdraw" => EmpireCraft.Scripts.HelperFunc.TranslateHelper.GetTemporaryFactionClaimText((TemporaryFactionType)term.amount),
        _ => term.amount.ToString()
    };

    public static string StanceName(int stance) =>
        LM.Get(stance > 0 ? "court_stance_support" : stance < 0 ? "court_stance_oppose" : "court_stance_neutral");

    #endregion

    #region 付得起与付账

    // 上奏一派付不付得起这一项；付不起返回原因键
    public static string CannotPay(Empire empire, FixedFaction proposer, FixedFaction partner, CourtTerm term, IEnumerable<CourtTerm> others = null)
    {
        CourtFactionState mine = FactionState(empire, proposer);
        List<CourtTerm> rest = (others ?? Enumerable.Empty<CourtTerm>()).Where(other => other != term).ToList();
        switch (term.kind)
        {
            case "capital":
                return term.amount + rest.Where(other => other.kind == "capital").Sum(other => other.amount) > mine.capital ? "deal_no_capital" : null;
            case "money":
                Actor leader = proposer.GetLeader();
                return leader?.data == null || leader.isRekt() ||
                       term.amount + rest.Where(other => other.kind == "money").Sum(other => other.amount) > leader.money ? "deal_no_money" : null;
            case "promise":
                return mine.owed.Values.Sum() + 1 + rest.Count(other => other.kind == "promise") > MaxPromises ? "deal_too_many_promises" : null;
            case "ally":
                return !string.IsNullOrEmpty(mine.ally) || !string.IsNullOrEmpty(FactionState(empire, partner).ally) ? "deal_ally_taken"
                    : proposer.CentralRatio > 55 || partner.CentralRatio > 55 ? "deal_ally_too_big" : null;
            case "office":
                return OfficeManager.Offices.TryGetValue(term.office, out OfficeObject office) && office.GetActor()?.GetFaction() == proposer &&
                       Successor(partner, office) != null ? null : "deal_no_office";
            case "withdraw":
                TemporaryFaction own = WithdrawableClaim(empire, proposer, (TemporaryFactionType)term.amount);
                return own == null ? "deal_no_withdraw" : null;
        }
        return "deal_unknown";
    }

    // 对方顶这个官的人：它的成员里够得上这个官、名望最高的
    private static Actor Successor(FixedFaction partner, OfficeObject office) =>
        partner.AllMembers.Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && office.GetActor() != actor && office.Accepts(actor))
            .OrderByDescending(actor => actor.renown).FirstOrDefault();

    // 上奏一派能拿出来的筹码(交换台的"加筹码")：本派在中枢的官、各档资本与钱、人情、结盟
    public static List<(CourtTerm term, string reason)> Options(Empire empire, FixedFaction proposer, FixedFaction partner, IEnumerable<CourtTerm> onTable)
    {
        var result = new List<(CourtTerm term, string reason)>();
        List<CourtTerm> table = onTable?.ToList() ?? new List<CourtTerm>();
        void Add(CourtTerm term) => result.Add((term, CannotPay(empire, proposer, partner, term, table)));
        foreach (int amount in CapitalTiers) Add(new CourtTerm { kind = "capital", amount = amount });
        foreach (int amount in MoneyTiers) Add(new CourtTerm { kind = "money", amount = amount });
        Add(new CourtTerm { kind = "promise", amount = 1 });
        Add(new CourtTerm { kind = "ally", amount = 1 });
        // 撤回本派的诉求(条件交换)：对方反对的、没在施行、不是眼下这件奏议的
        foreach (TemporaryFaction own in proposer.TemporaryFactions ?? new List<TemporaryFaction>())
            if (own != null && WithdrawableClaim(empire, proposer, own.type) != null && CourtIssues.StanceLevel(partner, own) < 0)
                Add(new CourtTerm { kind = "withdraw", amount = (int)own.type });
        foreach (long id in CentralOffices(empire))
        {
            if (!OfficeManager.Offices.TryGetValue(id, out OfficeObject office) || office.GetActor()?.GetFaction() != proposer) continue;
            Add(new CourtTerm { kind = "office", office = id });
        }
        return result;
    }

    private static TemporaryFaction WithdrawableClaim(Empire empire, FixedFaction proposer, TemporaryFactionType type)
    {
        TemporaryFaction own = proposer?.TemporaryFactions?.FirstOrDefault(claim => claim != null && claim.type == type);
        CourtMemorial memorial = State(empire)?.memorial;
        bool current = memorial != null && memorial.proposer == proposer?.GetID() && memorial.claim == type.ToString();
        return own == null || own.IsStarted() || current ? null : own;
    }

    private static IEnumerable<long> CentralOffices(Empire empire) =>
        (empire.data.centerOffice?.CoreOffices ?? new List<long>()).Concat(empire.data.centerOffice?.Divisions ?? new List<long>()).Distinct();

    private static void Pay(Empire empire, FixedFaction proposer, FixedFaction partner, IEnumerable<CourtTerm> terms)
    {
        CourtFactionState mine = FactionState(empire, proposer), theirs = FactionState(empire, partner);
        foreach (CourtTerm term in terms)
        {
            switch (term.kind)
            {
                case "capital":
                    mine.capital -= term.amount;
                    theirs.capital = Mathf.Min(100f, theirs.capital + term.amount);
                    break;
                case "money":
                    proposer.GetLeader()?.addMoney(-term.amount);
                    partner.GetLeader()?.addMoney(term.amount);
                    break;
                case "promise":
                    mine.owed[partner.GetID()] = (mine.owed.TryGetValue(partner.GetID(), out int count) ? count : 0) + 1;
                    break;
                case "ally":
                    mine.ally = partner.GetID();
                    theirs.ally = proposer.GetID();
                    break;
                case "withdraw":
                    // 本派五年内不再提这一条
                    TemporaryFaction own = WithdrawableClaim(empire, proposer, (TemporaryFactionType)term.amount);
                    if (own != null) own.CountDown = Math.Max(own.CountDown, WithdrawYears);
                    break;
                case "office":
                    if (!OfficeManager.Offices.TryGetValue(term.office, out OfficeObject office)) break;
                    Actor successor = Successor(partner, office);
                    if (successor == null) break;
                    office.RemoveActor();
                    office.SetActor(successor);
                    break;
            }
        }
    }

    #endregion

    #region 串联(AI)

    // 票拟过后、廷议前：上奏一派收人情、换票(见文件头)
    private static void Bargain(Empire empire, CourtState state, CourtMemorial memorial, TemporaryFaction claim, FixedFaction proposer)
    {
        memorial.deals ??= new List<CourtDeal>();
        CourtFactionState mine = FactionState(empire, proposer);
        string issue = IssueName(claim.type, empire);
        foreach (FixedFaction other in Factions(empire))
        {
            if (other == proposer) continue;
            CourtFactionState theirs = FactionState(empire, other);
            if (!theirs.owed.TryGetValue(proposer.GetID(), out int count) || count <= 0 || IsBoycotting(theirs)) continue;
            int before = BaseStance(empire, claim, proposer, other);
            if (before > 0) continue;
            if (--count <= 0) theirs.owed.Remove(proposer.GetID());
            else theirs.owed[proposer.GetID()] = count;
            if (CourtIssues.Stance(other, claim) <= HardOpposition)
            {
                theirs.reneged++;
                mine.grievance = Mathf.Min(100f, mine.grievance + 5f);
                memorial.deals.Add(new CourtDeal { partner = other.GetID(), partner_name = other.Name, before = before, after = before, kind = "reneged" });
                Log(state, "court_log_deal_reneged", other.Name, proposer.Name, issue);
                continue;
            }
            memorial.deals.Add(new CourtDeal { partner = other.GetID(), partner_name = other.Name, before = before, after = 1, kind = "repay" });
            Log(state, "court_log_deal_repay", other.Name, proposer.Name, issue);
        }
        int made = 0;
        while (made < MaxAiDeals)
        {
            List<(FixedFaction faction, int stance, float weight)> stances = DebateStances(empire, claim, proposer, memorial);
            if (SupportShare(stances) >= DealTargetShare) break;
            bool dealt = false;
            foreach ((FixedFaction faction, int stance, float weight) in stances.Where(item => item.faction != proposer && item.stance <= 0)
                         .OrderByDescending(item => item.weight))
            {
                if (DealOf(memorial, faction) != null || IsBoycotting(FactionState(empire, faction)) ||
                    CourtIssues.Stance(faction, claim) <= HardOpposition) continue;
                int after = stance == 0 ? 1 : 0;
                List<CourtTerm> terms = Cheapest(empire, memorial, claim, proposer, faction, after, ai: true);
                if (terms == null) continue;
                Pay(empire, proposer, faction, terms);
                memorial.deals.Add(new CourtDeal { partner = faction.GetID(), partner_name = faction.Name, before = stance, after = after, terms = terms });
                Log(state, "court_log_deal_made", proposer.Name, faction.Name, issue, StanceName(after));
                dealt = true;
                made++;
                break;
            }
            if (!dealt) break;
        }
    }

    // 凑出对方肯、上奏一派付得起的最省一份：依次试资本(各档)、钱(各档)、人情、结盟、让官，不够再叠一项；凑不出返回 null。
    // ai 时资本至少留下上奏的本钱
    public static List<CourtTerm> Cheapest(Empire empire, CourtMemorial memorial, TemporaryFaction claim, FixedFaction proposer,
        FixedFaction partner, int after, bool ai = false)
    {
        var terms = new List<CourtTerm>();
        CourtFactionState mine = FactionState(empire, proposer);
        for (int round = 0; round < 3; round++)
        {
            DealOpinion opinion = Evaluate(empire, memorial, claim, proposer, partner, terms, after);
            if (opinion.Accepts) return terms;
            int gap = -opinion.Total;
            CourtTerm pick = null;
            foreach ((CourtTerm term, string reason) in Options(empire, proposer, partner, terms))
            {
                if (reason != null || terms.Any(item => item.kind == term.kind)) continue;
                if (ai && term.kind == "capital" && mine.capital - term.amount < ProposalCost) continue;
                int value = TermValue(empire, proposer, partner, term);
                // 一项够了就挑够的里最省的；都不够就挑最值钱的先放上
                if (pick == null) { pick = term; continue; }
                int pickValue = TermValue(empire, proposer, partner, pick);
                bool enough = value >= gap, pickEnough = pickValue >= gap;
                if (enough && (!pickEnough || value < pickValue) || !enough && !pickEnough && value > pickValue) pick = term;
            }
            if (pick == null) return null;
            terms.Add(pick);
        }
        return Evaluate(empire, memorial, claim, proposer, partner, terms, after).Accepts ? terms : null;
    }

    #endregion

    #region 交换台(神力)

    // 交换台能用：有奏议、还没批红准奏(留中的可以再谈)
    public static bool CanBargain(Empire empire, out CourtMemorial memorial, out TemporaryFaction claim, out FixedFaction proposer)
    {
        memorial = State(empire)?.memorial;
        claim = memorial == null ? null : FindClaim(empire, memorial);
        proposer = memorial == null ? null : FindFaction(empire, memorial.proposer);
        return memorial != null && claim != null && proposer != null && memorial.stage <= 3 && memorial.decision != "approve";
    }

    public static void Toggle(Empire empire, string partner, CourtTerm term)
    {
        DealDraft draft = Draft(empire, partner);
        int index = draft.terms.FindIndex(item => item.Key == term.Key);
        if (index >= 0) draft.terms.RemoveAt(index);
        else
        {
            // 资本、钱同一样只放一档(换档是替换)
            if (term.kind is "capital" or "money") draft.terms.RemoveAll(item => item.kind == term.kind);
            draft.terms.Add(term);
        }
    }

    public static bool Mediate(Empire empire, string partnerId)
    {
        if (!CanBargain(empire, out CourtMemorial memorial, out TemporaryFaction claim, out FixedFaction proposer)) return false;
        FixedFaction partner = FindFaction(empire, partnerId);
        if (partner == null) return false;
        DealDraft draft = Draft(empire, partnerId);
        List<CourtTerm> terms = Cheapest(empire, memorial, claim, proposer, partner, draft.after);
        if (terms == null) return false;
        draft.terms.Clear();
        draft.terms.AddRange(terms);
        return true;
    }

    // 成交：对方肯、付得起才成；forced 强令成交，不看意愿、不付代价，记作神力
    public static bool Sign(Empire empire, string partnerId, bool forced)
    {
        if (!CanBargain(empire, out CourtMemorial memorial, out TemporaryFaction claim, out FixedFaction proposer)) return false;
        FixedFaction partner = FindFaction(empire, partnerId);
        if (partner == null || partner == proposer) return false;
        DealDraft draft = Draft(empire, partnerId);
        int before = BaseStance(empire, claim, proposer, partner);
        if (draft.after <= before) return false;
        if (!forced)
        {
            if (!Evaluate(empire, memorial, claim, proposer, partner, draft.terms, draft.after).Accepts) return false;
            if (draft.terms.Any(term => CannotPay(empire, proposer, partner, term, draft.terms) != null)) return false;
            Pay(empire, proposer, partner, draft.terms);
        }
        memorial.deals ??= new List<CourtDeal>();
        memorial.deals.RemoveAll(deal => deal.partner == partnerId && deal.kind != "reneged");
        memorial.deals.Add(new CourtDeal
        {
            partner = partnerId, partner_name = partner.Name, before = before, after = draft.after,
            terms = forced ? new List<CourtTerm>() : draft.terms.ToList(), kind = forced ? "divine" : "deal"
        });
        Log(State(empire), forced ? "court_log_deal_divine" : "court_log_deal_made", proposer.Name, partner.Name,
            IssueName(claim.type, empire), StanceName(draft.after));
        Retally(empire, memorial, claim, proposer);
        ClearDraft(empire);
        return true;
    }

    // 撤下一笔成交的交换：资本、人情退回，结盟拆散；给出去的钱和官收不回
    public static bool Cancel(Empire empire, string partnerId)
    {
        if (!CanBargain(empire, out CourtMemorial memorial, out TemporaryFaction claim, out FixedFaction proposer)) return false;
        CourtDeal deal = memorial.deals?.LastOrDefault(item => item.partner == partnerId && item.kind != "reneged");
        if (deal == null) return false;
        FixedFaction partner = FindFaction(empire, partnerId);
        CourtFactionState mine = FactionState(empire, proposer);
        CourtFactionState theirs = partner == null ? null : FactionState(empire, partner);
        foreach (CourtTerm term in deal.terms)
        {
            switch (term.kind)
            {
                case "capital":
                    mine.capital = Mathf.Min(100f, mine.capital + term.amount);
                    if (theirs != null) theirs.capital = Mathf.Max(0f, theirs.capital - term.amount);
                    break;
                case "promise":
                    if (mine.owed.TryGetValue(partnerId, out int count))
                    {
                        if (count <= 1) mine.owed.Remove(partnerId);
                        else mine.owed[partnerId] = count - 1;
                    }
                    break;
                case "ally":
                    if (mine.ally == partnerId) mine.ally = "";
                    if (theirs != null && theirs.ally == proposer.GetID()) theirs.ally = "";
                    break;
            }
        }
        // 还人情的撤了：人情还记着
        if (deal.kind == "repay" && theirs != null)
            theirs.owed[proposer.GetID()] = (theirs.owed.TryGetValue(proposer.GetID(), out int owed) ? owed : 0) + 1;
        memorial.deals.Remove(deal);
        Log(State(empire), "court_log_deal_cancelled", proposer.Name, deal.partner_name, IssueName(claim.type, empire));
        Retally(empire, memorial, claim, proposer);
        return true;
    }

    // 人情账：谁欠谁几回(诉求天平页)
    public static List<(string debtor, string creditor, int count)> Ledger(Empire empire)
    {
        var result = new List<(string debtor, string creditor, int count)>();
        foreach (FixedFaction faction in Factions(empire))
            foreach (KeyValuePair<string, int> pair in FactionState(empire, faction).owed.Where(pair => pair.Value > 0))
                result.Add((faction.Name, FindFaction(empire, pair.Key)?.Name ?? pair.Key, pair.Value));
        return result;
    }

    #endregion
}

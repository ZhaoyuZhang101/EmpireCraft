using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 财阀与政治世家(用户 2026-11-24 第四批"宗族世家接成财阀与政治世家")。宗族、世家到了现代原本无事可做，现在接上两条出路：
// 一、财阀——工业时代起，本国工业够了规模(各城厂区按档次合计六档起)，每年有机会由一家宗族起家办成财阀：
//   · 先从郡望在本国的在册世家里按门第挑(累世之资转办实业)，没有就从在朝的人物(政府首脑、议员、各城城主)的宗族里挑，
//     皇室、已经是财阀的不算；工业每十二档容一家，至多四家；
//   · 份额：起家占本国工业百分之八，逐年坐大(繁荣、金融霸权、私有化更快，萧条时大鱼吃小鱼)，一家至多三成五；
//     各家份额之和是垄断程度：工厂产值多一点(规模)，劳工、市民、中小商人怨气跟着涨，景气也涨得更快；
//   · 献金：每家每年按份额给一个政党出钱(见 PartyFinanceSystem.Income)——台上的不是左翼就押台上的，否则押最亲商的那个党；
//   · 收场：诉求"反托拉斯"拆掉最大的一家；国有化一并收归国有；宗族绝了、工业垮了也就散了。
//   · 本国有公司的(见 CompanySystem)：财阀就是控股旗下各家公司的那个宗族——电气时代起，规模最大的私企的东家坐大成财阀，
//     份额即旗下公司占本国工业几成，逐年跨行业收购；工业时代还是自由竞争，起不了财阀；
//   · 干政(用户 2026-11-25 "大企业集团可以影响政治，比如三星这种")：献金之外，游说——垄断越重，"反托拉斯""国有化"
//     "劳动立法""承认工会""提高福利""提高赋税"越提不上议程，"私有化""削减福利""取缔工会""降低赋税"越急，
//     政府首脑出自财阀的宗族时再重五成；政商丑闻——扶持的党在台上时每年有机会案发，执政表现大跌；
//     大而不倒——市面崩盘那年，国库拿得出钱就得出手救最大的财阀(劳工、市民不满)，拿不出它就垮台、旗下公司各自独立。
// 二、政治世家——共和国、议会制下，同一宗族出过三位政府首脑(同一人再任不重记)即称政治世家：党魁出自政治世家的党多得百分之八的票。
// 数值都是模组自己的规则。
public static class ConglomerateSystem
{
    private const int MinIndustry = 6, IndustryPerHouse = 12, MaxHouses = 4;
    private const float FoundChance = 0.25f, StartShare = 0.08f, Growth = 0.02f, MaxShare = 0.35f, MaxMonopoly = 0.8f;
    private const float DonationScale = 300f, HouseVote = 1.08f, OutputPerMonopoly = 0.15f;
    public const int HouseTerms = 3;
    private const int ScandalYears = 8;
    private const float ScandalChance = 0.15f, BailoutShare = 0.15f;
    private const int AntitrustCooldownYears = 15;

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;
    private static ConstitutionalEconomyState State(Kingdom kingdom) => kingdom?.GetEmpire()?.data?.constitutional_economy;

    // 垄断程度(0~0.8)：各家财阀份额之和
    public static float Monopoly(Empire empire) => Monopoly(State(empire));

    private static float Monopoly(ConstitutionalEconomyState state)
    {
        if (state?.conglomerates == null || state.conglomerates.Count == 0) return 0f;
        float total = 0f;
        foreach (ConglomerateData house in state.conglomerates) total += house.share;
        return Mathf.Min(MaxMonopoly, total);
    }

    public static IReadOnlyList<ConglomerateData> Of(Empire empire) =>
        State(empire)?.conglomerates ?? (IReadOnlyList<ConglomerateData>)Array.Empty<ConglomerateData>();

    private static SpecificClan ClanOf(long id) =>
        id < 0 ? null : SpecificClanManager._specificClans.FirstOrDefault(clan => clan != null && clan.id == id);

    public static bool Applies(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.isRekt() || empire.IsArchived() || State(empire) == null) return false;
        if (TransmigrantSystem.Governs(empire) || State(empire).nationalization >= ModernPolicySystem.Nationalized) return false;
        return TechnologySystem.GetEraTier(InstitutionSystem.GetPrimaryCulture(empire)) >= EconomicCycleSystem.IndustrialEra;
    }

    // 本国工业的规模：各城厂区里的厂按档次合计
    public static int Industry(Empire empire)
    {
        int total = 0;
        foreach (City city in ClaimPolicySystem.Cities(empire)) total += FactorySystem.Units(city);
        return total;
    }

    #region 年度

    // 每年(ConstitutionalEconomySystem)
    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire?.CoreKingdom == null || state == null) return;
        state.conglomerates ??= new List<ConglomerateData>();
        TrackHouse(empire, state);
        if (!Applies(empire))
        {
            if (state.conglomerates.Count > 0 && state.nationalization < ModernPolicySystem.Nationalized) state.conglomerates.Clear();
            return;
        }
        int industry = Industry(empire);
        // 宗族绝了的散掉；工业垮了的从最小的散起
        state.conglomerates.RemoveAll(house => ClanOf(house.clan_id) is not { Count: > 0 });
        int room = industry < MinIndustry ? 0 : Mathf.Clamp(industry / IndustryPerHouse, 1, MaxHouses);
        while (state.conglomerates.Count > room)
            state.conglomerates.Remove(state.conglomerates.OrderBy(house => house.share).First());

        float pace = Growth;
        int phase = state.cycle_phase;
        if (phase is EconomicCycleSystem.Boom or EconomicCycleSystem.Overheat) pace *= 1.5f;
        else if (phase is EconomicCycleSystem.Panic or EconomicCycleSystem.Depression) pace *= 1.5f;
        if (state.finance_policy > 0) pace *= 1.5f;
        if (state.nationalization <= ModernPolicySystem.Privatized) pace *= 1.3f;
        foreach (ConglomerateData house in state.conglomerates.ToList())
        {
            // 本国有公司的：份额就是旗下公司占本国工业几成，一家不剩的散掉；没有公司的照旧逐年坐大
            float held = CompanySystem.GroupShare(empire, house.clan_id);
            if (held == 0f)
            {
                state.conglomerates.Remove(house);
                continue;
            }
            house.share = held > 0f ? held : Mathf.Min(MaxShare, house.share + pace);
            house.party_id = Patron(empire)?.GetID() ?? "";
        }
        Scandal(empire, state);
        // 合计不过八成：超了按比例压回去
        float total = state.conglomerates.Sum(house => house.share);
        if (total > MaxMonopoly)
            foreach (ConglomerateData house in state.conglomerates) house.share *= MaxMonopoly / total;

        if (state.conglomerates.Count >= room) return;
        if (state.antitrust_at >= 0d && Date.getYearsSince(state.antitrust_at) < AntitrustCooldownYears / 3) return;
        if (UnityEngine.Random.value >= FoundChance) return;
        Found(empire, state);
    }

    private static void Found(Empire empire, ConstitutionalEconomyState state)
    {
        var taken = new HashSet<long>(state.conglomerates.Select(house => house.clan_id));
        long royal = empire.Emperor != null && !RepublicSystem.IsRepublic(empire) ? empire.Emperor.GetSpecificClan()?.id ?? -1L : -1L;
        bool Fit(SpecificClan clan) => clan != null && clan.Count > 0 && clan.id != royal && !taken.Contains(clan.id);

        // 本国有公司的照资本主义的路子走(见 CompanySystem)：工业时代还是自由竞争，起不了财阀；电气时代起，
        // 规模最大的那家私企的东家坐大成财阀；没有这样的私企，世家(再不然是在朝的宗族)出资买下最大的一家
        bool companies = CompanySystem.GroupShare(empire, long.MinValue) >= 0f;
        if (companies && TechnologySystem.GetEraTier(InstitutionSystem.GetPrimaryCulture(empire)) < CompanySystem.TrustEra) return;
        CompanyData flagship = companies ? CompanySystem.FlagshipCandidate(empire, taken, royal) : null;
        SpecificClan founder = flagship == null ? null : ClanOf(flagship.clan_id);
        bool gentry = founder == null;
        SpecificClan pick = founder ?? GentrySystem.Of(empire).FirstOrDefault(Fit);
        if (pick == null)
        {
            gentry = false;
            var candidates = new List<SpecificClan>();
            void Add(Actor actor)
            {
                SpecificClan clan = actor != null && !actor.isRekt() ? actor.GetSpecificClan() : null;
                if (Fit(clan) && !candidates.Contains(clan)) candidates.Add(clan);
            }
            Add(ParliamentSystem.GetHeadOfGovernment(empire));
            foreach (Actor member in ParliamentSystem.GetRepresentatives(empire) ?? new List<Actor>()) Add(member);
            foreach (City city in ClaimPolicySystem.Cities(empire)) Add(city.leader);
            pick = candidates.OrderByDescending(clan => clan.Count).FirstOrDefault();
        }
        if (pick == null) return;
        if (founder == null) flagship = companies ? CompanySystem.LargestIndependent(empire) : null;
        if (companies && flagship == null) return;
        City seat = (founder != null ? CompanySystem.Home(flagship) : null) ?? GentrySystem.Seat(pick) ?? ClaimPolicySystem.Cities(empire)
            .OrderByDescending(FactorySystem.Units).FirstOrDefault();
        string surname = !string.IsNullOrEmpty(pick.shi) ? pick.shi.TrimEnd('氏') : pick.name ?? "";
        var house = new ConglomerateData
        {
            clan_id = pick.id,
            // 单字的姓带个"氏"字(鲁氏财阀)，复姓、外文姓直接接(田中财阀)
            name = string.Format(LM.Get(surname.Length == 1 ? "conglomerate_name_single" : "conglomerate_name"), surname),
            since = World.world.getCurWorldTime(),
            city_id = seat?.id ?? -1L,
            share = StartShare,
            party_id = Patron(empire)?.GetID() ?? ""
        };
        state.conglomerates.Add(house);
        if (flagship != null) CompanySystem.JoinGroup(flagship, pick.id, house.name);
        EventRecorder.RecordRoutine(empire, founder != null
            ? string.Format(LM.Get("conglomerate_founded_company_history"), flagship.name, surname, house.name)
            : string.Format(LM.Get(gentry ? "conglomerate_founded_gentry_history" : "conglomerate_founded_history"),
                gentry ? GentrySystem.JunWang(pick) : surname, seat?.GetCityName() ?? empire.GetEmpireFullName(), house.name));
    }

    // 财阀押的党：台上的不是左翼就押台上的，否则押最亲商的那个合法政党
    private static readonly PartyIdeology[] BusinessFriendly =
    {
        PartyIdeology.Capitalism, PartyIdeology.ConservativeLiberalism, PartyIdeology.Libertarianism, PartyIdeology.Conservatism,
        PartyIdeology.Authoritarianism, PartyIdeology.Fascism, PartyIdeology.Centrism, PartyIdeology.ReligiousDemocracy,
        PartyIdeology.SocialLiberalism
    };

    private static FixedFaction Patron(Empire empire)
    {
        if (!PartySystem.IsActive(empire)) return null;
        FixedFaction governing = PartySystem.GetGovernmentParty(empire);
        if (governing != null && !governing.Ban && !IdeologyFamilies.IsLeft(governing.Ideology)) return governing;
        List<FixedFaction> parties = PartySystem.GetParties(empire).Where(party => party != null && party.IsParty && !party.Ban).ToList();
        foreach (PartyIdeology ideology in BusinessFriendly)
            if (parties.FirstOrDefault(party => party.Ideology == ideology) is { } friendly) return friendly;
        return null;
    }

    // 这个党一年从财阀那里得的献金(见 PartyFinanceSystem.Income)
    public static float Donations(Empire empire, FixedFaction party)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state?.conglomerates == null || state.conglomerates.Count == 0 || party == null) return 0f;
        string id = party.GetID();
        float total = 0f;
        foreach (ConglomerateData house in state.conglomerates)
            if (house.party_id == id) total += house.share * DonationScale;
        return total;
    }

    #endregion

    #region 拆分与国有化

    public static ConglomerateData Largest(Empire empire) =>
        State(empire)?.conglomerates?.OrderByDescending(house => house.share).FirstOrDefault();

    public static bool CanBreak(Empire empire) => Largest(empire) != null &&
        (State(empire).antitrust_at < 0d || Date.getYearsSince(State(empire).antitrust_at) >= AntitrustCooldownYears);

    // 反托拉斯：拆掉最大的一家，返回它的字号(没有可拆的返回 null)
    public static string Break(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        ConglomerateData house = Largest(empire);
        if (state == null || house == null) return null;
        state.conglomerates.Remove(house);
        // 旗下公司各自独立
        CompanySystem.ReleaseGroup(empire, house.clan_id, house.name);
        state.antitrust_at = World.world.getCurWorldTime();
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, -6f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Citizen, -6f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, -3f);
        return house.name;
    }

    // 国有化：各家财阀收归国有
    internal static void Nationalize(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state?.conglomerates == null || state.conglomerates.Count == 0) return;
        string names = string.Join("、", state.conglomerates.Select(house => house.name));
        foreach (ConglomerateData house in state.conglomerates) CompanySystem.ReleaseGroup(empire, house.clan_id, house.name);
        state.conglomerates.Clear();
        EventRecorder.RecordRoutine(empire, string.Format(LM.Get("conglomerate_nationalized_history"), empire.GetEmpireFullName(), names));
    }

    #endregion

    #region 干政

    // 政府首脑是不是出自财阀的宗族(一年看一回)
    private static readonly Dictionary<long, (int year, bool value)> OfficeCache = new();

    public static bool InOffice(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state?.conglomerates == null || state.conglomerates.Count == 0) return false;
        int year = Date.getCurrentYear();
        if (OfficeCache.TryGetValue(empire.id, out var cached) && cached.year == year) return cached.value;
        long clan = ParliamentSystem.GetHeadOfGovernment(empire)?.GetSpecificClan()?.id ?? -1L;
        bool value = clan >= 0 && state.conglomerates.Any(house => house.clan_id == clan);
        OfficeCache[empire.id] = (year, value);
        return value;
    }

    // 游说(见 ClaimAgendaSystem.GetUrgency)：垄断越重，冲着财阀来的议题越提不上议程，合财阀意的越急
    public static float Lobby(Empire empire, TemporaryFactionType type, float urgency)
    {
        if (urgency <= 0f) return urgency;
        float weight = Monopoly(empire);
        if (weight <= 0f) return urgency;
        if (InOffice(empire)) weight *= 1.5f;
        return type switch
        {
            TemporaryFactionType.反托拉斯 or TemporaryFactionType.国有化 or TemporaryFactionType.劳动立法 or TemporaryFactionType.承认工会 or
                TemporaryFactionType.提高福利 or TemporaryFactionType.提高赋税 => urgency * (1f - Mathf.Min(0.6f, weight * 1.2f)),
            TemporaryFactionType.私有化 or TemporaryFactionType.削减福利 or TemporaryFactionType.取缔工会 or TemporaryFactionType.降低赋税 or
                TemporaryFactionType.拓展金融霸权 => urgency * (1f + weight),
            _ => urgency
        };
    }

    // 政商丑闻：财阀扶持的党在台上，每年有机会案发(八年至多一回)
    private static void Scandal(Empire empire, ConstitutionalEconomyState state)
    {
        float weight = Monopoly(state);
        if (weight < 0.25f || state.scandal_at >= 0d && Date.getYearsSince(state.scandal_at) < ScandalYears) return;
        FixedFaction governing = PartySystem.GetGovernmentParty(empire);
        ConglomerateData house = governing == null ? null
            : state.conglomerates.Where(item => item.party_id == governing.GetID()).OrderByDescending(item => item.share).FirstOrDefault();
        if (house == null || UnityEngine.Random.value >= weight * ScandalChance) return;
        state.scandal_at = World.world.getCurWorldTime();
        PartyFinanceSystem.Adjust(empire, "approval_scandal", -8f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Citizen, 5f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, 3f);
        EventRecorder.Record(empire, string.Format(LM.Get("conglomerate_scandal_history"), house.name, governing.Name, empire.GetEmpireFullName()));
    }

    // 大而不倒(市面崩盘那年，见 EconomicCycleSystem.Crash)：国库拿得出钱就出手救最大的财阀，拿不出它就垮台
    internal static void OnCrash(Empire empire, ConstitutionalEconomyState state)
    {
        ConglomerateData house = state?.conglomerates?.OrderByDescending(item => item.share).FirstOrDefault();
        if (house == null || house.share < BailoutShare || empire?.CoreKingdom == null) return;
        int cost = EconomicCycleSystem.StimulusCost(empire);
        if (empire.CoreKingdom.GetMoney() >= cost)
        {
            empire.CoreKingdom.SubMoney(cost, TreasuryCategory.Policy);
            state.bailout_at = World.world.getCurWorldTime();
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, 4f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Citizen, 4f);
            EventRecorder.RecordRoutine(empire, string.Format(LM.Get("conglomerate_bailout_history"), empire.GetEmpireFullName(), house.name));
            return;
        }
        state.conglomerates.Remove(house);
        CompanySystem.ReleaseGroup(empire, house.clan_id, house.name);
        EventRecorder.Record(empire, string.Format(LM.Get("conglomerate_collapsed_history"), house.name, empire.GetEmpireFullName()));
    }

    #endregion

    #region 效果

    // 工厂产值(见 FactorySystem.Produce)：规模
    public static float OutputFactor(City city) => 1f + OutputPerMonopoly * Monopoly(State(city?.kingdom));

    // 阶层怨气的目标值加减(见 InstitutionSystem)：垄断抬价、压工钱、挤垮小商号
    public static float GrievanceShift(Empire empire, SocialClass socialClass)
    {
        float monopoly = Monopoly(empire);
        if (monopoly <= 0f) return 0f;
        return socialClass switch
        {
            SocialClass.Labour => 15f * monopoly,
            SocialClass.Citizen => 10f * monopoly,
            SocialClass.Merchant => 8f * monopoly,
            _ => 0f
        };
    }

    #endregion

    #region 政治世家

    // 记一笔：共和国、议会制下出了一位新的政府首脑，他的宗族多一位；满三位称政治世家
    private static void TrackHouse(Empire empire, ConstitutionalEconomyState state)
    {
        state.house_terms ??= new Dictionary<long, int>();
        state.political_houses ??= new List<long>();
        if (!ParliamentSystem.HasParliament(empire) && !RepublicSystem.IsRepublic(empire)) return;
        Actor head = ParliamentSystem.HasParliament(empire) ? ParliamentSystem.GetPrimeMinister(empire) : empire.Emperor;
        state.house_counted ??= new List<long>();
        if (head == null || head.isRekt() || state.house_counted.Contains(head.getID())) return;
        state.house_counted.Add(head.getID());
        if (state.house_counted.Count > 40) state.house_counted.RemoveAt(0);
        SpecificClan clan = head.GetSpecificClan();
        if (clan == null) return;
        state.house_terms.TryGetValue(clan.id, out int terms);
        state.house_terms[clan.id] = ++terms;
        if (terms < HouseTerms || state.political_houses.Contains(clan.id)) return;
        state.political_houses.Add(clan.id);
        string surname = !string.IsNullOrEmpty(clan.shi) ? clan.shi.TrimEnd('氏') : clan.name ?? "";
        EventRecorder.RecordRoutine(empire, string.Format(LM.Get("political_house_history"), surname, terms, empire.GetEmpireFullName()), head);
    }

    public static bool IsPoliticalHouse(Empire empire, Actor actor) =>
        actor?.GetSpecificClan() is { } clan && State(empire)?.political_houses?.Contains(clan.id) == true;

    // 党魁出自政治世家的党多得一点票(见 PartyFinanceSystem.VoteFactor)
    public static float VoteFactor(Empire empire, FixedFaction party) =>
        State(empire)?.political_houses?.Count > 0 && IsPoliticalHouse(empire, party?.GetLeader()) ? HouseVote : 1f;

    // 界面用：各政治世家的姓氏与出过几位首脑
    public static List<(string name, int terms)> Houses(Empire empire)
    {
        var list = new List<(string, int)>();
        ConstitutionalEconomyState state = State(empire);
        if (state?.political_houses == null) return list;
        foreach (long id in state.political_houses)
        {
            SpecificClan clan = ClanOf(id);
            if (clan == null) continue;
            string surname = !string.IsNullOrEmpty(clan.shi) ? clan.shi.TrimEnd('氏') : clan.name ?? "";
            list.Add((surname, state.house_terms != null && state.house_terms.TryGetValue(id, out int terms) ? terms : HouseTerms));
        }
        return list;
    }

    #endregion
}

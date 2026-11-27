using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 特许公司(用户 2026-11-28 "那像这种这种东印度公司的怎么处理"，问答定"两边打通")：殖民地政权里的特许公司(见 ColonySystem)
// 同时算宗主国的一家公司——
//   · 公司名录、财富榜、股市里都有它，标「特许」；宗主国有交易所就挂牌(特许公司从来就是股份制，不看企业形态)；
//   · 股份：国库占几成，头两家大股东(宗主国最大的财阀，或都城的宗族)各占几成，余下是散户；分红照股份分——国库那份进宗主国库，
//     别的进宗主国都城的民间(见 PayCharterDividend)；
//   · 特许状(新加的)：几年一续，到期时公司还撑得住就多半续(交一笔特许费)，否则撤销特许、改设总督府；
//     界面里可以续特许、撤特许、照市值买下散户与大股东的股份收归国有；照旧做到几座城或亏空成债就被收归国有(见 ColonySystem)。
// 数值在 CompanyRules.json 的 charter 一段。
public static partial class CompanySystem
{
    private static CharterNumbers C => CompanyRules.Charter;

    private static readonly Dictionary<long, CompanyData> CharterIndex = new();

    private static void IndexCharters()
    {
        CharterIndex.Clear();
        foreach (CompanyData company in _book.companies)
            if (company != null && company.charter_kingdom_id >= 0) CharterIndex[company.charter_kingdom_id] = company;
    }

    public static CompanyData ByCharter(long kingdomId)
    {
        if (CharterIndex.Count == 0 && _book.companies.Any(company => company.charter_kingdom_id >= 0)) IndexCharters();
        return kingdomId >= 0 && CharterIndex.TryGetValue(kingdomId, out CompanyData company) ? company : null;
    }

    // 特许公司经营的那个殖民地国；已经收归国有、亡了的返回空
    public static Kingdom CharterColony(CompanyData company)
    {
        if (company == null || company.charter_kingdom_id < 0 || World.world?.kingdoms == null) return null;
        Kingdom colony = World.world.kingdoms.get(company.charter_kingdom_id);
        return colony?.data != null && !colony.isRekt() && ColonySystem.Of(colony)?.kind == ColonyState.Company ? colony : null;
    }

    private static SpecificClan ClanById(long id) =>
        id < 0 ? null : SpecificClanManager._specificClans.FirstOrDefault(clan => clan != null && clan.id == id);

    // 每年(宗主国对账时)：给本国的特许公司立账、对账；收归国有了、亡了的散掉
    private static List<CompanyData> SyncCharters(Empire empire, double now)
    {
        var list = new List<CompanyData>();
        foreach (CompanyData gone in _book.companies.Where(company => company.charter_kingdom_id >= 0 && CharterColony(company) == null).ToList())
            Remove(gone);
        if (World.world?.kingdoms == null) return list;
        foreach (Kingdom colony in World.world.kingdoms.list)
        {
            if (colony?.data == null || colony.isRekt() || ColonySystem.Of(colony)?.kind != ColonyState.Company) continue;
            Kingdom metropole = ColonySystem.MetropoleOf(colony);
            if (metropole == null || metropole.GetEmpire() != empire) continue;
            CompanyData company = ByCharter(colony.id) ?? Charter(empire, colony, metropole, now);
            company.name = colony.GetKingdomFullName();
            company.short_name = CompanyNaming.Brief(company.name);
            company.city_id = colony.capital?.id ?? -1L;
            company.culture = CultureService.GetRealmCulture(metropole) ?? company.culture;
            company.value = StateDebtSystem.AnnualStableIncome(colony);
            company.capital = colony.GetMoney();
            company.scale = colony.cities.Count;
            company.reputation = Mathf.Clamp(70f - (ColonySystem.Of(colony)?.autonomy ?? 0f) * 0.4f, 5f, 100f);
            list.Add(company);
        }
        return list;
    }

    private static CompanyData Charter(Empire realm, Kingdom colony, Kingdom metropole, double now)
    {
        var company = new CompanyData
        {
            id = _book.next_id++, sector = CompanyRules.CharterSector, charter_kingdom_id = colony.id, city_id = colony.capital?.id ?? -1L,
            culture = CultureService.GetRealmCulture(metropole) ?? "", founded = ColonySystem.Of(colony)?.founded_at ?? now,
            reputation = 60f, state_share = Mathf.Clamp01(C.state_share), name = colony.GetKingdomFullName()
        };
        company.short_name = CompanyNaming.Brief(company.name);
        SeedHolders(company, realm, metropole);
        _book.companies.Add(company);
        Index[company.id] = company;
        CharterIndex[colony.id] = company;
        Note(company, string.Format(LM.Get("company_note_charter"), metropole.GetKingdomFullName()));
        return company;
    }

    // 大股东：头一家是宗主国最大的财阀，没有就从都城的宗族里挑；第二家另挑一家都城的宗族
    private static void SeedHolders(CompanyData company, Empire realm, Kingdom metropole)
    {
        company.holders = new List<CompanyHolder>();
        City seat = metropole.capital;
        void Add(SpecificClan clan, float share)
        {
            if (clan == null || share <= 0f || company.holders.Any(holder => holder.clan_id == clan.id)) return;
            company.holders.Add(new CompanyHolder { clan_id = clan.id, owner = Surname(clan), share = share });
        }
        ConglomerateData house = ConglomerateSystem.Of(realm).OrderByDescending(item => item.share).FirstOrDefault();
        Add(house != null ? ClanById(house.clan_id) : seat == null ? null : PickOwner(realm, seat, company.id * 13L), C.first_holder);
        for (int attempt = 0; attempt < 4 && company.holders.Count < 2 && seat != null; attempt++)
            Add(PickOwner(realm, seat, company.id * 29L + attempt * 7L), C.second_holder);
        CompanyHolder top = company.holders.FirstOrDefault();
        company.clan_id = top?.clan_id ?? -1L;
        company.owner = top?.owner ?? "";
    }

    // 散户占几成
    public static float RetailShare(CompanyData company) =>
        Mathf.Max(0f, 1f - company.state_share - (company.holders?.Sum(holder => holder.share) ?? 0f));

    // 分红(见 ColonySystem 特许公司那一段)：国库那份进宗主国库，大股东与散户那份进宗主国都城的民间
    public static void PayCharterDividend(Kingdom colony, Kingdom metropole, int dividend)
    {
        CompanyData company = ByCharter(colony?.id ?? -1L);
        if (company == null)
        {
            if (dividend > 0) metropole.AddMoney(dividend, TreasuryCategory.Tribute);
            return;
        }
        company.profit = Mathf.Max(0, dividend);
        if (dividend <= 0) return;
        int treasury = Mathf.RoundToInt(dividend * Mathf.Clamp01(company.state_share)), rest = dividend - treasury;
        if (treasury > 0) metropole.AddMoney(treasury, TreasuryCategory.Tribute);
        if (rest <= 0) return;
        if (metropole.capital != null) EnterpriseSystem.ReturnToLocal(metropole.capital, rest);
        else metropole.AddMoney(rest, TreasuryCategory.Tribute);
    }

    #region 特许状

    // 特许状还有几年(没立账的旧档按满期算)
    public static float CharterYearsLeft(Kingdom colony)
    {
        ColonyState state = ColonySystem.Of(colony);
        if (state == null) return 0f;
        return state.charter_until < 0d ? C.years : Mathf.Max(0f, (float)((state.charter_until - World.world.getCurWorldTime()) / 60d));
    }

    // 续一回要交的特许费
    public static int RenewFee(Kingdom colony) =>
        Mathf.Max(Mathf.RoundToInt(C.renew_fee_min), Mathf.RoundToInt(StateDebtSystem.AnnualStableIncome(colony) * C.renew_fee_share));

    // 每年(ColonySystem)：头一年立下特许状；到期时公司撑得住就多半续，否则撤销。撤了返回 true
    public static bool CharterDue(Kingdom colony, Kingdom metropole, ColonyState state, double now)
    {
        if (state.charter_until < 0d)
        {
            state.charter_until = now + C.years * 60d;
            return false;
        }
        if (now < state.charter_until) return false;
        bool healthy = colony.GetTreasuryBalance() >= 0 && colony.cities.Count < C.nationalize_cities - 1;
        if (healthy && UnityEngine.Random.value < C.renew_chance && Renew(colony, metropole, state) == null) return false;
        EndCharter(colony, metropole, "colony_charter_expired");
        return true;
    }

    private static string Renew(Kingdom colony, Kingdom metropole, ColonyState state)
    {
        double now = World.world.getCurWorldTime();
        if (state.charter_until >= 0d && state.charter_until - now > C.years * 60d) return "company_charter_not_due";
        int fee = RenewFee(colony);
        if (colony.GetMoney() < fee) return "company_charter_no_money";
        colony.SubMoney(fee, TreasuryCategory.Tribute);
        metropole.AddMoney(fee, TreasuryCategory.Tribute);
        state.charter_until = Math.Max(state.charter_until, now) + C.years * 60d;
        state.charter_renewals++;
        string text = string.Format(LM.Get("company_charter_renewed_history"), metropole.GetKingdomFullName(), colony.GetKingdomFullName(), C.years, fee);
        if (ByCharter(colony.id) is { } company) Note(company, text);
        EventRecorder.RecordRoutine(metropole.GetEmpire(), text);
        return null;
    }

    // 撤销特许、改设总督府(大股东与散户的股份就此作废，宗主国商人怨)
    private static void EndCharter(Kingdom colony, Kingdom metropole, string key)
    {
        if (metropole.GetEmpire() is { } realm) ClaimPolicySystem.AddGrievance(realm, SocialClass.Merchant, 4f);
        ColonySystem.RevokeCharter(colony, key);
    }

    // 界面：续特许
    public static string RenewCharter(CompanyData company)
    {
        Kingdom colony = CharterColony(company), metropole = ColonySystem.MetropoleOf(colony);
        if (colony == null || metropole == null) return "diplomacy_reason_invalid";
        return Renew(colony, metropole, ColonySystem.Of(colony));
    }

    // 界面：撤特许
    public static string RevokeCharter(CompanyData company)
    {
        Kingdom colony = CharterColony(company), metropole = ColonySystem.MetropoleOf(colony);
        if (colony == null || metropole == null) return "diplomacy_reason_invalid";
        EndCharter(colony, metropole, "colony_charter_revoked");
        return null;
    }

    // 收归国有要出的钱：照身价买下国库以外的股份
    public static int BuyOutCost(CompanyData company) =>
        company == null ? 0 : Mathf.Max(0, Mathf.RoundToInt(Worth(company) * (1f - Mathf.Clamp01(company.state_share))));

    // 界面：照市值买下股份、收归国有(钱进宗主国都城的民间)
    public static string BuyOutCharter(CompanyData company)
    {
        Kingdom colony = CharterColony(company), metropole = ColonySystem.MetropoleOf(colony);
        if (colony == null || metropole == null) return "diplomacy_reason_invalid";
        int cost = BuyOutCost(company);
        if (metropole.GetMoney() < cost) return "company_charter_buyout_no_money";
        if (cost > 0)
        {
            metropole.SubMoney(cost, TreasuryCategory.PublicEnterprise);
            if (metropole.capital != null) EnterpriseSystem.ReturnToLocal(metropole.capital, cost);
        }
        return ColonySystem.NationalizeNow(colony) ? null : "diplomacy_reason_invalid";
    }

    // 兵力(各军团人数合计)
    public static int CharterTroops(CompanyData company)
    {
        Kingdom colony = CharterColony(company);
        return colony == null ? 0 : Mathf.RoundToInt(CorpsSystem.Of(colony).Sum(corps => corps.strength));
    }

    public static int NationalizeCities => C.nationalize_cities;

    #endregion
}

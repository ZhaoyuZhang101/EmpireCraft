using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 股票与股市、财富榜(用户 2026-11-25："可股票和股市""然后再写个财富榜")。
//   · 交易所：研究出银行业、本国有三家以上私企的国家开一所证券交易所(每国一所)；国有化的国家不开；
//   · 上市：年产值够大、声誉过得去的私企上市，每家按一百股算；上市那年按市值募得一成五的资本(并购更有底气)；
//     收归国有的退市；
//   · 股价：每年向"该值多少"靠拢六成、再带一点涨跌——该值多少 = 盈利 × 市盈率 + 一半家底 + 三成资产；
//     市盈率跟着景气走(景气 0 时 8 倍、满时 24 倍：过热的年份股价虚高)，恐慌年只有 5 倍、萧条 6.5 倍——
//     所以市面崩盘那年股价腰斩，泡沫是看得见的；
//   · 股指：开业那年 100，每年按各上市公司股价涨跌(按市值加权)连乘；
//   · 财富榜：公司按身价排(上市的按市值，没上市的按盈利与家底估)；富豪按东家的宗族排——财阀旗下的并到财阀名下，
//     上市公司东家只算六成股份，国企不算，外资子公司算到母公司的东家头上。
// 每年跟在公司的账后面结(见 CompanySystem.Update)。数值都是模组自己的规则。
public static class StockMarketSystem
{
    public const int Shares = 100;
    public const string ExchangeTech = "banking";
    private const float ListValue = 120f, ListReputation = 40f, IpoRaise = 0.15f, OwnerStake = 0.6f;
    private const int MinFirms = 3, Years = 30;

    public static IReadOnlyList<StockExchangeData> Exchanges => CompanySystem.Book.exchanges;

    public static StockExchangeData ExchangeOf(Empire empire) =>
        empire == null ? null : CompanySystem.Book.exchanges.FirstOrDefault(exchange => exchange.empire_id == empire.id);

    private static float PE(ConstitutionalEconomyState state) => state.cycle_phase switch
    {
        EconomicCycleSystem.Panic => 5f,
        EconomicCycleSystem.Depression => 6.5f,
        _ => 8f + state.cycle_heat * 0.16f
    };

    private static float Fair(CompanyData company, ConstitutionalEconomyState state) => Mathf.Max(0.5f,
        (Mathf.Max(0f, company.profit) * PE(state) + Mathf.Max(0f, company.capital) * 0.5f + company.value * 0.3f) / Shares);

    internal static void Update(Empire empire, ConstitutionalEconomyState state, List<CompanyData> own, double now)
    {
        StockExchangeData exchange = ExchangeOf(empire);
        if (exchange == null)
        {
            // 国有化的国家不开交易所
            if (state.nationalization >= ModernPolicySystem.Nationalized || own.Count(company => !company.state) < MinFirms ||
                !TechnologySystem.HasTech(InstitutionSystem.GetPrimaryCulture(empire), ExchangeTech)) return;
            exchange = new StockExchangeData { empire_id = empire.id, since = now };
            CompanySystem.Book.exchanges.Add(exchange);
            EventRecorder.RecordRoutine(empire, string.Format(LM.Get("stock_exchange_opened_history"), empire.GetEmpireFullName(),
                empire.CoreKingdom.capital?.GetCityName() ?? ""));
        }
        foreach (CompanyData company in own)
        {
            if (company.state)
            {
                company.listed = false;
                continue;
            }
            if (company.listed || company.value < ListValue || company.reputation < ListReputation) continue;
            company.listed = true;
            company.listed_at = now;
            company.price = Fair(company, state);
            company.prices = new List<float>();
            company.capital += company.price * Shares * IpoRaise;
            // 上市的多，只记进公司自己的大事记
            CompanySystem.Note(company, string.Format(LM.Get("stock_listed_history"), company.name, empire.GetEmpireFullName()));
        }
        float weighted = 0f, weight = 0f;
        foreach (CompanyData company in own.Where(company => company.listed))
        {
            company.prices ??= new List<float>();
            float before = company.prices.Count > 0 ? company.price : 0f;
            company.price = Mathf.Max(0.1f, Mathf.Lerp(company.price, Fair(company, state), 0.6f) * UnityEngine.Random.Range(0.92f, 1.08f));
            company.prices.Add(company.price);
            while (company.prices.Count > Years) company.prices.RemoveAt(0);
            if (before <= 0f) continue;
            float cap = before * Shares;
            weighted += cap * company.price / before;
            weight += cap;
        }
        if (weight > 0f) exchange.index = Mathf.Max(1f, exchange.index * weighted / weight);
        exchange.history ??= new List<float>();
        exchange.history.Add(exchange.index);
        while (exchange.history.Count > Years) exchange.history.RemoveAt(0);
    }

    // 去年的涨跌(0.1 = 涨一成)；没有上年的价返回 0
    public static float Change(IList<float> series) =>
        series == null || series.Count < 2 || series[series.Count - 2] <= 0f ? 0f : series[series.Count - 1] / series[series.Count - 2] - 1f;

    public static string ChangeText(float change) =>
        (change >= 0f ? "+" : "") + Mathf.RoundToInt(change * 100f) + "%";

    #region 财富榜

    public sealed class Fortune
    {
        public long clanId;
        public string name;
        public Empire realm;
        public float wealth;
        public int companies;
        public bool group;
    }

    public static List<CompanyData> TopCompanies(int top) =>
        CompanySystem.All.Where(company => CompanySystem.RealmOf(company) != null)
            .OrderByDescending(CompanySystem.Worth).Take(top).ToList();

    // 富豪：按东家的宗族(财阀旗下的并到财阀名下)
    public static List<Fortune> RichList(int top)
    {
        var fortunes = new Dictionary<long, Fortune>();
        void Add(CompanyData holder, float wealth)
        {
            long clan = holder.group_clan_id >= 0 ? holder.group_clan_id : holder.clan_id;
            if (clan < 0 || wealth <= 0f) return;
            if (!fortunes.TryGetValue(clan, out Fortune fortune))
                fortunes[clan] = fortune = new Fortune { clanId = clan, realm = CompanySystem.RealmOf(holder), group = holder.group_clan_id >= 0 };
            fortune.wealth += wealth;
            fortune.companies++;
            if (string.IsNullOrEmpty(fortune.name) || holder.clan_id == clan) fortune.name = holder.clan_id == clan ? holder.owner : fortune.name;
        }
        foreach (CompanyData company in CompanySystem.All)
        {
            if (company.state || CompanySystem.RealmOf(company) == null) continue;
            float stake = CompanySystem.Worth(company) * (company.listed ? OwnerStake : 1f);
            // 外资子公司算到母公司的东家头上
            CompanyData holder = CompanySystem.ById(company.foreign_company_id) ?? company;
            Add(holder, stake);
        }
        foreach (Fortune fortune in fortunes.Values)
            if (string.IsNullOrEmpty(fortune.name))
                fortune.name = CompanySystem.Surname(SpecificClanManager._specificClans.FirstOrDefault(clan => clan != null && clan.id == fortune.clanId));
        return fortunes.Values.Where(fortune => !string.IsNullOrEmpty(fortune.name))
            .OrderByDescending(fortune => fortune.wealth).Take(top).ToList();
    }

    #endregion
}

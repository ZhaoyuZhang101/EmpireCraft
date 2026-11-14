using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 内帑：君主自己的钱(用户 2026-11-15 "玩家怎么给自己赚钱"，四样都要)。规则是模组自己的：
//   · 年例：每月从国库拨上月岁入的 0/2/5/10%(不拨/低/中/高)，记宫廷开支；高档每年天命 −1、各派圣眷 −3，中档各派圣眷 −1。
//     只有设过档的(手机席位上的君主)才拨，AI 君主不动(默认不拨)；
//   · 皇庄：君主本宗(SpecificClan)在本国各城占的私田每月收租进内帑(每 1 点占地 RentPerPoint 两)；
//     置田：花内帑在本国一座城买 BuyPoints 点私田(地价照土地买卖的价，钱给卖地的百姓)，土地不许买卖的不能置；
//   · 卖官鬻爵：中枢空着的官卖给朝中有钱的人(价钱 OfficePrice)，本国积弊 +2%、各派怨望 +3；
//   · 动用公帑：从国库现银里拿一成或两成半(不管留存)，记贪污罪状；吏治越清明越容易被察觉(察觉的几率 = 0.3 + 0.6 × (1 − 积弊))，
//     察觉了天命 −3、各派怨望 +6。
// 账：各君主今年、去年的进项按来源记(年例、皇庄、卖官、公帑，还有积弊漏进私囊的，见 TreasurySystem)，随存档。
public sealed class PrivyPurseData
{
    public long kingdom = -1;
    // 有人亲自掌着过(手机席位)：年例、皇庄收租才算；AI 君主只记账不动
    public bool active;
    public int tier;
    public int year;
    public Dictionary<string, long> thisYear = new(), lastYear = new();
}

public static class PrivyPurse
{
    public static readonly float[] TierShare = { 0f, 0.02f, 0.05f, 0.10f };
    public const float RentPerPoint = 0.25f, BuyPoints = 1f;
    public const float SmallTake = 0.10f, BigTake = 0.25f;
    public const string Allowance = "allowance", Estates = "estates", Offices = "offices", Taken = "embezzle", Graft = "graft";

    private static readonly Dictionary<long, PrivyPurseData> Books = new();
    private static readonly Dictionary<long, float> RentCarry = new();
    private static int _month = -1;

    public static void ResetWorldState()
    {
        Books.Clear();
        RentCarry.Clear();
        _month = -1;
    }

    public static List<PrivyPurseData> Save() => Books.Values.ToList();

    public static void Load(List<PrivyPurseData> saved)
    {
        ResetWorldState();
        if (saved == null) return;
        foreach (PrivyPurseData data in saved)
            if (data != null && data.kingdom >= 0) Books[data.kingdom] = data;
    }

    public static PrivyPurseData Book(Kingdom realm)
    {
        if (realm?.data == null) return null;
        if (!Books.TryGetValue(realm.data.id, out PrivyPurseData data))
            Books[realm.data.id] = data = new PrivyPurseData { kingdom = realm.data.id, year = Date.getCurrentYear() };
        Roll(data);
        return data;
    }

    private static void Roll(PrivyPurseData data)
    {
        int year = Date.getCurrentYear();
        if (data.year == year) return;
        data.lastYear = data.year == year - 1 ? data.thisYear : new Dictionary<string, long>();
        data.thisYear = new Dictionary<string, long>();
        data.year = year;
    }

    // 记一笔进项(别处漏进君主私囊的也记，见 TreasurySystem)
    public static void Note(Kingdom realm, string source, long amount)
    {
        if (amount <= 0 || realm?.data == null) return;
        PrivyPurseData book = Book(realm);
        book.thisYear[source] = (book.thisYear.TryGetValue(source, out long sum) ? sum : 0L) + amount;
    }

    private static void Credit(Kingdom realm, string source, int amount)
    {
        if (amount <= 0 || realm?.king == null) return;
        realm.king.addMoney(amount);
        Note(realm, source, amount);
    }

    private static int MonthlyIncome(Kingdom realm)
    {
        TreasuryReport report = TreasurySystem.Report(realm);
        // 中央直收加各城上缴(transfer_in)：这才是国库每月实际进的钱
        return report == null ? 0 : (int)(Math.Max(0L, report.income + report.transfer_in) / Math.Max(1, report.months));
    }

    public static int AllowanceOf(Kingdom realm, int tier) =>
        tier <= 0 || tier >= TierShare.Length ? 0 : (int)(MonthlyIncome(realm) * TierShare[tier]);

    // ───────── 每月(战略调度经 FrameJobs 调来) ─────────
    public static void Tick()
    {
        if (World.world == null || Books.Count == 0) return;
        int month = Date.getCurrentYear() * 12 + Date.getCurrentMonth();
        if (month == _month) return;
        bool newYear = _month >= 0 && month / 12 != _month / 12;
        _month = month;
        foreach (PrivyPurseData data in Books.Values.ToArray())
        {
            Kingdom realm = World.world.kingdoms.get(data.kingdom);
            if (!data.active || realm?.data == null || realm.isRekt() || realm.king == null || !realm.king.isAlive()) continue;
            Roll(data);
            if (data.tier > 0)
            {
                int amount = AllowanceOf(realm, data.tier);
                if (amount > 0 && TreasurySystem.TrySpend(realm, amount, TreasuryCategory.Palace, false)) Credit(realm, Allowance, amount);
                if (newYear && data.tier >= 2) TierCost(realm, data.tier);
            }
            CollectRent(realm);
        }
    }

    private static void TierCost(Kingdom realm, int tier)
    {
        Empire empire = realm.GetEmpire();
        if (empire == null || empire.CoreKingdom != realm) return;
        if (tier >= 3) empire.AddMandate(-1);
        foreach (FixedFaction faction in CourtSystem.Factions(empire))
            CourtSystem.AdjustFaction(empire, faction, "favor", tier >= 3 ? -3f : -1f);
    }

    // 皇庄：本宗在本国各城的私田，每 1 点每月 RentPerPoint 两(零头累计)
    public static float RoyalLand(Kingdom realm, out List<(City city, float share)> holdings)
    {
        holdings = new List<(City, float)>();
        SpecificClan clan = realm?.king?.GetSpecificClan();
        if (clan == null || realm.cities == null) return 0f;
        float total = 0f;
        foreach (City city in realm.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            float share = LandEconomySystem.ClanShare(city, clan);
            if (share <= 0f) continue;
            holdings.Add((city, share));
            total += share;
        }
        holdings.Sort((a, b) => b.share.CompareTo(a.share));
        return total;
    }

    public static float MonthlyRent(Kingdom realm) => RoyalLand(realm, out _) * RentPerPoint;

    private static void CollectRent(Kingdom realm)
    {
        float rent = MonthlyRent(realm);
        if (rent <= 0f) return;
        RentCarry.TryGetValue(realm.data.id, out float carry);
        carry += rent;
        int whole = (int)carry;
        RentCarry[realm.data.id] = carry - whole;
        Credit(realm, Estates, whole);
    }

    // ───────── 君主下令(手机上照政令一个月后办) ─────────
    public static string SetTier(Kingdom realm, int tier)
    {
        if (realm?.data == null || tier < 0 || tier >= TierShare.Length) return "purse_fail";
        PrivyPurseData book = Book(realm);
        book.tier = tier;
        book.active = true;
        return null;
    }

    public static int LandPrice(City city) => Mathf.CeilToInt(LandEconomySystem.LandPrice(BuyPoints));

    public static bool CanBuyLand(Kingdom realm, City city) =>
        city?.data != null && !city.isRekt() && city.kingdom == realm && realm?.king?.GetSpecificClan() != null &&
        CityPopulationSystem.AbstractPopulationEnabled && LandEconomySystem.IsLandMarketOpen(realm);

    public static string BuyLand(Kingdom realm, City city)
    {
        Actor king = realm?.king;
        if (!CanBuyLand(realm, city)) return "purse_land_closed";
        int price = LandPrice(city);
        if (king.money < price) return "purse_poor";
        float got = LandEconomySystem.AnnexForClan(city, king.GetSpecificClan(), BuyPoints);
        if (got <= 0f) return "purse_land_none";
        int paid = Mathf.CeilToInt(LandEconomySystem.LandPrice(got));
        king.spendMoney(paid);
        LandEconomySystem.PaySellers(city, paid);
        return null;
    }

    // 卖官：中枢空着的官，价钱 = 上月岁入的两成，至少 OfficeFloor
    public const int OfficeFloor = 40;
    public static int OfficePrice(Kingdom realm) => Math.Max(OfficeFloor, MonthlyIncome(realm) / 5);

    public static List<(int slot, OfficeObject office)> VacantOffices(Empire empire)
    {
        var list = new List<(int, OfficeObject)>();
        List<OfficeObject> all = empire?.data?.centerOffice?.GetAllOffices(empire);
        if (all == null) return list;
        for (int i = 0; i < all.Count; i++)
        {
            OfficeObject office = all[i];
            // 后宫的位分、君主本人的位子不卖
            if (office == null || office.leader_select_method == LeaderSelectMethod.Harem ||
                office.meta_object is Kingdom ruled && ruled.GetOffice() == office) continue;
            Actor holder = office.GetActor();
            if (holder == null || !holder.isAlive()) list.Add((i, office));
        }
        return list;
    }

    public static string SellOffice(Empire empire, int slot, out string buyer)
    {
        buyer = null;
        Kingdom realm = empire?.CoreKingdom;
        List<OfficeObject> all = empire?.data?.centerOffice?.GetAllOffices(empire);
        if (realm?.king == null || all == null || slot < 0 || slot >= all.Count || all[slot] == null) return "purse_fail";
        Actor holder = all[slot].GetActor();
        if (holder != null && holder.isAlive()) return "purse_office_taken";
        OfficeObject office = all[slot];
        if (VacantOffices(empire).All(found => found.slot != slot)) return "purse_fail";
        Actor pick = CourtSystem.CabinetCandidates(empire).Where(office.Accepts).OrderByDescending(actor => actor.money).FirstOrDefault();
        if (pick == null) return "purse_no_buyer";
        int price = OfficePrice(realm);
        office.SetActor(pick);
        if (office.GetActor() != pick) return "purse_no_buyer";
        pick.spendMoney(Math.Min(pick.money, price));
        Credit(realm, Offices, price);
        realm.AddCorruptionRate(0.02d);
        foreach (FixedFaction faction in CourtSystem.Factions(empire)) CourtSystem.AdjustFaction(empire, faction, "grievance", 3f);
        buyer = pick.getName();
        return null;
    }

    // 动用公帑：拿可支配的 share；察觉了返回 true
    // 挪用不管留存，按国库现银拿
    public static int TakeAmount(Kingdom realm, float share) => realm == null ? 0 : (int)(Math.Max(0, realm.GetMoney()) * share);

    public static string Embezzle(Kingdom realm, float share, out bool exposed, out int taken)
    {
        exposed = false;
        taken = TakeAmount(realm, share);
        Actor king = realm?.king;
        if (king == null || taken <= 0) return "purse_poor";
        if (!TreasurySystem.TrySpend(realm, taken, TreasuryCategory.Corruption, false)) return "purse_poor";
        Credit(realm, Taken, taken);
        EmpireLawSystem.AddCrimeAmount(king, LawType.贪污, taken);
        double chance = 0.3d + 0.6d * (1d - Math.Max(0d, Math.Min(1d, realm.GetCorruptionRate())));
        if (Randy.randomFloat(0f, 1f) >= chance) return null;
        exposed = true;
        Empire empire = realm.GetEmpire();
        if (empire != null && empire.CoreKingdom == realm)
        {
            empire.AddMandate(-3);
            foreach (FixedFaction faction in CourtSystem.Factions(empire)) CourtSystem.AdjustFaction(empire, faction, "grievance", 6f);
        }
        return null;
    }
}

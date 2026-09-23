using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

public sealed class CityLandHolderView
{
    public string Name;
    public float Share;
    public bool Institutional;
}

public sealed class CityLandReport
{
    public bool MarketOpen;
    public bool LandlordClassEnabled;
    public bool MigrationBlocked;
    public float LandlessPopulationRatio;
    public List<CityLandHolderView> Holders = new List<CityLandHolderView>();
}

public sealed class FamilyLandHoldingView
{
    public string CityName;
    public float OwnershipShare;
    public float TenureShare;
    public bool MarketOpen;
}

/// <summary>
/// 城市土地与家庭经济身份。土地数据归城市所有，因此没有建立帝国的王国也能运行。
/// </summary>
public static class LandEconomySystem
{
    public const float HouseholdBaseShare = 0.5f;
    public const float LandlordThreshold = 1f;
    public const float RebellionLandlessThreshold = 0.20f;
    public const float ContagionLandlessThreshold = 0.30f;
    private const int MerchantMinimumAnnualIncome = 40;
    private const int MerchantEntryYears = 2;
    private const int MerchantExitYears = 3;
    private const int LandPurchasePrice = 25;
    private const int RebellionCooldownYears = 10;

    public static void RecordIncome(Actor actor, int amount)
    {
        if (actor == null || actor.isRekt() || amount <= 0) return;
        ActorExtension.ActorExtraData data = actor.GetOrCreate();
        data.economic_income_current_year = Math.Max(0, data.economic_income_current_year + amount);
    }

    public static bool IsLandlord(Actor actor)
    {
        if (actor?.city == null || !IsLandMarketOpen(actor.city.kingdom)) return false;
        if (!IsLandlordClassEnabled(actor.city.kingdom)) return false;
        return GetHouseholdShare(actor.city, GetHouseholdKey(actor)) > LandlordThreshold;
    }

    public static bool IsLandMarketOpen(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return false;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        return data.private_land_market_open && IsLandlordClassEnabled(kingdom);
    }

    public static bool IsLandlordClassEnabled(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return false;
        if (kingdom.GetOrCreate().is_peasant_revolutionary_government) return true;
        RegimeType type = kingdom.GetRegime()?.type ?? RegimeType.Feudalism;
        return type != RegimeType.Feudalism && type != RegimeType.ZhouFeudalism;
    }

    public static void OpenPrivateLandMarket(Kingdom kingdom, bool redistribute, bool revolutionary)
    {
        if (kingdom == null || kingdom.isRekt()) return;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        data.private_land_market_open = true;
        if (revolutionary) data.is_peasant_revolutionary_government = true;
        foreach (City city in kingdom.cities?.Where(city => city != null && !city.isRekt()).ToList() ??
                                     new List<City>())
        {
            if (redistribute) RedistributeLand(city);
            else EnsureInitialHouseholdShares(city);
        }
    }

    public static void RedistributeLand(City city)
    {
        if (city == null || city.isRekt()) return;
        CityExtension.CityExtraData data = EnsureData(city);
        data.household_land_shares.Clear();
        data.household_land_representatives.Clear();
        foreach (KeyValuePair<string, List<Actor>> household in BuildHouseholds(city))
        {
            Actor representative = SelectRepresentative(household.Value);
            if (representative == null || IsPrivilegedHousehold(household.Value)) continue;
            data.household_land_shares[household.Key] = HouseholdBaseShare;
            data.household_land_representatives[household.Key] = representative.id;
        }
        TrimPrivateSharesToCapacity(city, data);
        data.land_redistribution_pending = false;
        RefreshClasses(city);
    }

    public static void UpdateCity(City city)
    {
        if (city == null || city.isRekt() || city.kingdom == null || city.kingdom.isRekt()) return;
        CityExtension.CityExtraData data = EnsureData(city);
        double now = World.world?.getCurWorldTime() ?? -1d;
        if (data.last_land_economy_timestamp < 0d)
        {
            data.last_land_economy_timestamp = now;
            return;
        }
        if (Date.getYearsSince(data.last_land_economy_timestamp) < 1) return;
        data.last_land_economy_timestamp = now;

        Dictionary<string, List<Actor>> households = BuildHouseholds(city);
        UpdateMerchantHouseholds(households);
        if (IsLandMarketOpen(city.kingdom))
        {
            if (data.land_redistribution_pending) RedistributeLand(city);
            EnsureInitialHouseholdShares(city, households);
            ProcessLandPurchases(city, households);
            TryMoveLandlessHousehold(city, households);
        }
        RefreshClasses(city);

        float landlessRatio = CalculateLandlessPopulationRatio(city);
        if (!TryJoinAdjacentPeasantRebellion(city, landlessRatio))
            TryStartPeasantLandRebellion(city, landlessRatio);
    }

    public static CityLandReport GetReport(City city)
    {
        var report = new CityLandReport();
        if (city == null || city.isRekt() || city.kingdom == null) return report;
        report.MarketOpen = IsLandMarketOpen(city.kingdom);
        report.LandlordClassEnabled = IsLandlordClassEnabled(city.kingdom);
        report.MigrationBlocked = city.kingdom.GetOrCreate().fugitive_household_law_enacted ||
                                  city.kingdom.HasLaw(LawType.逃人法);
        report.LandlessPopulationRatio = CalculateLandlessPopulationRatio(city);
        float crown = GetCrownShare(city);
        float nobles = GetNobleShare(city);
        if (crown > 0f)
            report.Holders.Add(new CityLandHolderView
                { Name = GetCrownName(city), Share = crown, Institutional = true });
        if (nobles > 0f)
            report.Holders.Add(new CityLandHolderView
                { Name = LM.Get("city_land_local_nobles"), Share = nobles, Institutional = true });
        if (report.MarketOpen)
        {
            CityExtension.CityExtraData data = EnsureData(city);
            foreach (KeyValuePair<string, float> pair in data.household_land_shares
                         .Where(pair => pair.Value > 0f))
            {
                report.Holders.Add(new CityLandHolderView
                {
                    Name = ResolveHouseholdName(city, pair.Key),
                    Share = pair.Value,
                    Institutional = false
                });
            }
        }
        report.Holders = report.Holders.OrderByDescending(holder => holder.Share).Take(10).ToList();
        return report;
    }

    public static List<FamilyLandHoldingView> GetFamilyHoldings(Family family)
    {
        var result = new List<FamilyLandHoldingView>();
        if (family == null || World.world?.cities?.list == null) return result;
        string householdKey = $"f:{family.id}";
        var residenceCityIds = new HashSet<long>((family.units ?? new List<Actor>())
            .Where(IsLivingResident).Select(actor => actor.city?.id ?? -1L).Where(id => id > 0));
        foreach (City city in World.world.cities.list.Where(city => city != null && !city.isRekt()))
        {
            CityExtension.CityExtraData data = EnsureData(city);
            bool resides = residenceCityIds.Contains(city.id);
            float ownership = data.household_land_shares.TryGetValue(householdKey, out float share)
                ? Mathf.Max(0f, share)
                : 0f;
            bool marketOpen = IsLandMarketOpen(city.kingdom);
            if (!resides && ownership <= 0f) continue;
            result.Add(new FamilyLandHoldingView
            {
                CityName = city.GetCityName(),
                OwnershipShare = marketOpen ? ownership : 0f,
                TenureShare = !marketOpen && resides ? HouseholdBaseShare : 0f,
                MarketOpen = marketOpen
            });
        }
        return result.OrderByDescending(view => view.OwnershipShare)
            .ThenByDescending(view => view.TenureShare).ThenBy(view => view.CityName).ToList();
    }

    public static SocialClass GetFamilyEconomicClass(Family family)
    {
        List<Actor> members = family?.units?.Where(IsLivingResident).ToList() ?? new List<Actor>();
        if (members.Any(IsLandlord)) return SocialClass.Landlord;
        if (members.Any(actor => actor.GetOrCreate().socialClass == SocialClass.Noble)) return SocialClass.Noble;
        if (members.Any(actor => actor.GetOrCreate().is_economic_merchant)) return SocialClass.Merchant;
        if (members.Any(actor => actor.GetOrCreate().socialClass == SocialClass.Officer)) return SocialClass.Officer;
        if (members.Any(actor => actor.GetOrCreate().socialClass == SocialClass.Army)) return SocialClass.Army;
        if (members.Any(actor => actor.GetOrCreate().socialClass == SocialClass.Labour)) return SocialClass.Labour;
        return SocialClass.Peasant;
    }

    public static void MarkPeasantSocialRebellion(War war, Kingdom rebel, Kingdom origin, City originCity,
        float landlessRatio, string cause)
    {
        if (war == null || rebel == null || origin == null) return;
        WarExtension.WarExtraData warData = war.GetOrCreate();
        warData.peasant_land_rebellion = true;
        warData.peasant_land_rebellion_origin_kingdom_id = origin.id;
        warData.peasant_land_rebellion_kingdom_id = rebel.id;
        warData.peasant_land_rebellion_origin_city_id = originCity?.id ?? rebel.capital?.id ?? -1L;
        warData.peasant_land_rebellion_cause = cause ?? "";
        warData.peasant_landless_ratio = Mathf.Clamp01(landlessRatio);
        KingdomExtension.KingdomExtraData rebelData = rebel.GetOrCreate();
        rebelData.is_peasant_land_rebellion = true;
        rebelData.peasant_land_rebellion_war_id = war.id;
        rebelData.peasant_land_rebellion_origin_kingdom_id = origin.id;
    }

    public static void ResolvePeasantLandRebellion(War war, WarWinner winner)
    {
        if (war == null) return;
        WarExtension.WarExtraData warData = war.GetOrCreate();
        if (!warData.peasant_land_rebellion) return;
        Kingdom rebel = war.getMainAttacker() ?? World.world?.kingdoms?.get(warData.peasant_land_rebellion_kingdom_id);
        bool won = winner == WarWinner.Attackers && rebel != null && !rebel.isRekt();
        if (won)
        {
            OpenPrivateLandMarket(rebel, redistribute: true, revolutionary: true);
            string victory = string.Format(LM.Get("land_rebellion_victory_history"), rebel.GetKingdomName());
            RecordRebellionHistory(rebel, victory);
            Kingdom origin = World.world?.kingdoms?.get(warData.peasant_land_rebellion_origin_kingdom_id);
            Empire originEmpire = origin?.GetEmpire();
            if (originEmpire != null && originEmpire != rebel.GetEmpire())
                originEmpire.RecordHistory(directContent: victory, actorId: rebel.king?.id ?? -1L,
                    kingdomId: rebel.id);
            ActionLibrary.showWhisperTip(victory);
        }
        if (rebel != null)
        {
            KingdomExtension.KingdomExtraData rebelData = rebel.GetOrCreate();
            rebelData.is_peasant_land_rebellion = false;
            rebelData.peasant_land_rebellion_war_id = -1L;
            rebelData.peasant_land_rebellion_origin_kingdom_id = -1L;
        }
    }

    private static CityExtension.CityExtraData EnsureData(City city)
    {
        CityExtension.CityExtraData data = city.GetOrCreate();
        data.household_land_shares ??= new Dictionary<string, float>();
        data.household_land_representatives ??= new Dictionary<string, long>();
        return data;
    }

    private static Dictionary<string, List<Actor>> BuildHouseholds(City city)
    {
        var result = new Dictionary<string, List<Actor>>();
        foreach (Actor actor in city.units?.Where(IsLivingResident).ToList() ?? new List<Actor>())
        {
            string key = GetHouseholdKey(actor);
            if (!result.TryGetValue(key, out List<Actor> members))
            {
                members = new List<Actor>();
                result[key] = members;
            }
            members.Add(actor);
        }
        return result;
    }

    private static bool IsLivingResident(Actor actor)
    {
        return actor != null && !actor.isRekt() && actor.isAlive();
    }

    private static string GetHouseholdKey(Actor actor)
    {
        return actor?.hasFamily() == true ? $"f:{actor.family.id}" : $"a:{actor?.id ?? -1L}";
    }

    private static Actor SelectRepresentative(IEnumerable<Actor> members)
    {
        return members?.Where(IsLivingResident).OrderByDescending(actor => actor.money)
            .ThenByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
    }

    private static bool IsPrivilegedHousehold(IEnumerable<Actor> members)
    {
        return members.Any(actor => actor.IsOnOffice() || actor == actor.kingdom?.king ||
                                    actor.GetOrCreate().socialClass == SocialClass.Noble);
    }

    private static void UpdateMerchantHouseholds(Dictionary<string, List<Actor>> households)
    {
        List<int> incomes = households.Values.Select(members => members.Sum(actor =>
            Math.Max(0, actor.GetOrCreate().economic_income_current_year))).OrderBy(value => value).ToList();
        int median = incomes.Count == 0 ? 0 : incomes[incomes.Count / 2];
        int threshold = Math.Max(MerchantMinimumAnnualIncome, median * 2);
        foreach (List<Actor> members in households.Values)
        {
            int income = members.Sum(actor => Math.Max(0, actor.GetOrCreate().economic_income_current_year));
            Actor representative = SelectRepresentative(members);
            bool merchant = members.Any(actor => actor.GetOrCreate().is_economic_merchant);
            int highYears = members.Max(actor => actor.GetOrCreate().merchant_high_income_years);
            int lowYears = members.Max(actor => actor.GetOrCreate().merchant_low_income_years);
            if (income >= threshold)
            {
                highYears++;
                lowYears = 0;
                if (highYears >= MerchantEntryYears) merchant = true;
            }
            else
            {
                lowYears++;
                highYears = 0;
                if (lowYears >= MerchantExitYears) merchant = false;
            }
            foreach (Actor actor in members)
            {
                ActorExtension.ActorExtraData data = actor.GetOrCreate();
                data.economic_income_previous_year = income;
                data.economic_income_current_year = 0;
                data.merchant_high_income_years = highYears;
                data.merchant_low_income_years = lowYears;
                data.is_economic_merchant = merchant;
            }
            if (representative != null && merchant) representative.GetOrCreate().is_economic_merchant = true;
        }
    }

    private static void EnsureInitialHouseholdShares(City city,
        Dictionary<string, List<Actor>> households = null)
    {
        if (!IsLandMarketOpen(city?.kingdom)) return;
        CityExtension.CityExtraData data = EnsureData(city);
        households ??= BuildHouseholds(city);
        float capacity = GetPrivateLandCapacity(city);
        float used = data.household_land_shares.Values.Where(value => value > 0f).Sum();
        foreach (KeyValuePair<string, List<Actor>> household in households)
        {
            Actor representative = SelectRepresentative(household.Value);
            if (representative == null || IsPrivilegedHousehold(household.Value)) continue;
            data.household_land_representatives[household.Key] = representative.id;
            if (data.household_land_shares.ContainsKey(household.Key)) continue;
            if (used + HouseholdBaseShare > capacity + 0.001f)
            {
                data.household_land_shares[household.Key] = 0f;
                continue;
            }
            data.household_land_shares[household.Key] = HouseholdBaseShare;
            used += HouseholdBaseShare;
        }
        TrimPrivateSharesToCapacity(city, data);
    }

    private static void ProcessLandPurchases(City city, Dictionary<string, List<Actor>> households)
    {
        CityExtension.CityExtraData data = EnsureData(city);
        List<string> sellers = households.Where(pair => GetHouseholdShare(city, pair.Key) > 0f &&
                                                        pair.Value.Sum(actor => actor.GetOrCreate()
                                                            .economic_income_previous_year) <= 0 &&
                                                        !IsPrivilegedHousehold(pair.Value))
            .Select(pair => pair.Key).ToList();
        foreach (KeyValuePair<string, List<Actor>> buyerHousehold in households
                     .Where(pair => pair.Value.Any(actor => actor.GetOrCreate().is_economic_merchant))
                     .OrderByDescending(pair => SelectRepresentative(pair.Value)?.money ?? 0))
        {
            if (sellers.Count == 0) break;
            Actor buyer = SelectRepresentative(buyerHousehold.Value);
            if (buyer == null || buyer.money < LandPurchasePrice) continue;
            string sellerKey = sellers[0];
            sellers.RemoveAt(0);
            Actor seller = ResolveRepresentative(data, sellerKey);
            float amount = Math.Min(HouseholdBaseShare, GetHouseholdShare(city, sellerKey));
            if (amount <= 0f) continue;
            buyer.addMoney(-LandPurchasePrice);
            seller?.addMoney(LandPurchasePrice);
            data.household_land_shares[sellerKey] = Mathf.Max(0f,
                GetHouseholdShare(city, sellerKey) - amount);
            data.household_land_shares[buyerHousehold.Key] =
                GetHouseholdShare(city, buyerHousehold.Key) + amount;
            data.household_land_representatives[buyerHousehold.Key] = buyer.id;
        }
    }

    private static void TryMoveLandlessHousehold(City city, Dictionary<string, List<Actor>> households)
    {
        if (!IsLandMarketOpen(city?.kingdom)) return;
        if (city.kingdom.GetOrCreate().fugitive_household_law_enacted || city.kingdom.HasLaw(LawType.逃人法)) return;
        CityExtension.CityExtraData data = EnsureData(city);
        if (data.household_land_shares.Values.Sum() + HouseholdBaseShare <= GetPrivateLandCapacity(city)) return;
        KeyValuePair<string, List<Actor>> landless = households.FirstOrDefault(pair =>
            GetHouseholdShare(city, pair.Key) <= 0f && !IsPrivilegedHousehold(pair.Value));
        if (string.IsNullOrEmpty(landless.Key)) return;
        City destination = city.kingdom.cities?.Where(candidate => candidate != null && candidate != city &&
                !candidate.isRekt() && IsLandMarketOpen(candidate.kingdom) &&
                EnsureData(candidate).household_land_shares.Values.Sum() + HouseholdBaseShare <=
                GetPrivateLandCapacity(candidate))
            .OrderBy(candidate => Vector2.SqrMagnitude(candidate.city_center - city.city_center)).FirstOrDefault();
        if (destination == null) return;
        foreach (Actor actor in landless.Value.ToList()) actor.joinCity(destination);
    }

    private static void RefreshClasses(City city)
    {
        foreach (Actor actor in city.units?.Where(IsLivingResident).ToList() ?? new List<Actor>())
            actor.SetSocialClass(AI.ActorAI.EmpireCaftActorJudgeClass.JudgeClass(actor));
    }

    private static float CalculateLandlessPopulationRatio(City city)
    {
        if (city == null || !IsLandMarketOpen(city.kingdom)) return 0f;
        List<Actor> population = city.units?.Where(IsLivingResident).ToList() ?? new List<Actor>();
        if (population.Count == 0) return 0f;
        int landless = population.Count(actor => IsAgrarianCommoner(actor) &&
                                                 GetHouseholdShare(city, GetHouseholdKey(actor)) <= 0f);
        return (float)landless / population.Count;
    }

    private static bool IsAgrarianCommoner(Actor actor)
    {
        if (actor == null || actor.IsOnOffice() || actor.isWarrior() ||
            actor.GetOrCreate().is_economic_merchant || IsLandlord(actor)) return false;
        if (actor.GetOrCreate().socialClass == SocialClass.Noble) return false;
        string job = actor.citizen_job?.id ?? "";
        return job != "woodcutter" && job != "miner" && job != "miner_deposit" &&
               job != "road_builder" && job != "cleaner" && job != "manure_cleaner";
    }

    private static bool TryStartPeasantLandRebellion(City city, float ratio)
    {
        if (ratio < RebellionLandlessThreshold || city == null || city.kingdom == null ||
            city.kingdom.isRekt() || city.kingdom.getWars().Any() || city == city.kingdom.capital) return false;
        CityExtension.CityExtraData data = EnsureData(city);
        if (data.last_land_rebellion_timestamp >= 0d &&
            Date.getYearsSince(data.last_land_rebellion_timestamp) < RebellionCooldownYears) return false;
        Actor leader = city.units?.Where(actor => IsLivingResident(actor) && IsAgrarianCommoner(actor))
            .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
        if (leader == null) return false;
        Kingdom origin = city.kingdom;
        Kingdom rebel = city.makeOwnKingdom(leader, pRebellion: true);
        if (rebel == null || !rebel.StartLocalRebelling(EmpireWarType.地方叛乱)) return false;
        rebel.data.name = string.Format(LM.Get("land_rebel_kingdom_name"), city.GetCityName());
        War war = World.world.diplomacy.startWar(rebel, origin, WarTypeLibrary.rebellion);
        if (war == null)
        {
            rebel.EndLocalRebelling();
            return false;
        }
        war.SetEmpireWarType(EmpireWarType.地方叛乱);
        string cause = string.Format(LM.Get("land_rebellion_cause"), city.GetCityName(), ratio * 100f);
        war.data.name = LM.Get("land_rebellion_war_name");
        MarkPeasantSocialRebellion(war, rebel, origin, city, ratio, cause);
        data.last_land_rebellion_timestamp = World.world.getCurWorldTime();
        string history = string.Format(LM.Get("land_rebellion_started_history"), city.GetCityName(),
            ratio * 100f, cause);
        RecordRebellionHistory(origin, history, leader);
        ActionLibrary.showWhisperTip(history);
        return true;
    }

    private static bool TryJoinAdjacentPeasantRebellion(City city, float ratio)
    {
        if (city == null || ratio < ContagionLandlessThreshold || city.neighbours_cities == null) return false;
        foreach (City neighbour in city.neighbours_cities)
        {
            Kingdom rebel = neighbour?.kingdom;
            KingdomExtension.KingdomExtraData rebelData = rebel?.GetOrCreate();
            if (rebelData?.is_peasant_land_rebellion != true || rebelData.peasant_land_rebellion_war_id <= 0) continue;
            War war = World.world?.wars?.get(rebelData.peasant_land_rebellion_war_id);
            if (war == null || war.hasEnded() || !war.isDefender(city.kingdom)) continue;
            city.joinAnotherKingdom(rebel, pCaptured: true, pRebellion: true);
            string history = string.Format(LM.Get("land_rebellion_city_joined_history"), city.GetCityName(),
                ratio * 100f, rebel.GetKingdomName());
            RecordRebellionHistory(rebel, history);
            ActionLibrary.showWhisperTip(history);
            return true;
        }
        return false;
    }

    private static void RecordRebellionHistory(Kingdom kingdom, string content, Actor actor = null)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire != null)
            empire.RecordHistory(directContent: content, actorId: actor?.id ?? kingdom.king?.id ?? -1L,
                kingdomId: kingdom.id);
        (actor ?? kingdom?.king)?.RecordPersonalHistory(content, "peasant_land_rebellion");
    }

    private static float GetHouseholdShare(City city, string key)
    {
        if (city == null || string.IsNullOrEmpty(key)) return 0f;
        return EnsureData(city).household_land_shares.TryGetValue(key, out float share) ? share : 0f;
    }

    private static Actor ResolveRepresentative(CityExtension.CityExtraData data, string key)
    {
        return data.household_land_representatives.TryGetValue(key, out long id)
            ? World.world?.units?.get(id)
            : null;
    }

    private static string ResolveHouseholdName(City city, string key)
    {
        CityExtension.CityExtraData data = EnsureData(city);
        Actor representative = ResolveRepresentative(data, key);
        if (representative == null) return LM.Get("city_land_unknown_household");
        string actorName = representative.getName();
        if (representative.hasFamily() && !string.IsNullOrWhiteSpace(representative.family.data?.name) &&
            !string.Equals(actorName, representative.family.data.name, StringComparison.Ordinal))
            return $"{actorName} ({representative.family.data.name})";
        return actorName;
    }

    private static string GetCrownName(City city)
    {
        Actor ruler = city?.kingdom?.GetEmpire()?.Emperor ?? city?.kingdom?.king;
        return ruler == null
            ? LM.Get("city_land_crown")
            : string.Format(LM.Get("city_land_crown_holder"), ruler.getName());
    }

    private static float GetCrownShare(City city)
    {
        if (city?.kingdom == null || city.kingdom.GetOrCreate().is_peasant_revolutionary_government) return 0f;
        Empire empire = city.kingdom.GetEmpire();
        return empire?.CoreKingdom == city.kingdom ? 50f : 20f;
    }

    private static float GetNobleShare(City city)
    {
        if (city?.kingdom == null || city.kingdom.GetOrCreate().is_peasant_revolutionary_government) return 0f;
        return IsLandMarketOpen(city.kingdom) ? 20f : 100f - GetCrownShare(city);
    }

    private static float GetPrivateLandCapacity(City city)
    {
        return Mathf.Max(0f, 100f - GetCrownShare(city) - GetNobleShare(city));
    }

    private static void TrimPrivateSharesToCapacity(City city, CityExtension.CityExtraData data)
    {
        float capacity = GetPrivateLandCapacity(city);
        float used = data.household_land_shares.Values.Where(value => value > 0f).Sum();
        if (used <= capacity || used <= 0f) return;
        float scale = capacity / used;
        foreach (string key in data.household_land_shares.Keys.ToList())
            data.household_land_shares[key] = Mathf.Max(0f, data.household_land_shares[key] * scale);
    }
}

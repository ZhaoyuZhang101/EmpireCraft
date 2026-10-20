using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 帝国的成员构成(封建制帝国)：
//   · 帝国自由城市：诸侯领内人口最多的非都城，得皇帝特许状脱离诸侯，自立为一城一国(市议会推举执政，不世袭)，
//     直属皇帝，每年把一成年度经常收入缴给中央，在帝国议会城市院有席位；数目不超过成员国的三分之一(至少可有两座)；
//     中央缺钱(国库不足一成年收)时，皇帝可把自由城市典押给最有钱的诸侯，城就并入那个诸侯国(原型"典押")，
//     记下押价，三十年内可赎回(见 Immediacy)；
//   · 教会诸侯(主教领)：皇帝把中央一座非都城的城封给主教(原型奥托王朝的帝国教会)，每年一成可能；
//     中央三城以上、帝国六城以上，每四家成员至多一家、最多三家(见 ChurchBlocker)；
//     主教不世袭，出缺时按叙任权另派——教会叙任由教廷派、政教协约由本地座堂教士推举、君主叙任由皇帝派自己人；
//     选帝侯团的三个教会席位先给主教领的主教(见 EmpireCraftEmpireBehCheckCabinet)。
public static partial class HolyRomanSystem
{
    public const int EstateNone = 0, EstateFreeCity = 1, EstateChurch = 2;
    private const float FreeCityChance = 0.12f, ChurchChance = 0.1f, PawnChance = 0.25f;
    private const int MaxChurchPrinces = 3, MinFreeCityCap = 2, ChurchMinCoreCities = 3, ChurchMinEmpireCities = 6;
    private const float FreeCityTax = 0.1f, PawnYears = 3f;

    public static int Estate(Kingdom kingdom) => kingdom?.data == null ? EstateNone : kingdom.GetOrCreate().hre_estate;
    public static bool IsFreeCity(Kingdom kingdom) => Estate(kingdom) == EstateFreeCity;
    public static bool IsChurchEstate(Kingdom kingdom) => Estate(kingdom) == EstateChurch;

    // 投票时算教会诸侯的：主教领，以及教宗国、教区
    public static bool IsEcclesiastical(Kingdom kingdom) =>
        IsChurchEstate(kingdom) || kingdom?.data != null &&
        kingdom.GetKingdomType() is KingdomType.Feudalism_papal_state or KingdomType.Feudalism_diocese;

    private static void MembersYearly(Empire empire, HolyRomanState state)
    {
        RepairEstates(empire);
        TryCharterFreeCity(empire);
        TryEndowChurch(empire);
        FreeCityTaxes(empire);
        TryPawnFreeCity(empire);
    }

    // 旧档修补：自由城市、主教领以前税级被设成"无"(一分税收不上)，改回"低"(直接写选项：议会掌握征税权时
    // SetTaxLevel 会被否决，这是修补不是改税)；自由城市的市长被推举为皇帝、整座城成了中央的，中央不再算自由城市
    private static void RepairEstates(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        if (Estate(core) != EstateNone) core.GetOrCreate().hre_estate = EstateNone;
        foreach (Kingdom member in Members(empire))
        {
            if (Estate(member) == EstateNone) continue;
            Regime regime = member.GetRegime();
            if (regime?.options != null && regime.options.TryGetValue("option_tax_level", out int[] tax) && tax != null &&
                tax.Length > 0 && tax[0] == (int)TaxLevel.None)
                tax[0] = (int)TaxLevel.Low;
        }
    }

    #region 自由城市

    private static void TryCharterFreeCity(Empire empire)
    {
        List<Kingdom> members = Members(empire);
        int cap = Math.Max(MinFreeCityCap, members.Count / 3);
        if (members.Count(IsFreeCity) >= cap || UnityEngine.Random.value >= FreeCityChance) return;
        City city = FreeCityCandidate(empire);
        if (city != null) CharterFreeCity(empire, city);
    }

    public static Kingdom ActCharterFreeCity(Empire empire)
    {
        City city = HasDiet(empire) ? FreeCityCandidate(empire) : null;
        return city == null ? null : CharterFreeCity(empire, city);
    }

    // 候选：三城以上的普通成员国(含中央)里，人口最多的非都城
    private static City FreeCityCandidate(Empire empire) =>
        empire.kingdoms_list
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.GetEmpire() == empire &&
                              kingdom.cities.Count >= 3 && Estate(kingdom) == EstateNone && !IsBanned(kingdom) &&
                              !kingdom.isInWarWith(empire.CoreKingdom))
            .SelectMany(kingdom => kingdom.cities.Where(item => item?.data != null && !item.isRekt() && item != kingdom.capital &&
                                                                !EnfeoffmentHelper.IsRealmCapital(item)))
            .OrderByDescending(item => item.getPopulationPeople()).FirstOrDefault();

    public static Kingdom CharterFreeCity(Empire empire, City city)
    {
        Kingdom former = city?.kingdom;
        if (former == null || former.GetEmpire() != empire || city == former.capital) return null;
        Actor leader = Burgher(city, former.king);
        if (leader == null) return null;
        string cityName = city.GetCityName();
        Kingdom free = SpinOff(empire, city, leader, LeaderSelectMethod.Vote, EstateFreeCity);
        if (free == null) return null;
        Record(empire, string.Format(LM.Get("hre_free_city_history"), cityName, former.GetKingdomName()));
        return free;
    }

    // 市民领袖：商人优先，其次城中其他成年人，按威望
    private static Actor Burgher(City city, Actor exclude)
    {
        List<Actor> locals = (city.units ?? new List<Actor>()).ToList()
            .Where(actor => actor?.data != null && actor != exclude && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                            !actor.isKing() && actor.isUnitFitToRule()).ToList();
        return locals.Where(actor => actor.GetOrCreate().socialClass == SocialClass.Merchant)
                   .OrderByDescending(actor => actor.data.renown).FirstOrDefault()
               ?? locals.OrderByDescending(actor => actor.data.renown).FirstOrDefault()
               ?? CityPopulationSystem.SpawnOfficial(city, city.kingdom);
    }

    // 一城立为帝国成员：不带法理头衔、用城名，不世袭，不给中央出兵
    private static Kingdom SpinOff(Empire empire, City city, Actor ruler, LeaderSelectMethod method, int estate)
    {
        if (ruler.city != city) ruler.joinCity(city);
        Kingdom realm = city.makeOwnKingdom(ruler);
        if (realm?.data == null) return null;
        realm.SetRegimeType(RegimeType.Feudalism);
        realm.LoadRegime();
        Regime regime = realm.GetRegime();
        if (regime != null)
        {
            regime.SetLeaderSelectMethod(method);
            regime.SetAllowSupportCenterArmy(false);
            regime.SetAllowDiplomacy(false);
            // 税级是本国向百姓收税的税率(见 KingdomExtension.GetTaxRate)，不是向帝国的贡赋：以前设成"无"，
            // 自由城市一分税收不上，年收从几十掉到 0、年年废债重组(玩家日志)。向帝国缴的是税款分成与每年一成(见 FreeCityTaxes)
            regime.SetTaxLevel(TaxLevel.Low);
            if (estate == EstateChurch) regime.SetReligionLevel(ReligionLevel.High);
        }
        if (realm.HasMainTitle()) realm.RemoveMainTitle();
        string name = city.GetCityName();
        realm.data.name = name;
        realm.SetKingdomCoreName(name, name);
        realm.GetOrCreate().hre_estate = estate;
        realm.SetFiedTimestamp(World.world.getCurWorldTime());
        empire.join(realm, true, true);
        return realm;
    }

    // 自由城市只给帝国缴税：本城税款照封建成员的分成交中央(见 TreasurySystem.TaxShares)，另每年缴一成年收入。
    // 这一成只从可用资金里出(扣掉安全储备)，不为它动用储备、举债
    private static void FreeCityTaxes(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        foreach (Kingdom free in Members(empire).Where(IsFreeCity))
        {
            if (free.isInWarWith(core)) continue;
            int amount = (int)Math.Min(StateSettlementSystem.DiscretionaryFunds(free),
                (long)(StateDebtSystem.AnnualStableIncome(free) * FreeCityTax));
            if (amount <= 0) continue;
            free.SubMoney(amount, TreasuryCategory.Tribute);
            core.AddMoney(amount, TreasuryCategory.Tribute);
        }
    }

    // 典押：中央国库不足一成年收时，把一座自由城市押给出得起钱(三年城市收入)的最富诸侯
    private static void TryPawnFreeCity(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        if (core.GetMoney() >= StateDebtSystem.AnnualStableIncome(core) / 10 || UnityEngine.Random.value >= PawnChance) return;
        PawnFreeCity(empire);
    }

    public static bool PawnFreeCity(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        foreach (Kingdom free in Members(empire).Where(IsFreeCity).OrderBy(item => item.id))
        {
            City city = free.capital;
            if (city == null) continue;
            int price = (int)Math.Max(100L, (long)(StateDebtSystem.AnnualStableIncome(free) * PawnYears));
            Kingdom buyer = Members(empire)
                .Where(member => Estate(member) == EstateNone && !IsBanned(member) && member.GetMoney() >= price &&
                                 !member.isInWarWith(core))
                .OrderByDescending(member => member.IsNeighbourWith(free))
                .ThenByDescending(member => member.GetMoney()).FirstOrDefault();
            if (buyer == null) continue;
            string cityName = city.GetCityName();
            buyer.SubMoney(price, TreasuryCategory.Other);
            core.AddMoney(price, TreasuryCategory.Other);
            foreach (City item in free.cities.ToList()) item.joinAnotherKingdom(buyer);
            if (city.kingdom == buyer) NotePawn(State(empire), city, buyer, price);
            Record(empire, string.Format(LM.Get("hre_pawn_history"), cityName, buyer.GetKingdomName(), MoneyDisplay.Format(price)));
            return true;
        }
        return false;
    }

    #endregion

    #region 教会诸侯

    private static void TryEndowChurch(Empire empire)
    {
        if (ChurchBlocker(empire) != null || UnityEngine.Random.value >= ChurchChance) return;
        City city = ChurchCandidate(empire);
        if (city != null) EndowChurch(empire, city);
    }

    // 封不出主教领的缘由(年度诊断也用)，能封时为 null。以前要中央五城以上——选帝制下中央就是当选者的自家领国，
    // 多半一到三城，从来封不出(玩家日志 0 次)。现在中央三城以上(封后还剩两城)、帝国合计六城以上即可；
    // 每四家成员至多一家主教领(至少一家、至多三家)
    public static string ChurchBlocker(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        if (!core.hasReligion()) return "中央没有国教";
        int coreCities = core.cities?.Count ?? 0, total = empire.countCities();
        if (coreCities < ChurchMinCoreCities) return $"中央{coreCities}城，要{ChurchMinCoreCities}城";
        if (total < ChurchMinEmpireCities) return $"帝国共{total}城，要{ChurchMinEmpireCities}城";
        List<Kingdom> members = Members(empire);
        int cap = Mathf.Clamp(members.Count / 4, 1, MaxChurchPrinces), have = members.Count(IsChurchEstate);
        if (have >= cap) return $"已有{have}家，上限{cap}家";
        return ChurchCandidate(empire) == null ? "中央没有可封的非都城" : null;
    }

    public static Kingdom ActEndowChurch(Empire empire)
    {
        City city = HasDiet(empire) && empire.CoreKingdom.cities.Count >= 2 ? ChurchCandidate(empire) : null;
        return city == null ? null : EndowChurch(empire, city);
    }

    // 中央的非都城里挑一座不大不小的(最小的那座留给中央)
    private static City ChurchCandidate(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        List<City> cities = core.cities.Where(item => item?.data != null && !item.isRekt() && item != core.capital &&
                                                      !EnfeoffmentHelper.IsRealmCapital(item))
            .OrderBy(item => item.zones?.Count ?? 0).ToList();
        return cities.Count > 1 ? cities[1] : cities.FirstOrDefault();
    }

    public static Kingdom EndowChurch(Empire empire, City city)
    {
        if (city?.kingdom == null || city.kingdom.GetEmpire() != empire || city == city.kingdom.capital) return null;
        Actor bishop = ChooseBishop(empire, city, out string appointer);
        if (bishop == null) return null;
        string cityName = city.GetCityName();
        Kingdom church = SpinOff(empire, city, bishop, LeaderSelectMethod.Exam, EstateChurch);
        if (church == null) return null;
        if (!bishop.hasTrait("cleric")) bishop.addTrait("cleric");
        Record(empire, string.Format(LM.Get("hre_church_endowed_history"), empire.Emperor?.getName() ?? empire.CoreKingdom.GetKingdomName(),
            cityName, bishop.getName()));
        return church;
    }

    // 主教出缺(EmpireCraftKingdomBehCheckKing 在主教领没有君主时调用)：按叙任权另派
    public static bool TryAppointBishop(Kingdom church)
    {
        try
        {
            if (!IsChurchEstate(church) || church.hasKing() || church.capital == null) return false;
            Empire empire = church.GetEmpire();
            if (empire == null)
            {
                // 离开帝国(独立、被并)后就是普通的国家
                church.GetOrCreate().hre_estate = EstateNone;
                return false;
            }
            Actor bishop = ChooseBishop(empire, church.capital, out string appointer);
            if (bishop == null) return false;
            if (bishop.city != church.capital) bishop.joinCity(church.capital);
            GraceEdictService.Crown(church, bishop);
            if (church.king != bishop) return false;
            if (!bishop.hasTrait("cleric")) bishop.addTrait("cleric");
            Record(empire, string.Format(LM.Get("hre_bishop_appointed_history"), appointer, bishop.getName(), church.GetKingdomName()));
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 任命主教失败: {exception.Message}");
            return false;
        }
    }

    // 人选：教会叙任由教廷派(教廷都城里的人)，君主叙任由皇帝派(帝都里的人，同族优先)，政教协约由本地推举；
    // 有教士特质的优先，再按威望
    private static Actor ChooseBishop(Empire empire, City seat, out string appointer)
    {
        int investiture = PapacySystem.Applies(empire) ? PapacySystem.Investiture(empire) : PapacySystem.InvestitureCrown;
        Religion religion = PapacySystem.ReligionOf(empire);
        Kingdom papacy = PapacySystem.Papacy(religion);
        Actor emperor = empire.Emperor;
        City source = seat;
        appointer = LM.Get("hre_appointer_chapter");
        if (investiture == PapacySystem.InvestitureChurch && papacy?.capital != null)
        {
            source = papacy.capital;
            appointer = PapacySystem.Pope(religion)?.getName() ?? papacy.GetKingdomName();
        }
        else if (investiture == PapacySystem.InvestitureCrown && empire.CoreKingdom.capital != null)
        {
            source = empire.CoreKingdom.capital;
            appointer = emperor?.getName() ?? empire.CoreKingdom.GetKingdomName();
        }
        Actor pick = Candidates(source).OrderByDescending(actor => actor.hasTrait("cleric"))
            .ThenByDescending(actor => investiture == PapacySystem.InvestitureCrown && SameHouse(actor, emperor))
            .ThenByDescending(actor => actor.data.renown).FirstOrDefault();
        if (pick == null && source != seat)
        {
            pick = Candidates(seat).OrderByDescending(actor => actor.hasTrait("cleric"))
                .ThenByDescending(actor => actor.data.renown).FirstOrDefault();
            appointer = LM.Get("hre_appointer_chapter");
        }
        return pick ?? CityPopulationSystem.SpawnOfficial(seat, seat.kingdom);
    }

    private static IEnumerable<Actor> Candidates(City city) =>
        (city?.units ?? new List<Actor>()).ToList().Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() &&
                                                                    actor.isAdult() && !actor.isKing() && !actor.isCityLeader() &&
                                                                    actor.isUnitFitToRule());

    // 选帝侯团的教会席位：主教领的主教优先(最多三席)
    public static List<long> ChurchElectors(Empire empire) =>
        Members(empire).Where(IsChurchEstate).Where(kingdom => kingdom.king?.data != null && !IsBanned(kingdom))
            .OrderByDescending(kingdom => kingdom.cities.Count).Select(kingdom => kingdom.king.id).Take(MaxChurchPrinces).ToList();

    #endregion
}

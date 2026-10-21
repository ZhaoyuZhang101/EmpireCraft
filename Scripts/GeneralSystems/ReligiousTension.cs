using System;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗教影响稳定(用户 2026-10-19 选"宗教影响稳定"；以前宗教与稳定、情绪无关，宗教叛乱从没发生过)：
//   · 城里通行的宗教与国教(国家的宗教)不同的是异教之城，按国家的宗教政策：
//       宽容(特性 religious_tolerance：游牧宗教宽容、罗马世俗化)——稳定度少 3，民族情绪不受影响；
//       收人头税(特性 poll_tax：阿拉伯人丁税制)——稳定度少 6，情绪每年 +1，每座异教之城每年多交一笔人头税；
//         交了人头税就豁免兵役：异教之城的人口只征 DhimmiLevy(同野人，见 CorpsSystem.Mobilize)，打下异教之地添税不添兵；
//       不宽容(其余)——稳定度少 10，情绪每年 +2；
//   · 宗教叛乱：不宽容的国家，异教之城稳定度低于起事线时每年有半成机会举教起事(新国奉本城之教，十年内同城不再起事)；
//   · 圣战(见 EmpireCraftKingdomBehCheckPlots.CheckJoinReligionWar)：同教的国家要对发起者有好感、不与被攻者同属一个帝国、
//     都城离被攻者都城不太远，一场圣战攻方最多六国。
public static class ReligiousTension
{
    public const string ToleranceFeature = "religious_tolerance", PollTaxFeature = "poll_tax";
    public const int MaxHolyWarAttackers = 6;
    // 收人头税的国家，异教之城的人口征兵只征几成
    public const float DhimmiLevy = 0.25f;
    public const float HolyWarReach = 160f;
    // 每年两成 → 半成(玩家 2026-10-21 反馈"除了阿拉伯和游牧，其他国家很容易分裂"：西欧、华夏长期不宽容，异教城一乱就起事)
    private const float RevoltChance = 0.05f;
    private const int RevoltCooldownYears = 10;
    private static double _lastScan = -1d;

    public enum Policy { Intolerant, PollTax, Tolerant }

    public static void ResetWorldState() => _lastScan = -1d;

    public static bool IsHeterodox(City city)
    {
        if (city?.data == null || city.isRekt() || city.kingdom?.data == null || city.kingdom.wild) return false;
        Religion state = city.kingdom.religion, local = city.religion;
        return state != null && local != null && state != local;
    }

    public static Policy PolicyOf(Kingdom kingdom)
    {
        string culture = CultureService.GetRealmCulture(kingdom);
        // 宽容、人头税是制度特性，照制度文化(旧制犹存的照旧制，见 LegacyInstitutionSystem)；法教传统是文化本身的
        string institutions = CultureService.GetInstitutionCulture(kingdom);
        // 法教传统(印度)向来宽容(见 PapacySystem.DharmicTrait)
        if (InstitutionSystem.HasFeature(institutions, ToleranceFeature) ||
            EmpireCraft.Scripts.Data.OnomasticsRule.HasPoliticalTrait(culture, PapacySystem.DharmicTrait)) return Policy.Tolerant;
        return InstitutionSystem.HasFeature(institutions, PollTaxFeature) ? Policy.PollTax : Policy.Intolerant;
    }

    // 异教之城征兵打几折(见 CorpsSystem.Mobilize)：收人头税的国家豁免异教之民的兵役，只征 DhimmiLevy
    public static float LevyFactor(City city) =>
        IsHeterodox(city) && PolicyOf(city.kingdom) == Policy.PollTax ? DhimmiLevy : 1f;

    // 异教之城的稳定度损失(见 CityStabilitySystem.Natural)
    public static float StabilityLoss(City city) =>
        !IsHeterodox(city) ? 0f : PolicyOf(city.kingdom) switch
        {
            Policy.Tolerant => 3f,
            Policy.PollTax => 6f,
            _ => 10f
        };

    // 异教之城的民族情绪每年增长(见 NationalSentimentSystem)
    public static float SentimentGain(City city) =>
        !IsHeterodox(city) ? 0f : PolicyOf(city.kingdom) switch
        {
            Policy.Tolerant => 0f,
            Policy.PollTax => 1f,
            _ => 2f
        };

    public static string Describe(City city) =>
        string.Format(LM.Get("stability_religion_detail"), city.religion?.name ?? "", LM.Get("religion_policy_" + PolicyOf(city.kingdom)));

    // 每年一次：人头税、宗教叛乱
    public static void TryYearlyScan()
    {
        if (World.world?.cities == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (City city in World.world.cities.ToList())
        {
            try
            {
                if (!IsHeterodox(city)) continue;
                Policy policy = PolicyOf(city.kingdom);
                if (policy == Policy.PollTax) CollectPollTax(city);
                else if (policy == Policy.Intolerant) TryRevolt(city);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][宗教] {city?.data?.name} 结算失败: {exception.Message}");
            }
        }
        // 无小人模式：按背景人口创教、传教(见 BackgroundReligionSystem)
        try
        {
            BackgroundReligionSystem.YearlyPass();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宗教] 背景人口传教结算失败: {exception.Message}");
        }
    }

    private static void CollectPollTax(City city)
    {
        if (!TreasurySystem.Enabled(city.kingdom)) return;
        int tax = CityPopulationSystem.Households(city) / 10;
        if (tax > 0) city.kingdom.AddMoney(tax, TreasuryCategory.ResidentTax);
    }

    private static void TryRevolt(City city)
    {
        Kingdom origin = city.kingdom;
        if (city == origin.capital || origin.getWars().Any() || !RebellionSystem.CanAttempt(origin) ||
            !CityStabilitySystem.CanRise(city) || CityStabilitySystem.Effective(city) >= CityStabilityRules.RebellionThreshold ||
            !ModernStability.PassRebellionGate(origin) || UnityEngine.Random.value >= RevoltChance) return;
        var extra = city.GetOrCreate();
        if (extra.last_harsh_uprising >= 0d && Date.getYearsSince(extra.last_harsh_uprising) < RevoltCooldownYears) return;
        Religion faith = city.religion;
        Actor leader = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.SpawnRebelLeader(city) : null;
        leader ??= city.units?.Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                              !actor.isKing() && actor.CanFoundCivKingdom())
            .OrderByDescending(actor => actor.religion == faith).ThenByDescending(actor => actor.data?.renown ?? 0)
            .FirstOrDefault();
        if (leader == null) return;
        string cityName = city.GetCityName();
        RebellionCauseData cause = RebellionSystem.Capture(origin, city, "rebellion_reason_religion", Describe(city));
        Kingdom rebel = city.makeOwnKingdom(leader, pRebellion: true);
        if (rebel == null) return;
        if (!rebel.StartLocalRebelling(EmpireWarType.宗教叛乱))
        {
            RebellionStartupService.RollbackCitySplit(city, origin, rebel);
            return;
        }
        rebel.setReligion(faith);
        rebel.data.name = string.Format(LM.Get("religious_revolt_kingdom_name"), cityName);
        War war = World.world.diplomacy.startWar(rebel, origin, WarTypeLibrary.rebellion);
        if (war == null)
        {
            RebellionStartupService.RollbackCitySplit(city, origin, rebel);
            return;
        }
        war.SetEmpireWarType(EmpireWarType.宗教叛乱);
        RebellionSystem.Record(rebel, war, cause);
        war.data.name = string.Format(LM.Get("religious_revolt_war_name"), cityName);
        RebellionStartupService.RaiseUprisingMilitia(rebel, 0.5f);
        extra.last_harsh_uprising = World.world.getCurWorldTime();
        string history = string.Format(LM.Get("religious_revolt_history"), cityName, faith?.name ?? "");
        Empire empire = origin.GetEmpire();
        if (empire != null) EventRecorder.Record(empire, history, leader, origin);
        else EventRecorder.Record(origin, history, leader);
    }

    // 圣战参战：没满员、不与被攻者同属一个帝国、都城离被攻者都城不太远
    public static bool CanJoinHolyWar(Kingdom kingdom, War war)
    {
        Kingdom defender = war?.main_defender;
        if (kingdom?.capital == null || defender?.capital == null || defender.isRekt()) return false;
        if (war._list_attackers.Count >= MaxHolyWarAttackers || kingdom.IsInSameEmpire(defender)) return false;
        return Vector2.Distance(kingdom.capital.city_center, defender.capital.city_center) <= HolyWarReach;
    }
}

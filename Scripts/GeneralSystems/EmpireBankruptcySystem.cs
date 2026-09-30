using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国库亏空(帝国核心国库 <0)：朝廷发不出军饷。
//   · 军饷断绝：除京师(帝国核心王国的首都)外，各城兵额为 0，地方驻军就地解散(见 CityPatch 的兵额补丁)；
//   · 地方离心：每年亏空越久，成员国越容易离开——
//       军府(节度使)与自主性强的(可自募军队、自办外交、不供养中央军)拥兵自立，打独立战争；
//       自主性弱的就近投靠正在与朝廷交战的叛军，举地归附；附近没有叛军的才偶尔自立；
//   · 正统逐年流失，拖得越久流失越快。
// 国库转正后亏空结束。每年由 ConstitutionalEconomySystem 的年度结算调用。
public static class EmpireBankruptcySystem
{
    private const float BaseChance = 0.05f;
    private const float ChancePerYear = 0.04f;
    private const float MaxChance = 0.75f;
    private const float JiedushiFactor = 2f;
    private const float AutonomousFactor = 1.5f;
    private const float StrandedFactor = 0.25f;
    private const float MaxLossShare = 0.5f;
    private const float NearbyRebelDistance = 60f;

    public static bool IsBankrupt(Empire empire) =>
        empire?.CoreKingdom != null && !empire.CoreKingdom.isRekt() && empire.CurrentMoney < 0;

    // 亏空期间只有京师养得起兵
    public static bool IsUnpaidGarrison(City city)
    {
        try
        {
            Kingdom kingdom = city?.kingdom;
            if (kingdom == null || kingdom.isRekt()) return false;
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.isRekt() || !IsBankrupt(empire)) return false;
            return city != empire.CoreKingdom.capital;
        }
        catch
        {
            return false;
        }
    }

    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (state == null || empire?.CoreKingdom == null || World.world == null) return;
        if (!IsBankrupt(empire))
        {
            if (state.bankrupt_since >= 0d)
                EventRecorder.Record(empire, string.Format(LM.Get("bankruptcy_end_history"), empire.GetEmpireFullName()));
            state.bankrupt_since = -1d;
            return;
        }
        if (state.bankrupt_since < 0d)
        {
            state.bankrupt_since = World.world.getCurWorldTime();
            EventRecorder.Record(empire, string.Format(LM.Get("bankruptcy_start_history"), empire.GetEmpireFullName()));
            return;
        }
        int years = Date.getYearsSince(state.bankrupt_since);
        empire.AddMandate(-Mathf.Min(5, 1 + years / 5));
        try
        {
            Disintegrate(empire, years);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 国库亏空离心失败: {exception.Message}");
        }
    }

    private static void Disintegrate(Empire empire, int years)
    {
        Kingdom core = empire.CoreKingdom;
        float chance = Mathf.Min(MaxChance, BaseChance + ChancePerYear * years);
        List<Kingdom> rebels = RebellionSnowballSystem.FindRebels(empire, core).ToList();
        List<Kingdom> members = empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt() &&
                kingdom != core && !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling() &&
                !kingdom.getWars().Any())
            .OrderBy(_ => UnityEngine.Random.value).ToList();
        int budget = Math.Max(1, Mathf.CeilToInt(members.Count * MaxLossShare));
        int lost = 0;
        foreach (Kingdom member in members)
        {
            if (lost >= budget || empire.isRekt() || core.isRekt()) break;
            bool jiedushi = member.GetKingdomType() == KingdomType.LvLing_jiedushi;
            bool autonomous = member.hasKing() && (jiedushi || IsAutonomous(member));
            Kingdom rebel = autonomous ? null : FindNearbyRebel(member, rebels);
            float factor = jiedushi ? JiedushiFactor
                : autonomous ? AutonomousFactor
                : rebel != null ? 1f
                : member.hasKing() ? StrandedFactor : 0f;
            if (UnityEngine.Random.value >= chance * factor) continue;
            if (rebel != null ? Defect(empire, member, rebel) : DeclareIndependence(empire, core, member)) lost++;
        }
    }

    // 自主性强：可自募军队、自办外交、不供养中央军
    private static bool IsAutonomous(Kingdom kingdom)
    {
        Regime regime = kingdom.GetRegime();
        return regime != null && regime.IsAllowArmy() && regime.IsAllowDiplomacy() && !regime.IsAllowSupportCenterArmy();
    }

    // 与本国接壤的叛军优先，其次是首都相距不远的
    private static Kingdom FindNearbyRebel(Kingdom member, List<Kingdom> rebels)
    {
        if (rebels.Count == 0 || member.cities == null) return null;
        Kingdom bordering = rebels.FirstOrDefault(rebel => !rebel.isRekt() && member.cities.Any(city =>
            city?.neighbours_cities != null && city.neighbours_cities.Any(other => other?.kingdom == rebel)));
        if (bordering != null) return bordering;
        WorldTile home = member.capital?.getTile(false);
        if (home == null) return null;
        return rebels.Where(rebel => !rebel.isRekt() && rebel.capital?.getTile(false) != null)
            .Select(rebel => (rebel, distance: Toolbox.DistTile(home, rebel.capital.getTile(false))))
            .Where(pair => pair.distance <= NearbyRebelDistance)
            .OrderBy(pair => pair.distance).Select(pair => pair.rebel).FirstOrDefault();
    }

    private static bool Defect(Empire empire, Kingdom member, Kingdom rebel)
    {
        string name = member.GetKingdomFullName();
        City capital = member.capital;
        int moved = 0;
        foreach (City city in member.cities.ToList().OrderBy(city => city == capital ? 1 : 0))
        {
            if (city == null || city.isRekt()) continue;
            city.joinAnotherKingdom(rebel, pCaptured: true, pRebellion: true);
            if (city.kingdom == rebel) moved++;
        }
        if (moved == 0) return false;
        EventRecorder.Record(empire, actor: rebel.king, logKingdom: rebel, text: string.Format(
            LM.Get("bankruptcy_defect_history"), empire.GetEmpireFullName(), name, rebel.GetKingdomFullName()));
        return true;
    }

    private static bool DeclareIndependence(Empire empire, Kingdom core, Kingdom member)
    {
        if (!member.hasKing()) return false;
        empire.leave(member);
        War war = DiplomacyHelpers.wars.newWar(member, core, WarTypeLibrary.normal);
        war?.SetEmpireWarType(EmpireWarType.地方独立);
        EventRecorder.Record(empire, actor: member.king, logKingdom: member, text: string.Format(
            LM.Get("bankruptcy_independence_history"), empire.GetEmpireFullName(), member.GetKingdomFullName()));
        return true;
    }
}

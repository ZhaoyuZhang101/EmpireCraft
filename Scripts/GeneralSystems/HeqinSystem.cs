using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 和亲、互市、降部戍边(用户 2026-10-17 选"和亲互市与降部戍边"，定居王朝与游牧之间的往来)：
//   · 和亲：定居的君主帝国与接壤的游牧政权(游牧政体，或文化带 nomadic 特质)不在打仗、游牧一方国力不弱于己(八成以上)，
//     每年两成机会和亲——有合适的宗室女就嫁给对方的君主或储君(见 RoyalMarriageSystem.Heqin)，两国结为姻亲，
//     二十年互不开战(与西方联姻同一份婚约，见 RoyalMarriageSystem.Allied)；另赠陪嫁(国库一成，至多二百)；
//   · 互市：定居帝国与接壤的游牧一方是姻亲、或一方是另一方的朝贡国时，每年各得互市之利(接壤的城每座三，至多三十)；
//   · 降部戍边：游牧的独立小国(不是帝国、不超过三城)是施行了羁縻的定居帝国的朝贡国、与之接壤，每年一成机会内附——
//     并入帝国为羁縻之地(本地首领世守、少纳税，中央与接壤的敌国开战时随征，见 JimiSystem 的边防义务)。
public static class HeqinSystem
{
    public const string NomadicTrait = "nomadic";
    private const float HeqinChance = 0.2f, PowerFloor = 0.8f, SettleChance = 0.1f;
    private const int DowryCap = 200, MarketPerCity = 3, MarketCap = 30, SettleMaxCities = 3;
    private static double _lastScan = -1d;
    private static object _world;

    // 游牧政权：游牧政体，或文化带 nomadic 特质
    public static bool Nomadic(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() &&
        (kingdom.GetRegime()?.type == RegimeType.YouMu || CultureService.GetRealmCulture(kingdom).HasPoliticalTrait(NomadicTrait));

    // 定居的君主国(不是游牧、不是原始部落)
    private static bool Settled(Kingdom kingdom)
    {
        RegimeType? type = kingdom?.GetRegime()?.type;
        return kingdom?.data != null && !kingdom.isRekt() && kingdom.hasKing() && RegimeManager.IsMonarchy(type) &&
               !RegimeManager.IsPrimitive(type) && !Nomadic(kingdom);
    }

    // 每年一次(CityPatch 的世界扫描轮转)
    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.EMPIRE_MANAGER == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _lastScan = now;
            return;
        }
        if (_lastScan >= 0d && now >= _lastScan && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            try
            {
                Yearly(empire);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][和亲互市] 结算失败: {exception.Message}");
            }
        }
    }

    private static void Yearly(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived()) return;
        Kingdom core = empire.CoreKingdom;
        if (!Settled(core)) return;
        double ours = WarSituation.Power(empire);
        foreach (Kingdom other in Neighbours(empire))
        {
            if (!Nomadic(other) || core.isInWarWith(other)) continue;
            bool kin = RoyalMarriageSystem.Allied(core, other);
            bool tribute = other.GetTakenAllianceEmpire() == empire || core.GetTakenAllianceEmpire() == other.GetEmpire() && other.GetEmpire() != null;
            if (!kin && other.hasKing() && WarSituation.Power(other) >= ours * PowerFloor && UnityEngine.Random.value < HeqinChance)
                kin = Heqin(empire, core, other);
            if (kin || tribute) Market(empire, core, other);
            if (tribute && other.GetTakenAllianceEmpire() == empire) TrySettle(empire, other);
        }
    }

    // 帝国接壤的各家(独立国家、别的帝国按中央算)
    private static IEnumerable<Kingdom> Neighbours(Empire empire)
    {
        var seen = new HashSet<Kingdom>();
        foreach (Kingdom neighbour in empire.GetKingdomNeighbours())
        {
            Kingdom sovereign = EmpireCoreControl.Sovereign(neighbour);
            if (sovereign?.data == null || sovereign.isRekt() || sovereign.wild || sovereign == empire.CoreKingdom ||
                sovereign.GetEmpire() == empire || !seen.Add(sovereign)) continue;
            yield return sovereign;
        }
    }

    private static bool Heqin(Empire empire, Kingdom core, Kingdom nomad)
    {
        double now = World.world.getCurWorldTime();
        Actor princess = RoyalMarriageSystem.Heqin(core, nomad);
        core.GetOrCreate().marriage_pacts[nomad.id] = now;
        nomad.GetOrCreate().marriage_pacts[core.id] = now;
        int dowry = Math.Min(DowryCap, Math.Max(0, core.GetMoney()) / 10);
        if (dowry > 0)
        {
            core.SubMoney(dowry, TreasuryCategory.Gift);
            nomad.AddMoney(dowry, TreasuryCategory.Gift);
        }
        string text = princess != null
            ? string.Format(LM.Get("heqin_marry_history"), empire.GetEmpireName(), princess.getName(), nomad.GetKingdomName())
            : string.Format(LM.Get("heqin_history"), empire.GetEmpireName(), nomad.GetKingdomName());
        empire.RecordHistory(directContent: text, kingdomId: core.id);
        Empire nomadEmpire = nomad.GetEmpire();
        if (nomadEmpire != null && nomadEmpire != empire) nomadEmpire.RecordHistory(directContent: text, kingdomId: nomad.id);
        TranslateHelper.LogEventMessage(text, core);
        return true;
    }

    // 互市：接壤的城每座三，两边各得
    private static void Market(Empire empire, Kingdom core, Kingdom other)
    {
        int border = 0;
        foreach (City city in empire.cities_list)
        {
            if (city?.data == null || city.isRekt() || city.neighbours_cities == null) continue;
            if (city.neighbours_cities.Any(next => next?.kingdom != null && !next.isRekt() &&
                                                   EmpireCoreControl.Sovereign(next.kingdom) == other)) border++;
        }
        int value = Math.Min(MarketCap, border * MarketPerCity);
        if (value <= 0) return;
        core.AddMoney(value, TreasuryCategory.PublicTrade);
        other.AddMoney(value, TreasuryCategory.PublicTrade);
    }

    // 降部戍边：游牧的独立小国内附为羁縻之地
    private static void TrySettle(Empire empire, Kingdom nomad)
    {
        // 还在打仗的不内附(入帝国不会了结它的仗，会把仗带进来)
        if (!JimiSystem.Enacted(empire) || nomad.IsEmpire() || nomad.IsInEmpire() || nomad.hasEnemies() || nomad.getWars().Any() ||
            (nomad.cities?.Count ?? 0) > SettleMaxCities || UnityEngine.Random.value >= SettleChance) return;
        string name = nomad.GetKingdomName();
        empire.join(nomad, pForce: true);
        if (nomad.GetEmpire() != empire) return;
        var data = nomad.GetOrCreate();
        data.jimi_mode = (int)JimiMode.Jimi;
        data.last_kingdom_status_ts = -1d;
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(nomad);
        string text = string.Format(LM.Get("heqin_settle_history"), name, empire.GetEmpireName());
        empire.RecordHistory(directContent: text, kingdomId: nomad.id);
        TranslateHelper.LogEventMessage(text, nomad);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 2. 部落争草场(仿神罗私战"只争边城、不吞并、限期议和"，见 HolyRomanSystem.Feuds)：每年，相邻的两个游牧政权——
//    都是一方之主(独立的国或帝国核心)而不在同一帝国，或同一汗国的两个兀鲁思——为牧场打一场小仗(EmpireWarType.草场之争)：
//    · 开战率见 SteppeRules.FeudChance：两边草场压力(草原压力里牲畜粮食那一项)越大越容易打，攻方强出一半再加，比对方弱打折；
//      攻方兵力(国力)要有守方八成；部落阶段(原始政体)的不打(部落之间照部落联合的规矩)；同一对十年内不再打；
//      每国每年至多挑起一场，全天下每年至多 MaxFeudsPerYear 场；不在同一帝国的要过开战闸门(停战、同盟、岁币……见 WarGate)，
//      同一汗国的两个兀鲁思只看停战年限；
//    · 只争守方挨着攻方的非都城，至多两座(小的在前)，没有就打不起来；两家之间别的城打不下(见 BlocksPastureCity)；
//      不拉姻亲、不扩封建、不升级世界大战(见 HolyRomanSystem.ContainedWar)；
//    · 打满 FeudYears 年强制议和。攻方胜或议和：争的城里攻方占了一半以上地块的割给攻方；守方胜：攻方赔款、打下的争城退还；
//    · 劫掠被打退后反目的部与盟主之间也打这样一场(见 NomadRaidSystem.Hero)。
public static partial class SteppeOrderSystem
{
    private const int FeudYears = 2, FeudPairCooldownYears = 10, FeudMaxCities = 2, MaxFeudsPerYear = 4;
    private const float FeudMinStrength = 0.8f, FeudHeldShare = 0.5f;

    // 正在开争草场之战(startWar 前后)：开战钩子据此不拉姻亲、不扩封建、不升级世界大战
    public static bool Declaring;

    private static bool Alive(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && kingdom.hasCapital() && kingdom.cities?.Count > 0;

    private static bool IsRealm(Kingdom kingdom) =>
        Alive(kingdom) && (kingdom.GetEmpire() is not Empire empire || empire.CoreKingdom == kingdom);

    private static Kingdom LiveKingdom(long id)
    {
        Kingdom kingdom = id >= 0 ? World.world.kingdoms.get(id) : null;
        return kingdom?.data != null && !kingdom.isRekt() ? kingdom : null;
    }

    private static float YearsSince(double at) =>
        at < 0d || World.world.getCurWorldTime() < at ? float.MaxValue : Date.getYearsSince(at);

    // 兀鲁思：汗国(游牧帝国)里游牧政体的成员(核心、行省、羁縻不算)
    private static bool IsUlus(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (!Alive(kingdom) || empire?.CoreKingdom == null || empire.CoreKingdom == kingdom || !NomadRaidSystem.IsNomad(empire.CoreKingdom))
            return false;
        if (kingdom.GetKingdomType() == KingdomType.YouMu_province || JimiSystem.IsJimiAdministration(kingdom) ||
            JimiSystem.IsJimiRegion(kingdom, empire)) return false;
        return kingdom.GetRegime()?.type is RegimeType.YouMu or RegimeType.NomadFeudalism;
    }

    // 能为草场兴兵：游牧、走出部落阶段、有君、不在打仗、不在起事
    private static bool FeudCapable(Kingdom kingdom) =>
        Alive(kingdom) && NomadRaidSystem.IsNomad(kingdom) && !TribalFissionService.IsTribal(kingdom) && kingdom.hasKing() &&
        !kingdom.hasEnemies() && !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling();

    // 两家能不能争草场(不看机会)：同为一方之主而不在同一帝国(过开战闸门)，或同一汗国的两个兀鲁思(只看停战年限)
    private static bool FeudPair(Kingdom attacker, Kingdom defender)
    {
        if (attacker == defender || !FeudCapable(attacker) || !FeudCapable(defender)) return false;
        if (IsRealm(attacker) && IsRealm(defender))
            return !attacker.IsInSameEmpire(defender) && WarGate.CanDeclare(attacker, defender);
        if (!IsUlus(attacker) || !IsUlus(defender) || attacker.GetEmpire() != defender.GetEmpire()) return false;
        double lastWarEnded = World.world.diplomacy?.getRelation(attacker, defender)?.data?.timestamp_last_war_ended ?? -1d;
        return (lastWarEnded < 0d || Date.getYearsSince(lastWarEnded) >= SimGlobals.m.minimum_years_between_wars) &&
               attacker.king != defender.king && !RoyalMarriageSystem.Allied(attacker, defender);
    }

    private static bool FeudCooling(Kingdom a, Kingdom b)
    {
        Dictionary<long, double> record = NomadRaidSystem.State(a).feud_at;
        return record != null && record.TryGetValue(b.id, out double at) && YearsSince(at) < FeudPairCooldownYears;
    }

    // 争的城：守方挨着攻方的非都城，至多两座，小的在前
    private static List<City> Contested(Kingdom attacker, Kingdom defender) =>
        defender.cities.Where(city => city?.data != null && !city.isRekt() && city != defender.capital &&
                                      city.neighbours_cities?.Any(other => other?.kingdom == attacker) == true)
            .OrderBy(city => city.zones?.Count ?? 0).ThenBy(city => city.id).Take(FeudMaxCities).ToList();

    // 每年(NomadRaidSystem 算完草原压力后调用)
    private static void FeudsYearly(List<Kingdom> nomads)
    {
        int started = 0;
        foreach (Kingdom attacker in nomads.Where(FeudCapable).OrderBy(_ => UnityEngine.Random.value).ToList())
        {
            if (started >= MaxFeudsPerYear) return;
            if (!FeudCapable(attacker)) continue;
            double ours = attacker.GetNationalPower();
            float herd = NomadRaidSystem.State(attacker).herd;
            foreach (Kingdom defender in Neighbours(attacker).OrderBy(_ => UnityEngine.Random.value))
            {
                if (!FeudPair(attacker, defender) || FeudCooling(attacker, defender)) continue;
                double theirs = Math.Max(1d, defender.GetNationalPower());
                if (ours < theirs * FeudMinStrength) continue;
                float chance = SteppeRules.FeudChance(herd, NomadRaidSystem.State(defender).herd, ours / theirs);
                if (UnityEngine.Random.value >= chance) continue;
                if (!DeclareFeud(attacker, defender, "steppe_feud_history")) continue;
                started++;
                break;
            }
        }
    }

    private static List<Kingdom> Neighbours(Kingdom kingdom)
    {
        var result = new List<Kingdom>();
        foreach (City city in kingdom.cities)
            foreach (Kingdom next in city?.neighbours_kingdoms ?? new HashSet<Kingdom>())
                if (next != null && next != kingdom && !result.Contains(next)) result.Add(next);
        return result;
    }

    // 劫掠被打退后反目的部与盟主打一场(见 NomadRaidSystem.Hero)：不掷机会、不看强弱与冷却
    public static bool Rift(Kingdom attacker, Kingdom defender) =>
        FeudPair(attacker, defender) && DeclareFeud(attacker, defender, "nomad_raid_rift_war_history");

    private static bool DeclareFeud(Kingdom attacker, Kingdom defender, string historyKey)
    {
        List<City> cities = Contested(attacker, defender);
        if (cities.Count == 0) return false;
        War war;
        Declaring = true;
        try
        {
            war = World.world.diplomacy.startWar(attacker, defender, WarTypeLibrary.normal);
            if (war?.data != null) war.SetEmpireWarType(EmpireWarType.草场之争);
        }
        finally
        {
            Declaring = false;
        }
        if (war?.data == null) return false;
        string names = string.Join("、", cities.Select(city => city.GetCityName()));
        war.data.name = string.Format(LM.Get("steppe_feud_war_name"), attacker.GetKingdomName(), cities[0].GetCityName());
        war.GetOrCreate().pasture = new PastureFeudState
        {
            attacker_id = attacker.id, defender_id = defender.id, attacker_name = attacker.GetKingdomName(),
            defender_name = defender.GetKingdomName(), city_ids = cities.Select(city => city.id).ToList(), city_names = names
        };
        double now = World.world.getCurWorldTime();
        (NomadRaidSystem.State(attacker).feud_at ??= new Dictionary<long, double>())[defender.id] = now;
        (NomadRaidSystem.State(defender).feud_at ??= new Dictionary<long, double>())[attacker.id] = now;
        Chronicle(string.Format(LM.Get(historyKey), attacker.GetKingdomName(), defender.GetKingdomName(), names), attacker, defender);
        LogService.LogInfo($"{LogTag} 争草场：{attacker.GetKingdomName()}(牲畜粮食压力{NomadRaidSystem.State(attacker).herd:0}，" +
                           $"国力{attacker.GetNationalPower():0})争{defender.GetKingdomName()}" +
                           $"(压力{NomadRaidSystem.State(defender).herd:0}，国力{defender.GetNationalPower():0})的{names}");
        return true;
    }

    private static List<War> FeudWars() =>
        World.world.wars.list.Where(war => war?.data != null && war.isAlive() && !war.hasEnded() &&
                                           war.GetEmpireWarType() == EmpireWarType.草场之争).ToList();

    // 每月：打满两年强制议和
    private static void FeudsMonthly()
    {
        foreach (War war in FeudWars())
            if (WarSituation.WarYears(war) >= FeudYears)
                World.world.wars.endWar(war, WarWinner.Peace);
    }

    // 争草场进行中：两家之间不在争城清单里的城一律打不下(CityPatch 攻城结算调用)
    public static bool BlocksPastureCity(Kingdom newKingdom, City city)
    {
        Kingdom holder = city?.kingdom;
        if (newKingdom?.data == null || holder?.data == null || holder == newKingdom) return false;
        try
        {
            foreach (War war in newKingdom.getWars())
            {
                if (war?.data == null || war.hasEnded() || war.GetEmpireWarType() != EmpireWarType.草场之争) continue;
                if (!(war.isAttacker(newKingdom) && war.isDefender(holder) || war.isDefender(newKingdom) && war.isAttacker(holder)))
                    continue;
                PastureFeudState record = war.GetOrCreate().pasture;
                return record != null && !record.city_ids.Contains(city.id);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 争草场拦城失败: {exception.Message}");
        }
        return false;
    }

    // 收场(WarPatch.end_war)：攻方胜或议和，争城里攻方占了一半以上地块的割给攻方；守方胜，攻方赔款、打下的争城退还
    public static void OnWarEnded(War war, WarWinner winner)
    {
        if (war?.data == null || war.GetEmpireWarType() != EmpireWarType.草场之争) return;
        PastureFeudState record = war.GetOrCreate().pasture;
        if (record == null || record.settled) return;
        record.settled = true;
        try
        {
            Kingdom attacker = LiveKingdom(record.attacker_id), defender = LiveKingdom(record.defender_id);
            bool defendersWon = winner == WarWinner.Defenders;
            var taken = new List<string>();
            int returned = 0;
            foreach (long id in record.city_ids)
            {
                City city = World.world.cities.get(id);
                if (city?.data == null || city.isRekt() || attacker == null) continue;
                if (defendersWon)
                {
                    if (defender != null && city.kingdom == attacker)
                    {
                        WarGoalSystem.QueueCession(city, defender);
                        returned++;
                    }
                    continue;
                }
                if (city.kingdom == attacker) taken.Add(city.GetCityName());
                else if (city.kingdom == defender && city.zones?.Count > 0 &&
                         city.GetOccupiedTileZoneCount(attacker) >= city.zones.Count * FeudHeldShare)
                {
                    WarGoalSystem.QueueCession(city, attacker);
                    taken.Add(city.GetCityName());
                }
            }
            string text;
            if (defendersWon)
            {
                if (attacker != null && defender != null) WarReparationsSystem.Impose(attacker, defender);
                text = string.Format(LM.Get("steppe_feud_lost_history"), record.attacker_name, record.defender_name, returned);
            }
            else
                text = taken.Count > 0
                    ? string.Format(LM.Get("steppe_feud_won_history"), record.attacker_name, record.defender_name, string.Join("、", taken))
                    : string.Format(LM.Get("steppe_feud_draw_history"), record.attacker_name, record.defender_name);
            Chronicle(text, attacker, defender);
            LogService.LogInfo($"{LogTag} 争草场收场({winner})：{text}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} {war.data.name} 争草场收场失败: {exception.Message}");
        }
    }

    // 记一笔：世界日志一条，两家所在的帝国史书各一条
    private static void Chronicle(string text, params Kingdom[] involved)
    {
        Kingdom first = null;
        var empires = new HashSet<Empire>();
        foreach (Kingdom kingdom in involved)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            first ??= kingdom;
            Empire empire = kingdom.GetEmpire();
            if (empire?.data != null && !empire.isRekt() && empires.Add(empire))
                empire.RecordHistory(directContent: text, kingdomId: kingdom.id);
        }
        TranslateHelper.LogEventOnly(text, first);
    }
}

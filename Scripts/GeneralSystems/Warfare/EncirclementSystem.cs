using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 合围(借鉴钢铁雄心的包围战，用户 2026-11-02 "先做第一批")：以前军团断了补给只是战力打折，组织度掉光就从敌军中间走回家。
//   · 判定(每月后勤结算后)：军所在的地块往外走，不穿敌占区、不翻不可通行的地，能走到的地不超过 PocketZones 块，
//     里面又没有己方的城池，四周有敌占区——就是被围了。海外靠海路还有补给的不算；义军、飞机、船上的不算；
//   · 被围的军：补给断绝(照后勤算减员、掉组织度)，不能行军，就地打所在的那座城，打下四周的地算自己杀出来；
//   · 突围：组织度还在的，每月有机会杀出重围，折损四分之一的人，退到最近的己方地块；有名将统领的机会大些；
//   · 投降：围满 SurrenderFrom 个月起每月越来越可能全军投降——多数阵亡、溃散，统领按战场个人戏的被俘处理；
//     也可能被敌方收编成它的一支军(只剩一半人)；
//   · AI：救被围的友军、收拾被围的敌军都算好战线(见 ScoreBonus)，进攻时先拿敌军所在地块四周的地(抄后路，见 FrontSystem.FlankOrder)。
public static class EncirclementSystem
{
    private const int PocketZones = 12;
    // 补给还有这么多的不必判(前线路远的军也常掉到这么低)
    private const float SupplyWatch = 0.8f;
    private const int SurrenderFrom = 3;
    private const float BreakoutOrg = 25f, BreakoutChance = 0.12f, FamousBonus = 0.1f, BreakoutLoss = 0.25f, BreakoutOrgLoss = 30f;
    private const int BreakoutReach = 6;
    private const float SurrenderStep = 0.1f, LowOrg = 10f, LowOrgBonus = 0.3f, FewMen = 0.25f, FewMenBonus = 0.2f;
    private const float AbsorbChance = 0.3f, AbsorbKeep = 0.5f, SlainShare = 0.6f;
    private const float NoticeMen = 3000f;
    private const float ReliefBonus = 0.8f, FinishBonus = 0.5f;

    public static bool Encircled(CorpsData corps) => corps != null && corps.pocket_months > 0;

    private static readonly HashSet<int> Seen = new();
    private static readonly Queue<TileZone> Frontier = new();

    // 军所在的这块地是不是被围住了；算完 Seen 里是围内的地块(突围要用)
    private static bool InPocket(Kingdom kingdom, TileZone start)
    {
        Seen.Clear();
        Frontier.Clear();
        Seen.Add(start.id);
        Frontier.Enqueue(start);
        bool enemy = false;
        while (Frontier.Count > 0)
        {
            TileZone zone = Frontier.Dequeue();
            City city = zone.city;
            if (city?.data != null && !city.isRekt() && city.getTile()?.zone == zone && FrontSystem.Friendly(kingdom, city.kingdom))
                return false;
            foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
            {
                if (next == null || Seen.Contains(next.id) || SeaRegionSystem.IsSea(next) || TerrainSystem.Impassable(next) ||
                    !TerrainSystem.Crossable(zone, next)) continue;
                Kingdom controller = FrontSystem.Controller(next);
                if (controller != null && FrontSystem.Hostile(kingdom, controller))
                {
                    enemy = true;
                    continue;
                }
                Seen.Add(next.id);
                if (Seen.Count > PocketZones) return false;
                Frontier.Enqueue(next);
            }
        }
        return enemy;
    }

    private static bool Exempt(Kingdom kingdom, CorpsData corps) =>
        corps.embarked || corps.militia || corps.strength < 1f || CorpsArmSystem.Flies(corps) || CorpsSystem.Insurgent(kingdom);

    // 每月后勤结算里(算完补给、发粮之前)：会把投降的军从 list 里拿掉
    public static void Monthly(Kingdom kingdom, List<CorpsData> list)
    {
        bool war = CorpsSystem.IsAtWar(kingdom);
        foreach (CorpsData corps in list.ToList())
        {
            TileZone zone = CorpsSystem.Zone(corps);
            bool pocket = war && zone != null && !Exempt(kingdom, corps) && corps.supply < SupplyWatch && !SeaBorne(corps, zone) &&
                          InPocket(kingdom, zone);
            if (!pocket)
            {
                if (corps.pocket_months > 0)
                {
                    corps.pocket_months = 0;
                    FrontSystem.Log(kingdom, string.Format(LM.Get("corps_log_pocket_relieved"), CorpsSystem.Name(corps)));
                }
                continue;
            }
            corps.supply = 0f;
            corps.pocket_months++;
            if (corps.pocket_months == 1)
            {
                Announce(kingdom, corps, zone);
                continue;
            }
            if (TryBreakout(kingdom, corps, zone)) continue;
            TrySurrender(kingdom, corps, zone);
        }
    }

    // 海外的军海路还通：滩头被围也有粮，不算(见 LogisticsSystem.SupplyLine)
    private static bool SeaBorne(CorpsData corps, TileZone zone)
    {
        City home = CorpsSystem.Home(corps);
        return corps.supply > 0f && home != null && !CorpsSystem.SameLand(zone, home.getTile()?.zone);
    }

    private static string Place(TileZone zone) => zone?.city?.data != null && !zone.city.isRekt()
        ? zone.city.GetCityName() : LM.Get("corps_pocket_wilds");

    private static void Announce(Kingdom kingdom, CorpsData corps, TileZone zone)
    {
        string name = CorpsSystem.Name(corps), place = Place(zone);
        FrontSystem.Log(kingdom, string.Format(LM.Get("corps_log_pocket"), name, place));
        Kingdom captor = Captor(kingdom, zone);
        if (captor != null)
            FrontSystem.Log(captor, string.Format(LM.Get("corps_log_pocket_enemy"), kingdom.GetKingdomName(), name, place));
        if (corps.strength >= NoticeMen)
            WarChronicle.RecordFor(kingdom, WarChronicle.Commander, string.Format(LM.Get("corps_log_pocket"), name, place));
    }

    // 围住这支军的敌国：所在地的控制者；不是敌国就看四周
    private static Kingdom Captor(Kingdom kingdom, TileZone zone)
    {
        Kingdom own = FrontSystem.Controller(zone);
        if (own != null && FrontSystem.Hostile(kingdom, own)) return own;
        foreach (int id in Seen)
        {
            TileZone inside = CorpsSystem.ZoneById(id);
            foreach (TileZone next in inside?.neighbours_all ?? Array.Empty<TileZone>())
                if (next != null && FrontSystem.Controller(next) is Kingdom holder && FrontSystem.Hostile(kingdom, holder))
                    return holder;
        }
        return null;
    }

    private static bool TryBreakout(Kingdom kingdom, CorpsData corps, TileZone zone)
    {
        if (corps.org < BreakoutOrg) return false;
        Actor general = corps.general_id >= 0 ? World.world.units.get(corps.general_id) : null;
        float chance = BreakoutChance + (general != null && FamousTalentSystem.IsFamous(general) ? FamousBonus : 0f);
        if (UnityEngine.Random.value >= chance) return false;
        TileZone exit = NearestFriendly(kingdom, zone);
        if (exit == null) return false;
        float lost = corps.strength * BreakoutLoss;
        CorpsSystem.Casualties(corps, lost);
        corps.org = Mathf.Max(5f, corps.org - BreakoutOrgLoss);
        corps.zone_id = exit.id;
        corps.pocket_months = 0;
        corps.march_progress = 0f;
        corps.task = "retreat";
        corps.sector_city_id = -1L;
        corps.target_zone_id = CorpsSystem.Home(corps)?.getTile()?.zone?.id ?? exit.id;
        string text = string.Format(LM.Get("corps_log_breakout"), CorpsSystem.Name(corps), Place(zone), CorpsSystem.ShortStrength(lost));
        FrontSystem.Log(kingdom, text);
        WarChronicle.RecordFor(kingdom, WarChronicle.Commander, text);
        return true;
    }

    // 围外最近的己方地块(BreakoutReach 步以内，可以穿敌占区)；Seen 是这支军的围内
    private static TileZone NearestFriendly(Kingdom kingdom, TileZone start)
    {
        var visited = new HashSet<int> { start.id };
        var level = new List<TileZone> { start };
        for (int depth = 0; depth < BreakoutReach && level.Count > 0; depth++)
        {
            var next = new List<TileZone>();
            foreach (TileZone zone in level)
                foreach (TileZone neighbour in zone.neighbours_all ?? Array.Empty<TileZone>())
                {
                    if (neighbour == null || !visited.Add(neighbour.id) || SeaRegionSystem.IsSea(neighbour) ||
                        TerrainSystem.Impassable(neighbour) || !TerrainSystem.Crossable(zone, neighbour)) continue;
                    if (!Seen.Contains(neighbour.id) && FrontSystem.Controller(neighbour) is Kingdom holder &&
                        FrontSystem.Friendly(kingdom, holder))
                        return neighbour;
                    next.Add(neighbour);
                }
            level = next;
        }
        return null;
    }

    private static void TrySurrender(Kingdom kingdom, CorpsData corps, TileZone zone)
    {
        if (corps.pocket_months < SurrenderFrom) return;
        float chance = SurrenderStep * (corps.pocket_months - 1) + (corps.org <= LowOrg ? LowOrgBonus : 0f) +
                       (corps.strength < corps.full * FewMen ? FewMenBonus : 0f);
        if (UnityEngine.Random.value >= chance) return;
        Surrender(kingdom, corps, zone);
    }

    private static void Surrender(Kingdom kingdom, CorpsData corps, TileZone zone)
    {
        Kingdom captor = Captor(kingdom, zone);
        string name = CorpsSystem.Name(corps), place = Place(zone);
        float men = corps.strength;
        City city = zone.city?.data != null && !zone.city.isRekt() ? zone.city : null;
        // 围城的军记战功、统领落到谁手里
        List<(Kingdom kingdom, CorpsData corps)> besiegers = captor == null ? new List<(Kingdom, CorpsData)>()
            : CorpsSystem.All().Where(entry => entry.kingdom == captor && entry.corps.strength >= 1f &&
                                               CorpsSystem.ZoneDistance(CorpsSystem.Zone(entry.corps), zone) <= 4f).ToList();
        if (city != null && captor != null) BattleDrama.Surrendered(city, kingdom, corps, captor, besiegers);
        bool absorbed = captor?.data != null && !captor.isRekt() && captor.capital != null && UnityEngine.Random.value < AbsorbChance;
        if (absorbed)
        {
            CorpsSystem.Casualties(corps, men * (1f - AbsorbKeep));
            CorpsSystem.CaptureCorps(kingdom, corps, captor);
        }
        else
        {
            CorpsSystem.Casualties(corps, men * SlainShare);
            CorpsSystem.Disband(kingdom, corps);
        }
        string key = absorbed ? "corps_log_pocket_absorbed" : "corps_log_pocket_lost";
        string text = string.Format(LM.Get(key), kingdom.GetKingdomName(), name, place, CorpsSystem.ShortStrength(men), captor?.GetKingdomName() ?? "");
        FrontSystem.Log(kingdom, text);
        if (captor != null)
        {
            FrontSystem.Log(captor, text);
            if (besiegers.Count > 0)
                FamousTalentSystem.OnBattle(besiegers, place, besiegers.Sum(entry => entry.corps.strength), men, false, false);
        }
        WarChronicle.RecordFor(kingdom, WarChronicle.Commander, text);
        // 大军覆没才进史书(帝国、大国的才上屏，见 ScreenFeedPatch)
        if (men >= NoticeMen) TranslateHelper.LogEventMessage(text, kingdom);
        else TranslateHelper.LogEventOnly(text, kingdom);
    }

    // ───────── 战线分派 ─────────
    // 被围的军就地打仗(FrontSystem.AssignMonth 分派完之后)：敌城里就打这座城，自家的城就守
    public static void Hold(Kingdom kingdom, List<CorpsData> list)
    {
        foreach (CorpsData corps in list)
        {
            if (corps.pocket_months <= 0) continue;
            TileZone zone = CorpsSystem.Zone(corps);
            if (zone == null) continue;
            City city = zone.city?.data != null && !zone.city.isRekt() ? zone.city : null;
            corps.target_zone_id = zone.id;
            corps.sector_city_id = city?.id ?? -1L;
            corps.task = city != null && FrontSystem.Hostile(kingdom, city.kingdom) ? "attack" : "defend";
        }
    }

    // 各城里被围的军(每月分派前重算，见 FrontSystem.BeginMonth)
    private static readonly Dictionary<long, List<Kingdom>> PocketCities = new();

    public static void BeginAssign()
    {
        PocketCities.Clear();
        foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All())
        {
            if (corps.pocket_months <= 0 || CorpsSystem.Zone(corps)?.city is not City city || city.data == null) continue;
            if (!PocketCities.TryGetValue(city.id, out List<Kingdom> list)) PocketCities[city.id] = list = new List<Kingdom>();
            list.Add(kingdom);
        }
    }

    public static void ResetWorldState() => PocketCities.Clear();

    // 这段战线上有被围的军：友军赶去解围、敌军赶去收拾
    public static float ScoreBonus(Kingdom kingdom, City city)
    {
        if (PocketCities.Count == 0 || city == null || !PocketCities.TryGetValue(city.id, out List<Kingdom> list)) return 0f;
        float bonus = 0f;
        foreach (Kingdom besieged in list)
        {
            if (FrontSystem.Friendly(kingdom, besieged)) bonus = Mathf.Max(bonus, ReliefBonus);
            else if (FrontSystem.Hostile(kingdom, besieged)) bonus = Mathf.Max(bonus, FinishBonus);
        }
        return bonus;
    }
}

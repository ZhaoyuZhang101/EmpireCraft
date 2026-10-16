using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 平滑国界下的逐格占领(模组设置「平滑国界」+ 军团模式，见 SmoothBorders)：
//   交战占优的一方不再整块拿地，而是从己方一侧一格一格吃进紧挨着自己军团的那块地，进度按世界时间连续推进，
//   每月推两块地(地块模式一月一块的两倍)；推满一块才真正易手，离它最近的军团进驻，接着推身边的下一块。
//   当月没占优、或军团不在场就停在原处；对方占优时先把推进中的那块逐格推回去，推回到头再由对方开始推进。
//   守方收复被占的地块同理。每座城同一时间只有一条推进线。吃下的格子在国家、帝国图层上直接算推进方的地
//   (不画占领斜纹，国界跟着推进线走，见 SmoothBorders)。
public static partial class FrontSystem
{
    private const float SmoothZonesPerMonth = 2f, SecondsPerMonth = 5f;
    private const int PaintSteps = 24;

    public static bool SmoothActive => SmoothBorders.Enabled && CorpsSystem.Enabled;

    private sealed class Push
    {
        public long City, Side;
        public int Zone;
        // 收复(守方把被占的地块夺回)；Free：没有攻方来争、不打仗的收复(攻方看守着的地不收)
        public bool Retake, Free;
        public float Progress;
        // +1 推进 / -1 被推回 / 0 停住
        public int Direction;
        public double Last;
        public bool Defended, Landing, Confirmed;
        public readonly List<(long kingdom, long corps)> Corps = new();
        public Vector2 Center, Dir;
        public float Min, Max;
        public int Painted = -1;
    }

    private static readonly Dictionary<long, Push> Pushes = new();
    private static readonly Dictionary<int, Push> PushByZone = new();

    private static void ResetSmooth()
    {
        Pushes.Clear();
        PushByZone.Clear();
    }

    #region 月度交战的结果(ResolveCity)

    private static void BeginMonthPushes()
    {
        foreach (Push push in Pushes.Values) push.Confirmed = false;
    }

    // 本月没有军团来争的城：推进线停在原处
    private static void EndMonthPushes()
    {
        foreach (Push push in Pushes.Values)
            if (!push.Confirmed) push.Direction = 0;
    }

    // side 本月在 city 占优：继续(或开始)推进；城里有对方推进中的地块就先把它推回去
    private static void SmoothAdvance(City city, Kingdom side, List<CorpsData> spearheads, bool defended, bool landing,
        bool retake, bool free = false)
    {
        if (Pushes.TryGetValue(city.id, out Push push))
        {
            push.Confirmed = true;
            if (push.Side != side.id)
            {
                push.Direction = -1;
                return;
            }
            push.Direction = 1;
            push.Defended = defended;
            push.Landing = landing;
            push.Free = free;
            SetCorps(push, spearheads);
            return;
        }
        TileZone zone = retake ? RetakeTarget(city, side, spearheads, free) : AdvanceTarget(city, side, spearheads, defended, landing);
        if (zone == null) return;
        push = new Push
        {
            City = city.id, Side = side.id, Zone = zone.id, Retake = retake, Free = free, Direction = 1, Defended = defended,
            Landing = landing, Confirmed = true, Last = World.world.getCurWorldTime()
        };
        SetCorps(push, spearheads);
        Aim(push, zone, side);
        Pushes[city.id] = push;
        PushByZone[zone.id] = push;
    }

    // 本月相持：停住
    private static void SmoothHold(City city)
    {
        if (!Pushes.TryGetValue(city.id, out Push push)) return;
        push.Confirmed = true;
        push.Direction = 0;
    }

    private static void SetCorps(Push push, List<CorpsData> corps)
    {
        push.Corps.Clear();
        foreach (CorpsData entry in corps) push.Corps.Add((entry.kingdom_id, entry.id));
    }

    private static List<CorpsData> CorpsOf(Push push) =>
        push.Corps.Select(pair => CorpsSystem.Find(World.world.kingdoms.get(pair.kingdom), pair.corps))
            .Where(corps => corps != null).ToList();

    // 推进的目标：紧挨着进攻军团、敌方控制的地块(挑法同 Advance)；守军还在时不拿城中最后一块、城池放到最后，溃散后先取城池
    private static TileZone AdvanceTarget(City city, Kingdom side, List<CorpsData> spearheads, bool defended, bool landing)
    {
        List<TileZone> targets = TargetZones(side, city);
        if (targets.Count == 0 && landing) targets = LandingZones(city).Where(zone => Hostile(side, Controller(zone))).ToList();
        targets = targets.Where(candidate => Beside(spearheads, candidate)).ToList();
        if (targets.Count == 0) return null;
        int remaining = CityDistricts.Count(city, zone => !Friendly(side, Controller(zone)) && !TerrainSystem.Impassable(zone));
        if (defended && remaining <= 1) return null;
        TileZone center = city.getTile()?.zone;
        targets = Outskirts(city, side, targets, center, defended);
        if (targets.Count == 0) return null;
        return targets.OrderBy(candidate => CenterOrder(city, candidate, center, defended))
            .ThenByDescending(candidate => (candidate.neighbours ?? Array.Empty<TileZone>())
                .Count(neighbour => Friendly(side, Controller(neighbour)))).First();
    }

    // 收复的目标：本城被敌军占去、与己方相连、紧挨着己方军团的地块；没有攻方来争时，攻方军团看守着的地不收(见 Guarded)
    private static TileZone RetakeTarget(City city, Kingdom side, List<CorpsData> troops, bool free = false) =>
        city.zones?.FirstOrDefault(candidate => candidate != null &&
                                                city.GetTileZoneOccupier(candidate) is Kingdom occupier && Hostile(side, occupier) &&
                                                Beside(troops, candidate) && (!free || !Guarded(side, candidate)) &&
                                                (candidate.neighbours ?? Array.Empty<TileZone>())
                                                .Any(neighbour => Friendly(side, Controller(neighbour))));

    // 推进方向：从相邻的己方地块指向这块地；记下各格在这个方向上的投影范围(画进度用)
    private static void Aim(Push push, TileZone zone, Kingdom side)
    {
        Vector2 center = ZoneCenter(zone);
        Vector2 from = Vector2.zero;
        int count = 0;
        foreach (TileZone neighbour in zone.neighbours_all ?? zone.neighbours ?? Array.Empty<TileZone>())
        {
            if (neighbour == null || !Friendly(side, Controller(neighbour))) continue;
            from += ZoneCenter(neighbour);
            count++;
        }
        Vector2 dir = count == 0 ? Vector2.right : center - from / count;
        push.Dir = dir.sqrMagnitude < 0.01f ? Vector2.right : dir.normalized;
        push.Center = center;
        push.Min = float.MaxValue;
        push.Max = float.MinValue;
        foreach (WorldTile tile in zone.tiles)
        {
            float s = Vector2.Dot(new Vector2(tile.x + 0.5f, tile.y + 0.5f) - center, push.Dir);
            push.Min = Mathf.Min(push.Min, s);
            push.Max = Mathf.Max(push.Max, s);
        }
        if (push.Max - push.Min < 0.01f) push.Max = push.Min + 1f;
    }

    private static Vector2 ZoneCenter(TileZone zone) =>
        new((zone.x + 0.5f) * Config.CITY_ZONE_SIZE, (zone.y + 0.5f) * Config.CITY_ZONE_SIZE);

    #endregion

    #region 每帧推进(CorpsSystem.Update)

    public static void SmoothTick()
    {
        if (Pushes.Count == 0) return;
        if (!SmoothActive)
        {
            foreach (Push push in Pushes.Values) Repaint(push.Zone);
            ResetSmooth();
            return;
        }
        double now = World.world.getCurWorldTime();
        foreach (Push push in Pushes.Values.ToList())
        {
            float months = (float)((now - push.Last) / SecondsPerMonth);
            push.Last = now;
            if (months <= 0f || push.Direction == 0) continue;
            try
            {
                Step(push, months);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][战线] 逐格推进失败: {exception.Message}");
                Remove(push);
            }
        }
    }

    private static void Step(Push push, float months)
    {
        City city = World.world.cities.get(push.City);
        Kingdom side = World.world.kingdoms.get(push.Side);
        TileZone zone = World.world.zone_calculator.getZoneByID(push.Zone);
        if (city?.data == null || city.isRekt() || side?.data == null || side.isRekt() || zone?.city != city || !Valid(push, city, side, zone))
        {
            Remove(push);
            return;
        }
        // 开国雄主、正统高的帝国打一般国家推得更快(见 PushFactor)；被推回时按原速
        float pace = push.Direction > 0 ? PushFactor(side, push.Retake ? Controller(zone) : city.kingdom) : 1f;
        push.Progress += push.Direction * SmoothZonesPerMonth * months * pace;
        if (push.Progress <= 0f && push.Direction < 0)
        {
            Remove(push);
            return;
        }
        if (push.Progress >= 1f && push.Direction > 0) Complete(push, city, side, zone);
        else Paint(push, zone);
    }

    private static bool Valid(Push push, City city, Kingdom side, TileZone zone) => push.Retake
        ? city.GetTileZoneOccupier(zone) is Kingdom occupier && Hostile(side, occupier)
        : Hostile(side, Controller(zone));

    // 推满一块：真正易手(同 Advance / Retake)，离它最近的军团进驻，接着推身边的下一块
    private static void Complete(Push push, City city, Kingdom side, TileZone zone)
    {
        Kingdom owner = city.kingdom;
        List<CorpsData> corps = CorpsOf(push);
        float leftover = push.Progress - 1f;
        if (push.Retake)
        {
            Kingdom occupier = city.GetTileZoneOccupier(zone);
            if (occupier != null) city.RemoveOccupiedTileZone(occupier, zone);
            SmoothBorders.Settle(zone);
            // 这一片一并收回(见 CityDistricts)
            RetakeDistrict(city, side, zone);
        }
        else
        {
            int remaining = CityDistricts.Count(city, candidate => !Friendly(side, Controller(candidate)) &&
                                                                   !TerrainSystem.Impassable(candidate));
            if (push.Defended && remaining <= 1)
            {
                push.Progress = 0.98f;
                push.Direction = 0;
                Paint(push, zone);
                return;
            }
            Kingdom occupier = Belligerent(side, owner);
            Kingdom current = city.GetTileZoneOccupier(zone);
            if (current != null && current != owner) city.RemoveOccupiedTileZone(current, zone);
            city.AddOccupiedTileZone(occupier, zone, landing: true);
            SmoothBorders.Settle(zone);
            // 这一片(村镇四周或城区)一并到手；城区那片就是主城池，陷落即整城投降
            OccupyDistrict(city, side, owner, zone);
            if (CityDistricts.SameDistrict(city, zone, city.getTile()?.zone)) Surrender(city, side, owner);
            SweepImpassable(city, side, owner);
            if (city.kingdom != owner)
            {
                Log(side, string.Format(LM.Get("corps_log_city_taken"), city.GetCityName(), side.GetKingdomName()));
                Log(owner, string.Format(LM.Get("corps_log_city_lost"), city.GetCityName(), side.GetKingdomName()));
            }
        }
        CorpsData lead = corps.Where(entry => CorpsSystem.ZoneDistance(CorpsSystem.Zone(entry), zone) <= 1f)
            .OrderBy(entry => CorpsSystem.ZoneDistance(CorpsSystem.Zone(entry), zone))
            .ThenByDescending(entry => entry.strength).FirstOrDefault();
        if (lead != null)
        {
            lead.zone_id = zone.id;
            lead.march_progress = 0f;
        }
        Remove(push);
        if (city.isRekt() || city.kingdom != owner) return;
        TileZone next = push.Retake ? RetakeTarget(city, side, corps, push.Free) : AdvanceTarget(city, side, corps, push.Defended, push.Landing);
        if (next == null) return;
        var chained = new Push
        {
            City = push.City, Side = push.Side, Zone = next.id, Retake = push.Retake, Direction = 1, Defended = push.Defended,
            Landing = push.Landing, Confirmed = push.Confirmed, Last = push.Last, Progress = Mathf.Clamp01(leftover)
        };
        chained.Corps.AddRange(push.Corps);
        Aim(chained, next, side);
        Pushes[city.id] = chained;
        PushByZone[next.id] = chained;
        Paint(chained, next);
    }

    private static void Remove(Push push)
    {
        if (Pushes.TryGetValue(push.City, out Push current) && current == push) Pushes.Remove(push.City);
        if (PushByZone.TryGetValue(push.Zone, out Push byZone) && byZone == push) PushByZone.Remove(push.Zone);
        Repaint(push.Zone);
    }

    private static void Paint(Push push, TileZone zone)
    {
        int step = Mathf.Clamp(Mathf.FloorToInt(push.Progress * PaintSteps), 0, PaintSteps);
        if (step == push.Painted) return;
        push.Painted = step;
        zone.resetRenderHelpers();
        ZoneCalculator calc = World.world.zone_calculator;
        calc.getZone(zone.x + 1, zone.y)?.resetRenderHelpers();
        calc.getZone(zone.x - 1, zone.y)?.resetRenderHelpers();
        calc.getZone(zone.x, zone.y + 1)?.resetRenderHelpers();
        calc.getZone(zone.x, zone.y - 1)?.resetRenderHelpers();
    }

    private static void Repaint(int zoneId) => World.world?.zone_calculator?.getZoneByID(zoneId)?.resetRenderHelpers();

    #endregion

    #region 画(SmoothBorders)

    // 这块地正在被逐格推进：推进方、是否收复
    public static bool SmoothPushOn(TileZone zone, out Kingdom side, out bool retake)
    {
        side = null;
        retake = false;
        if (zone == null || !PushByZone.TryGetValue(zone.id, out Push push)) return false;
        side = World.world.kingdoms.get(push.Side);
        retake = push.Retake;
        return side?.data != null && !side.isRekt();
    }

    // 地格 (x, y) 已被推进方吃下(从推进方一侧按进度算，边缘带一点起伏)
    public static bool SmoothCovers(TileZone zone, int x, int y)
    {
        if (zone == null || !PushByZone.TryGetValue(zone.id, out Push push)) return false;
        float s = Vector2.Dot(new Vector2(x + 0.5f, y + 0.5f) - push.Center, push.Dir);
        float fraction = (s - push.Min) / (push.Max - push.Min);
        return fraction + 0.12f * (SmoothBorders.FrontNoise(x, y) - 0.5f) <= push.Progress;
    }

    #endregion
}

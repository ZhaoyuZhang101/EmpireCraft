using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 关城即城墙上的关门(玩家反馈：关放不到城墙上，城墙被当成了山，关城只能挨着墙放)：
//   · 筑关点在城墙上或城墙边 Snap 格以内，关城落到城墙上(见 RuralSettlementSystem.Place；山上的墙也行)；村、镇、城照旧不能压在城墙上；
//   · 完好的关城方城(5×5，中心起 RuralSettlementSystem.FortReach = 2 格，不随等级变，与粗图、精细图画出来的一样大)
//     压着的城墙格是关门——城墙数据照旧留着，
//     只是画城墙时让给关城，城墙画到关城两侧接上(见 MapWallOverlay)；关城毁成废墟或抹去，关门没了，城墙原样合上；
//     后画的城墙穿过已有的关城，同样接进关里；
//   · 路(路网干道、城郊大路小路、官道)只能从关门过墙(见 RoadNetwork、UrbanFabric)；
//   · 地形阻力：守关一方(关城所在地块的控制者)与同一势力、并肩作战的友邦、同盟过关不加城墙阻力，别人照旧(见 TerrainResistance)；
//   · 纯净地图下城墙只是图层数据，人马本来就走得过；普通地图下原版城墙是挡路的格子，关门那几格的原版城墙让开(见 MapWallSystem)。
// 只算关城自己城的地：关城画在本城的地上(别城、无主的地上画不出来)，那里的城墙仍照常画、照常挡路。
// 城门(用户 2026-10-28 问答拍板"城墙变真的……挡兵、四门是关门")：城池全图真筑的城墙(见 CityPlanWorks.SyncWalls)上，
//   城门中线与城墙相交的那几格也是关门——放行、阻力、军团过墙同关城，守门的是城门所在那块地的控制者(见 WallPassage)；
//   画法归城市平面图(城门楼)，不是关城，不让城墙(Covered、Centers 只管关城)。
// 关门不存档：读档后按关城位置与城墙现算；城门按各城存档里记下的城门格(CityExtraData.plan_gate_tiles)，城墙还在的才算。
public static class WallGateSystem
{
    // 点在城墙边几格以内就吸到墙上(关城中心落在墙上，方城总压着墙；方城改成 5×5 后吸附半径照旧 5 格)
    public const int Snap = 5;
    // 关城那片地易主、城毁了都不经过关城的增删，隔一会儿重算一遍(关城只能手动筑，没几座，很便宜)
    private const float RefreshSeconds = 2f;

    private static HashSet<int> _gates = new(), _covered = new(), _cityGates = new();
    private static List<(int x, int y)> _centers = new();
    private static int _settlementRevision = int.MinValue, _wallRevision = int.MinValue, _cityRevision = int.MinValue;
    private static object _world;
    private static float _refreshedAt = -1000f;
    private static bool _changed;

    // 关门(关城压着的城墙格)
    public static IReadOnlyCollection<int> GateTiles
    {
        get
        {
            Ensure();
            return _gates;
        }
    }

    // 压着城墙的关城中心(画城墙挑敌楼时让开)
    public static IReadOnlyList<(int x, int y)> Centers
    {
        get
        {
            Ensure();
            return _centers;
        }
    }

    // 关城方城的半边长(格)：一律 RuralSettlementSystem.FortReach，不随等级变；与 UrbanFabric、PureMapPatch.Fort 画的一样
    public static int Reach(RuralSettlement fort) => RuralSettlementSystem.FortReach;

    public static bool IsGate(int tileId)
    {
        Ensure();
        return _gates.Count > 0 && _gates.Contains(tileId);
    }

    public static bool IsGate(WorldTile tile) => tile?.data != null && IsGate(tile.data.tile_id);

    // 城池全图筑的城墙上的城门(不是关城压着的关门；守门的是这一格所在的地块，见 WallPassage)
    public static bool IsCityGate(WorldTile tile)
    {
        Ensure();
        return tile?.data != null && _cityGates.Count > 0 && _cityGates.Contains(tile.data.tile_id);
    }

    // (x, y) 这座关城压着几格关门(城市平面图的指纹用)
    public static int GatesAround(int x, int y)
    {
        Ensure();
        if (_gates.Count == 0) return 0;
        int count = 0;
        const int far = RuralSettlementSystem.FortReach;
        for (int dy = -far; dy <= far; dy++)
        for (int dx = -far; dx <= far; dx++)
            if (World.world.GetTile(x + dx, y + dy)?.data is WorldTileData data && _gates.Contains(data.tile_id)) count++;
        return count;
    }

    // 这一格画的是压着城墙的关城(城墙图层在这里不画墙、不落影子、不起敌楼)
    public static bool Covered(int tileId)
    {
        Ensure();
        return _covered.Count > 0 && _covered.Contains(tileId);
    }

    // 筑关吸附：点的这一格或四周 Snap 格以内最近的一格城墙(只排除水面：长城多沿山脊，关隘本在山口，山上、岩石上的墙也吸；
    // 关城画在山格上照画，见 PureMapPatch.Fort)；优先落在点的那座城的地上。城池全图筑的城墙(城区外沿那一圈，自有城门，见 CityPlanWorks)
    // 不吸：在城外贴着城墙筑关，关城落在点的地方。没有返回 null
    public static WorldTile SnapToWall(WorldTile tile)
    {
        if (tile?.zone == null || MapWallSystem.Walls.Count == 0) return null;
        City clicked = tile.zone.city;
        WorldTile best = null;
        float bestScore = float.MaxValue;
        for (int dy = -Snap; dy <= Snap; dy++)
        for (int dx = -Snap; dx <= Snap; dx++)
        {
            WorldTile wall = World.world.GetTile(tile.x + dx, tile.y + dy);
            if (wall?.zone == null || !MapWallSystem.Contains(wall) || wall.Type == null || wall.Type.liquid ||
                CityPlanWorks.Planned(wall.data.tile_id)) continue;
            float score = dx * dx + dy * dy + (clicked != null && wall.zone.city != clicked ? 1000f : 0f);
            if (score >= bestScore) continue;
            bestScore = score;
            best = wall;
        }
        return best;
    }

    // 关城的城墙阻力(见 TerrainResistance)：这块地的关门放不放 visitor 过去——关在谁手里(被敌军占着就归占领者)，
    // 谁与同一势力、并肩作战的友邦、同盟才放
    public static bool LetsThrough(TileZone zone, Kingdom visitor)
    {
        City city = zone?.city;
        if (city?.data == null || city.isRekt() || visitor?.data == null) return false;
        Kingdom keeper = city.GetTileZoneOccupier(zone) ?? city.kingdom;
        if (keeper?.data == null) return false;
        if (FrontSystem.Friendly(keeper, visitor)) return true;
        return keeper.hasAlliance() && visitor.hasAlliance() && keeper.getAlliance() == visitor.getAlliance() &&
               !FrontSystem.Hostile(keeper, visitor);
    }

    // 每帧(MapWallSystem.Tick)：关城、城墙变了就重算，另外每 RefreshSeconds 秒重算一遍；关门变了返回 true
    public static bool Refresh()
    {
        float now = Time.unscaledTime;
        if (now >= _refreshedAt + RefreshSeconds || now < _refreshedAt)
        {
            _refreshedAt = now;
            Rebuild();
        }
        else Ensure();
        if (!_changed) return false;
        _changed = false;
        return true;
    }

    private static void Ensure()
    {
        if (ReferenceEquals(_world, World.world) && _settlementRevision == RuralSettlementSystem.Revision &&
            _wallRevision == MapWallSystem.Revision && _cityRevision == CityPlanWorks.WallRevision) return;
        Rebuild();
    }

    private static void Rebuild()
    {
        _world = World.world;
        _settlementRevision = RuralSettlementSystem.Revision;
        _wallRevision = MapWallSystem.Revision;
        _cityRevision = CityPlanWorks.WallRevision;
        var gates = new HashSet<int>();
        var cityGates = new HashSet<int>();
        var covered = new HashSet<int>();
        var centers = new List<(int x, int y)>();
        MapBox world = World.world;
        if (world?.tiles_list != null)
            foreach (RuralSettlement fort in RuralSettlementSystem.Forts())
            {
                if (fort.ruined_at >= 0d) continue;
                City city = world.GetTile(fort.x, fort.y)?.zone?.city;
                if (city?.data == null || city.isRekt()) continue;
                int reach = Reach(fort);
                bool onWall = false;
                var area = new List<int>();
                for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    WorldTile tile = world.GetTile(fort.x + dx, fort.y + dy);
                    if (tile?.data == null || tile.zone?.city != city) continue;
                    area.Add(tile.data.tile_id);
                    // 这里不能用 MapWallSystem.Blocks(它要问关门，会绕回来)
                    if (!MapWallSystem.Contains(tile) && tile.Type?.wall != true) continue;
                    gates.Add(tile.data.tile_id);
                    onWall = true;
                }
                if (!onWall) continue;
                covered.UnionWith(area);
                centers.Add((fort.x, fort.y));
            }
        // 城池全图筑的城墙上的城门：城墙还在(图层的或原版的)才算
        if (world?.tiles_list != null)
            foreach (int id in CityPlanWorks.PlannedGates)
            {
                WorldTile tile = id >= 0 && id < world.tiles_list.Length ? world.tiles_list[id] : null;
                if (tile?.data == null || !MapWallSystem.Contains(tile) && tile.Type?.wall != true) continue;
                cityGates.Add(id);
                gates.Add(id);
            }
        if (!gates.SetEquals(_gates) || !covered.SetEquals(_covered)) _changed = true;
        _cityGates = cityGates;
        _gates = gates;
        _covered = covered;
        _centers = centers;
    }
}

using System;
using System.Collections.Generic;
using ai;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 船贴着航线走(纯净地图模式，航线见 RoadNetwork)：原版给船寻完路后，远航(隔几个地块以上)的改走一条沿航线网的路——
// 按 RoadNetwork.SeaPath 在地块上寻路(航线上的海面代价低，所以会并到航线上)，与画出来的航线同样削角平滑后落到格子上，
// 走在航线上的那段就与虚线重合；平滑后切到岸上的几格就地绕开(小范围广度优先)，绕不开就照旧用原版的路。
// 近处(打渔、进出港)照原版。
public class BoatLanePatch : GamePatch
{
    public ModDeclare declare { get; set; }

    // 起止相隔多少格以上才改道；绕开岸角时最多搜几格
    private const int MinTiles = 24, BridgeSearch = 800;

    public void Initialize()
    {
        new Harmony(nameof(BoatLanePatch)).Patch(AccessTools.Method(typeof(ActorMove), nameof(ActorMove.goTo)),
            postfix: new HarmonyMethod(typeof(BoatLanePatch), nameof(AfterGoTo)));
    }

    private static void AfterGoTo(Actor pActor, WorldTile pTileTarget, int pLimitPathfindingRegions, ExecuteEvent __result)
    {
        if (!PureMapMode.Enabled || __result != ExecuteEvent.True || pLimitPathfindingRegions > 0 ||
            pActor?.asset == null || !pActor.asset.is_boat)
            return;
        WorldTile start = pActor.current_tile;
        if (start?.zone == null || pTileTarget?.zone == null || !pTileTarget.isGoodForBoat()) return;
        if (Mathf.Abs(start.x - pTileTarget.x) + Mathf.Abs(start.y - pTileTarget.y) < MinTiles) return;
        try
        {
            List<TileZone> zones = RoadNetwork.SeaPath(start.zone, pTileTarget.zone);
            if (zones == null || zones.Count < 3) return;
            List<WorldTile> path = Follow(start, pTileTarget, RoadNetwork.Smooth(zones, start, pTileTarget));
            if (path == null || path.Count == 0) return;
            // 原版已经按目标格设好了 tile_target；只换掉路径(整条给出，不再分段寻路)
            pActor.clearOldPath();
            pActor.current_path.AddRange(path);
            pActor.split_path = SplitPathStatus.Normal;
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][航线] 船只改道失败: {exception.Message}");
        }
    }

    // 沿平滑后的折线逐格走；落到岸上的格跳过，前后两段之间就地绕开
    private static List<WorldTile> Follow(WorldTile start, WorldTile end, List<Vector2> points)
    {
        var path = new List<WorldTile>();
        WorldTile last = start;
        bool gap = false;
        foreach (WorldTile tile in Raster(points))
        {
            if (tile == last) continue;
            if (!tile.isGoodForBoat())
            {
                gap = true;
                continue;
            }
            if ((gap || !Adjacent(last, tile)) && !Bridge(last, tile, path)) return null;
            gap = false;
            path.Add(tile);
            last = tile;
        }
        if (last != end)
        {
            if (!Adjacent(last, end) && !Bridge(last, end, path)) return null;
            path.Add(end);
        }
        return path;
    }

    private static IEnumerable<WorldTile> Raster(List<Vector2> points)
    {
        WorldTile previous = null;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            int x0 = Mathf.RoundToInt(points[i].x), y0 = Mathf.RoundToInt(points[i].y);
            int x1 = Mathf.RoundToInt(points[i + 1].x), y1 = Mathf.RoundToInt(points[i + 1].y);
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            for (int guard = 0; guard < 4096; guard++)
            {
                WorldTile tile = World.world.GetTile(x0, y0);
                if (tile != null && tile != previous)
                {
                    previous = tile;
                    yield return tile;
                }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }

    private static bool Adjacent(WorldTile a, WorldTile b) => Math.Abs(a.x - b.x) <= 1 && Math.Abs(a.y - b.y) <= 1;

    // 两格之间在海面上广度优先找一段(不含两端)接进路径
    private static bool Bridge(WorldTile from, WorldTile to, List<WorldTile> path)
    {
        var previous = new Dictionary<WorldTile, WorldTile> { [from] = null };
        var queue = new Queue<WorldTile>();
        queue.Enqueue(from);
        while (queue.Count > 0 && previous.Count < BridgeSearch)
        {
            WorldTile tile = queue.Dequeue();
            if (tile == to) break;
            foreach (WorldTile next in tile.neighboursAll ?? Array.Empty<WorldTile>())
            {
                if (next == null || previous.ContainsKey(next) || !next.isGoodForBoat()) continue;
                previous[next] = tile;
                queue.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(to)) return false;
        var bridge = new List<WorldTile>();
        for (WorldTile tile = previous[to]; tile != null && tile != from; tile = previous[tile]) bridge.Add(tile);
        bridge.Reverse();
        path.AddRange(bridge);
        return true;
    }
}

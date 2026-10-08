using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.HelperFunc;

namespace EmpireCraft.Scripts.GeneralSystems;

// 沿真实海面寻路，搜索队列只在运行时存在；选定的短航线以坐标保存。
internal sealed class SettlementSeaSearch
{
    internal sealed class SeaNode
    {
        public WorldTile tile;
        public SeaNode parent;
        public City portCity;
        public int distance;
        public int range;
    }

    internal sealed class Candidate
    {
        public TileZone zone;
        public WorldTile landfall;
        internal SeaNode sea;
        public int distance => sea.distance;

        public void Apply(StateSettlementProject project)
        {
            project.overseas = true;
            project.embarkation_city = sea.portCity.id;
            project.landfall = Point(landfall);
            project.sea_route ??= new List<SettlementRoutePoint>();
            project.sea_route.Clear();
            for (SeaNode node = sea; node != null; node = node.parent)
                project.sea_route.Add(Point(node.tile));
            project.sea_route.Reverse();
            project.travel_months = TravelMonths(project.sea_route.Count);
        }

        private static Candidate Create(TileZone zone, WorldTile landfall, SeaNode sea) =>
            new() { zone = zone, landfall = landfall, sea = sea };
        internal static Candidate FromLanding(Landing landing) => Create(landing.zone, landing.landfall, landing.sea);
    }

    internal sealed class Landing
    {
        public TileZone zone;
        public WorldTile landfall;
        internal SeaNode sea;
        public int depth;

        internal static Landing Create(TileZone zone, WorldTile landfall, SeaNode sea, int depth) =>
            new() { zone = zone, landfall = landfall, sea = sea, depth = depth };
        internal Landing Next(TileZone next) => Create(next, landfall, sea, depth + 1);
    }

    private const int MaxVisitedTiles = 80000;
    internal const int NearSeaRange = 64;
    internal const string OceanNavigationTech = "ocean_navigation";
    private readonly Queue<SeaNode> seaQueue = new();
    private readonly Queue<Landing> landQueue = new();
    private readonly Dictionary<WorldTile, int> seaSeen = new();
    private readonly HashSet<int> landSeen = new();
    private int shortlistDistance = int.MaxValue;
    private bool oceanAvailable;
    private readonly City existingTarget;

    public string unavailable { get; private set; } = "port";
    public bool Finished => seaQueue.Count == 0 && landQueue.Count == 0;

    public SettlementSeaSearch(City source, IEnumerable<City> ports, City existingTarget = null)
    {
        this.existingTarget = existingTarget;
        int count = 0;
        foreach (City city in ports)
        {
            if (!EligiblePortCity(city, source)) continue;
            Building port = Port(city);
            if (port == null) continue;
            if (unavailable == "port") unavailable = "navigation";
            if (!CanTransport(city, port)) continue;
            unavailable = "land";
            int range = Range(city, port);
            oceanAvailable |= range > NearSeaRange;
            foreach (WorldTile tile in port.component_docks.tiles_ocean)
                if (Navigable(tile) && Visit(tile, range))
                    seaQueue.Enqueue(new SeaNode { tile = tile, portCity = city, range = range });
            if (++count >= 4) break;
        }
    }

    // 每帧至多 128 格海面；候选陆地由调用者另外分帧检查，不在海岸线循环中做建城检查。
    public void Advance(City source)
    {
        for (int steps = 0; steps < 128 && seaQueue.Count > 0 && SimulationFrameBudget.HasTime; steps++)
        {
            SeaNode node = seaQueue.Dequeue();
            if (!oceanAvailable && node.distance >= node.range && node.range == NearSeaRange && Navigable(node.tile))
                unavailable = "overseas_tech";
            if (node.distance >= node.range || node.distance - 16 > shortlistDistance || !Navigable(node.tile)) continue;
            foreach (WorldTile tile in node.tile.neighbours ?? Array.Empty<WorldTile>())
            {
                if (Navigable(tile))
                {
                    if (Visit(tile, node.range - node.distance - 1))
                        seaQueue.Enqueue(new SeaNode { tile = tile, parent = node, portCity = node.portCity,
                            distance = node.distance + 1, range = node.range });
                }
                else if (tile?.Type?.ground == true && tile.region?.island != null &&
                    !tile.isSameIsland(source.getTile()) && tile.zone != null &&
                    (tile.zone.city == null || tile.zone.city == existingTarget) && landSeen.Add(tile.zone.id))
                    landQueue.Enqueue(Landing.Create(tile.zone, tile, node, 0));
            }
        }
    }

    private bool Visit(WorldTile tile, int remaining)
    {
        if (seaSeen.TryGetValue(tile, out int previous) ? remaining <= previous : seaSeen.Count >= MaxVisitedTiles)
            return false;
        seaSeen[tile] = remaining;
        return true;
    }

    public Landing NextLanding()
    {
        if (landQueue.Count == 0) return null;
        Landing landing = landQueue.Dequeue();
        if (landing.depth < 4)
            foreach (TileZone next in landing.zone.neighbours ?? Array.Empty<TileZone>())
                if (next?.centerTile != null && (next.city == null || next.city == existingTarget) &&
                    next.centerTile.isSameIsland(landing.landfall) && next.centerTile.reachableFrom(landing.landfall) &&
                    landSeen.Add(next.id)) landQueue.Enqueue(landing.Next(next));
        return landing;
    }

    public Candidate Accept(Landing landing)
    {
        shortlistDistance = Math.Min(shortlistDistance, landing.sea.distance);
        return Candidate.FromLanding(landing);
    }

    private static SettlementRoutePoint Point(WorldTile tile) => new() { x = tile.x, y = tile.y };
    internal static bool Navigable(WorldTile tile) => tile?.Type != null && tile.isGoodForBoat();
    internal static Building Port(City city) => city?.getBuildingOfType("type_docks", pCountOnlyFinished: true, pRandom: false);

    private static bool EligiblePortCity(City city, City source) => city?.data != null && !city.isRekt() &&
        !AncientWarfareCompatibility.OwnsObject(city) && city.getTile() != null &&
        StateSettlementSystem.FundingRealm(city.kingdom) == StateSettlementSystem.FundingRealm(source.kingdom) &&
        city.getTile().isSameIsland(source.getTile()) && city.getTile().reachableFrom(source.getTile());

    private static bool CanTransport(City city, Building port)
    {
        if (port?.component_docks?.tiles_ocean == null || port.asset?.boat_types == null ||
            city.getActorAsset()?.architecture_asset == null) return false;
        // 按本族建筑和已有科技验证运输能力；不要求实体移民或运输船完成登船任务。
        foreach (string type in port.asset.boat_types)
        {
            string id = port.asset.getBoatAssetIDFromType(type, city);
            if (!string.IsNullOrEmpty(id) && AssetManager.actor_library.get(id)?.is_boat_transport == true &&
                TechnologySystem.CanSpawnUnit(id, city)) return true;
        }
        return false;
    }

    // 近海小岛沿用基础运输能力；远洋必须确实完成研究，不能用旧档引导期的 CanUse 放行。
    private static bool HasOceanNavigation(City city) =>
        TechnologySystem.HasTech(TechnologySystem.GetCultureOf(city), OceanNavigationTech);
    private static int Range(City city, Building port) => HasOceanNavigation(city)
        ? 96 + Math.Min(3, Math.Max(0, port.asset.upgrade_level)) * 32 : NearSeaRange;
    internal static int TravelMonths(int routeLength) => Math.Max(1, (routeLength + 31) / 32);
    internal static int TransportGold(StateSettlementProject project) => project.overseas && !project.spontaneous
        ? 50 + (project.sea_route.Count + 7) / 8 * 5 : 0;

    internal static bool Validate(StateSettlementProject project, City source, out WorldTile landfall, out string reason,
        City existingTarget = null)
    {
        landfall = null;
        reason = "route";
        var route = project.sea_route;
        if (route == null || route.Count == 0 || route.Count > 192 || project.landfall == null) return false;
        City embarkation = World.world.cities.get(project.embarkation_city);
        if (!EligiblePortCity(embarkation, source)) return false;
        Building port = Port(embarkation);
        reason = "port";
        if (port == null) return false;
        reason = "navigation";
        if (!CanTransport(embarkation, port)) return false;
        reason = "overseas_tech";
        if (route.Count > NearSeaRange && !HasOceanNavigation(embarkation)) return false;
        reason = "route";
        if (route.Count > Range(embarkation, port)) return false;
        WorldTile previous = null;
        foreach (SettlementRoutePoint point in route)
        {
            if (point == null) return false;
            WorldTile tile = World.world.GetTile(point.x, point.y);
            if (!Navigable(tile) || previous != null && !Adjacent(previous, tile)) return false;
            if (previous == null && !port.component_docks.tiles_ocean.Contains(tile)) return false;
            previous = tile;
        }
        landfall = World.world.GetTile(project.landfall.x, project.landfall.y);
        return landfall?.Type?.ground == true && landfall.zone != null &&
            (landfall.zone.city == null || landfall.zone.city == existingTarget) && landfall.region?.island != null &&
            !landfall.isSameIsland(source.getTile()) && Adjacent(previous, landfall);
    }

    private static bool Adjacent(WorldTile a, WorldTile b) => a?.neighbours?.Contains(b) == true;
}

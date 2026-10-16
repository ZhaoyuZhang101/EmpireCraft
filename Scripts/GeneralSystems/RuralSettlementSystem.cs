using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 村镇实体(用户 2026-10-17 "村庄既然生成的就不要随意动了，因为已经是个固定地方了，只有可能通过战乱才能毁灭"
// "这些城市村庄虽然是模拟的，但在游戏中都是可以实际存在的、有互动的")。第一期：固定在地图上、随地易主——
//   · 城郊的镇、村一旦生成就记进存档(SaveData.rural_settlements)，钉在那一格上，不再随城市平面图每次重算挪地方；
//   · 归哪座城看它落在谁的地上：割地、城破时跟着土地换主，由新主人的城来画；
//   · 城长大(档位升高)、本城的镇村不够数时，才在本城空地上照原来的选址办法新建(见 UrbanFabric.PlaceCountryside)；
//     城变小不拆；被城区盖住的不画、不算数，城退回去又露出来；毁于兵火的留在原地(废墟)，照样算数，不在别处另建。
// 第二期：战火焚毁与重建(用户选"战火焚毁与重建")——
//   · 敌军占下一块地(战线推进、原版占领都走 CityExtension.AddOccupiedTileZone)，地上的村三成五、镇两成五毁于兵火；
//     劫掠之战七成、五成。毁了的在原地留一片废墟(精细图残垣断瓦、焦土；粗图上镇的废墟是一片暗土色)；
//   · 重建：废墟所在的城不在打仗、那块地没被敌军占着，毁了五年以后每年约两成机会在原址重建(每月查一次，见 Tick)。
// 第三期：住人、产粮交税(用户选"住人、产粮交税")——
//   · 城的背景人口有一部分住在城外镇村：村算 1 份、镇算 3 份、城里算 6 份，按份数分(点开镇村看到的人口即此，见 PeopleOf)；
//   · 镇村完好时产粮、交税照旧；毁了的那几份不产粮、不交税：本城岗位的粮食产出与赋税按
//     1 - 0.6 × 毁了的份数 / 总份数 打折(城外全毁约剩六成，见 OutputFactor、PopulationEconomySystem)；
//   · 毁的时候住在那里的人一半死伤逃散(按它的份数从本城各阶层的背景人口里扣)。
// 第四期：有名字、可以点开(用户选"有名字、可以点开")——
//   · 名字头一回标注时按位置起(华夏的村多是姓氏加家庄、村、屯，其余雅字加村、庄；镇是雅字或方位加镇，同城不重名，
//     见 MapLabelOverlay)，记进存档，从此不随先后、归属变；废墟标"某某·废墟"；
//   · 拉近到标注街名的程度，没选神力时点一下镇村：底部提示栏写它是镇是村、约多少人、属哪座城哪国，废墟写毁了几年。
// 城的地按村镇拓展(用户 2026-10-17 "城市的面积也按照村镇多少来拓展""但是会有个限制"，选"新建村镇就划进它一片"、限制"按离城远近")：
//   · 城长大要添村镇、本城地上没地方时，在城外紧挨着本城、离城心不太远(与选村址同一个距离，随城的档位放宽)的无主空地上建，
//     每城一年至多一处；建了就把它那块地和四周离城心同样不太远的无主之地划进本城(见 Found、ClaimPending)；
//   · 原版一块块往外领地，只领城区或本城村镇两块地以内的无主之地(见 CityExpansionPatch.Settled)，再往外要靠新建村镇。
//   领地照原版的规矩：开着原版"王国扩张"、地貌能定居、走得到(CityExpansionPatch.Reachable)、不超过每城地块上限。
public static class RuralSettlementSystem
{
    private const float VillageWeight = 1f, TownWeight = 3f, CityWeight = 6f, RuinedOutputLoss = 0.6f, BurnPopulationLoss = 0.5f;
    private const float VillageBurn = 0.35f, TownBurn = 0.25f, RaidVillageBurn = 0.7f, RaidTownBurn = 0.5f;
    private const int RebuildYears = 5;
    private const float RebuildChancePerYear = 0.2f;
    private static double _lastMonth = -1d;

    private static readonly Dictionary<int, RuralSettlement> All = new();
    // 地块 → 落在这块地上的镇村(读档后头一次用到时建)
    private static Dictionary<int, List<RuralSettlement>> _byZone;
    private static int _nextId = 1;

    // 镇村有增减、毁建就加一(城市平面图的指纹里算进去，见 PureMapPatch.Stamp)
    public static int Revision { get; private set; }

    public static void ResetWorldState()
    {
        All.Clear();
        _byZone = null;
        _nextId = 1;
        _lastMonth = -1d;
        LastFounded.Clear();
        Pending.Clear();
        Revision++;
    }

    public static void Load(List<RuralSettlement> saved)
    {
        ResetWorldState();
        if (saved == null) return;
        foreach (RuralSettlement settlement in saved)
        {
            if (settlement == null) continue;
            if (settlement.id <= 0 || All.ContainsKey(settlement.id)) settlement.id = _nextId;
            All[settlement.id] = settlement;
            _nextId = Math.Max(_nextId, settlement.id + 1);
        }
    }

    public static List<RuralSettlement> Save() => All.Values.ToList();

    private static Dictionary<int, List<RuralSettlement>> ByZone()
    {
        if (_byZone != null) return _byZone;
        var index = new Dictionary<int, List<RuralSettlement>>();
        if (World.world == null) return index;
        foreach (RuralSettlement settlement in All.Values)
        {
            TileZone zone = World.world.GetTile(settlement.x, settlement.y)?.zone;
            if (zone == null) continue;
            if (!index.TryGetValue(zone.id, out List<RuralSettlement> list)) index[zone.id] = list = new List<RuralSettlement>();
            list.Add(settlement);
        }
        _byZone = index;
        return index;
    }

    // 落在本城地上的镇村(含废墟)
    public static List<RuralSettlement> InCity(City city)
    {
        var result = new List<RuralSettlement>();
        if (city?.zones == null) return result;
        Dictionary<int, List<RuralSettlement>> index = ByZone();
        foreach (TileZone zone in city.zones)
            if (zone != null && index.TryGetValue(zone.id, out List<RuralSettlement> list)) result.AddRange(list);
        return result;
    }

    public static List<RuralSettlement> InZone(TileZone zone) =>
        zone != null && ByZone().TryGetValue(zone.id, out List<RuralSettlement> list) ? list : new List<RuralSettlement>();

    public static RuralSettlement Add(int x, int y, bool town, int size, bool walled)
    {
        var settlement = new RuralSettlement
        {
            id = _nextId++, x = x, y = y, town = town, size = size, walled = walled,
            founded = World.world?.getCurWorldTime() ?? 0d
        };
        All[settlement.id] = settlement;
        TileZone zone = World.world?.GetTile(x, y)?.zone;
        if (_byZone != null && zone != null)
        {
            if (!_byZone.TryGetValue(zone.id, out List<RuralSettlement> list)) _byZone[zone.id] = list = new List<RuralSettlement>();
            list.Add(settlement);
        }
        Revision++;
        return settlement;
    }

    public static void Touch() => Revision++;

    #region 新建村镇划地

    private const double FoundYears = 1d;
    // 各城上一回在城外新建村镇的时间(不存档，读档后头一回不限)
    private static readonly Dictionary<long, double> LastFounded = new();
    // 等着划地的：(城, 镇村, 城心, 离城心多远以内)；城市平面图里只记下，每次刷新城郊时划(见 ClaimPending)
    private static readonly List<(long city, int settlement, int gateX, int gateY, float far)> Pending = new();

    // 这座城这会儿能不能在城外新建村镇
    public static bool MayFound(City city)
    {
        if (city?.data == null || city.isRekt() || city.kingdom == null || ModClass.IS_CLEAR ||
            !WorldLawLibrary.world_law_kingdom_expansion.isEnabled() ||
            Compatibility.AncientWarfareCompatibility.OwnsObject(city) || GamePatches.CityExpansionPatch.AtLimit(city))
            return false;
        return !LastFounded.TryGetValue(city.data.id, out double last) || Date.getYearsSince(last) >= FoundYears;
    }

    // 这块无主之地本城领不领得到(原版的规矩)
    public static bool CanClaim(City city, TileZone zone) =>
        zone != null && zone.city == null && zone.tiles_with_ground > 0 && zone.canBeClaimedByCity(city) &&
        GamePatches.CityExpansionPatch.Reachable(zone, city) && !GamePatches.CityExpansionPatch.AtLimit(city);

    // 在城外新建了一处镇村：记下，等着划地
    public static void Found(City city, RuralSettlement settlement, int gateX, int gateY, float far)
    {
        if (city?.data == null || settlement == null) return;
        LastFounded[city.data.id] = World.world.getCurWorldTime();
        Pending.Add((city.data.id, settlement.id, gateX, gateY, far));
    }

    // 划地：镇村那块地，和四周离城心 far 格以内的无主之地
    public static void ClaimPending()
    {
        if (Pending.Count == 0 || World.world?.cities == null) return;
        var pending = new List<(long city, int settlement, int gateX, int gateY, float far)>(Pending);
        Pending.Clear();
        foreach ((long cityId, int settlementId, int gateX, int gateY, float far) in pending)
        {
            City city = World.world.cities.get(cityId);
            if (city?.data == null || city.isRekt() || !All.TryGetValue(settlementId, out RuralSettlement settlement)) continue;
            TileZone seat = World.world.GetTile(settlement.x, settlement.y)?.zone;
            if (seat == null) continue;
            if (seat.city == null && CanClaim(city, seat)) city.addZone(seat);
            if (seat.city != city || seat.neighbours_all == null) continue;
            foreach (TileZone zone in seat.neighbours_all)
            {
                if (zone?.centerTile == null || !CanClaim(city, zone)) continue;
                float dx = zone.centerTile.x - gateX, dy = zone.centerTile.y - gateY;
                if (dx * dx + dy * dy <= far * far) city.addZone(zone);
            }
            Revision++;
        }
    }

    #endregion

    // 正好落在 (x, y) 这一格的镇村
    public static RuralSettlement At(int x, int y)
    {
        TileZone zone = World.world?.GetTile(x, y)?.zone;
        return zone == null ? null : InZone(zone).Find(settlement => settlement.x == x && settlement.y == y);
    }

    // 点到的镇村：这一格及四邻地块里离得最近、点在它范围里的(镇按大小、村两格半)
    public static RuralSettlement Near(WorldTile tile)
    {
        TileZone zone = tile?.zone;
        if (zone == null || All.Count == 0) return null;
        RuralSettlement best = null;
        float bestDistance = float.MaxValue;
        var zones = new List<TileZone> { zone };
        if (zone.neighbours_all != null) zones.AddRange(zone.neighbours_all);
        foreach (TileZone around in zones)
            foreach (RuralSettlement settlement in InZone(around))
            {
                float dx = settlement.x - tile.x, dy = settlement.y - tile.y, d = dx * dx + dy * dy;
                float reach = settlement.town ? settlement.size + 1.5f : 2.5f;
                if (d > reach * reach || d >= bestDistance) continue;
                best = settlement;
                bestDistance = d;
            }
        return best;
    }

    #region 住人、产粮交税

    private static float Weight(RuralSettlement settlement) => settlement.town ? TownWeight : VillageWeight;

    // 本城城外镇村的份数：完好的、毁了的
    public static (float living, float ruined) Weights(City city)
    {
        float living = 0f, ruined = 0f;
        if (All.Count == 0) return (0f, 0f);
        foreach (RuralSettlement settlement in InCity(city))
        {
            if (settlement.ruined_at >= 0d) ruined += Weight(settlement);
            else living += Weight(settlement);
        }
        return (living, ruined);
    }

    // 粮食产出与赋税的折扣(城外镇村都完好时为 1)
    public static float OutputFactor(City city)
    {
        (float living, float ruined) = Weights(city);
        return ruined <= 0f ? 1f : 1f - RuinedOutputLoss * ruined / (CityWeight + living + ruined);
    }

    // 住在这处镇村的人(背景人口按份数分；废墟里没人)
    public static float PeopleOf(RuralSettlement settlement, City city)
    {
        if (settlement == null || city == null || settlement.ruined_at >= 0d) return 0f;
        (float living, float ruined) = Weights(city);
        return CityPopulationSystem.GetBackgroundTotal(city) * Weight(settlement) / (CityWeight + living + ruined);
    }

    // 毁的时候住在那里的人一半死伤逃散
    private static void Displace(City city, RuralSettlement settlement)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null) return;
        (float living, float ruined) = Weights(city);
        float rate = BurnPopulationLoss * Weight(settlement) / (CityWeight + living + ruined);
        foreach (PopGroup group in data.groups) CityPopulationSystem.RemoveBackground(group, group.Background * rate);
    }

    #endregion

    #region 兵火与重建

    // 举城投降、望风归附时成片易手的地不烧(见 FrontSystem.Surrender、CityExtension.AddOccupiedTileZone)
    public static bool Sparing;

    // 敌军占下一块地(CityExtension.AddOccupiedTileZone)
    public static void OnZoneOccupied(City city, Kingdom occupier, TileZone zone)
    {
        if (Sparing || All.Count == 0 || city?.kingdom == null || occupier == null || occupier == city.kingdom) return;
        List<RuralSettlement> list = InZone(zone);
        if (list.Count == 0) return;
        Kingdom owner = city.kingdom;
        bool raid = occupier.getWars().Any(war => war != null && !war.hasEnded() && war.GetEmpireWarType() == EmpireWarType.劫掠 &&
                                                  (war.isAttacker(occupier) && war.isDefender(owner) ||
                                                   war.isDefender(occupier) && war.isAttacker(owner)));
        double now = World.world.getCurWorldTime();
        foreach (RuralSettlement settlement in list)
        {
            if (settlement.ruined_at >= 0d) continue;
            float chance = settlement.town ? raid ? RaidTownBurn : TownBurn : raid ? RaidVillageBurn : VillageBurn;
            if (UnityEngine.Random.value >= chance) continue;
            Displace(city, settlement);
            settlement.ruined_at = now;
            Revision++;
        }
    }

    // 每月：在原址重建(见 PureMapPatch.RefreshUrbanSlice)；新建村镇的划地每次都办
    public static void Tick()
    {
        ClaimPending();
        if (World.world == null || All.Count == 0) return;
        double now = World.world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        foreach (RuralSettlement settlement in All.Values)
        {
            if (settlement.ruined_at < 0d || now >= settlement.ruined_at && Date.getYearsSince(settlement.ruined_at) < RebuildYears) continue;
            TileZone zone = World.world.GetTile(settlement.x, settlement.y)?.zone;
            City city = zone?.city;
            if (city?.kingdom == null || city.isRekt() || city.kingdom.hasEnemies() || city.GetTileZoneOccupier(zone) != null) continue;
            if (UnityEngine.Random.value >= RebuildChancePerYear / 12f) continue;
            settlement.ruined_at = -1d;
            settlement.founded = now;
            Revision++;
        }
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.General;

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
// 手动营建(用户 2026-10-17 "手动把村子划入城市势力范围""手动放置村/镇……和销毁他们"，见 SettlementPowers)：
//   · 立村、设镇：点在城的地上归这座城；点在紧挨城界的无主之地上，划给挨着的城里离得最近的那座，连同四周无主之地；
//     小路顺着地势自动连上(见 UrbanFabric.PlaceTrails)；
//   · 毁弃：完好的毁为废墟(同兵火，住的人一半逃散，太平年月会重建)；对废墟再点一下彻底抹去，城郊不再在原处另建；
//   · 改隶：一处村镇连同它那一片地(见 CityDistricts)改归一座接壤的城。
// 新城市规划(用户 2026-10-17 问答)：规划的单位是一处处村镇，位置、档位、名字由人定，里面的布局按各自的种子随机——
//   · 点开村镇的小窗口(见 SettlementWindow)：改名、升降级(村 → 镇 → 有墙大镇)、换个样子(只换这一处的种子)、毁弃或重建；
//   · 世界法则「固定城乡格局」：开了以后 AI 不新建村镇、兵火不烧、废墟不自己重建，只听手动(用来摆符合史实的地图)。
// 关城、军城(用户 2026-10-17 "城=关城/军城")：只有城墙、驻军，不住百姓、不算村镇数，只能手动筑(营建页「筑关」)；
//   三级(关隘、关城、雄关)；打仗时敌军攻它那一片(见 CityDistricts)，守方战力按等级多三成、六成、九成(见 FortFactor)；
//   不毁于兵火，可以毁弃。
public static class RuralSettlementSystem
{
    private const float VillageWeight = 1f, TownWeight = 3f, CityWeight = 6f, RuinedOutputLoss = 0.6f, BurnPopulationLoss = 0.5f;
    private const float VillageBurn = 0.35f, TownBurn = 0.25f, RaidVillageBurn = 0.7f, RaidTownBurn = 0.5f;
    private const int RebuildYears = 5;
    private const float RebuildChancePerYear = 0.2f;
    private static double _lastMonth = -1d;

    private static readonly Dictionary<int, RuralSettlement> All = new();
    // 地块 → 落在这块地上的镇村(读档后头一次用到时建)；彻底抹去的另记一份(只用来占住原处)
    private static Dictionary<int, List<RuralSettlement>> _byZone, _clearedByZone;
    private static int _nextId = 1;

    // 镇村有增减、毁建就加一(城市平面图的指纹里算进去，见 PureMapPatch.Stamp)
    public static int Revision { get; private set; }

    public static void ResetWorldState()
    {
        All.Clear();
        _byZone = _clearedByZone = null;
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
        var cleared = new Dictionary<int, List<RuralSettlement>>();
        if (World.world == null) return index;
        foreach (RuralSettlement settlement in All.Values)
        {
            TileZone zone = World.world.GetTile(settlement.x, settlement.y)?.zone;
            if (zone == null) continue;
            Dictionary<int, List<RuralSettlement>> target = settlement.cleared ? cleared : index;
            if (!target.TryGetValue(zone.id, out List<RuralSettlement> list)) target[zone.id] = list = new List<RuralSettlement>();
            list.Add(settlement);
        }
        _byZone = index;
        _clearedByZone = cleared;
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

    // 固定城乡格局(世界法则)：村镇只听手动
    public static bool Fixed => GameLibrary.EmpireCraftWorldLawLibrary.empirecraft_law_fixed_settlements?.isEnabled() == true;

    // 这座城这会儿能不能新建村镇：固定城乡格局时不建；打仗时不建(地块在交战双方之间易手，丢了地的城数目一少就补，
    // 收回来又多出一份，用户 2026-10-17 "每次战线变化的时候就重复刷新消失和生成新的村镇")
    public static bool MayGrow(City city) =>
        !Fixed && city?.data != null && !city.isRekt() && (city.kingdom == null || !city.kingdom.hasEnemies()) &&
        !FrontSystem.Battles.ContainsKey(city.data.id);

    // 这座城这会儿能不能在城外新建村镇
    public static bool MayFound(City city)
    {
        if (!MayGrow(city) || city.kingdom == null || ModClass.IS_CLEAR ||
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
            ClaimAround(city, settlement, gateX, gateY, far);
        }
    }

    // 镇村那块地，和四周离城心 far 格以内的无主之地划进本城
    private static void ClaimAround(City city, RuralSettlement settlement, int gateX, int gateY, float far)
    {
        TileZone seat = World.world.GetTile(settlement.x, settlement.y)?.zone;
        if (seat == null) return;
        if (seat.city == null && CanClaim(city, seat)) city.addZone(seat);
        if (seat.city != city || seat.neighbours_all == null) return;
        foreach (TileZone zone in seat.neighbours_all)
        {
            if (zone?.centerTile == null || !CanClaim(city, zone)) continue;
            float dx = zone.centerTile.x - gateX, dy = zone.centerTile.y - gateY;
            if (dx * dx + dy * dy <= far * far) city.addZone(zone);
        }
        Revision++;
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
        // 被城区盖住、没画出来的不算(见 UrbanFabric.PlaceCountryside)
        bool Hidden(RuralSettlement settlement)
        {
            TileZone seat = World.world.GetTile(settlement.x, settlement.y)?.zone;
            City owner = seat?.city;
            return owner != null && (seat == owner.getTile()?.zone || PureMapPatch.BuiltZones(owner)?.Contains(seat.id) == true);
        }
        RuralSettlement best = null;
        float bestDistance = float.MaxValue;
        var zones = new List<TileZone> { zone };
        if (zone.neighbours_all != null) zones.AddRange(zone.neighbours_all);
        foreach (TileZone around in zones)
            foreach (RuralSettlement settlement in InZone(around))
            {
                float dx = settlement.x - tile.x, dy = settlement.y - tile.y, d = dx * dx + dy * dy;
                float reach = settlement.fort ? settlement.size + 3.5f : settlement.town ? settlement.size + 1.5f : 2.5f;
                if (d > reach * reach || d >= bestDistance || Hidden(settlement)) continue;
                best = settlement;
                bestDistance = d;
            }
        return best;
    }

    #region 住人、产粮交税

    private static float Weight(RuralSettlement settlement) => settlement.fort ? 0f : settlement.town ? TownWeight : VillageWeight;

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

    #region 手动营建

    // 彻底抹去的镇村占着原处：(x, y) 曼哈顿距离 room 以内有没有(城郊自动选址时跳过，见 UrbanFabric.PlaceCountryside)
    public static bool ClearedNear(int x, int y, int room)
    {
        TileZone zone = World.world?.GetTile(x, y)?.zone;
        if (zone == null || All.Count == 0) return false;
        ByZone();
        if (_clearedByZone == null || _clearedByZone.Count == 0) return false;
        foreach (TileZone around in Around(zone))
            if (_clearedByZone.TryGetValue(around.id, out List<RuralSettlement> list) &&
                list.Exists(settlement => Math.Abs(settlement.x - x) + Math.Abs(settlement.y - y) < room)) return true;
        return false;
    }

    private static IEnumerable<TileZone> Around(TileZone zone)
    {
        yield return zone;
        if (zone.neighbours_all == null) yield break;
        foreach (TileZone neighbour in zone.neighbours_all)
            if (neighbour != null) yield return neighbour;
    }

    public static RuralSettlement ById(int id) =>
        All.TryGetValue(id, out RuralSettlement settlement) && !settlement.cleared ? settlement : null;

    // 落在谁的地上
    public static City CityOf(RuralSettlement settlement) =>
        settlement == null ? null : World.world?.GetTile(settlement.x, settlement.y)?.zone?.city;

    // 档位：0 村、1 镇、2 有墙大镇
    public const int MaxTier = 2;

    public static int Tier(RuralSettlement settlement) => settlement == null ? 0
        : settlement.fort ? Math.Max(1, Math.Min(3, settlement.size)) - 1
        : !settlement.town ? 0 : settlement.walled ? 2 : 1;

    public static string TierName(int tier) =>
        LM.Get(tier switch { 0 => "settlement_kind_village", 1 => "settlement_kind_town", _ => "settlement_kind_walled_town" });

    // 关城按等级叫关隘、关城、雄关
    public static string TierName(RuralSettlement settlement) =>
        settlement?.fort == true ? LM.Get("fort_level_" + Tier(settlement)) : TierName(Tier(settlement));

    // 关城守御：敌军攻的那块地所在的一片(见 CityDistricts)里有完好的关城，守方战力按等级多三成、六成、九成
    public static float FortFactor(City city, TileZone zone)
    {
        if (All.Count == 0 || city?.zones == null || zone == null) return 1f;
        int best = 0;
        foreach (TileZone part in CityDistricts.ZonesOf(city, zone))
            foreach (RuralSettlement settlement in InZone(part))
                if (settlement.fort && settlement.ruined_at < 0d) best = Math.Max(best, Math.Max(1, Math.Min(3, settlement.size)));
        return 1f + 0.3f * best;
    }

    // 改档位(窗口里的升级、降级)：镇三格见方，有墙大镇四格(城大的五格)
    public static void SetTier(RuralSettlement settlement, int tier)
    {
        if (settlement == null || settlement.cleared) return;
        tier = Math.Max(0, Math.Min(MaxTier, tier));
        if (tier == Tier(settlement)) return;
        City city = CityOf(settlement);
        if (settlement.fort) settlement.size = tier + 1;
        else
        {
            settlement.town = tier >= 1;
            settlement.walled = tier >= 2;
            settlement.size = tier switch
            {
                0 => 0,
                1 => 3,
                _ => city != null && city.GetOrCreate().urban_tier >= 3 ? 5 : 4
            };
        }
        Revision++;
        if (city != null) PureMapPatch.Redraw(city);
    }

    public static void Rename(RuralSettlement settlement, string name)
    {
        string value = (name ?? "").Trim();
        if (settlement == null || value.Length == 0 || value == settlement.name) return;
        settlement.name = value.Length > 8 ? value.Substring(0, 8) : value;
        Revision++;
        City city = CityOf(settlement);
        if (city != null) PureMapPatch.Redraw(city);
    }

    // 换个样子：只换这一处的布局种子，位置、档位、名字都不变
    public static void Reroll(RuralSettlement settlement)
    {
        if (settlement == null) return;
        long seed;
        do seed = ((long)UnityEngine.Random.Range(1, int.MaxValue) << 16) ^ UnityEngine.Random.Range(0, 1 << 16);
        while (seed == 0L || seed == settlement.seed);
        settlement.seed = seed;
        Revision++;
        City city = CityOf(settlement);
        if (city != null) PureMapPatch.Redraw(city);
    }

    // 手动重建废墟
    public static void Rebuild(RuralSettlement settlement)
    {
        if (settlement == null || settlement.ruined_at < 0d) return;
        settlement.ruined_at = -1d;
        settlement.founded = World.world.getCurWorldTime();
        Revision++;
        City city = CityOf(settlement);
        if (city != null) PureMapPatch.Redraw(city);
    }

    // 画法用的种子：按位置，换过样子的再加上它自己的种子(见 UrbanFabric.SettlementKey)
    public static long SeedAt(int x, int y) => At(x, y)?.seed ?? 0L;

    // 名字(还没标注过、没起名的写"村""镇")
    public static string Label(RuralSettlement settlement) =>
        !string.IsNullOrEmpty(settlement?.name) ? settlement.name
            : LM.Get(settlement?.fort == true ? "settlement_kind_fort"
                : settlement?.town == true ? "settlement_kind_town" : "settlement_kind_village");

    // 立村、设镇、筑关：返回提示文字
    public static string Place(WorldTile tile, bool town, bool fort = false)
    {
        if (!PureMapMode.Enabled) return LM.Get("settlement_need_pure_map");
        if (!UrbanFabric.Buildable(tile)) return LM.Get("settlement_place_no_land");
        TileZone zone = tile.zone;
        City city = zone.city;
        bool claim = city == null;
        if (claim)
        {
            // 紧挨城界的无主之地：挨着的城里离得最近、领得到这块地的那座
            float best = float.MaxValue;
            foreach (TileZone neighbour in zone.neighbours_all ?? Array.Empty<TileZone>())
            {
                City other = neighbour?.city;
                WorldTile center = other?.getTile();
                if (center == null || other.isRekt() || !CanClaim(other, zone)) continue;
                float d = Toolbox.SquaredDistTile(center, tile);
                if (d >= best) continue;
                best = d;
                city = other;
            }
        }
        if (city?.data == null || city.isRekt()) return LM.Get("settlement_place_no_city");
        string cityName = city.GetCityName();
        if (CityStyleSystem.Established(city) == "nomad") return string.Format(LM.Get("settlement_place_nomad"), cityName);
        HashSet<int> built = PureMapPatch.BuiltZones(city);
        if (zone == city.getTile()?.zone || built != null && built.Contains(zone.id))
            return string.Format(LM.Get("settlement_place_urban"), cityName);
        int room = town || fort ? 9 : 6;
        if (FactorySystem.Near(tile.x, tile.y, 3)) return LM.Get("factory_place_no_room");
        foreach (TileZone around in Around(zone))
            foreach (RuralSettlement other in InZone(around))
                if (Math.Abs(other.x - tile.x) + Math.Abs(other.y - tile.y) < room + (other.town ? other.size : 0))
                    return string.Format(LM.Get("settlement_place_crowded"), Label(other));
        // 原处抹去的占位让出来
        if (_clearedByZone != null)
            foreach (TileZone around in Around(zone))
                if (_clearedByZone.TryGetValue(around.id, out List<RuralSettlement> list))
                    foreach (RuralSettlement gone in list.FindAll(gone => Math.Abs(gone.x - tile.x) + Math.Abs(gone.y - tile.y) < room))
                    {
                        list.Remove(gone);
                        All.Remove(gone.id);
                    }
        int size = fort ? 1 : town ? (city.GetOrCreate().urban_tier >= 3 ? 4 : 3) : 0;
        RuralSettlement settlement = Add(tile.x, tile.y, town && !fort, size, false);
        settlement.fort = fort;
        if (claim)
        {
            WorldTile center = city.getTile();
            ClaimAround(city, settlement, center?.x ?? tile.x, center?.y ?? tile.y, float.MaxValue);
        }
        PureMapPatch.Redraw(city);
        return string.Format(LM.Get(fort ? "settlement_placed_fort" : town ? "settlement_placed_town" : "settlement_placed_village"), cityName);
    }

    // 毁弃：完好的毁为废墟，废墟彻底抹去；这里没有镇村时返回 null
    public static string Raze(WorldTile tile) => Raze(Near(tile));

    public static string Raze(RuralSettlement settlement)
    {
        if (settlement == null) return null;
        City city = World.world.GetTile(settlement.x, settlement.y)?.zone?.city;
        string name = Label(settlement);
        if (settlement.ruined_at < 0d)
        {
            if (city != null) Displace(city, settlement);
            settlement.ruined_at = World.world.getCurWorldTime();
            Revision++;
            if (city != null) PureMapPatch.Redraw(city);
            return string.Format(LM.Get("settlement_razed"), name);
        }
        Erase(settlement);
        if (city != null) PureMapPatch.Redraw(city);
        return string.Format(LM.Get("settlement_erased"), name);
    }

    private static void Erase(RuralSettlement settlement)
    {
        settlement.cleared = true;
        TileZone zone = World.world.GetTile(settlement.x, settlement.y)?.zone;
        ByZone();
        if (zone != null)
        {
            if (_byZone.TryGetValue(zone.id, out List<RuralSettlement> list)) list.Remove(settlement);
            if (!_clearedByZone.TryGetValue(zone.id, out List<RuralSettlement> gone)) _clearedByZone[zone.id] = gone = new List<RuralSettlement>();
            gone.Add(settlement);
        }
        Revision++;
    }

    // 改隶：这处村镇连同它那一片地改归 target(须与那一片接壤)
    public static string Assign(RuralSettlement settlement, City target)
    {
        TileZone seat = settlement == null ? null : World.world.GetTile(settlement.x, settlement.y)?.zone;
        City source = seat?.city;
        if (seat == null || target?.data == null || target.isRekt()) return LM.Get("settlement_place_no_city");
        string name = Label(settlement), targetName = target.GetCityName();
        if (source == target) return string.Format(LM.Get("settlement_assign_same"), name, targetName);
        List<TileZone> zones = source == null ? new List<TileZone> { seat } : CityDistricts.ZonesOfSettlement(source, settlement);
        if (!zones.Contains(seat)) zones.Add(seat);
        TileZone center = source?.getTile()?.zone;
        if (center != null) zones.Remove(center);
        HashSet<int> core = source == null ? null : PureMapPatch.BuiltZones(source);
        if (core != null) zones.RemoveAll(zone => core.Contains(zone.id));
        if (zones.Count == 0) return string.Format(LM.Get("settlement_place_urban"), source?.GetCityName() ?? "");
        if (source != null && zones.Exists(zone => source.GetTileZoneOccupier(zone) != null))
            return string.Format(LM.Get("settlement_assign_occupied"), name);
        var set = new HashSet<TileZone>(zones);
        bool beside = zones.Exists(zone => zone.neighbours_all != null &&
                                           Array.Exists(zone.neighbours_all, neighbour => neighbour?.city == target && !set.Contains(neighbour)));
        if (!beside) return string.Format(LM.Get("settlement_assign_far"), name, targetName);
        foreach (TileZone zone in zones) target.addZone(zone);
        Revision++;
        CityDistricts.Reset();
        PureMapPatch.Redraw(target);
        if (source != null) PureMapPatch.Redraw(source);
        return string.Format(LM.Get("settlement_assign_done"), name, targetName);
    }

    #endregion

    #region 兵火与重建

    // 举城投降、望风归附时成片易手的地不烧(见 FrontSystem.Surrender、CityExtension.AddOccupiedTileZone)
    public static bool Sparing;

    // 敌军占下一块地(CityExtension.AddOccupiedTileZone)
    public static void OnZoneOccupied(City city, Kingdom occupier, TileZone zone)
    {
        if (Sparing || Fixed || All.Count == 0 || city?.kingdom == null || occupier == null || occupier == city.kingdom) return;
        List<RuralSettlement> list = InZone(zone);
        if (list.Count == 0) return;
        Kingdom owner = city.kingdom;
        bool raid = occupier.getWars().Any(war => war != null && !war.hasEnded() && war.GetEmpireWarType() == EmpireWarType.劫掠 &&
                                                  (war.isAttacker(occupier) && war.isDefender(owner) ||
                                                   war.isDefender(occupier) && war.isAttacker(owner)));
        double now = World.world.getCurWorldTime();
        foreach (RuralSettlement settlement in list)
        {
            if (settlement.ruined_at >= 0d || settlement.fort) continue;
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
        if (World.world == null || All.Count == 0 || Fixed) return;
        double now = World.world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        foreach (RuralSettlement settlement in All.Values)
        {
            if (settlement.cleared || settlement.fort || settlement.ruined_at < 0d ||
                now >= settlement.ruined_at && Date.getYearsSince(settlement.ruined_at) < RebuildYears) continue;
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

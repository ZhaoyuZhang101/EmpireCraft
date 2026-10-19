using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战线(借鉴钢铁雄心)：每月结算一次。
//   ① 战线段：一座敌城里，与己方控制的地块相邻的敌控地块(己方城市被敌军占去的地块同理，可收复)；
//      与本方没有陆上接触的敌国(隔海、隔荒野)，就算别处还有陆上战线，也各开一路登陆(每月至多两路)：按离本国首都
//      由近及远挑够得着的敌城，从其外缘地块登陆——跨海登陆是硬限制：目标那片陆地上要有一处能靠岸的海滩，航线从本国港口
//      出发、不经过敌方制海海区、在舰种航程内(见 SeaLanding、NavySystem)；装得上运输船的兵力才能派去，整军装不下就按
//      剩下的运力分出先遣军(见 CorpsSystem.Detach)；登陆头一个月攻方战力 ×0.7；
//   ② 分派：各国先按上月敌军的攻势分兵守城，余下的军集中攻打一两段；组织度太低的军回乡休整；
//      (用户转玩家 2026-10-17 "十座城，五个军团全部决定守首都，太怪了")攻势按敌军离那座城的远近打折(四块地以内全算、
//      十块以内算一半、更远算四分之一，免得远在千里的敌军一把首都定为目标就把全军拉回去)；守军不够分时按各城攻势比例分，
//      不让攻势最大的一座城把军全拿走；没派到的预备军不再全挤在驻地(中央军的驻地都在首都)，分驻本国各城——
//      先驻都城与边境大城(见 Stations)；
//      军令(见 CorpsCommandSystem)：固守的城排第一、至少拿六成兵力；攻打的城所在(或离它最近)的一段，出征的军都投那里；
//   ③ 交战：攻方(兵力×组织度×火力) 对 守方(兵力×组织度×火力×(1.25+堑壕)，无军驻守时由团练抵抗)，
//      双方按战况伤亡、掉组织度；攻方占优就占领一块紧挨着进攻军团的地块，离它最近的进攻军团随即进驻
//      (一格一格推进，部队没到的地方战线不动；沿用地块占领模式，满城即陷落)，守方占优就夺回守军身边的一块；
//      守军还在时城中最后一块地不会丢；组织度归零的军溃退。
//      主城池(城心那块地)陷落即整城投降(用户 2026-10-17 "军队推进攻下主城池那么城市会直接投降")：守军还在时据城死守、
//      城池放到最后打；守军溃散后先取城池，城池一破，余下的地块一并归攻方(见 Surrender)。
//      不论怎么破的(军团推进、平滑战线、小人占领、高正统扩散、转交)，主城池那片一落入敌手整个行政区都投降
//      (用户 2026-10-17 "一旦主城被破整个行政区都会被迫投降"，统一挂在 CityExtension.AddOccupiedTileZone，见 OnZoneTaken)。
//      困守一隅(用户 2026-10-17 "攻打一个地方就只剩这么一丝地盘也能不停的反攻回来")：守方只剩不到三分之一的地时，
//      守军战力按剩下的比例打折(最低五成)；团练只从守方还占着的那部分城里抽；攻方军团在上面或紧挨着看守的地，
//      守方不打赢就收不回来(以前攻方一退下休整，守方就一块块白白收回，看着像不停反攻)。
//      辖区按村镇划(用户 2026-10-17 选"城的辖区按村镇划"，见 CityDistricts)：城区一片、其余每块地归最近的村镇，
//      推进与收复都按片——拿下一片里紧挨着军团的一块地，那一片就一并到手；城区那片就是主城池；守军还在时城中最后一片不丢。占领区的年度扩散、正统扩张在军团模式下都不生效
//      (见 CityExtension.TryYearlyOccupationSpread、TrySpreadOccupiedZonesByMandate)。
//      地形(见 TerrainSystem)看攻方要推进的那块地：攻方战力打折、双方额外掉组织度、限制每月推进的块数。
// 远距离不交战(用户 2026-10-15："能不能阻止远距离交战，但是很近的海岛却可以")：只打本方(连同所在帝国)够得着的敌城——
//   与本方的城接壤、同一块陆地上离本方某座城不过 LandReachTiles 格，或隔海不远(离本方水师港口不过 StraitReach 的距离)；
//   远方的盟友不再千里驰援，远洋登陆打不了，够不着的仗只是名义上开着。
//   陆上不穿第三国(用户 2026-10-17 "又在跨国打架""军队还过不去")：同一块陆地上离得近，还要从本方控制的地块出发、
//   只经过无主之地与本方、盟友、交战敌国的地块走得到那座城(见 LandRoute)；中间隔着不相干的第三国就够不着，
//   军团行军也尽量绕开第三国的地(见 CorpsSystem.FindPath)。
// 军团须开到战线附近(两块地块以内)才参战。
// 叛乱初起时被叛方有集结期(见 MusterSystem)：不反攻、只有附近的军能守城、守方战力打折。
public static partial class FrontSystem
{
    private const int ArriveDistance = 2;
    private const float DefenderBonus = 1.25f;
    // 红夷大炮破城：攻方有重炮营时守方城池守势降到这么多，堑壕只剩一半、团练抵抗剩七成(见 CorpsArmSystem.Breaches)
    private const float BreachedDefenderBonus = 1.1f, BreachedEntrench = 0.5f, BreachedMilitia = 0.7f;
    private const float CasualtyRate = 0.16f;
    private const float RetreatOrg = 10f;
    private const float ReadyOrg = 25f;
    private const int MaxOffensives = 3;
    private const float LandReachTiles = 80f;

    public sealed class BattleView
    {
        public long city_id;
        public int zone_id = -1;
        public float ratio;
        public float attack, defend;
        public long attacker_id = -1L, defender_id = -1L;
        public int taken;
        public double time;
        public FrontTerrain terrain;
    }

    public static readonly Dictionary<long, BattleView> Battles = new();
    private static readonly HashSet<long> Engaged = new();
    private static readonly Dictionary<long, List<(double time, string text)>> Logs = new();

    public static bool EngagedLastMonth(CorpsData corps) => corps != null && Engaged.Contains(corps.id);

    public static void ResetWorldState()
    {
        Battles.Clear();
        Engaged.Clear();
        Logs.Clear();
        ResetSmooth();
        TerrainSystem.ResetWorldState();
        SeaLandingDiagnostics.ResetWorldState();
        _contested.Clear();
        _contestedAt = -1;
        _fronts.Clear();
        _frontsNext.Clear();
        LandRoutes.Clear();
        MonthPressure.Clear();
        MonthLandingSectors.Clear();
        _assignFrame = -1;
    }

    #region 敌我

    public static Kingdom Realm(Kingdom kingdom) =>
        kingdom?.GetEmpire()?.CoreKingdom is Kingdom core && core.data != null && !core.isRekt() ? core : kingdom;

    public static bool Hostile(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null || a == b || a.isRekt() || b.isRekt()) return false;
        if (a.isInWarWith(b)) return true;
        Kingdom ra = Realm(a), rb = Realm(b);
        if (ra == rb) return false;
        return ra.isInWarWith(rb) || ra.isInWarWith(b) || a.isInWarWith(rb);
    }

    // 真打起来的两方(各取所在帝国的核心)：本月有战线段的(见 Assign)。地图上的战线朱红只画在这几对之间
    // (用户 2026-10-17 "明明没大仗还有战线"，选"真打起来的地方才画"：以前只看两国交没交战，宣了战却够不着、没接上火的也画)
    private static HashSet<(long, long)> _fronts = new(), _frontsNext = new();

    public static bool HasFront(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null) return false;
        if (_fronts.Contains((a.id, b.id))) return true;
        Kingdom ra = Realm(a), rb = Realm(b);
        return ra?.data != null && rb?.data != null && _fronts.Contains((ra.id, rb.id));
    }

    // 记两国本身(同一帝国的成员互相打时只有这一对)，再记各自帝国的核心
    private static void NoteFront(Kingdom side, Kingdom enemy)
    {
        if (side?.data == null || enemy?.data == null || side == enemy) return;
        _frontsNext.Add((side.id, enemy.id));
        _frontsNext.Add((enemy.id, side.id));
        Kingdom ra = Realm(side), rb = Realm(enemy);
        if (ra?.data == null || rb?.data == null || ra == rb) return;
        _frontsNext.Add((ra.id, rb.id));
        _frontsNext.Add((rb.id, ra.id));
    }

    // 本月的名单换上；变了的几方各城重画
    private static void SwapFronts()
    {
        var changed = new HashSet<long>();
        foreach ((long a, long b) in _fronts)
            if (!_frontsNext.Contains((a, b))) { changed.Add(a); changed.Add(b); }
        foreach ((long a, long b) in _frontsNext)
            if (!_fronts.Contains((a, b))) { changed.Add(a); changed.Add(b); }
        (_fronts, _frontsNext) = (_frontsNext, _fronts);
        _frontsNext.Clear();
        if (changed.Count > 0) SmoothBorders.RefreshFronts(changed);
    }

    public static bool Friendly(Kingdom a, Kingdom b) =>
        a?.data != null && b?.data != null && (a == b || !Hostile(a, b) && (Realm(a) == Realm(b) || a.isInWarOnSameSide(b)));

    // 占领记在谁名下：本国与城主正式交战就是本国，否则(帝国藩属替宗主作战)记在宗主名下
    public static Kingdom Belligerent(Kingdom side, Kingdom owner)
    {
        if (side == null || owner == null || side.isInWarWith(owner)) return side;
        Kingdom realm = Realm(side);
        return realm != null && realm != side && (realm.isInWarWith(owner) || realm.isInWarWith(Realm(owner))) ? realm : side;
    }

    public static Kingdom Controller(TileZone zone)
    {
        City city = zone?.city;
        if (city?.data == null || city.isRekt()) return null;
        return city.GetTileZoneOccupier(zone) ?? city.kingdom;
    }

    #endregion

    #region 战线段

    private sealed class Sector
    {
        public City City;
        public readonly List<TileZone> Targets = new();
        public bool Landing;
    }

    // 敌城里可以下手的地块：敌控、且与己方控制的地块相邻；不可通行的高山两边都不算(见 TerrainSystem.Impassable)
    private static List<TileZone> TargetZones(Kingdom side, City city)
    {
        var result = new List<TileZone>();
        if (city?.zones == null) return result;
        foreach (TileZone zone in city.zones)
        {
            if (zone == null || !Hostile(side, Controller(zone)) || TerrainSystem.Impassable(zone)) continue;
            foreach (TileZone neighbour in zone.neighbours ?? Array.Empty<TileZone>())
            {
                if (neighbour == null || neighbour.world_edge || TerrainSystem.Impassable(neighbour)) continue;
                Kingdom controller = Controller(neighbour);
                if (controller == null || !Friendly(side, controller)) continue;
                result.Add(zone);
                break;
            }
        }
        return result;
    }

    // 登陆点：敌城外缘(邻接无主地块或海)的地块
    private static List<TileZone> LandingZones(City city)
    {
        var result = new List<TileZone>();
        if (city?.zones == null) return result;
        foreach (TileZone zone in city.zones)
        {
            if (zone == null || TerrainSystem.Impassable(zone)) continue;
            if ((zone.neighbours ?? Array.Empty<TileZone>()).Any(neighbour => neighbour != null && !neighbour.world_edge &&
                                                                              neighbour.city == null))
                result.Add(zone);
        }
        return result;
    }

    private static List<Kingdom> _atWar = new();

    // 一个月内敌对国一样的国家(同一帝国的成员通常如此)共用一份陆上战线段。以前帝国每个成员都把全部敌城的地块扫一遍，
    // 几十个成员时月度结算一次卡几百毫秒。跨海登陆点按各国的都城与航线算，不共用
    private static readonly Dictionary<string, List<Sector>> SectorCache = new();

    private static List<Sector> CachedSectors(Kingdom side)
    {
        var key = new global::System.Text.StringBuilder();
        key.Append(Realm(side).id).Append(':');
        foreach (Kingdom other in _atWar)
            if (Hostile(side, other)) key.Append(other.id).Append(',');
        string signature = key.ToString();
        if (SectorCache.TryGetValue(signature, out List<Sector> shared))
        {
            NavySystem.DesiredLanding.Remove(side.id);
            return shared;
        }
        List<Sector> sectors = Sectors(side);
        if (!sectors.Any(sector => sector.Landing)) SectorCache[signature] = sectors;
        return sectors;
    }

    // 本方(连同所在帝国各成员国)的城
    private static HashSet<City> HomeCities(Kingdom side)
    {
        var home = new HashSet<City>();
        IEnumerable<Kingdom> members = side.GetEmpire() is Layer.Empire empire && !empire.isRekt() && !empire.IsArchived()
            ? empire.kingdoms_list.Append(side) : new[] { side };
        foreach (Kingdom member in members)
        {
            if (member?.cities == null || member.isRekt()) continue;
            foreach (City city in member.cities)
                if (city?.data != null && !city.isRekt()) home.Add(city);
        }
        return home;
    }

    // 够得着这座城：本方的城、与本方的城接壤、同一块陆地上离本方某座城不远，或隔海不远
    private static bool InReach(Kingdom side, City city, HashSet<City> home)
    {
        if (home.Contains(city)) return true;
        if (city.neighbours_cities != null && city.neighbours_cities.Any(home.Contains)) return true;
        WorldTile tile = city.getTile();
        if (tile == null) return false;
        float limit = LandReachTiles * LandReachTiles;
        foreach (City own in home)
        {
            WorldTile at = own.getTile();
            if (at != null && at.isSameIsland(tile) && Toolbox.SquaredDistTile(at, tile) <= limit && LandRoute(side, city)) return true;
        }
        return StraitReach.Reaches(side, city);
    }

    // 陆路：从这座城往外，只经过能走的地块(见 Passable)找得到本方控制的地块；按(本方帝国, 城)缓存三个月。
    // 以前每月清空，每月月初各国对近处每座敌城重新广度搜索(找不到的要搜满 6000 块)；
    // 本方的敌国一变(宣战、议和)，能走的地就变了，记下时的敌国签名对不上就重找
    private const int LandRouteSearch = 6000, LandRouteMonths = 3;
    private static readonly Dictionary<(long, long), (bool found, double at, long signature)> LandRoutes = new();
    // 当前在算战线段的一方的敌国签名(见 Sectors)
    private static long _routeSignature;

    // 能走的地块：陆地、不是高山，归属无主或本方、盟友、交战敌国(不相干的第三国不走)
    public static bool Passable(Kingdom side, TileZone zone)
    {
        if (zone == null || TerrainSystem.Impassable(zone) || SeaRegionSystem.IsSea(zone)) return false;
        Kingdom owner = Controller(zone);
        return owner == null || owner.wild || Friendly(side, owner) || Hostile(side, owner);
    }

    private static bool LandRoute(Kingdom side, City target)
    {
        if (target?.zones == null || target.data == null) return false;
        var key = (Realm(side).id, target.data.id);
        double now = World.world.getCurWorldTime();
        if (LandRoutes.TryGetValue(key, out var known) && known.signature == _routeSignature && now >= known.at &&
            Date.getMonthsSince(known.at) < LandRouteMonths) return known.found;
        var seen = new HashSet<int>();
        var queue = new Queue<TileZone>();
        foreach (TileZone zone in target.zones)
            if (zone != null && seen.Add(zone.id)) queue.Enqueue(zone);
        bool found = false;
        while (queue.Count > 0 && seen.Count < LandRouteSearch && !found)
        {
            TileZone zone = queue.Dequeue();
            foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
            {
                if (next == null || !seen.Add(next.id)) continue;
                if (Controller(next) is Kingdom owner && Friendly(side, owner) && !TerrainSystem.Impassable(next))
                {
                    found = true;
                    break;
                }
                if (Passable(side, next)) queue.Enqueue(next);
            }
        }
        if (LandRoutes.Count > 20000) LandRoutes.Clear();
        LandRoutes[key] = (found, now, _routeSignature);
        return found;
    }

    private static List<Sector> Sectors(Kingdom side)
    {
        NavySystem.DesiredLanding.Remove(side.id);
        var sectors = new Dictionary<City, Sector>();
        HashSet<City> home = HomeCities(side);
        void Collect(City city)
        {
            if (city?.data == null || city.isRekt() || sectors.ContainsKey(city) || !InReach(side, city, home)) return;
            List<TileZone> targets = TargetZones(side, city);
            if (targets.Count == 0) return;
            var sector = new Sector { City = city };
            sector.Targets.AddRange(targets);
            sectors[city] = sector;
        }
        var hostiles = _atWar.Where(other => Hostile(side, other)).ToList();
        // 敌国签名：陆路缓存(见 LandRoute)只在敌国没变时沿用
        long signature = Realm(side).id;
        foreach (Kingdom hostile in hostiles) signature = unchecked(signature * 31 + hostile.id);
        _routeSignature = signature;
        foreach (Kingdom hostile in hostiles)
            foreach (City city in hostile.cities ?? new List<City>())
                Collect(city);
        // 己方(含盟友)被敌军占去地块的城：收复
        foreach (Kingdom friend in _atWar.Where(other => Friendly(side, other)))
            foreach (City city in friend.cities ?? new List<City>())
                if (city?.GetOrCreate().OccupiedStatus?.Count > 0) Collect(city);
        WorldTile origin = side.capital?.getTile();
        if (hostiles.Count == 0 || origin == null) return sectors.Values.ToList();
        // 与本方没有陆上接触的敌国(隔海、隔着荒野)：各开一路登陆，就算别处还有陆上战线。
        // 玩家反馈"远洋打仗还是有问题，两个隔海的国家一千五百年没打下来"：以前本方只要还有一条陆上战线(连同帝国别处的仗、
        // 要收复的盟城)就不登陆，而且只试离都城最近的一座城，那座城航线不通就整个放弃
        var contact = new HashSet<long>();
        foreach (Sector sector in sectors.Values)
        {
            if (sector.City.kingdom is Kingdom owner && Hostile(side, owner)) contact.Add(owner.id);
            else if (sector.City.GetOrCreate().OccupiedStatus is { } occupied)
                foreach (var pair in occupied)
                    if (pair.Value?.Count > 0) contact.Add(pair.Key);
        }
        int fronts = 0;
        foreach (Kingdom hostile in hostiles.Where(hostile => !contact.Contains(hostile.id))
                     .OrderBy(hostile => hostile.capital?.getTile() is WorldTile at ? Toolbox.SquaredDistTile(at, origin) : float.MaxValue))
        {
            if (fronts >= MaxLandingFronts) break;
            Sector beachhead = LandingSector(side, hostile, home, origin, out City wanted, out int route);
            // 跨海：记下想登陆的城(舰队据此多造运输船、去打通航线)；航线不通也记下最近的那座
            City desired = beachhead != null && !beachhead.City.getTile().isSameIsland(origin) ? beachhead.City : wanted;
            if (desired != null && !NavySystem.DesiredLanding.ContainsKey(side.id)) NavySystem.DesiredLanding[side.id] = desired.id;
            SeaLandingDiagnostics.Pair(side, hostile, beachhead?.City, wanted, route);
            if (beachhead == null || sectors.ContainsKey(beachhead.City)) continue;
            sectors[beachhead.City] = beachhead;
            fronts++;
        }
        return sectors.Values.ToList();
    }

    // 每月至多另开几路登陆；每个敌国至多试几座跨海的城
    private const int MaxLandingFronts = 2, MaxLandingTries = 8;

    // 登陆战线：敌国够得着的城里，按离本国都城由近及远试，挑第一座有登陆点、跨海时有能靠岸的海滩(航线不经敌方制海、
    // 在舰种航程内，见 SeaLanding.Beachhead)的城；跨海的登陆点挑临海的地块。
    // wanted：第一座够得着的跨海的城(航线不通也算，舰队据此去打通航线)；route：选中那座城的航线经过几个海区(不跨海为 0)
    private static Sector LandingSector(Kingdom side, Kingdom hostile, HashSet<City> home, WorldTile origin, out City wanted,
        out int route)
    {
        wanted = null;
        route = 0;
        int tries = 0;
        foreach (City city in (hostile.cities ?? new List<City>())
                     .Where(city => city?.data != null && !city.isRekt() && city.getTile() != null)
                     .OrderBy(city => Toolbox.SquaredDistTile(city.getTile(), origin)).ToList())
        {
            if (!InReach(side, city, home)) continue;
            List<TileZone> landing = LandingZones(city);
            if (landing.Count == 0) continue;
            if (!city.getTile().isSameIsland(origin))
            {
                wanted ??= city;
                if (++tries > MaxLandingTries) break;
                var (shore, path) = SeaLanding.Beachhead(side, city.getTile().zone);
                if (shore == null) continue;
                route = path.Count;
                List<TileZone> coastal = landing.Where(zone => SeaLanding.Waters(zone).Count > 0).ToList();
                if (coastal.Count > 0) landing = coastal;
            }
            var sector = new Sector { City = city, Landing = true };
            sector.Targets.AddRange(landing);
            return sector;
        }
        return null;
    }

    #endregion

    #region 月度结算

    // 上月各城的守军(按有效兵力)：选进攻段时估算难度用
    private static readonly Dictionary<long, float> DefenseByCity = new();

    // 月度结算分三段(CorpsSystem 分帧调用)：BeginMonth 一次；AssignMonth 逐国分派，可以分几帧；EndMonth 交战结算一次。
    // 上月攻势、本月登陆战线在三段之间保留
    private static readonly Dictionary<long, List<(Kingdom attacker, float strength)>> MonthPressure = new();
    private static readonly Dictionary<long, bool> MonthLandingSectors = new();
    private static double _monthNow;
    private static int _assignFrame = -1;

    // 一帧里结完整个月
    public static void MonthTick(double now)
    {
        BeginMonth(now);
        using (FrameProfiler.Measure("战线·分派"))
            foreach (Kingdom kingdom in World.world.kingdoms.ToList())
                AssignMonth(kingdom);
        EndMonth();
    }

    public static void BeginMonth(double now)
    {
        _monthNow = now;
        SeaLandingDiagnostics.MonthTick();
        MusterSystem.MonthTick();
        FactionPurgeSystem.MonthTick();
        _atWar = World.world.kingdoms.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
                                                       (kingdom.hasEnemies() || Realm(kingdom).hasEnemies())).ToList();
        DefenseByCity.Clear();
        CityDistricts.Reset();
        foreach ((Kingdom owner, CorpsData corps) in CorpsSystem.All())
        {
            if (corps.sector_city_id < 0 || corps.task is not ("defend" or "attack")) continue;
            City city = World.world.cities.get(corps.sector_city_id);
            if (city?.kingdom == null || !Friendly(owner, city.kingdom)) continue;
            DefenseByCity.TryGetValue(city.id, out float value);
            DefenseByCity[city.id] = value + Effective(corps);
        }
        // 上月各城受到的攻势(哪国、多少兵力)：守方只按敌军的攻势分兵
        Dictionary<long, List<(Kingdom attacker, float strength)>> pressure = MonthPressure;
        pressure.Clear();
        foreach ((Kingdom attacker, CorpsData corps) in CorpsSystem.All())
            if (corps.task == "attack" && corps.sector_city_id >= 0)
            {
                if (!pressure.TryGetValue(corps.sector_city_id, out var list))
                    pressure[corps.sector_city_id] = list = new List<(Kingdom, float)>();
                // 离那座城越远，攻势越虚：四块地以内全算、十块以内算一半、更远算四分之一
                TileZone target = World.world.cities.get(corps.sector_city_id)?.getTile()?.zone;
                float distance = target == null ? 0f : CorpsSystem.ZoneDistance(CorpsSystem.Zone(corps), target);
                list.Add((attacker, corps.strength * (distance <= 4f ? 1f : distance <= 10f ? 0.5f : 0.25f)));
            }
        MonthLandingSectors.Clear();
        SectorCache.Clear();
        _frontsNext.Clear();
    }

    public static void AssignMonth(Kingdom kingdom)
    {
        List<CorpsData> list = kingdom?.data == null || kingdom.isRekt() ? null : kingdom.GetOrCreate().corps;
        if (list == null || list.Count == 0) return;
        // 分几帧分派时，共用的陆上战线段只在同一帧里共用：隔了几帧，逐格推进可能已改了地块归属
        int frame = UnityEngine.Time.frameCount;
        if (frame != _assignFrame)
        {
            _assignFrame = frame;
            SectorCache.Clear();
        }
        // 一国分派出错只跳过这一国：以前异常直接抛出 MonthTick，后面的交战结算整月不跑——
        // 没有交战提示、战线不结算，军团照旧推着上月的目标，国界来回翻(玩家日志 2026-10-17 连续三百多次)
        try
        {
            Assign(kingdom, list, MonthPressure, MonthLandingSectors);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][军团] {kingdom.GetKingdomName()} 分派失败: {exception}");
        }
    }

    public static void EndMonth()
    {
        double now = _monthNow;
        SectorCache.Clear();
        SwapFronts();
        Engaged.Clear();
        using (FrameProfiler.Measure("战线·交战结算")) Resolve(now, MonthLandingSectors);
        PruneBattles(now);
        MonthPressure.Clear();
        MonthLandingSectors.Clear();
    }

    // 过期的交战：一个多月没再打、时间倒退(读了更早的存档)、城已不在了
    private static void PruneBattles(double now)
    {
        foreach (long id in Battles.Where(pair => now < pair.Value.time || Date.getMonthsSince(pair.Value.time) > 1 ||
                                                  World.world.cities.get(pair.Key) is not City city || city.isRekt())
                     .Select(pair => pair.Key).ToList())
            Battles.Remove(id);
    }

    private static void Assign(Kingdom kingdom, List<CorpsData> list,
        Dictionary<long, List<(Kingdom attacker, float strength)>> pressure, Dictionary<long, bool> landingSectors)
    {
        bool war = _atWar.Contains(kingdom);
        SectorDefense.Clear();
        // 起义军没有进攻门槛：组织度再低也上阵，不退下去休整(见 CorpsSystem.Insurgent)
        bool insurgent = CorpsSystem.Insurgent(kingdom);
        foreach (CorpsData corps in list)
        {
            if (corps.org <= RetreatOrg && corps.task != "retreat" && !insurgent) Retreat(kingdom, corps);
        }
        // 就近动员(见 WarScopeSystem)：驻地在动员范围里的军、已经在打的军、义军才上前线；
        // 出征(决议或奉诏)与举国总动员时全军可以开往任何战线；其余留守驻地
        bool unbounded = WarScopeSystem.Unbounded(kingdom);
        bool Eligible(CorpsData corps) => unbounded || corps.militia || corps.task is "attack" or "defend" ||
                                          WarScopeSystem.InScope(CorpsSystem.Home(corps));
        var ready = list.Where(corps => (insurgent || corps.task != "retreat" && corps.org >= ReadyOrg) && corps.strength >= 1f &&
                                        Eligible(corps)).ToList();
        if (!war)
        {
            // 战备：动员范围里的军开往朝向目标国的边境(离自己最近的一段，见 WarReadinessSystem)；太平：回驻地
            IReadOnlyList<TileZone> border = CityPopulationSystem.IsMobilizing(kingdom)
                ? CityPopulationSystem.FrontZones(kingdom) : Array.Empty<TileZone>();
            var borderLoad = new Dictionary<int, int>();
            Dictionary<long, int> stations = Stations(kingdom, list);
            foreach (CorpsData corps in list)
            {
                corps.sector_city_id = -1L;
                if (corps.task == "retreat") { corps.target_zone_id = HomeZone(corps); continue; }
                bool marching = border.Count > 0 && WarScopeSystem.InScope(CorpsSystem.Home(corps));
                corps.task = marching ? "march" : "reserve";
                TileZone at = CorpsSystem.Zone(corps);
                // 在离自己最近的几块边境地块里散开站，不挤在同一块
                corps.target_zone_id = marching
                    ? Spread(corps, border.OrderBy(zone => CorpsSystem.ZoneDistance(zone, at)).Take(SpreadNearest).ToList(), borderLoad)?.id ?? HomeZone(corps)
                    : StationZone(corps, stations);
            }
            return;
        }
        float Threat(City city) => pressure.TryGetValue(city.id, out var entries)
            ? entries.Where(entry => Hostile(kingdom, entry.attacker)).Sum(entry => entry.strength) : 0f;
        // 战线段先算，眼下没有能上阵的军也算：跨海要先记下登陆点，舰队才会多造运输船、出海打通航线(见 NavySystem.Build/Assign)。
        // 以前没有能上阵的军就直接返回，登陆点一直没记下，隔海的仗永远开不了头。还在集结的叛乱：先不反攻叛军占的城(见 MusterSystem)
        List<Sector> sectors;
        using (FrameProfiler.Measure("战线·分派·战线段"))
            sectors = CachedSectors(kingdom).Where(sector => !MusterSystem.Mustering(kingdom, sector.City.kingdom)).ToList();
        foreach (Sector sector in sectors)
            if (sector.City?.kingdom != null) NoteFront(kingdom, sector.City.kingdom);
        // 跨海登陆的军(还没上岸的)兵力合计不超过运输船运力
        float shipping = sectors.Any(sector => sector.Landing) ? NavySystem.TransportCapacity(kingdom) : 0f;
        if (sectors.Any(sector => sector.Landing)) SeaLandingDiagnostics.Deployable(kingdom, ready, shipping);
        if (ready.Count == 0)
        {
            // 没有能上阵的军(远处留守、都在溃退休整)：有城受攻就算吃紧(下月扩大动员)
            if (kingdom.cities?.Any(city => city?.data != null && !city.isRekt() && Threat(city) > 0f) == true)
                WarScopeSystem.ReportShortfall(kingdom);
            foreach (CorpsData corps in list)
            {
                if (corps.task == "retreat") continue;
                corps.task = "reserve";
                corps.sector_city_id = -1L;
                corps.target_zone_id = HomeZone(corps);
            }
            return;
        }
        foreach (Sector sector in sectors.Where(sector => sector.Landing)) landingSectors[sector.City.id] = true;
        var assigned = new HashSet<CorpsData>();
        // ① 守城：本国受攻的城按攻势分兵(就近)
        var threatened = (kingdom.cities ?? new List<City>())
            .Where(city => city?.data != null && !city.isRekt() && Threat(city) > 0f)
            .OrderByDescending(Threat).ToList();
        // 叛乱初起、还在集结：只有附近的军赶得来(见 MusterSystem)
        bool Surprised(City city) => pressure.TryGetValue(city.id, out var entries) &&
                                     entries.Where(entry => Hostile(kingdom, entry.attacker))
                                         .All(entry => MusterSystem.Mustering(kingdom, entry.attacker));
        // 军令：集中固守的城排第一，至少拿六成能上阵的兵力(攻势更大就按攻势)
        float available = ready.Sum(corps => corps.strength);
        City hold = CorpsCommandSystem.HoldCity(kingdom);
        if (hold != null)
        {
            threatened.Remove(hold);
            threatened.Insert(0, hold);
        }
        float holdNeed = hold == null ? 0f : Mathf.Min(available, Mathf.Max(Threat(hold) * 0.9f, CorpsCommandSystem.HoldShare * available));
        // 守军不够分：按各城攻势比例分，不让攻势最大的一座城把军全拿走
        float totalThreat = threatened.Where(city => city != hold).Sum(Threat) * 0.9f, rest = Mathf.Max(0f, available - holdNeed);
        float shareOfNeed = totalThreat > rest && totalThreat > 0f ? rest / totalThreat : 1f;
        foreach (City city in threatened)
        {
            float need = city == hold ? holdNeed : Threat(city) * 0.9f * shareOfNeed, have = 0f;
            TileZone anchor = DefenseZone(kingdom, city);
            List<TileZone> line = DefenseLine(kingdom, city);
            var lineLoad = new Dictionary<int, int>();
            bool surprised = Surprised(city);
            foreach (CorpsData corps in ready.Where(corps => !assigned.Contains(corps) &&
                                                             (!surprised || corps.task == "defend" && corps.sector_city_id == city.id ||
                                                              CorpsSystem.ZoneDistance(CorpsSystem.Zone(corps), anchor) <= MusterSystem.LocalZones))
                         .OrderBy(corps => corps.task == "defend" && corps.sector_city_id == city.id ? -1f
                             : CorpsSystem.ZoneDistance(CorpsSystem.Zone(corps), anchor)).ToList())
            {
                if (have >= need) break;
                if (corps.task != "defend" || corps.sector_city_id != city.id) corps.entrench = 0f;
                corps.task = "defend";
                corps.sector_city_id = city.id;
                // 铺满防线：本城贴着敌占区的地块每块先放一支，放满了再叠
                corps.target_zone_id = (line.Count > 0 ? Spread(corps, line, lineLoad)?.id : anchor?.id) ?? HomeZone(corps);
                assigned.Add(corps);
                have += corps.strength;
            }
            // 分不出足够的守军：打不过了，下个月扩大动员
            if (have < need) WarScopeSystem.ReportShortfall(kingdom);
        }
        // ② 进攻：集中在少数几段；跨海登陆的军(还没上岸的)兵力合计不超过运输船运力，整军装不下就分出先遣军
        // 军令：集中攻打——出征的军都投那座城所在的一段；城在敌境深处、还不是战线的，投离它最近的一段
        City strike = CorpsCommandSystem.AttackCity(kingdom);
        Sector focus = null;
        if (strike != null && sectors.Count > 0)
        {
            TileZone goal = strike.getTile()?.zone;
            focus = sectors.FirstOrDefault(sector => sector.City == strike) ?? sectors.OrderBy(sector =>
                goal == null || sector.City.getTile()?.zone == null ? 0f : CorpsSystem.ZoneDistance(sector.City.getTile().zone, goal)).First();
        }
        FocusSectors[kingdom.id] = focus?.City?.data?.id ?? -1L;
        var attackOn = new Dictionary<Sector, float>();
        var attackLoad = new Dictionary<int, int>();
        // 不奉诏的禁军只守城、不出征(见 PalaceArmySystem)
        foreach (CorpsData whole in ready.Where(corps => !assigned.Contains(corps) && !PalaceArmySystem.Refuses(corps))
                     .OrderByDescending(corps => corps.strength))
        {
            if (sectors.Count == 0) break;
            CorpsData corps = whole;
            Sector keep = corps.task == "attack" ? sectors.FirstOrDefault(sector => sector.City.id == corps.sector_city_id) : null;
            // 船不够、也分不出先遣军的登陆战线不去(已在船上的照旧开过去)；登陆战线按装得上船的兵力估胜算
            bool Boardable(Sector sector) => Ashore(corps, sector) || corps.embarked || Shippable(corps, shipping) > 0f;
            if (keep != null && (!Boardable(keep) || focus != null && keep != focus)) keep = null;
            Sector choice = keep ?? (focus != null && Boardable(focus) ? focus : null) ?? sectors
                .Where(sector => (attackOn.ContainsKey(sector) || attackOn.Count < MaxOffensives) && Boardable(sector))
                .OrderByDescending(sector => Score(kingdom, corps, sector, attackOn,
                    Ashore(corps, sector) ? corps.strength : Mathf.Max(Shippable(corps, shipping), corps.embarked ? corps.strength : 0f)))
                .FirstOrDefault();
            if (choice == null) continue;
            if (!Ashore(corps, choice))
            {
                if (corps.strength > shipping && !corps.embarked)
                {
                    // 整军装不下：按剩下的运力分出一支先遣军渡海，本军留守(见 CorpsSystem.Detach)。以前整军装不下就不派——
                    // 战时一军一两万到几万人，蒸汽时代以前一两个港口的运力只有几千，大军永远过不了海
                    if (Shippable(corps, shipping) <= 0f) continue;
                    CorpsData vanguard = CorpsSystem.Detach(kingdom, corps, shipping);
                    if (vanguard == null) continue;
                    SeaLandingDiagnostics.Vanguard(kingdom);
                    corps = vanguard;
                }
                shipping -= corps.strength;
            }
            attackOn.TryGetValue(choice, out float strength);
            attackOn[choice] = strength + corps.strength;
            corps.task = "attack";
            corps.sector_city_id = choice.City.id;
            // 在这一段的目标地块之间平均分(不挤在离自己最近的同一块)
            // 这一段没有可推进的地块、军团又不在任何地块上时 Spread 返回空(玩家日志 2026-10-17 军团月度结算空引用)：回驻地
            TileZone target = Spread(corps, choice.Targets, attackLoad);
            corps.target_zone_id = target?.id ?? HomeZone(corps);
            corps.entrench = 0f;
            assigned.Add(corps);
        }
        // ③ 其余：预备，分驻本国各城(见 Stations)
        Dictionary<long, int> reserveStations = Stations(kingdom, list.Where(corps => !assigned.Contains(corps)).ToList());
        foreach (CorpsData corps in list.Where(corps => !assigned.Contains(corps) && corps.task != "retreat"))
        {
            corps.task = "reserve";
            corps.sector_city_id = -1L;
            corps.target_zone_id = StationZone(corps, reserveStations);
        }
    }

    // 一国这次分派里各段的守势(上月守军 + 团练)：每支军挑段都要比一遍各段，团练要扫城里各地块的归属，算一次就够
    // (分派中途不改城的归属与人口)；每次分派开头清空
    private static readonly Dictionary<Sector, float> SectorDefense = new();

    // strength：这支军能投到这一段的兵力(跨海时按装得上船的算，见 Shippable)
    private static float Score(Kingdom kingdom, CorpsData corps, Sector sector, Dictionary<Sector, float> attackOn, float strength)
    {
        attackOn.TryGetValue(sector, out float committed);
        if (!SectorDefense.TryGetValue(sector, out float defense))
        {
            DefenseByCity.TryGetValue(sector.City.id, out float garrison);
            defense = garrison * DefenderBonus + Militia(sector.City, kingdom);
            SectorDefense[sector] = defense;
        }
        float distance = sector.Targets.Count == 0 ? 50f
            : sector.Targets.Min(zone => CorpsSystem.ZoneDistance(zone, CorpsSystem.Zone(corps)));
        float odds = (committed + strength) / (defense + 1f);
        float capital = sector.City.isCapitalCity() ? 0.3f : 0f;
        float concentrate = committed > 0f ? 0.4f : 0f;
        float recapture = Friendly(kingdom, sector.City.kingdom) ? 0.6f : 0f;
        return Mathf.Min(odds, 3f) + capital + concentrate + recapture - distance * 0.03f;
    }

    // 已在登陆战线所在的那片陆地上(不必上船)；不是登陆战线也算
    private static bool Ashore(CorpsData corps, Sector sector)
    {
        if (!sector.Landing) return true;
        TileZone zone = CorpsSystem.Zone(corps);
        return !corps.embarked && zone != null && !SeaRegionSystem.IsSea(zone) &&
               CorpsSystem.SameLand(zone, sector.City.getTile()?.zone);
    }

    // 先遣军至少多少人：一千人，小军按半数
    private const float MinVanguard = 1000f;

    // 这支军能装上船渡海的兵力：整军装得下就是全军；装不下、剩下的运力够一支先遣军(见 CorpsSystem.Detach)就是剩下的运力；否则 0
    private static float Shippable(CorpsData corps, float shipping)
    {
        if (corps.strength <= shipping) return corps.strength;
        return CorpsSystem.CanDetach(corps) && shipping >= Mathf.Min(MinVanguard, corps.strength * 0.5f) ? shipping : 0f;
    }

    private static int HomeZone(CorpsData corps) => CorpsSystem.Home(corps)?.getTile()?.zone?.id ?? corps.zone_id;

    // 军令集中攻打时这个月投的那一段(城 id，没有为 -1)
    private static readonly Dictionary<long, long> FocusSectors = new();

    public static long FocusSector(Kingdom kingdom) =>
        kingdom != null && FocusSectors.TryGetValue(kingdom.id, out long id) ? id : -1L;

    // 预备军分驻本国各城：都城与边境的城在前(与外国接壤的城先、人多的先)，各军按编号依次轮着分，
    // 一城驻满一支才轮到下一城；义军留在本城。返回 军号 → 驻防地块
    private static Dictionary<long, int> Stations(Kingdom kingdom, List<CorpsData> corps)
    {
        var result = new Dictionary<long, int>();
        List<City> cities = kingdom.cities?.Where(city => city?.data != null && !city.isRekt() && city.getTile()?.zone != null)
            .ToList() ?? new List<City>();
        if (cities.Count <= 1) return result;
        bool Frontier(City city) => city.zones.Any(zone => (zone?.neighbours_all ?? Array.Empty<TileZone>())
            .Any(next => next?.city?.kingdom != null && next.city.kingdom != kingdom));
        List<City> order = cities.OrderByDescending(city => city == kingdom.capital)
            .ThenByDescending(Frontier).ThenByDescending(city => CityPopulationSystem.GetBackgroundTotal(city)).ToList();
        int index = 0;
        foreach (CorpsData entry in corps.Where(entry => !entry.militia).OrderBy(entry => entry.id))
            result[entry.id] = order[index++ % order.Count].getTile().zone.id;
        return result;
    }

    private static int StationZone(CorpsData corps, Dictionary<long, int> stations) =>
        stations.TryGetValue(corps.id, out int zone) ? zone : HomeZone(corps);

    // 散开站：在几块候选地块里挑放的军最少的(一样少的取最近的)；原来就在其中一块、那块还没别的军的，留在原地(免得每月换位)
    private const int SpreadNearest = 6;

    // 候选都空、军团又不在任何地块上时返回 null，调用处要兜底
    private static TileZone Spread(CorpsData corps, IReadOnlyList<TileZone> candidates, Dictionary<int, int> load)
    {
        TileZone current = CorpsSystem.Zone(corps);
        TileZone best = null;
        float bestScore = float.MaxValue;
        foreach (TileZone zone in candidates)
        {
            if (zone == null) continue;
            load.TryGetValue(zone.id, out int used);
            float score = used * 1000f + CorpsSystem.ZoneDistance(zone, current) - (zone.id == corps.target_zone_id ? 0.5f : 0f);
            if (score >= bestScore) continue;
            best = zone;
            bestScore = score;
        }
        if (best == null) return current;
        load[best.id] = load.TryGetValue(best.id, out int count) ? count + 1 : 1;
        return best;
    }

    // 守城的防线：本城与敌控地块相邻的地块(按离城心远近)；没有就空
    private static List<TileZone> DefenseLine(Kingdom kingdom, City city)
    {
        var line = new List<TileZone>();
        if (city?.zones == null) return line;
        foreach (TileZone zone in city.zones)
        {
            if (zone == null || !Friendly(kingdom, Controller(zone)) || TerrainSystem.Impassable(zone)) continue;
            if ((zone.neighbours ?? Array.Empty<TileZone>()).Any(neighbour => Hostile(kingdom, Controller(neighbour))))
                line.Add(zone);
        }
        return line;
    }

    // 守城的落脚地块：本城与敌控地块相邻的地块，没有就城中心
    private static TileZone DefenseZone(Kingdom kingdom, City city)
    {
        if (city?.zones != null)
            foreach (TileZone zone in city.zones)
            {
                if (zone == null || !Friendly(kingdom, Controller(zone)) || TerrainSystem.Impassable(zone)) continue;
                if ((zone.neighbours ?? Array.Empty<TileZone>()).Any(neighbour => Hostile(kingdom, Controller(neighbour))))
                    return zone;
            }
        return city?.getTile()?.zone;
    }

    private static void Retreat(Kingdom kingdom, CorpsData corps)
    {
        corps.task = "retreat";
        corps.sector_city_id = -1L;
        corps.target_zone_id = HomeZone(corps);
        Log(kingdom, string.Format(LM.Get("corps_log_routed"), CorpsSystem.Name(corps)));
    }

    // 军团该往哪块地走
    public static TileZone TargetZone(CorpsData corps) => CorpsSystem.ZoneById(corps.target_zone_id);

    private static bool Arrived(CorpsData corps)
    {
        TileZone zone = CorpsSystem.Zone(corps), target = TargetZone(corps);
        return zone != null && target != null && CorpsSystem.ZoneDistance(zone, target) <= ArriveDistance;
    }

    // 禁军的空饷不打仗(见 PalaceArmySystem)；武备废弛打折(见 MaladySystem)；起义军打折(见 CorpsSystem.Insurgent)
    internal static float Effective(CorpsData corps) =>
        corps.strength * PalaceArmySystem.RealShare(corps) * MaladySystem.MilitaryFactor(corps) * CorpsSystem.InsurgentFactor(corps) *
        (0.25f + 0.75f * Mathf.Clamp01(corps.org / 100f));

    // 火力：WarBox 的坦克、装甲车、飞机等载具每辆 +4%，最多 +60%
    private static readonly Dictionary<long, float> FirepowerCache = new();

    // 纯净地图模式：人物、载具都是数据，不靠 WarBox 的实体载具，火力按这支军的兵种(装备)算(见 CorpsArmSystem.Power)；
    // 其余模式照旧按本国载具数
    internal static float Firepower(Kingdom kingdom, CorpsData corps) =>
        PureMapMode.Enabled ? CorpsArmSystem.Power(CorpsArmSystem.Of(corps)) : Firepower(kingdom);

    private static float Firepower(Kingdom kingdom)
    {
        if (kingdom == null) return 1f;
        if (FirepowerCache.TryGetValue(kingdom.id, out float value)) return value;
        int vehicles = (kingdom.units?.Count(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() &&
                                                      CityPopulationSystem.IsVehicle(actor)) ?? 0) +
                       Compatibility.WarBoxCompatibility.VirtualVehicles(kingdom);
        value = 1f + Mathf.Min(0.6f, vehicles * 0.04f);
        FirepowerCache[kingdom.id] = value;
        return value;
    }

    // 团练：没有军驻守时，本城百姓的抵抗(人口的 2%，组织涣散只算六成)；民心归附时不抵抗
    private static float Militia(City city, Kingdom attacker)
    {
        if (city?.kingdom == null || attacker == null || Friendly(attacker, city.kingdom)) return 0f;
        if (LandEconomySystem.WouldSurrenderTo(city, attacker, out _)) return 0f;
        // 团练只从守方还占着的那部分城里抽；兵败如山倒时减半(见 FrontCollapse)
        return CityPopulationSystem.GetBackgroundTotal(city) * 0.02f * 0.6f * HeldShare(city) *
               FrontCollapse.Militia(city.kingdom, attacker);
    }

    // 城里还归守方(城主一方)控制的地块占几成(高山不算)
    private static float HeldShare(City city)
    {
        if (city?.zones == null || city.kingdom == null) return 1f;
        int total = 0, held = 0;
        foreach (TileZone zone in city.zones)
        {
            if (zone == null || TerrainSystem.Impassable(zone)) continue;
            total++;
            if (Friendly(city.kingdom, Controller(zone))) held++;
        }
        return total == 0 ? 1f : held / (float)total;
    }

    // 困守一隅：守方只剩不到三分之一的地时，守军战力按剩下的比例打折，最低五成
    private const float RemnantShare = 1f / 3f, RemnantFloor = 0.5f;

    private static float RemnantFactor(City city)
    {
        float share = HeldShare(city);
        return share >= RemnantShare ? 1f : Mathf.Lerp(RemnantFloor, 1f, share / RemnantShare);
    }

    // 攻方(与 side 交战的一方)有军团在这块地上或紧挨着看守：不打赢收不回来
    private static bool Guarded(Kingdom side, TileZone zone)
    {
        foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All())
            if (corps.strength >= 1f && Hostile(side, kingdom) && CorpsSystem.ZoneDistance(CorpsSystem.Zone(corps), zone) <= 1f)
                return true;
        return false;
    }

    private static void Resolve(double now, Dictionary<long, bool> landingSectors)
    {
        FirepowerCache.Clear();
        BeginMonthPushes();
        var bySector = new Dictionary<long, List<(Kingdom kingdom, CorpsData corps)>>();
        foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All())
        {
            if (corps.sector_city_id < 0 || corps.task is not ("attack" or "defend") || !Arrived(corps)) continue;
            if (!bySector.TryGetValue(corps.sector_city_id, out var group))
                bySector[corps.sector_city_id] = group = new List<(Kingdom, CorpsData)>();
            group.Add((kingdom, corps));
        }
        foreach (var pair in bySector)
        {
            City city = World.world.cities.get(pair.Key);
            if (city?.data == null || city.isRekt() || city.kingdom == null) continue;
            try { ResolveCity(city, pair.Value, now, landingSectors.ContainsKey(city.id)); }
            catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][战线] {city.data.name} 结算失败: {exception.Message}"); }
        }
        EndMonthPushes();
    }

    private static void ResolveCity(City city, List<(Kingdom kingdom, CorpsData corps)> group, double now, bool landing)
    {
        Kingdom owner = city.kingdom;
        var attackers = group.Where(entry => entry.corps.task == "attack" && Hostile(entry.kingdom, owner)).ToList();
        var holders = group.Where(entry => Friendly(entry.kingdom, owner)).ToList();
        if (attackers.Count == 0)
        {
            // 收复：本城被敌军占去的地块，没有敌军来争就逐块夺回
            var retakers = holders.Where(entry => entry.corps.task == "attack").ToList();
            if (retakers.Count == 0) return;
            Kingdom lead = retakers.OrderByDescending(entry => entry.corps.strength).First().kingdom;
            foreach (var entry in retakers) Engaged.Add(entry.corps.id);
            // 平滑国界：逐格收复(见 FrontSystem.Smooth)
            if (SmoothActive)
            {
                SmoothAdvance(city, lead, retakers.Where(entry => entry.kingdom == lead).Select(entry => entry.corps).ToList(),
                    false, false, retake: true, free: true);
                return;
            }
            int retaken = Retake(city, lead, 1, retakers.Select(entry => entry.corps), free: true);
            if (retaken > 0) Record(city, lead, owner, 1f, retakers.Sum(entry => entry.corps.strength), 0f, retaken, now);
            return;
        }
        Kingdom attacker = attackers.GroupBy(entry => entry.kingdom)
            .OrderByDescending(entries => entries.Sum(entry => entry.corps.strength)).First().Key;
        // 地形：攻方要推进的那块地(城中心那块是城池)
        FrontTerrain terrain = TerrainSystem.OfBattle(city, FrontZone(city, attacker, landing));
        TerrainEffect ground = TerrainSystem.Effect(terrain);
        // 跨海登陆的头一个月攻方吃亏
        // 战时统帅按兵略给本国各军加成(见 WarCommandSystem)
        // 补给不足打折(见 LogisticsSystem)
        // 骑士团的军是精锐(见 PapacySystem.Orders)
        // 开国雄主的军队锐不可当(见 RulerTraitSystem.FounderCombat)
        float attack = attackers.Sum(entry => Effective(entry.corps) * Firepower(entry.kingdom, entry.corps) *
                                              WarCommandSystem.Factor(entry.kingdom) * LogisticsSystem.AttackFactor(entry.corps) *
                                              PapacySystem.OrderFactor(entry.corps) * RulerTraitSystem.FounderCombat(entry.kingdom)) *
                       (landing ? 0.7f : 1f) * ground.Attack;
        // 叛乱初起还在集结的守方仓促应战(见 MusterSystem)
        // 红夷大炮破城：攻方有重炮营，城池守势减弱、堑壕只剩一半、团练抵抗剩七成
        bool breach = attackers.Any(entry => CorpsArmSystem.Breaches(entry.corps));
        // 困守一隅的守军战力打折(见 RemnantFactor)
        float remnant = RemnantFactor(city);
        float defend = holders.Sum(entry => Effective(entry.corps) * Firepower(entry.kingdom, entry.corps) *
                                            WarCommandSystem.Factor(entry.kingdom) * LogisticsSystem.AttackFactor(entry.corps) *
                                            PapacySystem.OrderFactor(entry.corps) * RulerTraitSystem.FounderCombat(entry.kingdom) *
                                            (breach ? BreachedDefenderBonus + entry.corps.entrench * BreachedEntrench
                                                : DefenderBonus + entry.corps.entrench) *
                                            MusterSystem.DefenseFactor(entry.kingdom, attacker)) * remnant;
        float militia = holders.Count == 0 ? Militia(city, attacker) * (breach ? BreachedMilitia : 1f) : 0f;
        defend += militia;
        // 关城守御：攻方军团够得着的地(同 Advance 挑的)里有关城那一片的，守方按等级加成；红夷大炮破城时只剩一半
        // (见 RuralSettlementSystem.FortFactor)
        float fort = 1f;
        List<CorpsData> spearheads = attackers.Select(entry => entry.corps).ToList();
        foreach (TileZone target in TargetZones(attacker, city))
            if (Beside(spearheads, target)) fort = Mathf.Max(fort, RuralSettlementSystem.FortFactor(city, target));
        if (fort > 1f) defend *= breach ? 1f + (fort - 1f) * 0.5f : fort;
        float ratio = attack + defend <= 0f ? 0.5f : attack / (attack + defend);
        // 校验平衡用：一方是游牧时记一行战况分解(交战前的兵力，限频，见 WarBalanceDiagnostics)
        WarBalanceDiagnostics.Battle(city, attacker, owner, attackers, holders, terrain, ground.Attack, landing, breach, remnant,
            militia, fort, attack, defend, ratio);
        // 伤亡与组织度
        float attackStrength = attackers.Sum(entry => entry.corps.strength);
        float holdStrength = holders.Sum(entry => entry.corps.strength);
        // 账面载具按本月伤亡比例折损
        float attackLoss = CasualtyRate * (1f - ratio) * (defend > 0f ? 1f : 0.2f), holdLoss = CasualtyRate * ratio;
        foreach (Kingdom side in attackers.Select(entry => entry.kingdom).Distinct())
            Compatibility.WarBoxCompatibility.LoseVehicles(side, attackLoss);
        foreach (Kingdom side in holders.Select(entry => entry.kingdom).Distinct())
            Compatibility.WarBoxCompatibility.LoseVehicles(side, holdLoss);
        foreach (var entry in attackers)
        {
            CorpsSystem.Casualties(entry.corps, entry.corps.strength * CasualtyRate * (1f - ratio) * (defend > 0f ? 1f : 0.2f));
            entry.corps.org = Mathf.Max(0f, entry.corps.org - (8f + 20f * (1f - ratio) + ground.AttackerOrg));
            Engaged.Add(entry.corps.id);
        }
        foreach (var entry in holders)
        {
            CorpsSystem.Casualties(entry.corps, entry.corps.strength * CasualtyRate * ratio);
            entry.corps.org = Mathf.Max(0f, entry.corps.org - (6f + 20f * ratio + ground.DefenderOrg));
            Engaged.Add(entry.corps.id);
        }
        bool standing = holders.Any(entry => entry.corps.org > RetreatOrg && entry.corps.strength >= 1f);
        int taken = 0;
        if (ratio >= 0.55f && SmoothActive)
        {
            // 平滑国界：逐格推进，每月两块地(见 FrontSystem.Smooth)
            if (ground.MaxAdvance > 0)
                SmoothAdvance(city, attacker, attackers.Where(entry => entry.kingdom == attacker).Select(entry => entry.corps).ToList(),
                    standing, landing, retake: false);
        }
        else if (ratio >= 0.55f)
        {
            // 开国雄主、正统高的帝国打一般国家，一月可多下几块地(见 PushFactor)
            float pace = PushFactor(attacker, owner);
            int zones = Mathf.Min(ground.MaxAdvance, Mathf.FloorToInt(pace) + (UnityEngine.Random.value < pace - Mathf.Floor(pace) ? 1 : 0));
            taken = Advance(city, attacker, zones, standing, landing,
                attackers.Where(entry => entry.kingdom == attacker).Select(entry => entry.corps).ToList());
            if (city.kingdom != owner)
            {
                Log(attacker, string.Format(LM.Get("corps_log_city_taken"), city.GetCityName(), attacker.GetKingdomName()));
                Log(owner, string.Format(LM.Get("corps_log_city_lost"), city.GetCityName(), attacker.GetKingdomName()));
            }
        }
        else if (ratio <= 0.4f && standing)
        {
            Kingdom lead = holders.OrderByDescending(entry => entry.corps.strength).First().kingdom;
            if (SmoothActive)
                SmoothAdvance(city, lead, holders.Where(entry => entry.kingdom == lead).Select(entry => entry.corps).ToList(),
                    false, false, retake: true);
            else Retake(city, lead, 1, holders.Select(entry => entry.corps));
        }
        else if (SmoothActive) SmoothHold(city);
        Record(city, attacker, owner, ratio, attackStrength, holdStrength + militia, taken, now, terrain);
        // 大败、溃散一方的统领：阵亡、被俘、突围(见 BattleDrama)
        BattleDrama.AfterBattle(city, attackers, holders, ratio);
        // 名将名臣：赢的一方的军团统领、战争统帅记战功(见 FamousTalentSystem)
        if (ratio >= 0.55f)
            FamousTalentSystem.OnBattle(attackers.Where(entry => entry.kingdom == attacker), city.GetCityName(),
                attackStrength, holdStrength + militia, city.kingdom != owner, false);
        else if (ratio <= 0.4f && standing)
            FamousTalentSystem.OnBattle(holders, city.GetCityName(), holdStrength, attackStrength, false, true);
    }

    // 攻方下一块要推进的地(与 Advance 的挑法一致)：交战地形按它
    // 推进加速：开国雄主(含中兴雄主) ×2；开国、中兴之君在位的帝国(核心或成员)打不属于任何帝国的一般国家，
    // 正统越高推得越快——正统 50 起算，100 时三倍，前提是军饷发足(欠饷一成半以上不算)。两者取大，最快三倍
    private const float ImperialPushMax = 3f, ImperialPushFloor = 50f;

    // 兵败如山倒的对手再快一倍(见 FrontCollapse)
    private static float PushFactor(Kingdom side, Kingdom opponent) =>
        BasePushFactor(side, opponent) * FrontCollapse.PushFactor(side, opponent);

    private static float BasePushFactor(Kingdom side, Kingdom opponent)
    {
        // 统一者打天下里的别家：快一半，统一十年后加倍(见 UnificationSystem)
        float factor = (RulerTraitSystem.FounderReigns(side) ? 2f : 1f) * UnificationSystem.PushFactor(side, opponent);
        EmpireCraft.Scripts.Layer.Empire empire = side?.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived() || opponent?.data == null || opponent.isRekt() ||
            opponent.IsInEmpire() || !RulerTraitSystem.HeroicReign(side) || !CorpsSystem.WellPaid(side)) return factor;
        float rise = Mathf.Clamp01((empire.Legitimacy - ImperialPushFloor) / (100f - ImperialPushFloor));
        return Mathf.Max(factor, 1f + (ImperialPushMax - 1f) * rise);
    }

    private static TileZone FrontZone(City city, Kingdom attacker, bool landing)
    {
        List<TileZone> targets = TargetZones(attacker, city);
        if (targets.Count == 0 && landing) targets = LandingZones(city).Where(zone => Hostile(attacker, Controller(zone))).ToList();
        TileZone center = city.getTile()?.zone;
        return targets.OrderBy(candidate => candidate == center ? 1 : 0)
            .ThenByDescending(candidate => (candidate.neighbours ?? Array.Empty<TileZone>())
                .Count(neighbour => Friendly(attacker, Controller(neighbour)))).FirstOrDefault() ?? center;
    }

    // 攻方占领紧挨着进攻军团的地块(部队没到的地方不推进)；占下后离它最近的进攻军团进驻。守军还在时留下城中最后一块
    private static int Advance(City city, Kingdom attacker, int count, bool defended, bool landing,
        List<CorpsData> spearheads)
    {
        Kingdom owner = city.kingdom;
        int taken = 0;
        for (int i = 0; i < count && city.kingdom == owner; i++)
        {
            List<TileZone> targets = TargetZones(attacker, city);
            if (targets.Count == 0 && landing) targets = LandingZones(city).Where(zone => Hostile(attacker, Controller(zone))).ToList();
            targets = targets.Where(candidate => Beside(spearheads, candidate)).ToList();
            if (targets.Count == 0) break;
            int remaining = CityDistricts.Count(city, zone => !Friendly(attacker, Controller(zone)) && !TerrainSystem.Impassable(zone));
            if (defended && remaining <= 1) break;
            TileZone center = city.getTile()?.zone;
            targets = Outskirts(city, attacker, targets, center, defended);
            if (targets.Count == 0) break;
            TileZone zone = targets.OrderBy(candidate => CenterOrder(city, candidate, center, defended))
                .ThenByDescending(candidate => (candidate.neighbours ?? Array.Empty<TileZone>())
                    .Count(neighbour => Friendly(attacker, Controller(neighbour)))).First();
            Kingdom occupier = Belligerent(attacker, owner);
            Kingdom current = city.GetTileZoneOccupier(zone);
            if (current != null && current != owner) city.RemoveOccupiedTileZone(current, zone);
            bool ok = city.AddOccupiedTileZone(occupier, zone, landing: true);
            if (!ok) break;
            taken++;
            // 这一片(村镇四周或城区)一并到手
            OccupyDistrict(city, attacker, owner, zone);
            CorpsData lead = spearheads.Where(corps => CorpsSystem.ZoneDistance(CorpsSystem.Zone(corps), zone) <= 1f)
                .OrderBy(corps => CorpsSystem.ZoneDistance(CorpsSystem.Zone(corps), zone))
                .ThenByDescending(corps => corps.strength).FirstOrDefault();
            if (lead != null)
            {
                lead.zone_id = zone.id;
                lead.march_progress = 0f;
            }
        }
        SweepImpassable(city, attacker, owner);
        return taken;
    }

    // 地块紧挨着(或就是)其中一支军团的所在地
    private static bool Beside(IEnumerable<CorpsData> corps, TileZone zone) =>
        zone != null && corps != null && corps.Any(entry => CorpsSystem.ZoneDistance(CorpsSystem.Zone(entry), zone) <= 1f);

    // 挑推进的地块：守军还在时城池(城区那片)放到最后，守军溃散后先取城池
    private static int CenterOrder(City city, TileZone candidate, TileZone center, bool defended) =>
        !CityDistricts.SameDistrict(city, candidate, center) ? 1 : defended ? 2 : 0;

    // 守军还在、城外还有别的片没打下：城区那片(主城池)先不打，只打城外
    private static List<TileZone> Outskirts(City city, Kingdom attacker, List<TileZone> targets, TileZone center, bool defended)
    {
        if (!defended || center == null) return targets;
        bool outsideLeft = city.zones.Any(zone => zone != null && !Friendly(attacker, Controller(zone)) &&
                                                  !TerrainSystem.Impassable(zone) && !CityDistricts.SameDistrict(city, zone, center));
        return outsideLeft ? targets.Where(zone => !CityDistricts.SameDistrict(city, zone, center)).ToList() : targets;
    }

    // 拿下一块地：它那一片里还不归攻方的地块一并占下(平滑国界下直接翻过去)
    private static void OccupyDistrict(City city, Kingdom attacker, Kingdom owner, TileZone taken)
    {
        Kingdom occupier = Belligerent(attacker, owner);
        foreach (TileZone part in CityDistricts.ZonesOf(city, taken))
        {
            if (part == taken || city.kingdom != owner || Friendly(attacker, Controller(part))) continue;
            Kingdom current = city.GetTileZoneOccupier(part);
            if (current != null && current != owner) city.RemoveOccupiedTileZone(current, part);
            if (city.AddOccupiedTileZone(occupier, part, landing: true) && SmoothActive) SmoothBorders.Settle(part);
        }
    }

    // 收复一块地：它那一片里被敌军占着的一并收回；free(没有攻方来争、不打仗收复)时攻方军团看守着的不收(见 Guarded)
    private static void RetakeDistrict(City city, Kingdom side, TileZone retaken, bool free = false)
    {
        foreach (TileZone part in CityDistricts.ZonesOf(city, retaken))
        {
            if (part == retaken || !(city.GetTileZoneOccupier(part) is Kingdom occupier) || !Hostile(side, occupier)) continue;
            if (free && Guarded(side, part)) continue;
            if (city.RemoveOccupiedTileZone(occupier, part) && SmoothActive) SmoothBorders.Settle(part);
        }
    }

    // 占下一块地(CityExtension.AddOccupiedTileZone 每次都调)：是主城池(城区那片，没有村镇的城就是城心那块)就举城投降
    [ThreadStatic] private static bool _surrendering;

    public static void OnZoneTaken(City city, Kingdom occupier, TileZone zone)
    {
        Kingdom owner = city?.kingdom;
        if (_surrendering || owner == null || zone == null || !Hostile(occupier, owner)) return;
        if (!CityDistricts.SameDistrict(city, zone, city.getTile()?.zone)) return;
        Surrender(city, occupier, owner);
    }

    // 主城池陷落：整个行政区投降，余下的地块(连同各村镇)一并归攻方(平滑国界下各块直接翻过去)
    private static void Surrender(City city, Kingdom attacker, Kingdom owner)
    {
        if (city?.zones == null || city.kingdom != owner || _surrendering) return;
        // 守城统领殉国、被俘或出走(见 BattleDrama)
        BattleDrama.CityFell(city, attacker, owner);
        Kingdom occupier = Belligerent(attacker, owner);
        // 举城投降：城外的镇村不遭兵火
        bool sparing = RuralSettlementSystem.Sparing;
        RuralSettlementSystem.Sparing = true;
        _surrendering = true;
        try
        {
            foreach (TileZone zone in city.zones.Where(zone => zone != null && !Friendly(attacker, Controller(zone))).ToList())
            {
                if (city.kingdom != owner) break;
                Kingdom current = city.GetTileZoneOccupier(zone);
                if (current != null && current != owner) city.RemoveOccupiedTileZone(current, zone);
                // 个别地块占不下(盟军占着等)就跳过，接着收余下的
                if (city.AddOccupiedTileZone(occupier, zone, landing: true) && SmoothActive) SmoothBorders.Settle(zone);
            }
        }
        finally
        {
            _surrendering = false;
            RuralSettlementSystem.Sparing = sparing;
        }
    }

    // 城里能走到的地都占了，剩下的高山随城陷落(军团进不去，否则这座城永远打不下)
    private static void SweepImpassable(City city, Kingdom attacker, Kingdom owner)
    {
        if (city.kingdom != owner || city.zones == null) return;
        var rest = city.zones.Where(zone => zone != null && !Friendly(attacker, Controller(zone))).ToList();
        if (rest.Count == 0 || rest.Any(zone => !TerrainSystem.Impassable(zone))) return;
        Kingdom occupier = Belligerent(attacker, owner);
        foreach (TileZone zone in rest)
        {
            if (city.kingdom != owner) break;
            Kingdom current = city.GetTileZoneOccupier(zone);
            if (current != null && current != owner) city.RemoveOccupiedTileZone(current, zone);
            if (!city.AddOccupiedTileZone(occupier, zone, landing: true)) break;
        }
    }

    // 守方(或收复的一方)夺回本城被敌军占去、与己方相连、紧挨着己方军团的地块
    // free：没有攻方来争、不打仗收复(攻方军团看守着的地不收，见 Guarded)
    private static int Retake(City city, Kingdom side, int count, IEnumerable<CorpsData> near, bool free = false)
    {
        int retaken = 0;
        List<CorpsData> troops = near?.ToList() ?? new List<CorpsData>();
        for (int i = 0; i < count; i++)
        {
            TileZone zone = city.zones?.FirstOrDefault(candidate => candidate != null &&
                city.GetTileZoneOccupier(candidate) is Kingdom occupier && Hostile(side, occupier) &&
                Beside(troops, candidate) && (!free || !Guarded(side, candidate)) &&
                (candidate.neighbours ?? Array.Empty<TileZone>()).Any(neighbour => Friendly(side, Controller(neighbour))));
            if (zone == null) break;
            if (!city.RemoveOccupiedTileZone(city.GetTileZoneOccupier(zone), zone)) break;
            retaken++;
            // 这一片一并收回
            RetakeDistrict(city, side, zone, free);
        }
        return retaken;
    }

    private static void Record(City city, Kingdom attacker, Kingdom defender, float ratio, float attack, float defend,
        int taken, double now, FrontTerrain terrain = FrontTerrain.Plains)
    {
        TileZone marker = TargetZones(attacker, city).FirstOrDefault() ?? city.getTile()?.zone;
        Battles[city.id] = new BattleView
        {
            city_id = city.id, zone_id = marker?.id ?? -1, ratio = ratio, attack = attack, defend = defend,
            attacker_id = attacker?.id ?? -1L, defender_id = defender?.id ?? -1L, taken = taken, time = now,
            terrain = terrain
        };
    }

    #endregion

    #region 纪事

    public static void Log(Kingdom kingdom, string text)
    {
        if (kingdom == null || string.IsNullOrEmpty(text) || World.world == null) return;
        if (!Logs.TryGetValue(kingdom.id, out var list)) Logs[kingdom.id] = list = new List<(double, string)>();
        list.Insert(0, (World.world.getCurWorldTime(), text));
        if (list.Count > 24) list.RemoveRange(24, list.Count - 24);
    }

    public static IReadOnlyList<(double time, string text)> LogOf(Kingdom kingdom) =>
        kingdom != null && Logs.TryGetValue(kingdom.id, out var list) ? list : Array.Empty<(double, string)>();

    #endregion

    #region 地图：交战地块边界

    private static readonly List<(TileZone a, TileZone b)> _contested = new();
    private static int _contestedAt = -1;

    // 交战双方控制区相接的地块边(画战线用)，每秒最多重算一次
    public static IReadOnlyList<(TileZone a, TileZone b)> ContestedEdges()
    {
        int second = Mathf.FloorToInt(Time.unscaledTime);
        if (_contestedAt == second) return _contested;
        _contestedAt = second;
        _contested.Clear();
        if (!CorpsSystem.Enabled || World.world?.cities == null) return _contested;
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt() || city.zones == null || city.kingdom == null) continue;
            if (!city.kingdom.hasEnemies() && !Realm(city.kingdom).hasEnemies()) continue;
            foreach (TileZone zone in city.zones)
            {
                Kingdom controller = Controller(zone);
                if (controller == null) continue;
                // 交战的两块地都属于参战国的城，只看右、上两个方向，每条边记一次
                foreach (TileZone neighbour in new[] { zone.zone_right, zone.zone_up })
                {
                    if (neighbour == null) continue;
                    Kingdom other = Controller(neighbour);
                    if (other != null && Hostile(controller, other)) _contested.Add((zone, neighbour));
                }
            }
        }
        return _contested;
    }

    #endregion
}

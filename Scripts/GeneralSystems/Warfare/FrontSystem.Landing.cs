using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 渡海登陆的派兵(群友反馈 2026-10-21："好像就没见过水军打登陆战""水军不冲岸吗？")：
//   以前登陆战线和陆上战线混在一起挑：每支军按"胜算 − 路程 × 0.03"挑一段，兵力大的先挑，三路攻势(MaxOffensives)挑满就不再开新段——
//   跨海的段隔着海离得远，有陆上战线的国家几乎从不派军渡海([渡海诊断]：琉球→新罗三十五年间可战军 26~60 支、运力四千到五万七、
//   航线 2~3 个海区，先遣、在船、靠岸全是 0，同期琉球的军都在南阳、长安、凉州一带打陆战；广南东的军宁可西去两百格打苍梧，
//   也不渡五十格的海峡打夷州)。
//   现在守城分完兵之后、陆上攻势之前，先为各路登陆抽一支渡海的兵(CommitLandings)，时机：
//     · 本国守城不吃紧(受攻的城分到的守军够八成)，没有军令集中攻打别处；
//     · 有航线(不经敌方制海、在舰种航程内，见 LandingSector)，运输船装得下至少一支军或一支先遣军；
//     · 装得上船的兵力打了登陆折扣(×LandingAttack)还有那座城守势的四成，不白白送死；
//   抽多少：各路登陆合计至多能上阵兵力(守城之外)的三成五，不超过运力，一路连同已派去的至多那座城守势的三倍(够打就行)；
//   离上船的港口不过十五块的军由近及远(上月正在陆上交战的算远八块，见 CorpsSystem.EmbarkZone)，
//   上月已派去的(在途、在船上)接着去，整军超过限额的按限额分出先遣军(见 CorpsSystem.Detach)。登陆战线不占三路陆上攻势的名额。
//   哪些军要坐船(见 SailsAround，挑兵时的 Ashore 与行军时的 MustSail 同一套)：在另一片陆地上的；同一片陆地上隔着第三国
//   走不过去、只因水师辐射够得着才算够得着的城(以前当作陆路，军从第三国的地上穿过去)。上了岸(离城心不过 LandedZones 块，
//   或这一仗已在那片陆地靠岸)的不再坐船；陆路走得通的照旧走陆路。
//   没派的原因按月记进[渡海诊断](见 SeaLandingDiagnostics.Landing)。
public static partial class FrontSystem
{
    private const float LandingShare = 0.35f, LandingHopeless = 0.4f;
    // 一路登陆够打就行：连同已派去的至多那座城守势的三倍(至少一支先遣军)
    private const float LandingOverkill = 3f;
    // 跨海登陆(还没在那座城站住脚)时攻方战力的倍数
    public const float LandingAttack = 0.7f;
    // 离登陆那座城的城心几块地以内算上了岸(不必再坐船)
    private const float LandedZones = 6f;
    // 守城分到的兵力不到要的这么多算吃紧，不另开登陆
    private const float DefenseShortfall = 0.8f;
    // 渡海的军离上船的地方至多几块地(步兵一个月走半块，十五块要走两年多)；上月正在陆上交战的按多这么几块排
    private const float EmbarkReachZones = 15f, EngagedPenaltyZones = 8f;

    // 各国本月的登陆战线：城 id → 陆路走不走得通(见 Sector.Overland)；分派时更新，行军时据此判要不要坐船(见 MustSail)
    private static readonly Dictionary<long, Dictionary<long, bool>> LandingWays = new();
    // 这次分派里各军上船的地方(见 CorpsSystem.EmbarkZone)，没有为空；每次分派开头清空
    private static readonly Dictionary<long, TileZone> EmbarkPoints = new();

    private static void ResetLandings()
    {
        LandingWays.Clear();
        EmbarkPoints.Clear();
    }

    // 记下本国这个月的登陆战线
    private static void NoteLandingWays(Kingdom kingdom, List<Sector> sectors)
    {
        Dictionary<long, bool> cities = null;
        if (sectors != null)
            foreach (Sector sector in sectors)
                if (sector.Landing)
                    (cities ??= new Dictionary<long, bool>())[sector.City.id] = sector.Overland;
        if (cities == null) LandingWays.Remove(kingdom.id);
        else LandingWays[kingdom.id] = cities;
    }

    // 行军(见 CorpsSystem.StepTowardTarget)：去打登陆战线、已在那座城那片陆地上的军，要不要先去港口坐船
    public static bool MustSail(CorpsData corps, TileZone current, TileZone target)
    {
        if (corps == null || corps.embarked || corps.task != "attack" || current == null || target == null) return false;
        if (!LandingWays.TryGetValue(corps.kingdom_id, out Dictionary<long, bool> cities) ||
            !cities.TryGetValue(corps.sector_city_id, out bool overland)) return false;
        City city = World.world.cities.get(corps.sector_city_id);
        // 本土没有能上船的港口的照旧走陆路(这种军挑兵时就不派去，见 Boardable)
        return city?.data != null && SailsAround(corps, current, city, overland) &&
               CorpsSystem.EmbarkZone(World.world.kingdoms.get(corps.kingdom_id), corps) != null;
    }

    // 已在登陆那座城那片陆地上的军还要不要坐船：陆路走不通(隔着第三国)、还没上岸的坐；走得通的走陆路
    private static bool SailsAround(CorpsData corps, TileZone at, City city, bool overland)
    {
        if (overland) return false;
        TileZone goal = city.getTile()?.zone;
        return goal != null && !Landed(corps, at, goal, corps.task == "attack" && corps.sector_city_id == city.id);
    }

    // 上了岸：离城心不过 LandedZones 块；或这一仗(beach：军正打着这座城)坐船在那片陆地上靠了岸(见 CorpsSystem.Disembark，
    // 改派别的城时清掉，见 ForgetBeach)、还在这片陆地上——上岸后往城下走，路上绕山绕墙也不再回头坐船
    private static bool Landed(CorpsData corps, TileZone at, TileZone goal, bool beach)
    {
        if (at == null || goal == null) return false;
        if (CorpsSystem.ZoneDistance(at, goal) <= LandedZones) return true;
        if (!beach) return false;
        TileZone shore = CorpsSystem.ZoneById(corps.landing_zone_id);
        return shore != null && !SeaRegionSystem.IsSea(shore) && CorpsSystem.SameLand(shore, goal) && CorpsSystem.SameLand(shore, at);
    }

    // 改派去打别的城：上一仗的靠岸处不再算数(在船上的不动，船到了照旧在选好的海滩靠岸)。登陆派兵与陆上攻势落位时都调
    private static void ForgetBeach(CorpsData corps, City city)
    {
        if (!corps.embarked && corps.sector_city_id != city.id) corps.landing_zone_id = -1;
    }

    // 上船的地方(按这次分派缓存)
    private static TileZone EmbarkPoint(Kingdom kingdom, CorpsData corps)
    {
        if (EmbarkPoints.TryGetValue(corps.id, out TileZone zone)) return zone;
        zone = CorpsSystem.EmbarkZone(kingdom, corps);
        EmbarkPoints[corps.id] = zone;
        return zone;
    }

    // 这一段的守势(上月守军 + 团练)，与 Score 同一套
    private static float DefenseOf(Kingdom kingdom, Sector sector)
    {
        if (SectorDefense.TryGetValue(sector, out float defense)) return defense;
        DefenseByCity.TryGetValue(sector.City.id, out float garrison);
        defense = garrison * DefenderBonus + Militia(sector.City, kingdom);
        SectorDefense[sector] = defense;
        return defense;
    }

    // 陆上攻势有几路(登陆战线不占名额)
    private static int LandOffensives(Dictionary<Sector, float> attackOn) => attackOn.Keys.Count(sector => !sector.Landing);

    // 守城分兵之后、陆上攻势之前：为各路登陆抽兵渡海(见文件头)。strained：守城吃紧；focus：军令集中攻打的一段
    private static void CommitLandings(Kingdom kingdom, List<Sector> sectors, List<CorpsData> ready, HashSet<CorpsData> assigned,
        Dictionary<Sector, float> attackOn, Dictionary<int, int> attackLoad, Sector focus, bool strained, ref float shipping)
    {
        EmbarkPoints.Clear();
        List<Sector> landings = sectors.Where(sector => sector.Landing).ToList();
        if (landings.Count == 0) return;
        List<CorpsData> free = ready.Where(corps => !assigned.Contains(corps) && !PalaceArmySystem.Refuses(corps)).ToList();
        float allowance = free.Sum(corps => corps.strength) * LandingShare / landings.Count;
        foreach (Sector sector in landings)
        {
            long cityId = sector.City.id;
            // 上了岸、走陆路去的(下面陆上攻势照旧派)也算这一路的兵
            float ashore = free.Where(corps => !assigned.Contains(corps) && corps.task == "attack" && corps.sector_city_id == cityId &&
                                               Ashore(corps, sector)).Sum(corps => corps.strength);
            // 上月派去的(在船上、在去港口的路上)接着去，占运力；军令集中攻打别处时，还没上船的改去那里(下面陆上攻势照军令派)
            float going = 0f;
            foreach (CorpsData corps in free.Where(corps => !assigned.Contains(corps) && corps.task == "attack" &&
                                                            corps.sector_city_id == cityId && !Ashore(corps, sector) &&
                                                            (corps.embarked || focus == null || focus == sector))
                         .OrderByDescending(corps => corps.embarked).ToList())
            {
                if (!corps.embarked && (corps.strength > shipping || EmbarkPoint(kingdom, corps) == null)) continue;
                Dispatch(corps, sector, assigned, attackOn, attackLoad, ref shipping);
                going += corps.strength;
            }
            string why = null;
            int sent = 0;
            float budget = Mathf.Min(shipping, Mathf.Min(allowance, Mathf.Max(DefenseOf(kingdom, sector) * LandingOverkill, MinVanguard)) -
                                               going - ashore);
            if (free.Count == 0) why = "兵力都在守城";
            else if (focus != null && focus != sector) why = "军令集中攻打别处";
            else if (strained) why = "守城吃紧";
            else if (shipping < 1f) why = ShippingShortage(kingdom);
            else if (budget < 1f) why = going + ashore > 0f ? null : "可上阵的兵力太少";
            else
            {
                var plan = PlanLanding(kingdom, sector, free, assigned, budget, shipping, out why);
                // 这一路已有兵在途、在岸上，这个月添不上人不算没派
                if (plan.Count == 0 && going + ashore > 0f) why = null;
                if (plan.Count > 0)
                {
                    float planned = plan.Sum(entry => entry.amount);
                    float defense = DefenseOf(kingdom, sector);
                    if ((planned + going + ashore) * LandingAttack < defense * LandingHopeless)
                        why = $"兵力不足(装得上船{Mathf.RoundToInt(planned + going)}人，守势{Mathf.RoundToInt(defense)})";
                    else
                        foreach ((CorpsData whole, float amount) in plan)
                        {
                            CorpsData corps = whole;
                            if (amount < whole.strength)
                            {
                                corps = CorpsSystem.Detach(kingdom, whole, amount);
                                if (corps == null) continue;
                                SeaLandingDiagnostics.Vanguard(kingdom);
                            }
                            Dispatch(corps, sector, assigned, attackOn, attackLoad, ref shipping);
                            sent++;
                        }
                }
            }
            SeaLandingDiagnostics.Landing(kingdom, sector.City.kingdom, sent, going > 0f, sent > 0 ? null : why);
        }
    }

    // 拟派的军：离上船的港口不过 EmbarkReachZones 块的，由近及远(上月正在陆上交战的算远几块，不轻易把正打着的仗抽空)；
    // 义军守土不出海，飞机不坐船(见 CorpsSystem.StepTowardTarget)，都不派；整军装得进限额的整军上；
    // 一支整军都装不进时，排头的一支按限额分出先遣军(先遣军至少一千人或半军，见 Shippable)，分不出来又装得下船、
    // 不超过限额两倍的整军上
    private static List<(CorpsData corps, float amount)> PlanLanding(Kingdom kingdom, Sector sector, List<CorpsData> free,
        HashSet<CorpsData> assigned, float budget, float shipping, out string why)
    {
        why = null;
        var plan = new List<(CorpsData corps, float amount)>();
        var pool = new List<(CorpsData corps, float distance)>();
        bool needShip = false, hasPort = false;
        foreach (CorpsData corps in free)
        {
            if (assigned.Contains(corps) || corps.militia || CorpsArmSystem.Flies(corps) || Ashore(corps, sector)) continue;
            needShip = true;
            TileZone port = EmbarkPoint(kingdom, corps);
            if (port == null) continue;
            hasPort = true;
            float distance = CorpsSystem.ZoneDistance(CorpsSystem.Zone(corps), port);
            if (distance > EmbarkReachZones) continue;
            pool.Add((corps, distance + (EngagedLastMonth(corps) ? EngagedPenaltyZones : 0f)));
        }
        if (pool.Count == 0)
        {
            // 都在登陆那片陆地上、陆路走得到的不必坐船(下面陆上攻势照旧派)，不算没派
            why = !needShip ? free.Any(corps => !assigned.Contains(corps)) ? null : "兵力已派往别路"
                : hasPort ? "能上阵的军离港口都太远" : "本土没有能上船的港口";
            return plan;
        }
        pool.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : b.corps.strength.CompareTo(a.corps.strength));
        float planned = 0f;
        foreach (var entry in pool)
        {
            if (budget - planned < 1f) break;
            if (entry.corps.strength > budget - planned) continue;
            plan.Add((entry.corps, entry.corps.strength));
            planned += entry.corps.strength;
        }
        if (plan.Count > 0) return plan;
        CorpsData first = pool[0].corps;
        float amount = Mathf.Floor(budget);
        if (CorpsSystem.CanDetach(first) && amount < first.strength && amount >= Mathf.Min(MinVanguard, first.strength * 0.5f))
            plan.Add((first, amount));
        else if (first.strength <= shipping && first.strength <= budget * 2f)
            plan.Add((first, first.strength));
        else why = "可派的兵力不够一支先遣军";
        return plan;
    }

    // 派去打这一路登陆(与陆上攻势同样落位)：要上船的占运力
    private static void Dispatch(CorpsData corps, Sector sector, HashSet<CorpsData> assigned, Dictionary<Sector, float> attackOn,
        Dictionary<int, int> attackLoad, ref float shipping)
    {
        ForgetBeach(corps, sector.City);
        if (!Ashore(corps, sector)) shipping -= corps.strength;
        attackOn.TryGetValue(sector, out float strength);
        attackOn[sector] = strength + corps.strength;
        corps.task = "attack";
        corps.sector_city_id = sector.City.id;
        corps.target_zone_id = Spread(corps, sector.Targets, attackLoad)?.id ?? HomeZone(corps);
        corps.entrench = 0f;
        assigned.Add(corps);
    }

    // 运力为什么不够(诊断)
    private static string ShippingShortage(Kingdom kingdom)
    {
        if (!NavySystem.NavyEra(kingdom) && !NavySystem.NavyEra(Realm(kingdom))) return "缺运力(还没到有水师的时代)";
        Kingdom realm = Realm(kingdom);
        List<City> ports = NavySystem.Ports(kingdom).ToList();
        if (realm != null && realm != kingdom) ports.AddRange(NavySystem.Ports(realm));
        if (ports.Count == 0) return "缺运力(没有港口)";
        if (ports.All(NavySystem.IsBlockaded)) return "缺运力(港口被封锁，新船出港就被击沉)";
        return "缺运力(运输船还没造够)";
    }

    // 本月有登陆战线却一支军都派不出(都在溃退休整、在动员范围外)：各路登陆记下原因
    private static void NoteLandingIdle(Kingdom kingdom, List<Sector> sectors, string why)
    {
        foreach (Sector sector in sectors)
            if (sector.Landing) SeaLandingDiagnostics.Landing(kingdom, sector.City.kingdom, 0, false, why);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 远距离不宣战(用户 2026-10-22 转群友截图"超长宣战"，选"统一拦下并清理"，名分之战也一样要够得着)：
// 本模组十几处开战(诉求、帝号之争、逼冕、索取皇位、复国、朝贡、游牧、统一战争……)多不判远近，开起来两边打不着，
// 只在地图上挂一条横跨大半张图的箭头。开战总入口统一过这道闸(见 WarReachPatch)：
//   够得着 = 两边(帝国连同成员)接壤、同一块陆地上有两座城相距不过 LandTiles 格(同 FrontSystem)且中间不隔着天堑
//   (沿直线的地形阻力不过 LandCost，见 TerrainResistance.LineCost；用户 2026-10-26："过厚的山脉，冻土与沙地也会阻隔……印度打不到西藏")，
//   或隔海在水师辐射内(StraitReach)；
//   内乱不拦：叛乱、煽动、挑拨(神力)、全面战争，同一帝国内，封建上下级，开国不满一年的新政权宣战(刚分出来的叛军、割据打原主)。
// 已经开着的：每月看一次，两边所有参战国两两都够不着、连续 ClearMonths 个月就判和(劫掠之战有自己的期限，不管)；
// 主攻主守够不着的战争不画箭头(见 WarMarkerPatch)。
// 远方的参战国(用户 2026-10-27 截图"为什么这里能够跨距离宣战")：几千年前开的扩张战争靠同盟援战滚成半个世界，原主攻主守都亡了，
// 原版按军队多少另选主攻主守，选到隔着半个世界的两家，军团渡海往对面开；只要还有一对接壤就不判和。现在：
//   · 主攻主守够不着、别的参战国有够得着的：换成够得着的一对(军队多的先)，目标与战名随之重定(WarGoalSystem.Refocus)；
//   · 主攻主守以外的参战国连续 ClearMonths 个月一个敌方都够不着的，退出战争(远方盟友本来也不驰援，见 FrontSystem.InReach)
public static class WarReach
{
    public const float LandTiles = 80f;
    // 不接壤的两城之间沿直线的地形阻力上限：十块平地 10、翻一道三层厚的山约 23 还够得着；四层厚的大山、五层宽的大漠够不着
    public const float LandCost = 24f;
    private const int ClearMonths = 12;
    private const string LogTag = "[EmpireCraft][远战]";

    private static object _world;
    private static double _lastMonth = -1d;
    // 主攻主守够不着的战争(不画箭头)；全体参战国都够不着的已连着几个月
    private static readonly HashSet<long> Far = new();
    private static readonly Dictionary<long, int> FarMonths = new();
    // 主攻主守以外的参战国一个敌方都够不着、已连着几个月(战争, 国家)
    private static readonly Dictionary<(long war, long kingdom), int> AllyFarMonths = new();
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    public static bool IsFar(War war) => war?.data != null && Far.Contains(war.data.id);

    // 开战闸：够不着又不是内乱的不开
    public static bool MayDeclare(Kingdom attacker, Kingdom defender, WarTypeAsset type)
    {
        if (attacker?.data == null || defender?.data == null || attacker == defender) return true;
        // 宗主与殖民地、同一宗主的殖民地不开战(殖民地起兵独立、神力开战除外，见 ColonySystem)
        if (!ExemptType(type) && !ColonySystem.Rising && ColonySystem.Bound(attacker, defender)) return false;
        if (ExemptType(type) || Related(attacker, defender) || Young(attacker)) return true;
        if (Reachable(attacker, defender)) return true;
        string caller = Caller();
        if (Reported.Count < 512 && Reported.Add($"{caller}|{attacker.id}|{defender.id}"))
            LogService.LogInfo($"{LogTag} 拦下 {attacker.GetKingdomName()} 对 {defender.GetKingdomName()} 宣战(来自 {caller})：两边够不着");
        return false;
    }

    private static bool ExemptType(WarTypeAsset type) =>
        type != null && (type.total_war || type == WarTypeLibrary.rebellion || type == WarTypeLibrary.inspire ||
                         type == WarTypeLibrary.whisper_of_war);

    // 同一帝国内、封建上下级；刚离开对方帝国(起兵、叛离、独立，一年内)或反过对方帝国的
    // (好几处先请出帝国再开战，以前因此被当成两个外国拦下：成员直接独立、没有战争，史书照写起兵)
    private static bool Related(Kingdom a, Kingdom b)
    {
        Empire empire = a.GetEmpire();
        if (empire != null && !empire.isRekt() && empire == b.GetEmpire()) return true;
        if (FeudalVassalService.GetOverlord(a) == b || FeudalVassalService.GetOverlord(b) == a) return true;
        if (ColonySystem.MetropoleOf(a) == b || ColonySystem.MetropoleOf(b) == a) return true;
        return LeftFrom(a, b) || LeftFrom(b, a);
    }

    private static bool LeftFrom(Kingdom kingdom, Kingdom other)
    {
        Empire empire = other.GetEmpire();
        if (empire == null || empire.isRekt()) return false;
        if (kingdom.HasRebelledAgainst(empire)) return true;
        var data = kingdom.GetOrCreate();
        return data.left_empire_id == empire.id && data.left_empire_at >= 0d &&
               World.world.getCurWorldTime() >= data.left_empire_at && Date.getYearsSince(data.left_empire_at) < 1f;
    }

    // 读档：上一个存档的标记与计数作废(读档时世界对象不换，战争编号又从头来)
    public static void ResetWorldState()
    {
        _world = null;
        _lastMonth = -1d;
        Far.Clear();
        FarMonths.Clear();
        AllyFarMonths.Clear();
    }

    private static bool Young(Kingdom kingdom) => Date.getYearsSince(kingdom.data.created_time) < 1f;

    // 两边(帝国连同成员)有一对够得着：接壤、同一块陆地上相距不过 LandTiles 格，或任一边的水师够得着对方临海的城
    public static bool Reachable(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null || a.isRekt() || b.isRekt()) return false;
        List<Kingdom> ours = Members(a), theirs = Members(b);
        foreach (Kingdom y in theirs)
        {
            if (ours.Contains(y)) return true;
            foreach (Kingdom x in ours)
                if (x.IsNeighbourWith(y)) return true;
        }
        if (LandNear(ours, theirs)) return true;
        foreach (Kingdom y in theirs)
            if (StraitReach.Across(a, y)) return true;
        foreach (Kingdom x in ours)
            if (StraitReach.Across(b, x)) return true;
        return false;
    }

    private static bool LandNear(List<Kingdom> ours, List<Kingdom> theirs)
    {
        List<City> mine = Cities(ours), others = Cities(theirs);
        float limit = LandTiles * LandTiles;
        foreach (City x in mine)
        {
            WorldTile at = x.getTile();
            if (at == null) continue;
            foreach (City y in others)
            {
                if ((x.city_center - y.city_center).sqrMagnitude > limit) continue;
                WorldTile tile = y.getTile();
                if (tile != null && at.isSameIsland(tile) &&
                    TerrainResistance.LineCost(at.zone, tile.zone, x.kingdom) <= LandCost) return true;
            }
        }
        return false;
    }

    private static List<City> Cities(List<Kingdom> realm) =>
        realm.Where(kingdom => kingdom.cities != null)
            .SelectMany(kingdom => kingdom.cities)
            .Where(city => city?.data != null && !city.isRekt())
            .ToList();

    private static List<Kingdom> Members(Kingdom kingdom)
    {
        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived()) return new List<Kingdom> { kingdom };
        List<Kingdom> members = empire.kingdoms_list.Where(member => member?.data != null && !member.isRekt()).ToList();
        if (!members.Contains(kingdom)) members.Add(kingdom);
        return members;
    }

    // 哪一处开的战(日志用)：调用栈里头一个本模组、又不是这道闸自己的方法
    private static string Caller()
    {
        try
        {
            foreach (StackFrame frame in new StackTrace(2, false).GetFrames() ?? Array.Empty<StackFrame>())
            {
                Type type = frame.GetMethod()?.DeclaringType;
                if (type?.Namespace == null || !type.Namespace.StartsWith("EmpireCraft", StringComparison.Ordinal)) continue;
                if (type == typeof(WarReach) || type.Name.StartsWith("WarReach", StringComparison.Ordinal)) continue;
                return $"{type.Name}.{frame.GetMethod().Name}";
            }
        }
        catch (Exception)
        {
            // 只是日志
        }
        return "原版";
    }

    // 每月一次(CityPatch 的全世界扫描轮到时)：标出够不着的战争，久不接触的判和
    public static void TryMonthlyScan()
    {
        if (World.world?.wars == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _lastMonth = -1d;
            Far.Clear();
            FarMonths.Clear();
            AllyFarMonths.Clear();
        }
        double now = World.world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        try
        {
            Sweep();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 月度清理失败: {exception.Message}");
        }
    }

    private static void Sweep()
    {
        var alive = new HashSet<long>();
        // 本月已算过的两家(按政权)是否够得着
        var cache = new Dictionary<(long, long), bool>();
        foreach (War war in World.world.wars.list.ToList())
        {
            if (war?.data == null || !war.isAlive() || war.hasEnded() || war.isTotalWar()) continue;
            Kingdom attacker = war.main_attacker, defender = war.main_defender;
            if (attacker?.data == null || defender?.data == null || attacker.isRekt() || defender.isRekt()) continue;
            long id = war.data.id;
            alive.Add(id);
            if (ExemptType(war.getAsset()) || Related(attacker, defender))
            {
                Far.Remove(id);
                FarMonths.Remove(id);
                continue;
            }
            if (war.GetEmpireWarType() != EmpireWarType.劫掠) PruneDistant(war, cache);
            if (Reachable(attacker, defender) || Refocus(war, attacker, defender, cache))
            {
                Far.Remove(id);
                FarMonths.Remove(id);
                continue;
            }
            Far.Add(id);
            // 劫掠之战打满一年自己退兵(见 NomadRaidSystem)
            if (war.GetEmpireWarType() == EmpireWarType.劫掠 || AnyPairReachable(war))
            {
                FarMonths.Remove(id);
                continue;
            }
            int months = FarMonths.TryGetValue(id, out int count) ? count + 1 : 1;
            FarMonths[id] = months;
            if (months < ClearMonths) continue;
            FarMonths.Remove(id);
            Far.Remove(id);
            LogService.LogInfo($"{LogTag} {war.data.name}：{attacker.GetKingdomName()} 与 {defender.GetKingdomName()} " +
                               $"连着 {ClearMonths} 个月两边够不着，判和");
            World.world.wars.endWar(war, WarWinner.Peace);
        }
        Far.RemoveWhere(id => !alive.Contains(id));
        foreach (long id in FarMonths.Keys.Where(id => !alive.Contains(id)).ToList()) FarMonths.Remove(id);
        foreach (var key in AllyFarMonths.Keys.Where(key => !alive.Contains(key.war)).ToList()) AllyFarMonths.Remove(key);
    }

    private static bool Near(Kingdom a, Kingdom b, Dictionary<(long, long), bool> cache)
    {
        Kingdom x = RealmOf(a), y = RealmOf(b);
        if (x == null || y == null) return false;
        var key = x.id < y.id ? (x.id, y.id) : (y.id, x.id);
        if (cache.TryGetValue(key, out bool near)) return near;
        near = Related(a, b) || Reachable(a, b);
        cache[key] = near;
        return near;
    }

    // 主攻主守够不着：换成够得着的一对——先留主攻换主守，再留主守换主攻，再两家都换；各边军队多的先(同原版另选主攻主守)
    private static bool Refocus(War war, Kingdom attacker, Kingdom defender, Dictionary<(long, long), bool> cache)
    {
        List<Kingdom> attackers = Ranked(war._list_attackers), defenders = Ranked(war._list_defenders);
        Kingdom newAttacker = null, newDefender = defenders.FirstOrDefault(d => d != defender && Near(attacker, d, cache));
        if (newDefender == null)
        {
            newAttacker = attackers.FirstOrDefault(a => a != attacker && Near(a, defender, cache));
            if (newAttacker == null)
                foreach (Kingdom a in attackers)
                {
                    newDefender = defenders.FirstOrDefault(d => Near(a, d, cache));
                    if (newDefender == null) continue;
                    newAttacker = a;
                    break;
                }
        }
        if (newAttacker == null && newDefender == null) return false;
        string before = war.data.name;
        if (newAttacker != null) war.data.main_attacker = newAttacker.id;
        if (newDefender != null) war.data.main_defender = newDefender.id;
        WarGoalSystem.Refocus(war);
        LogService.LogInfo($"{LogTag} {before}：{attacker.GetKingdomName()} 与 {defender.GetKingdomName()} 够不着，" +
                           $"主攻主守改为够得着的 {war.main_attacker.GetKingdomName()} 与 {war.main_defender.GetKingdomName()}(今称 {war.data.name})");
        return true;
    }

    private static List<Kingdom> Ranked(List<Kingdom> side)
    {
        List<Kingdom> list = side?.Where(Alive).ToList() ?? new List<Kingdom>();
        list.Sort(KingdomListComponent.sortByArmy);
        return list;
    }

    // 主攻主守以外的参战国：连续 ClearMonths 个月一个敌方都够不着的退出战争
    private static void PruneDistant(War war, Dictionary<(long, long), bool> cache)
    {
        Kingdom mainA = war.main_attacker, mainD = war.main_defender;
        foreach (bool attackerSide in new[] { true, false })
        {
            List<Kingdom> own = (attackerSide ? war._list_attackers : war._list_defenders)?.Where(Alive).ToList() ?? new List<Kingdom>();
            List<Kingdom> enemies = Distinct(attackerSide ? war._list_defenders : war._list_attackers);
            foreach (Kingdom kingdom in own)
            {
                if (kingdom == mainA || kingdom == mainD) continue;
                var key = (war.data.id, kingdom.id);
                if (enemies.Any(enemy => Near(kingdom, enemy, cache)))
                {
                    AllyFarMonths.Remove(key);
                    continue;
                }
                int months = AllyFarMonths.TryGetValue(key, out int count) ? count + 1 : 1;
                if (months < ClearMonths)
                {
                    AllyFarMonths[key] = months;
                    continue;
                }
                AllyFarMonths.Remove(key);
                LogService.LogInfo($"{LogTag} {war.data.name}：{kingdom.GetKingdomName()} 连着 {ClearMonths} 个月一个敌方都够不着，退出战争");
                war.leaveWar(kingdom);
            }
        }
    }

    // 主攻主守之外，两边的参战国有一对够得着(盟友在打)就不算久不接触。按政权去重(同一帝国的成员算一家，
    // Reachable 本来就按全体成员算)，主攻主守那一对已经算过
    private static bool AnyPairReachable(War war)
    {
        List<Kingdom> attackers = Distinct(war._list_attackers), defenders = Distinct(war._list_defenders);
        Kingdom mainA = RealmOf(war.main_attacker), mainD = RealmOf(war.main_defender);
        foreach (Kingdom a in attackers)
        foreach (Kingdom d in defenders)
            if ((a != mainA || d != mainD) && Reachable(a, d)) return true;
        return false;
    }

    private static List<Kingdom> Distinct(List<Kingdom> side) =>
        side?.Where(Alive).Select(RealmOf).Distinct().ToList() ?? new List<Kingdom>();

    private static Kingdom RealmOf(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire != null && !empire.isRekt() && empire.CoreKingdom != null ? empire.CoreKingdom : kingdom;
    }

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();
}

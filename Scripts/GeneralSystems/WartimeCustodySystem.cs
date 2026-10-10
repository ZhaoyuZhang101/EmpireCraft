using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战时中央代管与战后分配(参照钢铁雄心：战时占领与最终归属分开)：
//   帝国成员在中央也参战的对外战争里打下的城，先交中央暂管，记下是谁打下的、哪一场战争；
//   该场战争结束后逐城结算最终归属：
//     旧都 → 中央；城市法理属于某成员 → 该成员；
//     中央衰弱(正统 < WeakCenterLegitimacy)且打下它的成员与之接壤 → 打下它的成员；
//     否则交给与它边界最长的成员(同长度时打下它的成员、中央优先)；没有成员与它接壤 → 中央直辖。
//   只把城交给与它接壤或持有其法理的成员，不会出现"山东道管到新疆"；成员内部再自行规划行政区。
//   独立盟国、封国(有自己宗主的诸侯)、帝国内战和叛乱不走代管，各自按原规则结算。
public static class WartimeCustodySystem
{
    private const int WeakCenterLegitimacy = 40;
    // 一次扫描最多结算几座城(易主有原版开销)，没结完的下个月继续
    private const int SettlePerScan = 4;
    private static double _lastScan = -1d;

    // 返回中央(需要代管)或 null(不代管)
    public static Kingdom CustodianFor(Kingdom capturer, Kingdom oldKingdom)
    {
        if (capturer == null || oldKingdom == null || !capturer.IsInEmpire()) return null;
        Empire empire = capturer.GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt() || core == capturer) return null;
        if (capturer.IsInSameEmpire(oldKingdom)) return null;
        if (capturer.IsFactionRebelling() || capturer.IsLocalRebelling()) return null;
        if (FeudalVassalService.GetOverlord(capturer) != null) return null;
        if (!core.isInWarWith(oldKingdom)) return null;
        return core;
    }

    // 城市交给中央之前调用
    public static void Record(City city, Kingdom capturer, Kingdom oldKingdom, War war)
    {
        var data = city?.GetOrCreate();
        if (data == null) return;
        data.custody_capturer_id = capturer?.id ?? -1L;
        data.custody_enemy_id = oldKingdom?.id ?? -1L;
        data.custody_war_id = war?.getID() ?? -1L;
        LogService.LogInfo($"[EmpireCraft][战时代管] {capturer?.name} 打下 {city.name}，交中央暂管");
    }

    public static void Clear(City city)
    {
        var data = city?.GetOrCreate();
        if (data == null) return;
        data.custody_capturer_id = data.custody_enemy_id = data.custody_war_id = -1L;
    }

    public static bool InCustody(City city) => city?.data != null && city.GetOrCreate().custody_capturer_id >= 0;

    // 每月检查一次(挂在 CityPatch 的世界扫描轮转上)
    public static void TryMonthlyScan()
    {
        if (World.world?.cities == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getMonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        int settled = 0;
        foreach (City city in World.world.cities)
        {
            if (settled >= SettlePerScan) break;
            if (city?.data == null || city.isRekt() || !InCustody(city)) continue;
            try
            {
                if (TrySettle(city)) settled++;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][战时代管] 结算 {city.name} 失败: {exception.Message}");
                Clear(city);
            }
        }
    }

    public static void ResetWorldState() => _lastScan = -1d;

    private static bool TrySettle(City city)
    {
        var data = city.GetOrCreate();
        Kingdom holder = city.kingdom;
        Empire empire = holder?.GetEmpire();
        // 代管期间城市又丢了、中央换了或帝国没了：代管作废
        if (holder == null || holder.isRekt() || empire == null || empire.isRekt() || empire.CoreKingdom != holder)
        {
            Clear(city);
            return false;
        }
        if (!WarOver(data.custody_war_id, holder, data.custody_enemy_id)) return false;
        Kingdom capturer = World.world.kingdoms.get(data.custody_capturer_id);
        Clear(city);
        Kingdom target = Allocate(city, empire, capturer);
        if (target == null || target == holder) return false;
        city.joinAnotherKingdom(target);
        TranslateHelper.LogEventMessage(string.Format(LM.Get("wartime_custody_settled"),
            empire.GetEmpireFullName(), city.GetCityName(), target.name), holder);
        return true;
    }

    private static bool WarOver(long warId, Kingdom core, long enemyId)
    {
        War war = warId >= 0 ? World.world.wars.get(warId) : null;
        if (war != null && war.isAlive() && !war.hasEnded()) return false;
        // 同一敌人还在别的战争里与中央交战：等那场也打完再分
        Kingdom enemy = enemyId >= 0 ? World.world.kingdoms.get(enemyId) : null;
        return enemy == null || enemy.isRekt() || !core.isInWarWith(enemy);
    }

    private static Kingdom Allocate(City city, Empire empire, Kingdom capturer)
    {
        Kingdom core = empire.CoreKingdom;
        // 收复旧都归中央
        City oldCapital = EmpireCoreManager.Get(empire)?.GetCoreCapital();
        if (oldCapital == city) return core;

        // 法理
        KingdomTitle title = city.GetTitle();
        Kingdom dejure = title?.main_kingdom;
        if (dejure == null && title?.owner != null && !title.owner.isRekt()) dejure = title.owner.kingdom;
        if (CanReceive(dejure, empire, core)) return dejure;

        bool capturerOk = CanReceive(capturer, empire, core);
        Dictionary<Kingdom, int> border = BorderLengths(city, empire);
        if (capturerOk && empire.Legitimacy < WeakCenterLegitimacy && border.ContainsKey(capturer)) return capturer;

        Kingdom best = null;
        int bestLength = 0;
        foreach (KeyValuePair<Kingdom, int> pair in border)
        {
            if (pair.Key != core && !CanReceive(pair.Key, empire, core)) continue;
            int length = pair.Value;
            bool better = length > bestLength ||
                          length == bestLength && best != null && Rank(pair.Key, capturer, core) < Rank(best, capturer, core);
            if (!better) continue;
            best = pair.Key;
            bestLength = length;
        }
        return best ?? core;
    }

    private static int Rank(Kingdom kingdom, Kingdom capturer, Kingdom core) =>
        kingdom == capturer ? 0 : kingdom == core ? 1 : 2;

    private static bool CanReceive(Kingdom kingdom, Empire empire, Kingdom core)
    {
        if (kingdom == null || kingdom.isRekt() || kingdom.capital == null) return false;
        if (kingdom == core) return true;
        if (!EmpireMembershipService.BelongsTo(empire, kingdom)) return false;
        if (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling() || kingdom.isInWarWith(core)) return false;
        // 治理能力：自己首府都濒临叛乱的成员不再接收新地
        var state = kingdom.capital.GetOrCreate().stability;
        if (state != null && state.owner_id == kingdom.id && state.stability < CityStabilityRules.RebellionThreshold) return false;
        return !EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom);
    }

    // 与本城相邻的各成员共有多少段边界(按区域四向相邻计)
    private static Dictionary<Kingdom, int> BorderLengths(City city, Empire empire)
    {
        var result = new Dictionary<Kingdom, int>();
        if (city.zones == null) return result;
        foreach (TileZone zone in city.zones)
        {
            if (zone?.neighbours == null || zone.city != city) continue;
            foreach (TileZone neighbour in zone.neighbours)
            {
                City other = neighbour?.city;
                if (other == null || other == city || other.isRekt()) continue;
                Kingdom kingdom = other.kingdom;
                if (kingdom == null || !EmpireMembershipService.BelongsTo(empire, kingdom)) continue;
                result.TryGetValue(kingdom, out int count);
                result[kingdom] = count + 1;
            }
        }
        return result;
    }
}

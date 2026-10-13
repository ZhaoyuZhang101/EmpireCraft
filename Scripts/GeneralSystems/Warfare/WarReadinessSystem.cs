using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 先动员、陈兵边境，再宣战：谋划开战(见 CityPopulationSystem.WarPlots)的进度满了也不马上宣战——
//   · 动员只动就近的军(动员范围见 WarScopeSystem，离敌境远的城照太平兵额、军留守驻地)；
//   · 要等动员范围里的军补足战时兵额(八成以上)、开到朝向目标国的边境(七成五兵力离边境两块地块以内)才宣战；
//   · 等满 12 个月还没集结好就仓促开战；与目标不接壤(跨海)、本国没有能动员的军时不等；
//   · 大军一开到边境，被侵略方就看得见，跟着动员(见 CityPopulationSystem.Alert)。
//   只在军团模式(无小人 + 占领模式)下生效，进度由 WarReadinessPatch 在 Plot.updateProgressTarget 前拦下。
public static class WarReadinessSystem
{
    private const float FilledShare = 0.8f, MassedShare = 0.75f;
    private const int MassedZones = 2;
    private const int MaxWaitMonths = 12;

    // 谋划 id → 开始等集结的时间
    private static readonly Dictionary<long, double> Waiting = new();

    public static void ResetWorldState() => Waiting.Clear();

    // 进度已满、大军还没陈兵边境：暂缓宣战
    public static bool Hold(Plot plot)
    {
        if (!CorpsSystem.Enabled || plot?.data == null || plot.getAsset() == null ||
            global::System.Array.IndexOf(CityPopulationSystem.WarPlots, plot.getAsset().id) < 0 ||
            plot.data.progress_current < plot.getProgressMax()) return false;
        Kingdom kingdom = plot.getAuthor()?.kingdom, target = plot.target_kingdom;
        if (kingdom?.data == null || target?.data == null || kingdom.isRekt() || target.isRekt()) return false;
        if (CityPopulationSystem.FrontZones(kingdom).Count == 0) return false;
        long key = plot.data.id;
        if (Ready(kingdom))
        {
            if (Waiting.Remove(key))
                FrontSystem.Log(kingdom, string.Format(LM.Get("war_ready_massed"), kingdom.GetKingdomName(), target.GetKingdomName()));
            return false;
        }
        double now = World.world.getCurWorldTime();
        if (!Waiting.TryGetValue(key, out double since))
        {
            Waiting[key] = now;
            FrontSystem.Log(kingdom, string.Format(LM.Get("war_ready_waiting"), kingdom.GetKingdomName(), target.GetKingdomName()));
            return true;
        }
        if (Date.getMonthsSince(since) < MaxWaitMonths) return true;
        Waiting.Remove(key);
        FrontSystem.Log(kingdom, string.Format(LM.Get("war_ready_rushed"), kingdom.GetKingdomName(), target.GetKingdomName()));
        return false;
    }

    // 本国动员的军已有开到边境的(对方看得见，见 CityPopulationSystem.Alert)
    public static bool Massing(Kingdom kingdom)
    {
        if (!CorpsSystem.Enabled || kingdom?.data == null) return false;
        IReadOnlyList<TileZone> front = CityPopulationSystem.FrontZones(kingdom);
        if (front.Count == 0) return false;
        return CorpsSystem.Of(kingdom).Any(corps => corps.task == "march" && corps.strength >= 1f &&
                                                    CorpsSystem.Zone(corps) is TileZone at &&
                                                    front.Any(zone => CorpsSystem.ZoneDistance(zone, at) <= MassedZones));
    }

    // 动员范围里的军补足了战时兵额、大部分开到了边境
    public static bool Ready(Kingdom kingdom)
    {
        List<CorpsData> list = CorpsSystem.Of(kingdom)
            .Where(corps => !corps.militia && corps.order_id < 0 && corps.task != "retreat" &&
                            WarScopeSystem.InScope(CorpsSystem.Home(corps))).ToList();
        if (list.Count == 0) return true;
        float full = list.Sum(corps => corps.full), strength = list.Sum(corps => corps.strength);
        if (strength <= 0f) return true;
        if (full > 0f && strength < full * FilledShare) return false;
        IReadOnlyList<TileZone> front = CityPopulationSystem.FrontZones(kingdom);
        float massed = list.Where(corps => CorpsSystem.Zone(corps) is TileZone at &&
                                           front.Any(zone => CorpsSystem.ZoneDistance(zone, at) <= MassedZones))
            .Sum(corps => corps.strength);
        return massed >= strength * MassedShare;
    }
}

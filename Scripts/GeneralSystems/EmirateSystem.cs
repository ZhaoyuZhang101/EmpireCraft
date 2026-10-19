using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 埃米尔坐大(用户 2026-10-17 转群友"游牧太强了几乎不会分封""还有阿拉伯"，选"埃米尔行省世袭坐大")：
//   · 阿拉伯政体的帝国打下的法理设为行省，由朝廷派的埃米尔治理(考选、不世袭、不通外交，见 Empire.update 的领土分封)；
//   · 哈里发正统不到 50 时，每个行省每年有 (50 − 正统)% 的机会埃米尔世袭其位：法理归他所有，改为世袭、通外交、
//     不再支援中央军——正统再掉到 40 以下就能拥兵自立为苏丹国(见 kingdom_allow_independent)；
//   · 鞭长莫及(用户 2026-10-19 选"鞭长莫及也会坐大""按帝国大小算")：正统再高，边远的行省也会坐大——各行省按都城离
//     帝国都城的远近排位，最远的三成才有份：排在七成处为 0，越往外越高，最远的到顶；顶多少看帝国多大，
//     8 城以下每年 4%，40 城以上每年 10%，中间平滑过渡。与正统那一项取大的。帝国越大越先从边远处裂开；
//   · 正统 60 以上才能削藩把世袭的埃米尔收回(见 TempFac_削藩)。
// 苏丹架空哈里发(用户 2026-10-19 选"补……苏丹架空哈里发""改用权臣机制")：本文化施行"苏丹官僚"后，哈里发照旧在位、
// 成为傀儡，苏丹以权臣掌实权、在本家承袭(同幕府的征夷大将军，见 MilitaryRegency)；离开阿拉伯政体时不再有苏丹。
public static class EmirateSystem
{
    public const int WeakLegitimacy = 50, StrongLegitimacy = 60;
    // 鞭长莫及：远近排位在 DistantPercentile 以外的行省有份；最远的每年机会(%)按帝国城数从 SmallMax 平滑升到 LargeMax
    public const float DistantPercentile = 0.7f;
    public const float DistantSmallMax = 4f, DistantLargeMax = 10f;
    public const int DistantSmallCities = 8, DistantLargeCities = 40;
    // 各帝国上次结算的时间(不存档：读档、换世界时清掉，重新计满一年)
    private static readonly Dictionary<long, double> LastYear = new();

    public static void ResetWorldState() => LastYear.Clear();

    public static bool Applies(Empire empire) => empire?.CoreKingdom?.GetRegime()?.type == RegimeType.Arabic;

    public static bool IsSultanate(Empire empire) =>
        Applies(empire) && MilitaryRegency.KindOf(empire) == MilitaryRegency.Sultan;

    // 离开阿拉伯政体：不再有苏丹、哈里发之分
    public static void OnRegimeChanged(Empire empire, RegimeType? previous, RegimeType current)
    {
        if (empire?.data == null || previous != RegimeType.Arabic || current == RegimeType.Arabic) return;
        if (MilitaryRegency.KindOf(empire) == MilitaryRegency.Sultan) MilitaryRegency.Dissolve(empire, null);
    }

    // 每年一次(由 Empire.update 调用)
    public static void Tick(Empire empire)
    {
        if (empire?.data == null || World.world == null || !Applies(empire)) return;
        double now = World.world.getCurWorldTime();
        if (LastYear.TryGetValue(empire.id, out double last) && now >= last && Date.getYearsSince(last) < 1) return;
        LastYear[empire.id] = now;
        int legitimacy = empire.Legitimacy;
        float weakChance = global::System.Math.Max(0, WeakLegitimacy - legitimacy);
        Dictionary<long, float> distantChances = DistantChances(empire);
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == empire.CoreKingdom || kingdom.king == null) continue;
            Regime regime = kingdom.GetRegime();
            KingdomTitle title = kingdom.GetAdministrativeTitle();
            if (regime?.type != RegimeType.Arabic || title == null || kingdom.GetMainTitle() != null) continue;
            distantChances.TryGetValue(kingdom.id, out float distantChance);
            if (UnityEngine.Random.value * 100f >= global::System.Math.Max(weakChance, distantChance)) continue;
            // 史书按主因写：远在边陲写鞭长莫及，否则写朝廷衰微
            bool distant = distantChance > weakChance;
            try
            {
                kingdom.king.AddOwnedTitle(title);
                kingdom.SetMainTitle(title);
                regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
                regime.SetAllowDiplomacy(true);
                regime.SetAllowSupportCenterArmy(false);
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
                string content = string.Format(LM.Get(distant ? "emirate_hereditary_distant" : "emirate_hereditary"),
                    kingdom.GetKingdomName(), kingdom.king.getName(), empire.GetEmpireName());
                empire.RecordHistory(directContent: content, kingdomId: kingdom.id);
                LogService.LogInfo("[EmpireCraft][埃米尔] " + content);
            }
            catch (global::System.Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][埃米尔] {kingdom.GetKingdomName()} 世袭失败: {exception.Message}");
            }
        }
    }

    // 鞭长莫及：各行省(含已世袭的埃米尔国，好让"边远"始终按版图算)按都城离帝国都城的直线距离排位，
    // 排位 = 第几近 / (行省数 − 1)，过了 DistantPercentile 才有份，线性升到最远的 DistantMax。只有一个行省时谈不上边远
    private static Dictionary<long, float> DistantChances(Empire empire)
    {
        var result = new Dictionary<long, float>();
        UnityEngine.Vector2? seat = SeatOf(empire.CoreKingdom);
        if (seat == null) return result;
        var ranked = new List<(Kingdom kingdom, float distance)>();
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == empire.CoreKingdom ||
                kingdom.GetRegime()?.type != RegimeType.Arabic) continue;
            UnityEngine.Vector2? position = SeatOf(kingdom);
            // 只比远近，按平均地形折算的系数(见 AdministrativeControlSystem.BeyondReach)不影响排位，这里不乘
            if (position != null) ranked.Add((kingdom, UnityEngine.Vector2.Distance(seat.Value, position.Value)));
        }
        if (ranked.Count < 2) return result;
        ranked.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.kingdom.id.CompareTo(b.kingdom.id));
        float max = DistantMax(empire.countCities());
        for (int i = 0; i < ranked.Count; i++)
        {
            float percentile = (float)i / (ranked.Count - 1);
            if (percentile <= DistantPercentile) continue;
            result[ranked[i].kingdom.id] = max * (percentile - DistantPercentile) / (1f - DistantPercentile);
        }
        return result;
    }

    // 最远行省每年的机会(%)：城数从 DistantSmallCities 到 DistantLargeCities 按 smoothstep 由 SmallMax 升到 LargeMax
    public static float DistantMax(int cities)
    {
        float t = UnityEngine.Mathf.Clamp01((float)(cities - DistantSmallCities) / (DistantLargeCities - DistantSmallCities));
        return DistantSmallMax + (DistantLargeMax - DistantSmallMax) * t * t * (3f - 2f * t);
    }

    // 治所位置(城心所在区块的格坐标，同行政控制力)
    private static UnityEngine.Vector2? SeatOf(Kingdom kingdom)
    {
        City capital = kingdom?.capital;
        if (capital?.data == null || capital.isRekt() || capital.zones == null || capital.zones.Count == 0) return null;
        TileZone zone = AdministrativeControlSystem.CenterZone(capital);
        return zone == null ? null : new UnityEngine.Vector2(zone.x, zone.y);
    }
}

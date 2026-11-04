using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诊断：战中推进线为什么没了、版图上的缺口是哪一种。只记录，不改任何东西。
//   每条推进线结束都按原因计数，一年一行合计；除了正常推满，各种撤销另记明细(全图每分钟至多 MaxDetailPerMinute 行)，
//   写上地块归哪座城、城主、真实占领者、推进方、是不是收复、进度，以及当前图层和整图画布上这块取到的归属。
//   撤销后过两秒回看那块地：地块脱离了城(实际领土变动)，还是城和占领记录都在、只有画布空了(绘制缺口)，分开记。
//   另外每半分钟抽查正在打仗的城有没有画布缺口，一轮至多查 GapCitiesPerScan 座城。不扫全图、不逐帧。
public static partial class FrontSystem
{
    private const int MaxDetailPerMinute = 8, MaxProbes = 16, GapCitiesPerScan = 16, MaxGapLinesPerScan = 3;
    private const float ProbeDelaySeconds = 2f, GapScanSeconds = 30f;

    private enum PushEnd
    {
        Completed,         // 正常推满
        SettleFailed,      // 推满了却占不下，或推进出错
        PushedBack,        // 被对方推到头
        AtPeace,           // 推进方与城主停战、或不再是敌对
        SideDestroyed,     // 推进方亡国
        CityDestroyed,     // 城没了
        CityChangedHands,  // 城市易主
        ZoneLeftCity,      // 地块改隶、脱离这座城
        OccupationCleared, // 要收复的地，占领记录已经没了
        ActionChanged,     // 同一家进攻与收复互换
        Abandoned          // 推进方的军都走了，别家要在这座城开推
    }

    private static readonly Dictionary<PushEnd, int> PushEndCounts = new();
    private static readonly List<(int zone, long city, PushEnd why, float at)> PushProbes = new();
    private static int _pushHandOvers, _pushDiagYear = int.MinValue, _pushDetailLines, _gapCursor;
    private static float _pushDetailWindow = -1f, _gapScanAt = -1f;

    private static void ResetPushDiagnostics()
    {
        PushEndCounts.Clear();
        PushProbes.Clear();
        _pushHandOvers = 0;
        _pushDiagYear = int.MinValue;
        _pushDetailLines = 0;
        _gapCursor = 0;
        _pushDetailWindow = -1f;
        _gapScanAt = -1f;
    }

    private static string PushEndLabel(PushEnd why) => why switch
    {
        PushEnd.Completed => "推满",
        PushEnd.SettleFailed => "结算失败",
        PushEnd.PushedBack => "被推回到头",
        PushEnd.AtPeace => "停战或不再敌对",
        PushEnd.SideDestroyed => "推进方亡国",
        PushEnd.CityDestroyed => "城没了",
        PushEnd.CityChangedHands => "城市易主",
        PushEnd.ZoneLeftCity => "地块改隶",
        PushEnd.OccupationCleared => "占领记录已没",
        PushEnd.ActionChanged => "进攻收复互换",
        PushEnd.Abandoned => "军已撤、让位",
        _ => why.ToString()
    };

    // 推进线为什么失效了
    private static PushEnd WhyInvalid(Push push)
    {
        City city = World.world.cities.get(push.City);
        if (city?.data == null || city.isRekt()) return PushEnd.CityDestroyed;
        Kingdom side = World.world.kingdoms.get(push.Side);
        if (side?.data == null || side.isRekt()) return PushEnd.SideDestroyed;
        if (World.world.zone_calculator.getZoneByID(push.Zone)?.city != city) return PushEnd.ZoneLeftCity;
        if ((city.kingdom?.id ?? -1L) != push.Owner) return PushEnd.CityChangedHands;
        if (push.Retake && city.GetTileZoneOccupier(World.world.zone_calculator.getZoneByID(push.Zone)) == null) return PushEnd.OccupationCleared;
        return PushEnd.AtPeace;
    }

    private static void NotePushHandOver(Push push, Kingdom heir)
    {
        _pushHandOvers++;
        if (!PushDetailAllowed()) return;
        LogService.LogInfo($"[EmpireCraft][战线诊断] 友军接手推进：{PushLine(push)}；接手方 {KingdomLabel(heir)}");
    }

    private static void NotePushEnd(Push push, PushEnd why)
    {
        PushEndCounts.TryGetValue(why, out int count);
        PushEndCounts[why] = count + 1;
        FlushPushCounts();
        if (why == PushEnd.Completed) return;
        if (PushProbes.Count < MaxProbes) PushProbes.Add((push.Zone, push.City, why, Time.unscaledTime));
        if (!PushDetailAllowed()) return;
        LogService.LogInfo($"[EmpireCraft][战线诊断] 推进撤销({PushEndLabel(why)})：{PushLine(push)}");
    }

    // 全图每分钟至多几行明细
    private static bool PushDetailAllowed()
    {
        float now = Time.unscaledTime;
        if (now < _pushDetailWindow || now - _pushDetailWindow >= 60f)
        {
            _pushDetailWindow = now;
            _pushDetailLines = 0;
        }
        return ++_pushDetailLines <= MaxDetailPerMinute;
    }

    private static string KingdomLabel(Kingdom kingdom) =>
        kingdom?.data == null ? "无" : kingdom.isRekt() ? $"已亡#{kingdom.id}" : $"{kingdom.GetKingdomName()}#{kingdom.id}";

    private static string PushLine(Push push)
    {
        try
        {
            City city = World.world.cities.get(push.City);
            TileZone zone = World.world.zone_calculator.getZoneByID(push.Zone);
            return $"城 {(city?.data == null ? "无" : city.GetCityName())}#{push.City} 城主 {KingdomLabel(city?.kingdom)} 地块 {push.Zone} 归城 {ZoneCityLabel(zone)} " +
                   $"占领者 {KingdomLabel(zone == null ? null : city?.GetTileZoneOccupier(zone))} 推进方 {KingdomLabel(World.world.kingdoms.get(push.Side))} " +
                   $"收复 {(push.Retake ? "是" : "否")} 不打仗 {(push.Free ? "是" : "否")} 方向 {push.Direction} 进度 {push.Progress:0.00} 显示 {SmoothBorders.DescribeDisplay(zone)}";
        }
        catch (Exception exception)
        {
            return $"城#{push.City} 地块 {push.Zone} (取详情失败: {exception.Message})";
        }
    }

    private static string ZoneCityLabel(TileZone zone) =>
        zone?.city?.data == null ? "无" : $"{zone.city.GetCityName()}#{zone.city.id}";

    // 一年一行合计
    private static void FlushPushCounts()
    {
        int year = Date.getCurrentYear();
        if (_pushDiagYear == year) return;
        if (_pushDiagYear != int.MinValue && (PushEndCounts.Count > 0 || _pushHandOvers > 0))
        {
            string counts = string.Join("、", PushEndCounts.OrderByDescending(pair => pair.Value)
                .Select(pair => $"{PushEndLabel(pair.Key)} {pair.Value}"));
            LogService.LogInfo($"[EmpireCraft][战线诊断] {_pushDiagYear} 年推进线结束：{counts}；友军接手 {_pushHandOvers} 次");
        }
        PushEndCounts.Clear();
        _pushHandOvers = 0;
        _pushDiagYear = year;
    }

    // 每帧(SmoothTick)：到点的才动，平时只比一下时间
    private static void PushDiagnosticsTick()
    {
        if (PushProbes.Count == 0 && Time.unscaledTime < _gapScanAt) return;
        try
        {
            float now = Time.unscaledTime;
            for (int i = PushProbes.Count - 1; i >= 0; i--)
            {
                var probe = PushProbes[i];
                if (now >= probe.at && now - probe.at < ProbeDelaySeconds) continue;
                PushProbes.RemoveAt(i);
                ProbeZone(probe.zone, probe.city, probe.why);
            }
            if (now >= _gapScanAt)
            {
                _gapScanAt = now + GapScanSeconds;
                ScanBattleGaps();
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][战线诊断] 回看失败: {exception.Message}");
            PushProbes.Clear();
        }
    }

    // 撤销后回看：地块脱离了原来的城是实际领土变动；城还在、画布却空了才是绘制缺口
    private static void ProbeZone(int zoneId, long cityId, PushEnd why)
    {
        TileZone zone = World.world?.zone_calculator?.getZoneByID(zoneId);
        if (zone == null) return;
        bool blank = SmoothBorders.CanvasBlank(zone);
        bool moved = zone.city?.id != cityId;
        if (!blank && !moved) return;
        string kind = blank ? "城和归属都在、画布空了(绘制缺口)" : zone.city == null ? "地块已不归任何城(实际领土变动)" : "地块改归别城(实际领土变动)";
        LogService.LogInfo($"[EmpireCraft][版图缺口] 推进撤销({PushEndLabel(why)})后回看：{kind}；地块 {zoneId} 原城#{cityId} 现归 {ZoneCityLabel(zone)} " +
                           $"城主 {KingdomLabel(zone.city?.kingdom)} 占领者 {KingdomLabel(zone.city?.GetTileZoneOccupier(zone))} 显示 {SmoothBorders.DescribeDisplay(zone)}");
    }

    // 抽查正在打仗的城：有城有归属、画布上却空着的地块
    private static void ScanBattleGaps()
    {
        if (!SmoothActive || !SmoothBorders.CanvasActive || Battles.Count == 0) return;
        List<long> ids = Battles.Keys.OrderBy(id => id).ToList();
        int lines = 0;
        for (int i = 0; i < Mathf.Min(GapCitiesPerScan, ids.Count) && lines < MaxGapLinesPerScan; i++)
        {
            _gapCursor = (_gapCursor + 1) % ids.Count;
            City city = World.world.cities.get(ids[_gapCursor]);
            if (city?.zones == null || city.isRekt()) continue;
            foreach (TileZone zone in city.zones)
            {
                if (zone == null || !SmoothBorders.CanvasBlank(zone)) continue;
                lines++;
                LogService.LogInfo($"[EmpireCraft][版图缺口] 战中抽查：城 {city.GetCityName()}#{city.id} 城主 {KingdomLabel(city.kingdom)} 地块 {zone.id} " +
                                   $"占领者 {KingdomLabel(city.GetTileZoneOccupier(zone))} 显示 {SmoothBorders.DescribeDisplay(zone)}");
                break;
            }
        }
    }
}

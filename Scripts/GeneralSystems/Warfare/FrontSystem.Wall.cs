using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 隔城墙攻坚(城墙沿线守方加成，用户 2026-10-21 定)：攻方要推进的那块地与攻方一侧隔着城墙——
//   紧挨着它(八方向)、攻方(连同友邦)控制的地块，以及紧挨着它的攻方军团所在地块，与它之间都隔着城墙、
//   又都没有对攻方开着的关门(见 WallPassage；关城那块地被攻方拿下，关门就对攻方开了)：
//     攻方战力 ×0.7；推进速度 ×0.5(地块模式每月推进的块数按它掷，平滑国界逐格推进的速度也按它)；
//     每月最多推进块数减一(至少一块)；
//   红夷大炮破城(攻方有重炮营，见 CorpsArmSystem.Breaches)时这几项减半：战力 ×0.85、推进速度 ×0.75、块数不减。
//   与关城守御(RuralSettlementSystem.FortFactor，守方战力加三到九成)叠加。
//   跨海登陆(四周没有攻方的地、也没有攻方军团挨着)、守方收复不算；挑推进的地块时先挑不隔墙的(见 FrontZone、Advance、AdvanceTarget)。
// 交战标记悬停里写"隔城墙攻坚"(见 CorpsMapRenderer.ShowBattleTooltip)。
public static partial class FrontSystem
{
    public const float WallAttack = 0.7f, WallPace = 0.5f;
    private const int WallAdvanceCut = 1;
    private const float WallLogSeconds = 2f;
    private static float _wallLoggedAt = -100f;

    // 攻方 side 推进 target 要隔城墙攻坚吗(spearheads：攻方挨着它的军团，从它们所在的地块过去也算)
    public static bool WallBetween(Kingdom side, TileZone target, IEnumerable<CorpsData> spearheads = null)
    {
        if (side?.data == null || target == null || !WallPassage.Active) return false;
        bool approached = false;
        foreach (TileZone neighbour in target.neighbours_all ?? Array.Empty<TileZone>())
        {
            if (neighbour == null || neighbour.world_edge || TerrainSystem.Impassable(neighbour)) continue;
            Kingdom controller = Controller(neighbour);
            if (controller == null || !Friendly(side, controller)) continue;
            approached = true;
            if (!WallPassage.BlocksKnown(neighbour, target, side)) return false;
        }
        if (spearheads != null)
            foreach (CorpsData corps in spearheads)
            {
                TileZone at = CorpsSystem.Zone(corps);
                if (at == null || CorpsSystem.ZoneDistance(at, target) > 1f) continue;
                if (at == target) return false;
                approached = true;
                if (!WallPassage.BlocksKnown(at, target, side)) return false;
            }
        return approached;
    }

    // 隔城墙时攻方战力、推进速度的倍数(红夷大炮破城减半)
    public static float WallAttackFactor(bool breach) => breach ? 1f - (1f - WallAttack) * 0.5f : WallAttack;

    public static float WallPaceFactor(bool breach) => breach ? 1f - (1f - WallPace) * 0.5f : WallPace;

    // 隔城墙时每月最多推进几块：少一块、至少一块；红夷大炮破城不减
    private static int WallMaxAdvance(int max, bool breach) => breach ? max : Mathf.Max(1, max - WallAdvanceCut);

    // 平滑国界逐格推进 zone 的速度倍数
    private static float WallPaceOf(Kingdom side, TileZone zone, List<CorpsData> spearheads) =>
        WallBetween(side, zone, spearheads) ? WallPaceFactor(spearheads != null && spearheads.Any(CorpsArmSystem.Breaches)) : 1f;

    // 诊断：隔城墙攻坚的交战(每 2 秒最多一行)
    private static void LogWall(City city, Kingdom attacker, Kingdom owner, bool breach, float fort, float ratio)
    {
        float now = Time.unscaledTime;
        if (now < _wallLoggedAt + WallLogSeconds && now >= _wallLoggedAt) return;
        _wallLoggedAt = now;
        string extra = breach ? "，红夷大炮破城减半" : "，每月最多推进块数减一";
        if (fort > 1f) extra += $"，关城守御 ×{fort:0.##}";
        LogService.LogInfo($"[EmpireCraft][隔墙攻坚] {city.GetCityName()}：{attacker.GetKingdomName()} 隔城墙攻 {owner.GetKingdomName()}，" +
                           $"攻方战力 ×{WallAttackFactor(breach):0.##}，推进速度 ×{WallPaceFactor(breach):0.##}{extra}，攻方胜算 {ratio:0.00}");
    }
}

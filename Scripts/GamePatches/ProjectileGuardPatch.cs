using System;
using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 远程攻击投射物为空(用户 2026-10-22 报 Projectile.start 里 AssetLibrary.get 空键)：
// 原版远程与否看缓存的攻击方式，投射物却取当下武器；武器换成近战、属性还没重算时投射物是空的，发射就崩。
// 发射前把空的或找不到的投射物换成箭、让该单位下次重算属性，按"单位种类+武器"各记一次日志
public class ProjectileGuardPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    public void Initialize()
    {
        new Harmony(nameof(ProjectileGuardPatch)).Patch(
            AccessTools.Method(typeof(ProjectileManager), nameof(ProjectileManager.spawn)),
            prefix: new HarmonyMethod(typeof(ProjectileGuardPatch), nameof(BeforeSpawn)));
    }

    public static void BeforeSpawn(BaseSimObject pInitiator, ref string pAssetID)
    {
        if (!string.IsNullOrEmpty(pAssetID) && AssetManager.projectiles.get(pAssetID) != null) return;
        string missing = string.IsNullOrEmpty(pAssetID) ? "(空)" : pAssetID;
        pAssetID = "arrow";
        try
        {
            Actor actor = pInitiator != null && pInitiator.isActor() ? pInitiator.a : null;
            actor?.setStatsDirty();
            string unit = actor?.asset?.id ?? pInitiator?.GetType().Name ?? "无";
            string weapon = actor?.getWeaponAsset()?.id ?? "无";
            if (Reported.Add(unit + "|" + weapon + "|" + missing))
                LogService.LogWarning($"[EmpireCraft][投射物] {unit} 远程攻击缺投射物 {missing}(武器 {weapon})，改用箭");
        }
        catch (Exception)
        {
            // 只是诊断，失败了照样发射
        }
    }
}

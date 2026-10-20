using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 草原常态(用户 2026-10-20 定"常态下——没灾、没雄主——的游牧国家"四项，全要)：
//   1. 平时不大扩张：大规模的游牧扩张诉求只在雄主在位时发动(见 TempFac_游牧扩张)；平时游牧政权(部落阶段除外，部落之间的征伐
//      照部落联合的规矩走，见 TribalWarSystem)主动开的一般战争只打 PeaceMaxCities 城以内、不在任何帝国里的小国——
//      至多小打小闹(原版挑宣战对象见 WarTargetPatch，帝国与成员见 EmpireCraftPlotsAddition.GetWarTarget)；
//      平时的动静只有草原压力引起的劫掠(见 NomadRaidSystem)与下面的争草场。
//   2. 部落争草场(见 SteppeOrderSystem.Feuds)；
//   3. 依附强部(见 SteppeOrderSystem.Attach)；
//   4. 朝贡换赏赐(见 SteppeOrderSystem.Tribute)。
// 每年随草原压力一起结算(NomadRaidSystem 算完压力后调用 Yearly)，每月看争草场到期(Monthly)。诊断日志 [EmpireCraft][游牧常态]。
public static partial class SteppeOrderSystem
{
    public const string LogTag = "[EmpireCraft][游牧常态]";
    public const int PeaceMaxCities = 2;

    // 平时(没有雄主在位)的游牧政权主动开一般战争，只许打两城以内、不在帝国里的小国；别的政权、雄主、部落不受此限
    public static bool PeacetimeWarAllowed(Kingdom initiator, Kingdom target)
    {
        if (initiator?.data == null || target?.data == null) return true;
        Kingdom realm = FrontSystem.Realm(initiator);
        if (!NomadRaidSystem.IsNomad(realm) || TribalFissionService.IsTribal(initiator) || RulerTraitSystem.FounderReigns(realm))
            return true;
        return !target.IsEmpire() && !target.IsInEmpire() && (target.cities?.Count ?? 0) <= PeaceMaxCities;
    }

    // 每年：nomads 是算过草原压力的有劫掠传统的国家
    public static void Yearly(List<Kingdom> nomads)
    {
        try
        {
            FeudsYearly(nomads);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 争草场结算失败: {exception.Message}");
        }
        try
        {
            AttachYearly(nomads);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 依附强部结算失败: {exception.Message}");
        }
        try
        {
            TributeYearly(nomads);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 朝贡换赏赐结算失败: {exception.Message}");
        }
    }

    public static void Monthly()
    {
        try
        {
            FeudsMonthly();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 争草场到期结算失败: {exception.Message}");
        }
    }
}

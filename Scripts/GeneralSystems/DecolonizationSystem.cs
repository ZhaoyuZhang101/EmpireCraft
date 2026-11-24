using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 全球去殖民浪潮(用户 2026-11-24 第五批)：殖民地原本只按各自的离心一块一块闹独立，没有"殖民帝国的黄昏"那样的大势。
//   · 起：世上还有三块以上殖民地，近代国家过半进了现代(第九个科技时代)，又逢世界大战打完不到二十年、或万国公会已经成立；
//     上一回浪潮落幕不满五十年的不再起；
//   · 浪潮里：各殖民地的离心每年多涨六点、宗主强过自己六倍才不敢起兵(平时三倍，见 ColonySystem.Drift)；
//     离心过半、不在打仗的殖民地(租借地有租期，不算)，宗主每年有机会顺应潮流放手让它独立——左翼、自由派、中间派当政的
//     两成，正在萧条里、是万国公会会员的各多一成；威权、保守当政的只有百分之三，多半要等殖民地自己起兵；
//   · 止：殖民地一块不剩，或者满了四十年。
// 每年在世界格局的年度结算里走一遍，数据在 WorldOrderData。数值都是模组自己的规则。
public static class DecolonizationSystem
{
    private const int MinColonies = 3, ModernEra = 9, AfterWarYears = 20, QuietYears = 50, MaxYears = 40;
    private const float WaveDrift = 6f, WaveDare = 6f, NormalDare = 3f;
    private const float WillingChance = 0.2f, UnwillingChance = 0.03f, SlumpBonus = 0.1f, LeagueBonus = 0.1f, ReleaseAutonomy = 50f;

    private static WorldOrderData Data => WorldOrderSystem.Data;

    public static bool Active => Data.decolonization_since >= 0d;
    public static double Since => Data.decolonization_since;

    // 殖民地离心每年多涨多少、宗主强过自己几倍才不敢起兵(见 ColonySystem.Drift)
    public static float DriftBonus => Active ? WaveDrift : 0f;
    public static float DareRatio => Active ? WaveDare : NormalDare;

    private static List<Kingdom> Colonies() => World.world.kingdoms.list
        .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && ColonySystem.IsColony(kingdom)).ToList();

    // 每年(WorldOrderSystem.Scan)
    internal static void Yearly(List<Empire> states, List<Empire> greats, double now)
    {
        List<Kingdom> colonies = Colonies();
        if (!Active)
        {
            if (colonies.Count < MinColonies || states.Count == 0) return;
            if (Data.decolonization_ended >= 0d && Date.getYearsSince(Data.decolonization_ended) < QuietYears) return;
            int modern = states.Count(empire => TechnologySystem.GetEraTier(InstitutionSystem.GetPrimaryCulture(empire)) >= ModernEra);
            if (modern * 2 < states.Count) return;
            bool afterWar = Data.world_war_ended_at >= 0d && Date.getYearsSince(Data.world_war_ended_at) < AfterWarYears;
            if (!afterWar && WorldOrderSystem.League == null) return;
            Data.decolonization_since = now;
            WorldOrderSystem.Record(LM.Get("decolonization_started_history"), greats.FirstOrDefault() ?? states[0], states);
            return;
        }
        int years = Date.getYearsSince(Data.decolonization_since);
        if (colonies.Count == 0 || years >= MaxYears)
        {
            Data.decolonization_since = -1d;
            Data.decolonization_ended = now;
            WorldOrderSystem.Record(string.Format(LM.Get("decolonization_ended_history"), years), greats.FirstOrDefault(), states);
            return;
        }
        foreach (Kingdom colony in colonies)
        {
            ColonyState state = ColonySystem.Of(colony);
            Kingdom metropole = ColonySystem.MetropoleOf(colony);
            if (state == null || metropole == null || state.kind == ColonyState.Lease || state.autonomy < ReleaseAutonomy ||
                colony.hasEnemies() || metropole.hasEnemies()) continue;
            Empire lord = metropole.GetEmpire();
            PartyIdeology ideology = IdeologyFamilies.StateIdeology(metropole);
            bool willing = IdeologyFamilies.IsLeft(ideology) || IdeologyFamilies.IsLiberal(ideology) || ideology == PartyIdeology.Centrism;
            float chance = willing ? WillingChance : UnwillingChance;
            if (lord != null && EconomicCycleSystem.InCrisis(lord)) chance += SlumpBonus;
            if (lord != null && WorldOrderSystem.IsLeagueMember(lord)) chance += LeagueBonus;
            if (UnityEngine.Random.value >= chance) continue;
            ColonySystem.Release(colony, string.Format(LM.Get("decolonization_released_history"), colony.GetKingdomFullName(),
                metropole.GetKingdomFullName()), friendly: metropole);
        }
    }
}

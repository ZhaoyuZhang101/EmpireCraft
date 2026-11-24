using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 航天竞赛(用户 2026-11-24 第五批)：原子时代以后大国之间不动刀兵的较量。
//   · 三步：人造卫星(要研究出人造卫星)、载人航天、登月(都要研究出载人航天)；先得有火箭技术；
//   · 大国和结了阵营的国家才下场：每年从国库拨一笔(照一档福利的开支)，攒进度；冷战里更卖力，国民教育办得好也更快；
//   · 天下头一个办到的记入史册、正统大涨、执政表现看涨；它若正在冷战，对面紧张陡增、奋起直追(进度多攒一截)；
//     后来者只记一笔；
//   · 每走完一步，本文化的研究点多百分之三(见 ModernPolicySystem.ResearchBonus)。
// 数据随世界格局存档(WorldOrderData.space_*)，每年在世界格局的年度结算里走一遍。数值都是模组自己的规则。
public static class SpaceRaceSystem
{
    public const int Satellite = 1, Crewed = 2, Moon = 3;
    public const string RocketTech = "rocketry", SatelliteTech = "satellites", ProgramTech = "space_program";
    private const float YearlyProgress = 20f, ColdWarProgress = 10f, EducationProgress = 5f, RivalSpur = 15f;
    public const float ResearchPerLevel = 0.03f;

    private static WorldOrderData Data => WorldOrderSystem.Data;

    public static int Level(Empire empire) =>
        empire != null && Data.space_levels != null && Data.space_levels.TryGetValue(empire.id, out int level) ? level : 0;

    public static float Progress(Empire empire) =>
        empire != null && Data.space_progress != null && Data.space_progress.TryGetValue(empire.id, out float value) ? value : 0f;

    public static IReadOnlyList<SpaceFirstData> Firsts => Data.space_firsts;

    private static string TechFor(int step) => step == Satellite ? SatelliteTech : ProgramTech;

    // 每年(WorldOrderSystem.Scan)
    internal static void Yearly(List<Empire> states, List<Empire> greats, double now)
    {
        Data.space_levels ??= new Dictionary<long, int>();
        Data.space_progress ??= new Dictionary<long, float>();
        Data.space_firsts ??= new List<SpaceFirstData>();
        foreach (Empire empire in states)
        {
            int level = Level(empire);
            if (level >= Moon) continue;
            string culture = InstitutionSystem.GetPrimaryCulture(empire);
            int next = level + 1;
            if (!TechnologySystem.HasTech(culture, RocketTech) || !TechnologySystem.HasTech(culture, TechFor(next))) continue;
            BlocData bloc = WorldOrderSystem.BlocOf(empire);
            if (!greats.Contains(empire) && bloc == null) continue;
            Kingdom core = empire.CoreKingdom;
            int cost = Math.Max(10, ConstitutionalEconomySystem.GetWelfareAnnualCost(empire, 1));
            if (core.GetMoney() < cost * 2) continue;
            core.SubMoney(cost, TreasuryCategory.Research);
            bool coldWar = bloc != null && WorldOrderSystem.ColdWars.Any(war => war.a == bloc.id || war.b == bloc.id);
            ConstitutionalEconomyState state = empire.data?.constitutional_economy;
            float gain = YearlyProgress + (coldWar ? ColdWarProgress : 0f) +
                         (state is { education_funded: true } ? EducationProgress * state.education_level : 0f);
            float progress = Progress(empire) + gain;
            if (progress < 100f)
            {
                Data.space_progress[empire.id] = progress;
                continue;
            }
            Data.space_progress[empire.id] = 0f;
            Data.space_levels[empire.id] = next;
            string name = empire.GetEmpireFullName();
            if (Data.space_firsts.Any(first => first.kind == next))
            {
                EventRecorder.RecordRoutine(empire, string.Format(LM.Get("space_reached_" + next + "_history"), name));
                continue;
            }
            Data.space_firsts.Add(new SpaceFirstData { kind = next, empire_id = empire.id, at = now, name = name });
            WorldOrderSystem.Record(string.Format(LM.Get("space_first_" + next + "_history"), name), empire, greats);
            empire.AddMandate(8);
            PartyFinanceSystem.Adjust(empire, "approval_space", 5f);
            // 冷战的对面：紧张陡增，奋起直追
            if (bloc == null) continue;
            foreach (ColdWarData war in WorldOrderSystem.ColdWars)
            {
                if (war.a != bloc.id && war.b != bloc.id) continue;
                war.tension = Mathf.Min(100f, war.tension + 8f);
                BlocData rival = WorldOrderSystem.Bloc(war.a == bloc.id ? war.b : war.a);
                if (WorldOrderSystem.LeaderOf(rival) is { } other && Level(other) < next)
                    Data.space_progress[other.id] = Mathf.Min(99f, Progress(other) + RivalSpur);
            }
        }
    }

    public static string StepName(int step) => LM.Get("space_step_" + Mathf.Clamp(step, 0, Moon));
}

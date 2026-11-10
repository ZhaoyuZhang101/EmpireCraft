using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 修筑城防(用户 2026-11-11 "增加更多的议题，比如开战，或者其他的符合利益的"，问答全选)：与外国接壤的边城(至多六座)起城墙，征发民力记工役；二十年内这些边城遭劫掠同屯田实边守得住
// (见 NomadRaidSystem.Defense)。军人怨气 -3、农民 +3。十五年一次
public class TempFac_修筑城防 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_修筑城防();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 15;

    public override bool CheckCondition() => GetEmpire()?.CoreKingdom != null && OffCooldown() && ClaimPolicySystem.HasForeignBorder(GetEmpire());

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Empire empire = GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (core != null)
        {
            List<City> cities = ClaimPolicySystem.ForeignBorders(empire).Select(pair => pair.own)
                .Where(city => city?.data != null && !city.isRekt()).Distinct().Take(6).ToList();
            foreach (City city in cities)
            {
                city.GetOrCreate().plan_walls = 1;
                CityPlanWorks.SyncWalls(city);
            }
            CorveeSystem.Add(core, cities.Count * CorveeSystem.IrrigationPerCity, false);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Army, -3f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, 3f);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_修筑城防_done"), cities.Count);
        }
        End();
    }
}

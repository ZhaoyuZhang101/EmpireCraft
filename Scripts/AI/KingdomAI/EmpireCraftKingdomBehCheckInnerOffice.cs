using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;
public class EmpireCraftKingdomBehCheckInnerOffice: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.IsEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            SelectOfficer(empire);
            StartCalcOfficePerformance(empire);
            CheckOfficePower(empire);
        }
        return BehResult.Continue;
    }

    public void CheckOfficePower(Empire empire)
    {
        foreach (var coreOffice in empire.data.centerOffice.CoreOffices)
        {
            var office = OfficeManager.Offices.TryGetValue(coreOffice, out var oo) ? oo : null;
            office?.DetectPower(empire);
        }

        foreach (var divisionOffice in empire.data.centerOffice.Divisions)
        {
            var office = OfficeManager.Offices.TryGetValue(divisionOffice, out var oo) ? oo : null;
            office?.DetectPower(empire);
        }
    }
    private void StartCalcOfficePerformance(Empire pEmpire)
    {
        if (pEmpire.IsNeedToOfficeExam())
        {
            pEmpire.AddRenown(-(int)(pEmpire.CoreKingdom.getRenown() * 0.07));

            Dictionary<Actor, double> pData = new Dictionary<Actor, double>();
            List<Actor> officers = pEmpire.data.centerOffice.GetAllOfficers(pEmpire);
            if (officers.Count > 0)
            {
                foreach (Actor actor in officers)
                {
                    OfficeIdentity identity = actor.GetIdentity();
                    if (identity == null) continue;
                    if (identity.performanceEvents == null) continue;
                    (PerformanceEvent pEvent, double pValue) performance = identity.performanceEvents.TriggerEvent(actor);
                    actor.editRenown((int)(performance.pValue*0.4));
                    //记录事件
                    pData[actor] = performance.pValue;
                    actor.GetIdentity().TotalPerformance += performance.pValue;
                    // LogService.LogInfo($"{actor.name}{performance.pEvent.is_good}{performance.pEvent.eventType}绩效增加{performance.pValue},当前绩效{actor.GetIdentity().TotalPerformance}");
                    actor.ResetPerformance();
                }
                if (pData.Values.Count > 0)
                {
                    double averagePerformance = pData.Values.Average();
                    double variancePerformance = pData.Values.Select(x => Math.Pow(x - averagePerformance, 2)).Average(); // 计算方差
                    double standardDeviationPerformance = Math.Sqrt(variancePerformance); // 计算标准方差
                    foreach (var item in pData)
                    {
                        Actor actor = item.Key;
                        double mark = item.Value;
                        if (mark >= averagePerformance + standardDeviationPerformance)
                        {
                            actor.AddOfficeExamLevel(EmpireExamLevel.HD);
                        }
                        else if (mark >= averagePerformance)
                        {
                            actor.AddOfficeExamLevel(EmpireExamLevel.CR);
                        }
                        else if (mark >= averagePerformance - standardDeviationPerformance)
                        {
                            actor.AddOfficeExamLevel(EmpireExamLevel.P);
                        }
                        else
                        {
                            actor.AddOfficeExamLevel(EmpireExamLevel.F);
                        }
                    }
                }
            }
            pEmpire.data.last_office_exam_timestamp = World.world.getCurWorldTime();
        }
    }
    //三省六部官员选拔机制
    public void SelectOfficer(Empire pEmpire)
    {
        foreach (var core in pEmpire.data.centerOffice.CoreOffices)
        {
            if (OfficeManager.Offices.TryGetValue(core, out var value))
            {
                if (value.GetOnTime() > 3||value.GetOnTime()<0)
                {
                    value.Select(pEmpire.CoreKingdom);
                }
            }
        }
        foreach (var division in pEmpire.data.centerOffice.Divisions)
        {
            if (OfficeManager.Offices.TryGetValue(division, out var value))
            {
                if (value.GetOnTime() > 3||value.GetOnTime()<0)
                {
                    value.Select(pEmpire.CoreKingdom);
                }
            }
        }
    }
}

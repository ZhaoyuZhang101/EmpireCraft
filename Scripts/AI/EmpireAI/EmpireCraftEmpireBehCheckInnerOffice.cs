using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.AI.EmpireAI;
public class EmpireCraftEmpireBehCheckInnerOffice: GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived()) return BehResult.Continue;
        if (empire.data.centerOffice == null)
        {
            var core = empire.CoreKingdom;
            if (core == null) return BehResult.Continue;
            empire.data.centerOffice = new CenterOffice();
            empire.data.centerOffice.Init(core);
        }
        SelectOfficer(empire);
        StartCalcOfficePerformance(empire);
        CheckOfficePower(empire);
        return base.execute(pKingdom);
    }

    public void CheckOfficePower(Empire empire)
    {
        if (empire == null || empire.isRekt() || empire.IsArchived()) return;
        var center = empire.data.centerOffice;
        if (center == null) return;
        empire.data.additions = new EmpireAddition();
        var office = center.GetAllOffices(empire);
        if (office == null) return;
        office.Shuffle();
        office.ForEach(o=>o.DetectPower(empire));
    }
    private void StartCalcOfficePerformance(Empire pEmpire)
    {
        if (pEmpire == null || pEmpire.isRekt() || pEmpire.IsArchived()) return;
        var center = pEmpire.data.centerOffice;
        if (center == null) return;
        if (pEmpire.IsNeedToOfficeExam())
        {
            pEmpire.AddRenown(-(int)(pEmpire.CoreKingdom.getRenown() * 0.07));

            Dictionary<Actor, double> pData = new Dictionary<Actor, double>();
            List<Actor> officers = center.GetAllOfficers(pEmpire);
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
        if (pEmpire == null || pEmpire.isRekt() || pEmpire.IsArchived()) return;
        var coreKingdom = pEmpire.CoreKingdom;
        if (coreKingdom == null) return;
        var center = pEmpire.data.centerOffice;
        if (center == null) return;
        foreach (var core in center.CoreOffices)
        {
            if (OfficeManager.Offices.TryGetValue(core, out var value))
            {
                if (value.GetOnTime() > 3||value.GetOnTime()<0)
                {
                    value.Select(coreKingdom);
                }
            }
        }
        foreach (var division in center.Divisions)
        {
            if (OfficeManager.Offices.TryGetValue(division, out var value))
            {
                if (value.GetOnTime() > 3||value.GetOnTime()<0)
                {
                    value.Select(coreKingdom);
                }
            }
        }
        foreach (var harem in center.Harems)
        {
            if (OfficeManager.Offices.TryGetValue(harem, out var value))
            {
                var emperor = pEmpire.Emperor;
                if (emperor != null && !emperor.isRekt())
                {
                    if (value.officeType == 13)
                    {
                        if (value.GetActor() != emperor.lover || !(emperor.hasLover()))
                        {
                            value.Select(coreKingdom);
                        }
                    }
                    else
                    {
                        var actor = value.GetActor();
                        if (value.GetOnTime() < 0 || (actor != null && actor.age > 35))
                        {
                            value.Select(coreKingdom);
                        }
                    }
                }
                else
                {
                    value.RemoveActor();
                }
            }
        }
    }
}

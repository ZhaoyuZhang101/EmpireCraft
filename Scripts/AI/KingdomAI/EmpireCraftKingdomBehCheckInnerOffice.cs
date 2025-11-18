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
        }
        return BehResult.Continue;
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
                    LogService.LogInfo($"{actor.name}{performance.pEvent.is_good}{performance.pEvent.eventType}绩效增加{performance.pValue},当前绩效{actor.GetIdentity().TotalPerformance}");
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
    //内阁官员选拔机制
    public void SelectOfficer(Empire pEmpire)
    {
        foreach (var core in pEmpire.data.centerOffice.CoreOffices)
        {
            
        }
        foreach (var division in pEmpire.data.centerOffice.Divisions)
        {
            
        }
        foreach (var kingdom in pEmpire.kingdoms_hashset)
        {
            
        }
    }

    private void SetOfficeBase(OfficeObject obj, Empire pEmpire)
    {
        long id = obj.actor_id;
        Actor actor = World.world.units.get(id);
        if (actor != null)
        {
            if (actor.GetPeeragesLevel() == PeeragesLevel.peerages_0)
            {
                obj.RemoveActor();
                id = obj.actor_id;
                actor = World.world.units.get(id);
            }
        }
        if (actor == null || id == -1L)
        {
            ListPool<Actor> pool = new ListPool<Actor>();
            ListPool<Actor> pool2 = new ListPool<Actor>();
            ListPool<Actor> pool3 = new ListPool<Actor>();
            foreach (Kingdom kingdom in pEmpire.kingdoms_list)
            {
                foreach (Actor potential in kingdom.units)
                {
                    if (potential != null)
                    {
                        if (potential.IsEmperor()) continue;
                        if (potential.isUnitFitToRule() && potential.hasTrait("officer"))
                        {
                            OfficeIdentity identity = potential.GetIdentity();
                            if (identity == null) continue;
                            if (identity.honoraryOfficial <= 2)
                            {
                                pool.Add(potential);
                            }
                        }
                        if (potential.hasClan() && !potential.isOfficer())
                        {
                            if (potential.clan == pEmpire.CoreKingdom.getKingClan())
                            {
                                pool2.Add(potential);
                            }
                        }

                        foreach (string requireTrait in obj.require_traits)
                        {
                            if (potential.hasTrait(requireTrait) && !potential.isOfficer())
                            {
                                pool3.Add(potential);
                                break;
                            }
                        }
                    }
                }
            }
            bool flag = false;
            Actor final = null;
            if (pool.Any())
            {
                final = pool.First();
                flag = true;
            }
            else if (pool2.Any())
            {
                if (pEmpire.CoreKingdom.hasCulture())
                {
                    final = ListSorters.getUnitSortedByAgeAndTraits(pool2, pEmpire.CoreKingdom.culture);
                }
                else
                {
                    pool2.Sort(ListSorters.sortUnitByAgeOldFirst);
                    final = pool2.First();
                }
                flag = true;
            }
            else if (pool3.Any())
            {
                if (pEmpire.CoreKingdom.hasCulture())
                {
                    final = ListSorters.getUnitSortedByAgeAndTraits(pool3, pEmpire.CoreKingdom.culture);
                } else
                {
                    pool3.Sort(ListSorters.sortUnitByAgeOldFirst);
                    final = pool3.First();
                }
                flag = true;
            }
            if (flag)
            {
                SetOfficer(id, final);
                final.joinCity(pEmpire.CoreKingdom.capital);
                final.goTo(pEmpire.CoreKingdom.capital._city_tile);
            }
        } else
        {
            if (obj.GetOnTime()>=16)
            {
                obj.RemoveActor();
            }
        }
    }

    public static void SetOfficer(long oid, Actor pActor)
    {
        if (OfficeManager.Offices.TryGetValue(oid, out var oObject))
        {
            oObject.SetActor(pActor);
            return;
        }
        LogService.LogInfo("设置官员失败");
    }
    //设置三省
    private void SelectCoreOffices(Empire pEmpire)
    {
        foreach(var office_id in pEmpire.data.centerOffice.CoreOffices)
        {
            if (OfficeManager.Offices.TryGetValue(office_id, out var oObject))
            {
                SetOfficeBase(oObject, pEmpire);
            }
        }
    }
    //设置六部
    private void SelectDivisions(Empire pEmpire)
    {
        foreach (var office_id in pEmpire.data.centerOffice.Divisions)
        {
            if (OfficeManager.Offices.TryGetValue(office_id, out var oObject))
            {
                SetOfficeBase(oObject, pEmpire);
            }
        }
    }
}

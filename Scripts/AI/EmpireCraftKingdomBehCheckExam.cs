using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.AI;
public class EmpireCraftKingdomBehCheckExam: BehaviourActionKingdom
{
    public override BehResult execute(Kingdom pKingdom)
    {
        if ( pKingdom.isEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (empire.isNeedToExam())
            {
                LogService.LogInfo("科举年");
                foreach(City city in empire.AllCities())
                {
                    ExamSystem.startExam(ExamSystem.ExamType.City, city);
                }
                foreach(Province province in empire.province_list)
                {
                    ExamSystem.startExam(ExamSystem.ExamType.Province, province);
                }
                ExamSystem.startExam(ExamSystem.ExamType.Empire, empire);
                empire.data.last_exam_timestamp = World.world.getCurWorldTime();
            }
            if (empire.isNeedToOfficeExam()) 
            {
                LogService.LogInfo("吏部考科");
                Dictionary<Actor, double> pData = new Dictionary<Actor, double>();
                List<Actor> officers = empire.data.centerOffice.GetAllOfficers(empire);
                if (officers.Count > 0)
                {
                    foreach (Actor actor in officers)
                    {
                        OfficeIdentity identity = actor.GetIdentity(empire);
                        if (identity == null) continue;
                        if (identity.performanceEvents == null) continue;
                        (PerformanceEvent pEvent, double pValue) performance = identity.performanceEvents.TriggerEvent();
                        LogService.LogInfo("事件：" + performance.pEvent.eventType.ToString() + " 分数：" + performance.pValue);
                        pData[actor] = performance.pValue;
                        actor.ResetPerformance();
                    }
                    if (pData.Values.Count > 0) 
                    {
                        double averagePerformance = pData.Values.Average();
                        LogService.LogInfo("均分:" + averagePerformance);
                        double variancerformance = pData.Values.Select(x => Math.Pow(x - averagePerformance, 2)).Average(); // 计算方差
                        double standardDeviationrformance = Math.Sqrt(variancerformance); // 计算标准方差
                        foreach(var item in pData)
                        {
                            Actor actor = item.Key;
                            double mark = item.Value;
                            if (mark>=averagePerformance+standardDeviationrformance)
                            {
                                actor.AddOfficeExamLevel(Enums.EmpireExamLevel.HD);
                            } else if (mark>=averagePerformance)
                            {
                                actor.AddOfficeExamLevel(Enums.EmpireExamLevel.CR);
                            } else if (mark>=averagePerformance-standardDeviationrformance)
                            {
                                actor.AddOfficeExamLevel(Enums.EmpireExamLevel.P);
                            } else
                            {
                                actor.AddOfficeExamLevel(Enums.EmpireExamLevel.F);
                            }
                            LogService.LogInfo($"{actor.data.name}的历年绩效考核{actor.GetEmpireExamLevelsString()}");
                        }
                    }
                }
                empire.data.last_office_exam_timestamp = World.world.getCurWorldTime();
            }
            return BehResult.Continue;
        }
        return BehResult.Continue;
    }
}

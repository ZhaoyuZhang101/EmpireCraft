using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.AI.KingdomAI;
public class EmpireCraftKingdomBehCheckExam: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        var regime = pKingdom.GetRegime();
        if (regime == null) return BehResult.Continue;
        if (regime.GetLeaderSelectMethod() == LeaderSelectMethod.Exam)
        {
            if (pKingdom.IsEmpire())
            {
                Empire empire = pKingdom.GetEmpire();
                if (empire != null && empire.IsNeedToExam())
                {
                    var cities = empire.AllCities();
                    if (cities != null)
                    {
                        foreach (City city in cities)
                        {
                            ExamSystem.startExam(ExamSystem.ExamType.City, city);
                        }
                    }

                    var provinces = empire.kingdoms_hashset;
                    if (provinces != null)
                    {
                        foreach (Kingdom province in provinces)
                        {
                            ExamSystem.startExam(ExamSystem.ExamType.Province, province);
                        }
                    }

                    ExamSystem.startExam(ExamSystem.ExamType.Empire, empire); 
                    empire.data.last_exam_timestamp = World.world.getCurWorldTime();
                }
            }
            else
            {
                if (!pKingdom.IsInEmpire())
                {
                    foreach (City city in pKingdom.cities)
                    {
                        ExamSystem.startExam(ExamSystem.ExamType.City, city);
                    }
                    ExamSystem.startExam(ExamSystem.ExamType.Province, pKingdom);
                    pKingdom.UpdateExamTime();
                }
            }
        }

        return BehResult.Continue;
    }
}

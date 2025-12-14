using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
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
        if (pKingdom.GetRegime().GetLeaderSelectMethod() == LeaderSelectMethod.Exam)
        {
            if (pKingdom.IsEmpire())
            {
                Empire empire = pKingdom.GetEmpire();
                if (empire.IsNeedToExam())
                {
                    foreach (City city in empire.AllCities())
                    {
                        ExamSystem.startExam(ExamSystem.ExamType.City, city);
                    }

                    foreach (Kingdom province in empire.kingdoms_hashset)
                    {
                        ExamSystem.startExam(ExamSystem.ExamType.Province, province);
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

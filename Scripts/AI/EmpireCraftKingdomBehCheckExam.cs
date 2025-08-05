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
            if (empire.IsNeedToExam())
            {
                foreach(City city in empire.AllCities())
                {
                    ExamSystem.startExam(ExamSystem.ExamType.City, city);
                }
                foreach(Province province in empire.ProvinceList)
                {
                    province.updateOccupied();
                    if(!province.IsTotalVassaled())
                    {
                        ExamSystem.startExam(ExamSystem.ExamType.Province, province);
                    }
                }
                ExamSystem.startExam(ExamSystem.ExamType.Empire, empire);
                empire.data.last_exam_timestamp = World.world.getCurWorldTime();
            }
        }
        return BehResult.Continue;
    }
}

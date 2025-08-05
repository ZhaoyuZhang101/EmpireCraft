using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI;

public class EmpireCraftKingdomBehCheckOfficer : BehaviourActionKingdom
{
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.isEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (!empire.isRekt()) 
            {
                if (empire.IsNeedToSetProvince())
                {
                    empire.DivideIntoProvince();
                }
                if (empire.ProvinceList.Any())
                {
                    foreach (Province province in empire.ProvinceList)
                    {
                        if (!province.IsTotalVassaled())
                        {
                            province.JudgeOfficer();
                        } else
                        {
                            province.checkCanbeTranfered();
                        }
                    }
                }
            }
            return BehResult.Continue;
        }
        return BehResult.Continue;
    }
}
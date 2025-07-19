using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class EmpireCraftKingdomBehCheckOfficer : BehaviourActionKingdom
{
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.isEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (!empire.isRekt()) 
            {
                if (empire.isNeedToSetProvince())
                {
                    empire.DivideIntoProvince();
                }
                if (empire.province_list.Count>0)
                {
                    foreach (Province province in empire.province_list)
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

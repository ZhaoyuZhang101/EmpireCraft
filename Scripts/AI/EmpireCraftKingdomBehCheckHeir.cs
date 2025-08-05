using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.HelperFunc;

namespace EmpireCraft.Scripts.AI;
public class EmpireCraftKingdomBehCheckHeir: BehaviourActionKingdom
{
    public override BehResult execute(Kingdom pKingdom)
    {
        if (!pKingdom.isEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        if (empire.Heir==null)
        {
            Actor heir = empire.CheckHeir();
            if (heir == null) return BehResult.Continue;
            empire.Heir = heir;
            LogService.LogInfo($"当前皇室继承人:{heir.data.name}");
        } else
        {
            if (empire.Emperor == null)
            {
                empire.Heir = null;
            } else if (!empire.Heir.isUnitFitToRule())
            {
                empire.Heir = null;
            }
            if (empire.IsNeedToEducateHeir())
            {
                Random rand = new Random();
                double i = rand.NextDouble();
                if (i>0.3)
                {
                    if (empire.Heir != null) empire.Heir.data.renown += 5;
                }

            }
        }
        return BehResult.Continue;
    }
}

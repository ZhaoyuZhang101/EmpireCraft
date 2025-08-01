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

public class EmpireCraftKingdomBehCheckPlots : BehaviourActionKingdom
{
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.isEmpire())
        {
            checkJoinWar(pKingdom);
            return BehResult.Continue;
        }
        return BehResult.Continue;
    }

    public void checkJoinWar(Kingdom pKingdom)
    {
        Empire empire = pKingdom.GetEmpire();
        if (!empire.canJoinWar()) return;
        if (!empire.isRekt())
        {
            if (pKingdom.getWars().Count() > 0)
            {
                foreach (War war in pKingdom.getWars())
                {
                    if (!war.isRekt())
                    {
                        List<Kingdom> opposites = war.getOppositeSideKingdom(pKingdom);
                        foreach (Kingdom empireKingdoms in empire.kingdoms_hashset)
                        {
                            if (empireKingdoms == null) return;
                            if (empireKingdoms.isRekt()) return;
                            if (!opposites.Contains(empireKingdoms) && pKingdom.getRenown() >= empireKingdoms.countTotalWarriors() && empireKingdoms.getWars().Count() <= 0)
                            {
                                if (war.isAttacker(pKingdom))
                                {
                                    war.joinAttackers(empireKingdoms);
                                }
                                else
                                {
                                    war.joinDefenders(empireKingdoms);
                                }
                                empire.addRenown(empireKingdoms.countTotalWarriors());
                                TranslateHelper.LogJoinEmpireWar(empireKingdoms, empire);
                                empire.data.timestamp_invite_war_cool_down = World.world.getCurWorldTime();
                                return;
                            }
                        }
                    }
                }
            }
        }
    }
}

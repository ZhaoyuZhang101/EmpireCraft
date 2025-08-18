using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckPlots : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.isEmpire())
        {
            CheckJoinWar(pKingdom);
            return BehResult.Continue;
        }
        return BehResult.Continue;
    }

    public void CheckJoinWar(Kingdom pKingdom)
    {
        Empire empire = pKingdom.GetEmpire();
        if (!empire.CanJoinWar()) return;
        if (!empire.isRekt())
        {
            if (pKingdom.getWars().Any())
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
                                empire.AddRenown(empireKingdoms.countTotalWarriors());
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
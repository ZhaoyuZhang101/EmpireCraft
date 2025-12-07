using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckPlots : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.IsInEmpire())
        {
            CheckJoinWar(pKingdom);
        }
        CheckMainTitle(pKingdom);
        return BehResult.Continue;
    }

    public void CheckMainTitle(Kingdom pKingdom)
    {
        if (pKingdom.HasMainTitle())
        {
            return;
        }
        if (pKingdom.hasKing())
        {
            var king = pKingdom.king;
            if (king.GetMainTitle() != null)
            {
                pKingdom.SetMainTitle(king.GetMainTitle());
            }
        }
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
                        if (opposites==null) return;
                        foreach (Kingdom empireKingdoms in empire.kingdoms_hashset)
                        {
                            if (empireKingdoms.isRekt()) continue;
                            if (empireKingdoms.IsEmpire()) continue;
                            if (!opposites.Contains(empireKingdoms) && (empire.CoreKingdom?.getRenown() >= empireKingdoms.countTotalWarriors()||!empireKingdoms.GetRegime().IsAllowDiplomacy()) && empireKingdoms.getWars()?.Count() <= 0)
                            {
                                if (war.isAttacker(pKingdom))
                                {
                                    war.joinAttackers(empireKingdoms);
                                }
                                else
                                {
                                    war.joinDefenders(empireKingdoms);
                                }

                                if (empireKingdoms.GetRegime().IsAllowDiplomacy())
                                {
                                    empire.AddRenown(-empireKingdoms.countTotalWarriors());
                                }
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
using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
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
        //检测加入圣战
        CheckJoinReligionWar(pKingdom);
        CheckMainTitle(pKingdom);
        return BehResult.Continue;
    }

    public void CheckJoinReligionWar(Kingdom pKingdom)
    {
        if (!pKingdom.hasReligion()) return;
        foreach (var war in DiplomacyHelpers.wars)
        {
            if (war.main_attacker?.capital == war.main_attacker?.religion.GetCity() &&
                war.GetEmpireWarType() == EmpireWarType.神圣)
            {
                if (pKingdom.religion == war.main_attacker?.religion)
                {
                    if (pKingdom.isOpinionTowardsKingdomGood(war.main_attacker))
                    {
                        war.joinAttackers(pKingdom);
                        TranslateHelper.LogJoinReligionWar(pKingdom, pKingdom.religion);
                        return;
                    }
                }
            }
        }
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
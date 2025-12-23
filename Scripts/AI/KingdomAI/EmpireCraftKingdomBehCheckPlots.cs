using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NCMS.Extensions;
using NeoModLoader.api.attributes;
using NeoModLoader.General.Game.extensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckPlots : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
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
        if (pKingdom.hasEnemies()) return;
        foreach (var war in DiplomacyHelpers.wars)
        {
            if (!war.main_attacker?.hasReligion()??true) continue;
            if (war.hasKingdom(pKingdom)) continue;
            if (war.GetEmpireWarType() == EmpireWarType.神圣)
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
    [Hotfixable]
    public void CheckJoinWar(Kingdom pKingdom)
    {
        Empire empire = pKingdom.GetEmpire();
        var regime = pKingdom.GetRegime();
        if (regime == null) return;
        if (!empire.CanJoinWar()) return;
        if (!empire.isRekt())
        {
            if (!pKingdom.IsEmpire())
            {
                var coreKingdom = empire.CoreKingdom;
                if (pKingdom.isInWarWith(coreKingdom)) return;
                if (pKingdom.isInWarOnSameSide(coreKingdom)) return;
                if (pKingdom.getWars().Any()) return;
                if (!coreKingdom.hasEnemies()) return;
                if (pKingdom.isOpinionTowardsKingdomGood(coreKingdom) || regime.IsAllowDiplomacy())
                {
                    coreKingdom.getWars(true).ToList().FindAll(w=>w.isAttacker(coreKingdom)).ForEach(w=>w.joinAttackers(pKingdom));
                    coreKingdom.getWars(true).ToList().FindAll(w=>w.isDefender(coreKingdom)).ForEach(w=>w.joinDefenders(pKingdom));
                    TranslateHelper.LogJoinEmpireWar(pKingdom, empire);
                } 
            }
            else
            {
                foreach (var empireKingdom in empire.kingdoms_list.ToList())
                {
                    LogService.LogInfo(empireKingdom.name);
                    if (empireKingdom.IsEmpire()) continue;
                    LogService.LogInfo("1");
                    if (pKingdom.isInWarWith(empireKingdom)) continue;
                    LogService.LogInfo("2");
                    if (pKingdom.isInWarOnSameSide(empireKingdom)) continue;
                    LogService.LogInfo("3");
                    if (!empireKingdom.hasEnemies())  continue;
                    LogService.LogInfo("4");
                    var kRegime = empireKingdom.GetRegime();
                    if (kRegime == null) continue;
                    LogService.LogInfo("5");
                    if (!pKingdom.isOpinionTowardsKingdomGood(empireKingdom)&&regime.IsAllowDiplomacy()) continue;
                    LogService.LogInfo("6");
                    empireKingdom.getWars(true).ToList().FindAll(w=>w.isAttacker(empireKingdom)).ForEach(w=>w.joinAttackers(pKingdom));
                    empireKingdom.getWars(true).ToList().FindAll(w=>w.isDefender(empireKingdom)).ForEach(w=>w.joinDefenders(pKingdom));
                    TranslateHelper.LogEmpireJoinWar(empire, empireKingdom);
                }
            }
        }
    }
}
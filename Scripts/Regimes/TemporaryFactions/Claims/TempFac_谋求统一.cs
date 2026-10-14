using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_谋求统一 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_谋求统一();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            Empire empire = GetEmpire();
            var war = World.world.diplomacy.startWar(empire.CoreKingdom, kingdom, WarTypeLibrary.normal);
            if (war != null)
            {
                if (ZhouEraSystem.Applies(empire)) ZhouEraSystem.MarkSubdueWar(war, empire, kingdom);
                else war.SetEmpireWarType(EmpireWarType.统一);
            }
        }
        CountDown = 5;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || WarSituation.TooBusyForWar(empire.CoreKingdom)) return false;
        if (CorpsSystem.CannotFundWar(empire.CoreKingdom)) return false;   // 发不出军饷不开新仗
        // 周制没有统一战争：天下只有一个共主，同文化而不在周室之内的国家就是不服王化，
        // 天子兴师讨伐，攻下其都城即迫其称臣、受封为诸侯；周室之内的诸侯(含战国离开帝国的)不在此列
        if (ZhouEraSystem.Applies(empire))
        {
            Kingdom target = ZhouEraSystem.SubdueTarget(empire);
            if (target == null) return false;
            SetKingdomTarget(target);
            return true;
        }
        EmpireCore selfCore = EmpireCoreManager.Get(empire);
        foreach (var kingdom in World.world.kingdoms)
        {
            if (empire.given_Kingdoms.Contains(kingdom)) continue;
            if (empire.taken_Kingdoms.Contains(kingdom)) continue;
            if (kingdom.IsLocalRebelling()) continue;
            if (kingdom.IsFactionRebelling()) continue;
            if (kingdom.IsEmpire() && !kingdom.IsInSameEmpire(empire.CoreKingdom))
            {
                EmpireCore targetCore = EmpireCoreManager.GetRiseCandidateCore(kingdom);
                if (selfCore != null && targetCore != null && selfCore.id == targetCore.id)
                {
                    Empire targetEmpire = kingdom.GetEmpire();
                    // 同争一个帝国核心的对手帝国也要与本帝国接壤，不隔着别国远征
                    if (targetEmpire != null && WarSituation.Power(empire) >= WarSituation.Power(targetEmpire) &&
                        targetEmpire.kingdoms_list.Any(member => member != null && !member.isRekt() && empire.IsNeighbourWith(member)))
                    {
                        SetKingdomTarget(targetEmpire.CoreKingdom);
                        return true;
                    }
                }
            }
            if (empire.GetKingdomNeighbours().Any(k => k.IsInEmpire() && k == kingdom && k.getSpecies()==empire.CoreKingdom.getSpecies()))
            {
                var targetEmpire = kingdom.GetEmpire();
                if (WarSituation.Power(empire) > WarSituation.Power(targetEmpire))
                {
                    SetKingdomTarget(targetEmpire.CoreKingdom);
                    return true;
                }
            };
            if (kingdom.IsInEmpire()) continue;
            // 接壤或隔海相望(见 StraitReach)
            if (empire.IsNeighbourWith(kingdom) || StraitReach.Across(empire.CoreKingdom, kingdom))
            {
                if (kingdom.species_id == empire.CoreKingdom.species_id)
                {
                    if (!kingdom.isInWarWith(empire.CoreKingdom))
                    {
                        if (WarSituation.Power(empire) > WarSituation.Power(kingdom))
                        {
                            SetKingdomTarget(kingdom);
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }
}

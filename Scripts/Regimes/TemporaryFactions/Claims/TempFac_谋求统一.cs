using System.Collections.Generic;
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
            // 挑目标到推动完成之间雄主可能已经不在了：开战前再看一遍平时游牧只打小国(见 SteppeOrderSystem.PeacetimeWarAllowed)
            var war = SteppeOrderSystem.PeacetimeWarAllowed(empire.CoreKingdom, kingdom)
                ? World.world.diplomacy.startWar(empire.CoreKingdom, kingdom, WarTypeLibrary.normal)
                : null;
            if (war != null)
            {
                if (ZhouEraSystem.Applies(empire)) ZhouEraSystem.MarkSubdueWar(war, empire, kingdom);
                // 天下大势里的统一者打天下里的别家才叫统一战争(见 UnificationSystem)；其余是寻常扩张
                else war.SetEmpireWarType(UnificationSystem.Contends(empire.CoreKingdom, kingdom) ? EmpireWarType.统一 : EmpireWarType.帝国扩张);
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
        // 没有雄主在位的游牧国平时只打两城以内、不在任何帝国里的小国(用户 2026-10-21 选"堵平时扩张的漏口"：宣战挑目标
        // 早有这道限制，统一诉求这条路以前没查)——对手帝国、别家帝国的成员都打不得；雄主在位、非游牧照旧不限
        bool Allowed(Kingdom target) => SteppeOrderSystem.PeacetimeWarAllowed(empire.CoreKingdom, target);
        // 周制没有统一战争：天下只有一个共主，同文化而不在周室之内的国家就是不服王化，
        // 天子兴师讨伐，攻下其都城即迫其称臣、受封为诸侯；周室之内的诸侯(含战国离开帝国的)不在此列
        if (ZhouEraSystem.Applies(empire))
        {
            Kingdom target = ZhouEraSystem.SubdueTarget(empire);
            if (target == null || !Allowed(target)) return false;
            SetKingdomTarget(target);
            return true;
        }
        EmpireCore selfCore = EmpireCoreManager.Get(empire);
        // 天下有主(别家受着天命)：不能问鼎(同统一战争，见 TianxiaSystem.BarsContender)
        EmpireCore realmLead = TianxiaSystem.LeadOf(empire.CoreKingdom.capital);
        if (realmLead != null && TianxiaSystem.BarsContender(realmLead, empire.CoreKingdom)) return false;
        HashSet<Kingdom> occupiers = Occupiers(realmLead, empire.CoreKingdom);
        foreach (var kingdom in World.world.kingdoms)
        {
            // 统一只统一同一个天下的(用户 2026-10-15："有的甚至还跨文化跨地域打"；2026-10-19 选"统一按帝国核心")；
            // 光复旧土：占着本朝天下里的城的邻国也打(见 Occupiers)，它不是天下一家，照常看停战、同盟、岁币朝贡
            if (!UnificationSystem.SameRealm(empire.CoreKingdom, kingdom) &&
                (!occupiers.Contains(kingdom) || !WarGate.CanDeclare(empire.CoreKingdom, kingdom))) continue;
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
                    if (targetEmpire != null && Allowed(targetEmpire.CoreKingdom) && WarSituation.Power(empire) >= WarSituation.Power(targetEmpire) &&
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
                if (Allowed(targetEmpire.CoreKingdom) && WarSituation.Power(empire) > WarSituation.Power(targetEmpire))
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
                    if (!kingdom.isInWarWith(empire.CoreKingdom) && Allowed(kingdom))
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

    // 占着本朝天下里的城的外国(本帝国成员、野生国不算)。只打同一天下(SameRealm)以后，都城在天下外、
    // 占的又不到四分之一的邻国算不上天下一家，对外扩张又只打异种——这样的邻国占去的城再也收不回。
    // 开战目标按法理定(对方手里有本朝法理的城即为收复，见 WarGoalSystem)
    private static HashSet<Kingdom> Occupiers(EmpireCore realmLead, Kingdom core)
    {
        var result = new HashSet<Kingdom>();
        if (realmLead == null || core == null) return result;
        foreach (City city in TianxiaSystem.Cities(realmLead))
        {
            Kingdom holder = city?.kingdom;
            if (holder?.data == null || holder.isRekt() || holder.wild || holder == core || holder.IsInSameEmpire(core)) continue;
            result.Add(holder);
        }
        return result;
    }
}

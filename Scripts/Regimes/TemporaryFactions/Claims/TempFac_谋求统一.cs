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

    // 目标：照 Targets 的先后，第一家就是朝堂自己会打的
    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || WarSituation.TooBusyForWar(empire.CoreKingdom)) return false;
        return !CorpsSystem.CannotFundWar(empire.CoreKingdom);   // 发不出军饷不开新仗
    }

    public override List<ClaimTargetOption> BuiltinCandidates()
    {
        var result = new List<ClaimTargetOption>();
        var seen = new HashSet<Kingdom>();
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null) return result;
        foreach (Kingdom target in Targets(empire))
            if (target?.data != null && !target.isRekt() && seen.Add(target)) result.Add(ClaimTargetOption.Of(target));
        return result;
    }

    // 打得的各家，照朝堂自己挑的先后(同一家可能出现两回)
    private static IEnumerable<Kingdom> Targets(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        // 没有雄主在位的游牧国平时只打两城以内、不在任何帝国里的小国(用户 2026-10-21 选"堵平时扩张的漏口"：宣战挑目标
        // 早有这道限制，统一诉求这条路以前没查)——对手帝国、别家帝国的成员都打不得；雄主在位、非游牧照旧不限
        bool Allowed(Kingdom target) => SteppeOrderSystem.PeacetimeWarAllowed(core, target);
        // 周制没有统一战争：天下只有一个共主，同文化而不在周室之内的国家就是不服王化，
        // 天子兴师讨伐，攻下其都城即迫其称臣、受封为诸侯；周室之内的诸侯(含战国离开帝国的)不在此列
        if (ZhouEraSystem.Applies(empire))
        {
            Kingdom subdue = ZhouEraSystem.SubdueTarget(empire);
            if (subdue != null && Allowed(subdue)) yield return subdue;
            yield break;
        }
        EmpireCore selfCore = EmpireCoreManager.Get(empire);
        // 天下有主(别家受着天命)：不能问鼎(同统一战争，见 TianxiaSystem.BarsContender)
        EmpireCore realmLead = TianxiaSystem.LeadOf(core.capital);
        if (realmLead != null && TianxiaSystem.BarsContender(realmLead, core)) yield break;
        HashSet<Kingdom> occupiers = Occupiers(realmLead, core);
        double ours = WarSituation.Power(empire);
        // 接壤的、同种族的别家帝国成员(按国一次算好；以前每看一国就重算一遍邻国)
        var memberNeighbours = new HashSet<Kingdom>(empire.GetKingdomNeighbours()
            .Where(k => k != null && k.IsInEmpire() && k.getSpecies() == core.getSpecies()));
        foreach (var kingdom in World.world.kingdoms)
        {
            // 统一只统一同一个天下的(用户 2026-10-15："有的甚至还跨文化跨地域打"；2026-10-19 选"统一按帝国核心")；
            // 光复旧土：占着本朝天下里的城的邻国也打(见 Occupiers)，它不是天下一家，照常看停战、同盟、岁币朝贡
            if (!UnificationSystem.SameRealm(core, kingdom) &&
                (!occupiers.Contains(kingdom) || !WarGate.CanDeclare(core, kingdom))) continue;
            if (empire.given_Kingdoms.Contains(kingdom)) continue;
            if (empire.taken_Kingdoms.Contains(kingdom)) continue;
            if (kingdom.IsLocalRebelling()) continue;
            if (kingdom.IsFactionRebelling()) continue;
            if (kingdom.IsEmpire() && !kingdom.IsInSameEmpire(core))
            {
                EmpireCore targetCore = EmpireCoreManager.GetRiseCandidateCore(kingdom);
                if (selfCore != null && targetCore != null && selfCore.id == targetCore.id)
                {
                    Empire rival = kingdom.GetEmpire();
                    // 同争一个帝国核心的对手帝国也要与本帝国接壤(法理相连且走得通，见下)，不隔着别国远征
                    if (rival != null && Allowed(rival.CoreKingdom) && ours >= WarSituation.Power(rival) &&
                        rival.kingdoms_list.Any(member => member != null && !member.isRekt() && WarReach.WarNear(core, member)))
                    {
                        yield return rival.CoreKingdom;
                        continue;
                    }
                }
            }
            if (memberNeighbours.Contains(kingdom))
            {
                Empire theirs = kingdom.GetEmpire();
                if (Allowed(theirs.CoreKingdom) && ours > WarSituation.Power(theirs))
                {
                    yield return theirs.CoreKingdom;
                    continue;
                }
            }
            if (kingdom.IsInEmpire()) continue;
            // 法理相连且走得通、或隔海相望(见 WarReach.WarNear，同开战谋划挑目标)。2026-11-07 群友"帝国不会主动出击统一天下"：
            // 以前只认原版邻城(地块相接，Empire.IsNeighbourWith)，隔着无人区的天下各家朝廷提不出统一；真接壤却走不通的开战闸本来也拦
            if (WarReach.WarNear(core, kingdom) && kingdom.species_id == core.species_id &&
                !kingdom.isInWarWith(core) && Allowed(kingdom) && ours > WarSituation.Power(kingdom))
                yield return kingdom;
        }
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

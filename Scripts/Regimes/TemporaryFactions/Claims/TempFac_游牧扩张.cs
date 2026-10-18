using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Regimes;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_游牧扩张 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_游牧扩张();
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
        Empire empire = GetEmpire();
        var target = GetKingdomTarget();
        if (target != null)
        {
            var war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, target, WarTypeLibrary.normal);
            war.SetEmpireWarType(EmpireWarType.游牧扩张);
        }
        // 同对外扩张：五年一次(用户 2026-10-17 选"游牧扩张加冷却比兵力")
        CountDown = 5;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || empire.CoreKingdom.isRekt()) return false;
        // 游牧文化才有：按文化特性不按政体(阿兹特克、林地部族同走游牧政体却不游牧；元、清式复合帝国看统治族群)
        if (!ClaimRules.HasTrait(empire, EmpireCraft.Scripts.Data.OnomasticsRule.NomadTrait)) return false;
        if (empire.CoreKingdom.hasEnemies()) return false;
        // 同对外扩张(用户 2026-10-17 "游牧太强了")：打仗太多、发不出军饷不开新仗；只打不在任何帝国里的邻国，
        // 自己给出去、收进来的不打，对方有核自己没有不敢碰，兵力(战力)要占优
        if (WarSituation.TooBusyForWar(empire.CoreKingdom) || CorpsSystem.CannotFundWar(empire.CoreKingdom)) return false;
        double power = WarSituation.Power(empire);
        foreach (Kingdom kingdom in empire.GetKingdomNeighbours())
        {
            if (kingdom == null || kingdom.isRekt() || kingdom.IsInEmpire() || empire.given_Kingdoms.Contains(kingdom) ||
                empire.taken_Kingdoms.Contains(kingdom) || empire.CoreKingdom.isOpinionTowardsKingdomGood(kingdom)) continue;
            if (EmpireCraft.Scripts.Compatibility.NuclearDoctrineSystem.IsDeterred(empire.CoreKingdom, kingdom)) continue;
            if (power <= WarSituation.Power(kingdom) || !WarGate.CanDeclare(empire.CoreKingdom, kingdom)) continue;
            SetKingdomTarget(kingdom);
            return true;
        }
        return false;
    }
}

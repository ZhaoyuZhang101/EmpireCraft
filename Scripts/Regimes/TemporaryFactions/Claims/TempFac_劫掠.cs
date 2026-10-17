using EmpireCraft.Scripts.AI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_劫掠 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_劫掠();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }
    
    public override void Execute()
    {
        Empire empire = GetEmpire();
        Kingdom target = GetKingdomTarget();
        if (target != null)
        {
            var war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, target, WarTypeLibrary.normal);
            war.SetEmpireWarType(EmpireWarType.劫掠);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        if (empire.CoreKingdom.hasEnemies()) return false;
        var neighbours = empire.GetKingdomNeighbours();
        if (!neighbours.Any()) return false;
        RegimeType? own = empire.CoreKingdom.GetRegime()?.type;
        // 五年内遭劫过的不再劫(用户 2026-10-17 选"劫掠只拿三成国库"：同一家几年内不再劫)
        var target = neighbours.Find(k => k?.data != null && !k.isRekt() && k.GetRegime()?.type != own &&
                                          (k.GetOrCreate().raided_at < 0d || Date.getYearsSince(k.GetOrCreate().raided_at) >= 5));
        if (target!= null)
        {
            SetKingdomTarget(target);
            return true;
        }
        return false;
    }
}

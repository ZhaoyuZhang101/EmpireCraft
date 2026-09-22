using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_制度融入 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_制度融入();
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
        var target = GetKingdomTarget();
        Regime targetRegime = target != null && !target.isRekt() ? target.GetRegime() : null;
        if (empire?.kingdoms_list != null && targetRegime != null)
        {
            foreach (var kingdom in empire.kingdoms_list)
            {
                if (kingdom == null || kingdom.isRekt() || kingdom == target) continue;
                kingdom.SetRegimeType(targetRegime.type);
                kingdom.LoadRegime();
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        Regime coreRegime = empire?.CoreKingdom?.GetRegime();
        if (coreRegime == null || empire.kingdoms_list == null) return false;

        var regimeCount = empire.kingdoms_list
            .Where(k => k != null && !k.isRekt())
            .Select(k => new { kingdom = k, regime = k.GetRegime() })
            .Where(entry => entry.regime != null)
            .GroupBy(entry => entry.regime.type).Select(g =>
            new
            {
                regimeType = g.Key,
                kingdom = g.First().kingdom,
                count = g.Count()
            })
            .OrderByDescending(x => x.count)
            .FirstOrDefault();
        if (regimeCount != null)
        {
            if (regimeCount.regimeType != coreRegime.type)
            {
                SetKingdomTarget(regimeCount.kingdom);
                return true;
            }
        }
        
        return false;
    }
}

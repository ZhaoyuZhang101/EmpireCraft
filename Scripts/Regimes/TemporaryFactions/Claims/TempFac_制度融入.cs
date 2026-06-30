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
        return res;
    }
    
    public override void Execute()
    {
        Empire empire = GetEmpire();
        var target = GetKingdomTarget();
        if (target != null)
        {
            foreach (var kingdom in empire.kingdoms_list)
            {
                if (kingdom==target) continue;
                kingdom.SetRegimeType(target.GetRegime().type);
                kingdom.LoadRegime();
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        var regimeCount = empire.kingdoms_list.GroupBy(k => k.GetRegime().type).Select(g =>
            new
            {
                regimeType = g.Key,
                kingdom = g.First(),
                count = g.Count()
            })
            .OrderByDescending(x => x.count)
            .FirstOrDefault();
        if (regimeCount != null)
        {
            if (regimeCount.regimeType != empire.CoreKingdom.GetRegime().type)
            {
                SetKingdomTarget(regimeCount.kingdom);
                return true;
            }
        }
        
        return false;
    }
}

using System.Linq;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_宗教融入 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_宗教融入();
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
        Religion religion = GetReligionTarget();
        if (religion != null)
        {
            empire.CoreKingdom.setReligion(religion);
            empire.CoreKingdom.units.ForEach(a=>a.setReligion(religion));
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        var allEmpireActors = empire.getUnits();
        var empireActors = allEmpireActors as Actor[] ?? allEmpireActors.ToArray();
        var mostPopularReligion = empireActors
            .Where(u => u.hasReligion())
            .GroupBy(u => u.religion)
            .Select(g => new
            {
                Religion = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        if (mostPopularReligion == null) return false;
        if (mostPopularReligion.Count / (float)empireActors.Length > 0.6f &&
            mostPopularReligion.Religion != empire.CoreKingdom.religion)
        {
            SetReligionTarget(mostPopularReligion.Religion);
            return true;
        }

        return false;
    }
}

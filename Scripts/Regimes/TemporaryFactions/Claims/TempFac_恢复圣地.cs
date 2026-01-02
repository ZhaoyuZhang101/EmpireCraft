using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_恢复圣地 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_恢复圣地();
        res.Init(faction);
        return res;
    }
    
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        var city = GetCityTarget();
        if (city != null)
        {
            Empire empire = GetEmpire();
            var kingdom = city.makeOwnKingdom(city.leader??city.units.FirstOrDefault());
            empire.join(kingdom, pForce:true);
            kingdom.SetMainTitle(city.GetTitle());
            kingdom.king.AddOwnedTitle(city.GetTitle());
            kingdom.GetRegime().SetReligionLevel(ReligionLevel.High);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        var religion = empire.Religion;
        if (!religion.isRekt())
        {
            var religionCity = religion.GetCity();
            if (!religionCity.isCapitalCity())
            {
                if (religionCity.kingdom.IsInEmpire())
                {
                    SetCityTarget(religionCity);
                    return true;
                }
            }
        }
        return false;
    }
}

using System.Linq;
using EmpireCraft.Scripts.Data;
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
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
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
            kingdom.ReconcileMainTitle(new[] { city.GetTitle() });
            empire.SynchronizeLandedLegalTitles(kingdom);
            kingdom.GetRegime().SetReligionLevel(ReligionLevel.High);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        // 西方封建：教宗国只有施行神权国家后才能建立；须先确立国教(下面的 empire.Religion)
        if (empire.CoreKingdom?.GetRegime()?.type == RegimeType.Feudalism &&
            !GeneralSystems.InstitutionSystem.HasFeature(empire, InstitutionFeatures.TheocraticState))
            return false;
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

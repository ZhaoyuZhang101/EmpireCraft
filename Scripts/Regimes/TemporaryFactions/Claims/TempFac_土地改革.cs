using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 土地改革：帝国境内已开放私人地权的城市，城中土地全部收归重分，按宗族(户数)平均授田。
// 只有无地人口已经比较多时才会提出。
public class TempFac_土地改革 : TemporaryFaction
{
    private const float LandlessThreshold = 0.10f;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_土地改革();
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
        if (empire != null)
        {
            foreach (City city in empire.kingdoms_hashset.Where(kingdom => kingdom != null && !kingdom.isRekt())
                         .SelectMany(kingdom => kingdom.cities).Where(city => city != null && !city.isRekt()).ToList())
            {
                if (LandEconomySystem.IsLandMarketOpen(city.kingdom)) LandEconomySystem.RedistributeLand(city);
            }
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || !LandEconomySystem.IsLandMarketOpen(empire.CoreKingdom)) return false;
        var ratios = empire.CoreKingdom.cities.Where(city => city != null && !city.isRekt())
            .Select(city => LandEconomySystem.GetReport(city).LandlessPopulationRatio).ToList();
        return ratios.Count > 0 && ratios.Average() >= LandlessThreshold;
    }
}

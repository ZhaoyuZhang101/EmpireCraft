using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 自由信仰：中央的宗教选项降到"有国教-自由信仰"(以前是空壳，提不出也不做事)；
// 宗教选项在自由信仰及以下的国家宽待异教(见 ReligiousTension.PolicyOf)
public class TempFac_自由信仰 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_自由信仰();
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
        Regime regime = CoreRegime(GetEmpire());
        if (regime != null && regime.GetReligionLevel() > ReligionLevel.Low) regime.SetReligionLevel(ReligionLevel.Low);
        End();
    }

    public override bool CheckCondition()
    {
        Regime regime = CoreRegime(GetEmpire());
        return regime != null && regime.GetReligionLevel() > ReligionLevel.Low;
    }

    private static Regime CoreRegime(Empire empire)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        return regime?.options != null && regime.options.ContainsKey("option_religion_type") ? regime : null;
    }
}

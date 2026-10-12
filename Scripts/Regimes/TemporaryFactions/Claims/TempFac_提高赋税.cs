using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 提高赋税(公共议题)：增税是国策，不需要拿哪个成员的罪名做由头。
// 以前要在成员里找有罪名的当目标，而且只看第一个成员，几乎提不成；减税却随时能提，税率只降不升，最后归零
public class TempFac_提高赋税 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_提高赋税();
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
        if (empire != null && ConstitutionalEconomySystem.CanChangeTax(empire))
        {
            empire.AddTaxRate();
        }

        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        return empire.data != null && empire.data.TaxRate < 1f && ConstitutionalEconomySystem.CanChangeTax(empire);
    }
}

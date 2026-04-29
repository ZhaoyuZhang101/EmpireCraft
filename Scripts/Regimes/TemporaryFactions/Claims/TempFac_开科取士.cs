using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_开科取士 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_开科取士();
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
            Regime regime = empire.CoreKingdom.GetRegime();
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            empire.AddMandate(20);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire != null)
        {
            Regime regime = empire.CoreKingdom.GetRegime();
            if (regime.leader_select_method == LeaderSelectMethod.Succession&&regime.GetLeaderSelectMethod() != LeaderSelectMethod.Exam)
            {
                return true;
            }
        }
        return false;
    }
}

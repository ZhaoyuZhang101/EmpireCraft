using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_供养宗室 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_供养宗室();
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
            empire.data.feed_royal = true;
        }
        FinishedAction();
        End();
    }
    
    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire != null)
        {
            if (empire.regime != null)
            {
                if (empire.regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession)
                {
                    if (!empire.data.feed_royal)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
}

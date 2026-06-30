using System.Security.Policy;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_供养宗室 : TemporaryFaction
{
    public override bool canBePushByLocal => true;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_供养宗室();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        return res;
    }
    
    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (empire != null)
        {
            empire.data.feed_royal = true;
        }
        FinishedAction();
        End();
    }

    public override bool CheckLocalCondition(Kingdom actor)
    {
        if (!base.CheckLocalCondition(actor)) return false;
        if (lRegime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession &&
            actor.king.GetSpecificClan() == lEmpire.EmpireSpecificClan)
        {
            if (actor.king.renown > 500)
            {
                return true;
            }
        }
        return false;
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

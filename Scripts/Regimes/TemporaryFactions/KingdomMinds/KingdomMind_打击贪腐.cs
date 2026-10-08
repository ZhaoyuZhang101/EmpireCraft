using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.KingdomMinds;

public class KingdomMind_打击贪腐 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new KingdomMind_打击贪腐();
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
        var kingdom = GetKingdom() ?? empire?.CoreKingdom;
        if (CorruptionSystem.TryCampaign(empire, kingdom)) FinishedAction();
        End();
    }
    
    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        var kingdom = GetKingdom() ?? empire?.CoreKingdom;
        return CorruptionSystem.CanCampaign(empire, kingdom);
    }
}

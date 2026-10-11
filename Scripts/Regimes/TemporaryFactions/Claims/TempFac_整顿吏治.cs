using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 整顿吏治(公共议题)：中央与地方腐败过重(≥25%)、五年内没有整顿过时，请旨清查吏治(见 CorruptionSystem.TryCampaign)
public class TempFac_整顿吏治 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_整顿吏治();
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
        CorruptionSystem.TryCampaign(empire, empire?.CoreKingdom);
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return CorruptionSystem.CanCampaign(empire, empire?.CoreKingdom);
    }
}

using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 禁党：执政的共产、法西斯政党推行一党专政，取缔其余政党(社会主义党领导时中间派与社会主义各党作友党保留)。
// 以前诉求表里配了禁党却没有这个类，配了也从不出现(用户 2026-11-10 选"一并修")。
// 条件：开放党禁、尚未一党制，本派是执政党，议席(无议会时按势力)过半，宪法没有被手定政党制度
public class TempFac_禁党 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_禁党();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override bool CheckContinue() => base.CheckContinue() && CheckCondition();

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        FixedFaction faction = GetFaction();
        return empire != null && faction != null && PartyBanSystem.CanBanByClaim(empire, faction);
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Empire empire = GetEmpire();
        FixedFaction faction = GetFaction();
        if (empire != null && faction != null && PartyBanSystem.CanBanByClaim(empire, faction))
        {
            PartyBanSystem.CloseByClaim(empire, faction);
            ParliamentSystem.Update(empire);
        }
        End();
    }
}

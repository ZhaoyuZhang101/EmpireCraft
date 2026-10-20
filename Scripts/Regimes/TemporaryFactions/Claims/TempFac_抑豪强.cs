using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 抑豪强(法家、帝党诉求，见 GentrySystem)：抄没门第最高的几家世家一半田产(空出的田归民田)，
// 家财入国库，这几家门第 -10；贵族、地主怨气上升，农民怨气下降。二十年至多一次。
public class TempFac_抑豪强 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_抑豪强();
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
        (int clans, int money) = GentrySystem.CurbMagnates(empire);
        if (clans > 0) CompletionOutcome = string.Format(GeneralSystems.CultureDoctrines.Text("claim_outcome_抑豪强_curbed", GetEmpire()), clans, MoneyDisplay.Format(money));
        End();
    }

    public override bool CheckCondition() => GentrySystem.CanCurbMagnates(GetEmpire());
}

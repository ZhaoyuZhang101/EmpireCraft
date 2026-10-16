using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 修氏族志(帝党、文官诉求，见 GentrySystem)：以当朝官爵重定门第——族中有人在朝的世家门第上升，
// 没人出仕的世家降等；法统 +3。三十年至多修一次。
public class TempFac_修氏族志 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_修氏族志();
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
        (int raised, int lowered) = GentrySystem.ReviseRegister(empire);
        CompletionOutcome = string.Format(LM.Get("claim_outcome_修氏族志_revised"), raised, lowered);
        End();
    }

    public override bool CheckCondition() => GentrySystem.CanReviseRegister(GetEmpire());
}

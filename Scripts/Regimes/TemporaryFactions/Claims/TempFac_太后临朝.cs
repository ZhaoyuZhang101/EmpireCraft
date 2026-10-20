using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 太后临朝(后党诉求，见 CliqueSystem)：幼主或庸弱、昏聩之君在位，太后在世时，后党请太后临朝称制——
// 太后声望大涨(幼主在位时由她辅政，见 Empire.UpdatePowerfulMinister)，外戚首领随之得势。
public class TempFac_太后临朝 : TemporaryFaction
{
    private const int DowagerRenown = 200, ChiefRenown = 80;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_太后临朝();
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
        Actor dowager = empire?.GetEmpressDowager();
        if (dowager?.data != null && dowager.isAlive())
        {
            dowager.editRenown(DowagerRenown);
            ConsortKinSystem.Chief(empire)?.editRenown(ChiefRenown);
            CompletionOutcome = string.Format(GeneralSystems.CultureDoctrines.Text("claim_outcome_太后临朝_regent", GetEmpire()), dowager.getName());
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        Actor emperor = empire?.Emperor;
        Actor dowager = empire?.GetEmpressDowager();
        if (emperor?.data == null || !emperor.isAlive() || dowager?.data == null || !dowager.isAlive()) return false;
        if (empire.data.powerful_minister_is_empress_dowager && empire.data.powerful_minister_id == dowager.id) return false;
        return !emperor.isAdult() || RulerQualitySystem.Of(emperor) is RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous;
    }
}

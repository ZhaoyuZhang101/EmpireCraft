using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 输出革命：向接壤的外国(另一个帝国或独立王国)输出革命——对方边城的劳工、农民转向本派理念，
// 所属帝国的劳工农民怨气上升(见 ClaimPolicySystem.ExportRevolution)。挑人口最多的接壤外国下手。
public class TempFac_输出革命 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_输出革命();
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
        FixedFaction faction = GetFaction();
        PartyIdeology ideology = faction?.IsParty == true ? faction.Ideology : PartyIdeology.Communism;
        Kingdom target = ClaimPolicySystem.ExportRevolution(empire, GetKingdomTarget(), ideology);
        if (target != null)
            CompletionOutcome = string.Format(LM.Get("claim_outcome_输出革命_target"), target.GetKingdomFullName());
        End();
    }

    // 目标：接壤、没在交战的外国，人口多的优先。规则在 ClaimRules.json 的 target(见 ClaimTargetRules)
    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    protected override bool TargetReady() => GetEmpire() != null && CityPopulationSystem.AbstractPopulationEnabled;
}

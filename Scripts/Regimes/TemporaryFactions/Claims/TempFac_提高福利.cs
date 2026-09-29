using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_提高福利 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_提高福利();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        if (CheckCondition())
        {
            Empire empire = GetEmpire();
            int level = ++empire.data.constitutional_economy.welfare_level;
            empire.RecordHistory(directContent: string.Format(LM.Get("agenda_welfare_enacted_history"), level),
                kingdomId: empire.CoreKingdom.id);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || empire.data?.constitutional_economy == null ||
            empire.data.constitutional_economy.welfare_level >= 3) return false;
        FixedFaction party = GetFaction();
        if (party?.IsParty != true || party.Ban ||
            InstitutionSystem.GetFeature(InstitutionSystem.GetPrimaryCulture(empire),
                IdeologyInstitutionPaths.StageFeature(party.Ideology, 2)) <= 0f) return false;
        return empire.CoreKingdom.GetMoney() >= ConstitutionalEconomySystem.GetWelfareAnnualCost(empire,
            empire.data.constitutional_economy.welfare_level + 1);
    }
}

using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 推动本文化的开放党禁改革(制度树上声明了 claim_reform:开放党禁 的节点)
public class TempFac_开放党禁 : TemporaryFaction
{
    public override bool CanProceedOutsideGovernment => true;

    public override bool CheckContinue() => base.CheckContinue() && CheckCondition();
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_开放党禁();
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
        if (!CheckCondition()) { End(); return; }
        if (PartySystem.IsActive(empire) && PartyBanSystem.IsClosed(empire))
        {
            PartyBanSystem.Open(empire, "party_ban_reopened_ideology_history");
            ParliamentSystem.Update(empire);
            End();
            return;
        }
        InstitutionNodeConfig node = InstitutionSystem.FindClaimReformTarget(empire, "开放党禁");
        if (empire != null && node != null)
        {
            bool force = !InstitutionSystem.CanStartReform(empire, node, out _, false) &&
                         InstitutionSystem.CanStartReform(empire, node, out _, true);
            InstitutionSystem.StartReform(empire, node.id, force, factionID);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (GetFaction()?.Ban == true || TechnologySystem.PremodernLocked ||
            ClaimAgendaSystem.BuildContext(empire)?.PartyOpening?.HasChallenge != true) return false;
        if (PartySystem.IsActive(empire)) return PartyBanSystem.IsClosed(empire) &&
            !ConstitutionSystem.IsPlayerLocked(empire, ConstitutionSystem.ClausePartySystem);
        InstitutionNodeConfig node = InstitutionSystem.FindClaimReformTarget(empire, "开放党禁");
        if (empire == null || node == null) return false;
        return InstitutionSystem.CanStartReform(empire, node, out _, false) ||
               InstitutionSystem.CanStartReform(empire, node, out _, true);
    }
}

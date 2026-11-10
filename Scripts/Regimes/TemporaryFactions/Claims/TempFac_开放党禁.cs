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
        // 用户 2026-11-10：硬发展门槛——核心繁荣不足的穷/贫瘠国家不能自主开放党禁，
        // 人民革命(本诉求)连立项都不行，只能等外力(战败和约)来逼。已开放的国家不走本诉求，不受影响。
        if (!PartyBanSystem.PassesModernizationGate(empire)) return false;
        // 一党政权：民间理念压过国家理念还不够，要重开压力满格又到了危局(正统崩、核心丢，见 PartyBanSystem.OnePartyCrisis)
        if (PartySystem.IsActive(empire)) return PartyBanSystem.IsClosed(empire) && PartyBanSystem.OnePartyCrisis(empire) &&
            !ConstitutionSystem.IsPlayerLocked(empire, ConstitutionSystem.ClausePartySystem);
        InstitutionNodeConfig node = InstitutionSystem.FindClaimReformTarget(empire, "开放党禁");
        if (empire == null || node == null) return false;
        return InstitutionSystem.CanStartReform(empire, node, out _, false) ||
               InstitutionSystem.CanStartReform(empire, node, out _, true);
    }
}

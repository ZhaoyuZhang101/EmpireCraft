using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 推动华夏制度「三省六部制」(制度树上声明了 claim_reform:推行三省六部 的节点)：绥靖派(文官)的诉求
public class TempFac_推行三省六部 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_推行三省六部();
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
        InstitutionNodeConfig node = InstitutionSystem.FindClaimReformTarget(empire, "推行三省六部");
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
        InstitutionNodeConfig node = InstitutionSystem.FindClaimReformTarget(empire, "推行三省六部");
        if (empire == null || node == null) return false;
        return InstitutionSystem.CanStartReform(empire, node, out _, false) ||
               InstitutionSystem.CanStartReform(empire, node, out _, true);
    }
}

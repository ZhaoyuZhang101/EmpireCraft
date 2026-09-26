using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_转天朝制度 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_转天朝制度();
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
        InstitutionNodeConfig node = InstitutionSystem.FindClaimReformTarget(empire, "转天朝制度");
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
        InstitutionNodeConfig node = InstitutionSystem.FindClaimReformTarget(empire, "转天朝制度");
        if (empire == null || node == null) return false;
        if (empire.Mandate<70) return false;
        if (empire.CoreKingdom.GetSystemChangeYear() < 50)
        {
            return false;
        }
        bool politicallyReady = empire.kingdoms_list.FindAll(k => !k.IsEmpire()).Sum(k => k.countTotalWarriors()) <
                                empire.CoreKingdom.countTotalWarriors();
        if (!politicallyReady)
        {
            politicallyReady = empire.kingdoms_list
                .Where(k => !k.IsEmpire())
                .All(k => !k.GetRegime().IsAllowDiplomacy());
        }
        if (!politicallyReady) return false;
        return InstitutionSystem.CanStartReform(empire, node, out _, false) ||
               InstitutionSystem.CanStartReform(empire, node, out _, true);
    }
}

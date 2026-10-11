using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 复辟帝制(见 RepublicSystem)：改制共和之后，保守主义政党执政且过半时恢复君主政体。
public class TempFac_复辟帝制 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_复辟帝制();
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
        FixedFaction party = GetFaction();
        if (empire != null && party != null) RepublicSystem.Restore(empire, party);
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        FixedFaction party = GetFaction();
        return empire != null && party != null && RepublicSystem.CanRestore(empire, party);
    }
}

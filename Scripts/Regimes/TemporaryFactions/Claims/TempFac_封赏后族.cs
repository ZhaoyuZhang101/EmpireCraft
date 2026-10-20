using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 封赏后族(后党诉求，见 CliqueSystem)：外戚首领(大将军，见 ConsortKinSystem.Chief)还没有爵位时，
// 后党请封他为公侯(本朝还空着的荣誉爵位)，声望随之上涨。
public class TempFac_封赏后族 : TemporaryFaction
{
    private const int Renown = 60;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_封赏后族();
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
        Actor chief = ConsortKinSystem.Chief(empire);
        string key = FreePeerage(empire);
        if (chief?.data != null && key != null && chief.GrantHonoraryPeerage(empire, key))
        {
            chief.editRenown(Renown);
            CompletionOutcome = string.Format(GeneralSystems.CultureDoctrines.Text("claim_outcome_封赏后族_granted", GetEmpire()), chief.getName(), LM.Get(key));
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        Actor chief = ConsortKinSystem.Chief(empire);
        return chief?.data != null && chief.isAlive() && !chief.HasHonoraryPeerage() && FreePeerage(empire) != null;
    }

    private static string FreePeerage(Empire empire) =>
        empire?.CoreKingdom?.GetRegime()?.virtual_honorary_peerages?
            .FirstOrDefault(key => key != "tang_honorary_anle_gong" && !empire.IsHonoraryPeerageReserved(key));
}

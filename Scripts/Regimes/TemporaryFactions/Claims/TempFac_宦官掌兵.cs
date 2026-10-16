using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 宦官掌兵(阉党诉求，见 CliqueSystem)：阉党首领请掌禁军——京师最大的一支禁军改由他统领(见 PalaceArmySystem)。
public class TempFac_宦官掌兵 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_宦官掌兵();
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
        Actor leader = GetFaction()?.GetLeader();
        CorpsData corps = Target(empire, leader);
        if (corps != null && CorpsSystem.AssignGeneral(empire.CoreKingdom, corps, leader))
            CompletionOutcome = string.Format(LM.Get("claim_outcome_宦官掌兵_command"), leader.getName(), CorpsSystem.Name(corps));
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        Actor leader = GetFaction()?.GetLeader();
        return CliqueSystem.IsEunuch(leader) && Target(empire, leader) != null;
    }

    private static CorpsData Target(Empire empire, Actor leader)
    {
        if (empire?.CoreKingdom == null || leader?.data == null || !leader.isAlive() || CorpsSystem.IsGeneral(leader)) return null;
        return PalaceArmySystem.Palace(empire.CoreKingdom).OrderByDescending(corps => corps.strength).FirstOrDefault();
    }
}

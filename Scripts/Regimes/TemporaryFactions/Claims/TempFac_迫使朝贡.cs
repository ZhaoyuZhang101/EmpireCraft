using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_迫使朝贡 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_迫使朝贡();
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
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            var war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, kingdom, WarTypeLibrary.normal);
            war?.SetEmpireWarType(EmpireWarType.迫使朝贡);
        }
        CountDown = 5;
        End();
    }

    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    // 谁能当目标、朝廷自己怎么挑写在 ClaimRules.json 的 target(见 ClaimTargetRules，政体编辑器的决议页可改)；这里只留决议自己的前提
    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        // 只在忙不过来时不提(同扩张地盘，见 WarSituation.TooBusyForWar；以前中央手上有一场仗就不提)
        return empire?.CoreKingdom != null && !WarSituation.TooBusyForWar(empire.CoreKingdom) &&
               FeudalConquestService.HasTributeInstitution(empire);
    }
}

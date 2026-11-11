using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_对外扩张 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_对外扩张();
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
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            var war = World.world.diplomacy.startWar(GetEmpire().CoreKingdom, kingdom, WarTypeLibrary.normal);
            // 攘夷是华夏的说法；别的文明对异族的对外扩张记为帝国扩张
            bool huaxia = GeneralSystems.InstitutionSystem.GetCultureLine(
                GeneralSystems.CultureService.GetRealmCulture(GetEmpire().CoreKingdom)) == "Huaxia";
            war?.SetEmpireWarType(huaxia ? EmpireWarType.攘夷 : EmpireWarType.帝国扩张);
        }
        CountDown = 5;
        End();
    }

    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    // 谁能当目标、朝廷自己怎么挑写在 ClaimRules.json 的 target(见 ClaimTargetRules，政体编辑器的决议页可改)；这里只留决议自己的前提
    protected override bool TargetReady()
    {
        Kingdom core = GetEmpire()?.CoreKingdom;
        // 发不出军饷不开新仗
        return core != null && !GeneralSystems.WarSituation.TooBusyForWar(core) && !GeneralSystems.CorpsSystem.CannotFundWar(core);
    }
}

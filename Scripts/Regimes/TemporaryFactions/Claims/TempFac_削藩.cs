using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_削藩 : TemporaryFaction
{
    // 大汗收回兀鲁思的正统门槛。埃米尔的门槛 60 → 75(EmirateSystem.StrongLegitimacy，为与坐大区间错开)，兀鲁思不按正统坐大，仍是 60
    private const int UlusReclaimLegitimacy = 60;

    public override bool RequireCrimeTarget => true;
    public override bool RequireRenown => true;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_削藩();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null && !CheckRebelling(kingdom) && !ZhouEraSystem.RoyalReclaimBlocked(GetEmpire()))
        {
            if (!TryEnforceCrimeForCurrentTarget())
            {
                End();
                return;
            }

            foreach (var city in kingdom.cities)
            {
                city.joinAnotherKingdom(GetEmpire().CoreKingdom);
            }
            GetEmpire().AddMandate(20);
            LogService.LogInfo("执行成功");
        }
        End();
    }

    public override bool CheckLocalCondition(Kingdom actor)
    {
        if (!base.CheckLocalContinue(actor)) return false;
        Empire empire = GetEmpire();
        if (ZhouEraSystem.RoyalReclaimBlocked(empire)) return false;
        var target = empire.kingdoms_list.ToList().Find(k =>
        {
            if (k == null || k.isRekt() || k == actor || k == empire.CoreKingdom) return false;
            if (!k.hasKing()) return false;
            if (k.king.GetFaction()==GetFaction()) return false;
            if (k.king.renown>actor.king.renown) return false;
            if (k.GetRegime().GetLeaderSelectMethod() != LeaderSelectMethod.Succession) return false;
            return true;
        });
        return TrySetTarget(target);
    }

    // 目标：世袭、其君已有不臣之迹(违逆 ≥ 90)、兵力不到本朝六分之一、不在打仗、有罪名可追究的成员，皇帝的名望还得压得住。
    // 规则在 ClaimRules.json 的 target(见 ClaimTargetRules)
    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        if (empire?.kingdoms_list == null || ZhouEraSystem.RoyalReclaimBlocked(empire)) return false;
        // 哈里发正统高才收得回世袭的埃米尔(见 EmirateSystem.StrongLegitimacy，印度按 InvestitureGap 减)；
        // 大汗也要正统高才收得回兀鲁思(见 EnfeoffmentHelper.UlusPartition)
        return !(EmirateSystem.Applies(empire) &&
                 empire.Legitimacy < EmirateSystem.StrongLegitimacy - EmirateSystem.InvestitureGap(empire)) &&
               !(empire.CoreKingdom?.GetRegime()?.type == RegimeType.YouMu && empire.Legitimacy < UlusReclaimLegitimacy);
    }
}

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

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null || ZhouEraSystem.RoyalReclaimBlocked(empire)) return false;
        // 哈里发正统高才收得回世袭的埃米尔(见 EmirateSystem.StrongLegitimacy，印度按 InvestitureGap 减)；
        // 大汗也要正统高才收得回兀鲁思(见 EnfeoffmentHelper.UlusPartition)
        if ((EmirateSystem.Applies(empire) &&
             empire.Legitimacy < EmirateSystem.StrongLegitimacy - EmirateSystem.InvestitureGap(empire)) ||
            (empire.CoreKingdom?.GetRegime()?.type == RegimeType.YouMu && empire.Legitimacy < UlusReclaimLegitimacy)) return false;
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            if (kingdom.IsEmpire()) continue;
            if (kingdom.IsFactionRebelling()) continue;
            if (kingdom.getWars().Any()) continue;
            if (!OwnEnoughRenown(kingdom)) continue;
            if ((kingdom?.king?.GetViolateValue()??0)<90) continue;
            Regime regime = kingdom.GetRegime();
            // 政教合一的成员(教会领地)不削。哈里发神权把政教合一推到全境的阿拉伯成员(见 arabic_caliphal_theocracy 的
            // set_regime_option)，世袭的埃米尔照削(用户 2026-10-21 选"埃米尔能收回"：以前神权路线的哈里发国一家也削不了)，圣城之主除外
            if (regime.GetReligionLevel() == ReligionLevel.High &&
                (!EmirateSystem.IsHereditaryEmir(empire, kingdom) || PapacySystem.HoldsHolyCity(kingdom))) continue;
            if (regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession)
            {
                if (kingdom.countTotalWarriors() * 5 <= empire.countWarriors() - kingdom.countTotalWarriors())
                {
                    return TrySetTarget(kingdom);
                }
            }
        }

        return false;
    }
}

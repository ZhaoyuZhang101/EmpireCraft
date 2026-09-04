using System.Collections.Generic;
using System.Linq;
using System;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_分封 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_分封();
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
        Actor actor = GetActorTarget();
        Empire empire = GetEmpire();
        Kingdom coreKingdom = empire?.CoreKingdom;
        Regime empireRegime = coreKingdom?.GetRegime();
        EmpireCore empireCore = EmpireCoreManager.Get(empire);
        City city = FindEnfeoffableCity(empire, empireCore);
        if (empireRegime == null || empireRegime.enfeoff_virtual_only ||
            !IsEligibleSibling(actor, empire) || city == null)
        {
            End();
            return;
        }

        Kingdom kingdom = city.makeOwnKingdom(actor);
        if (kingdom != null)
        {
            kingdom.SetRegimeType(empireRegime.type);
            kingdom.LoadRegime();
            Regime kingdomRegime = kingdom.GetRegime();
            if (kingdomRegime != null)
            {
                kingdomRegime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
                kingdomRegime.SetAllowSupportCenterArmy(false);
                kingdomRegime.SetTaxLevel(TaxLevel.None);
            }
            KingdomTitle title = city.GetTitle();
            if (title?.title_capital == city)
            {
                kingdom.SetMainTitle(title);
                kingdom.king?.AddOwnedTitle(title);
            }
            empire.join(kingdom, pForce:true);
            empire.AddMandate(10);
            actor.CheckSpecificClan(false);
            TranslateHelper.LogPeerageGranted(actor, empire,
                (title?.data?.name ?? kingdom.data.name) + LM.Get("default_peerages_2"));
        }
        End();
    }

    public override bool CheckContinue()
    {
        Empire empire = GetEmpire();
        Kingdom coreKingdom = empire?.CoreKingdom;
        Regime regime = coreKingdom?.GetRegime();
        if (empire == null || coreKingdom == null || coreKingdom.isRekt() || regime == null ||
            regime.enfeoff_virtual_only) return false;
        if (ShowAsPlot && (empire.Emperor == null || empire.Emperor.isRekt())) return false;
        if (!base.CheckContinue()) return false;
        return IsEligibleSibling(GetActorTarget(), empire) &&
            FindEnfeoffableCity(empire, EmpireCoreManager.Get(empire)) != null;
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null || empire.CoreKingdom == null) return false;
        Regime regime = empire.CoreKingdom.GetRegime();
        if (regime == null || regime.enfeoff_virtual_only) return false;
        if (FindEnfeoffableCity(empire, EmpireCoreManager.Get(empire)) == null) return false;

        Actor candidate = FindEnfeoffmentCandidate(empire);
        if (candidate == null) return false;
        SetActorTarget(candidate);
        return true;
    }

    private static City FindEnfeoffableCity(Empire empire, EmpireCore empireCore)
    {
        Kingdom coreKingdom = empire?.CoreKingdom;
        if (coreKingdom?.cities == null) return null;
        return coreKingdom.cities.FirstOrDefault(city =>
        {
            if (city == null || city.isRekt() || city == coreKingdom.capital || city.isCapitalCity()) return false;
            if (empireCore != null && city.GetEmpireCore() == empireCore) return false;
            KingdomTitle title = city.GetTitle();
            return title == null || !string.Equals(title.data?.name, empire.GetEmpireName(), StringComparison.Ordinal);
        });
    }

    private static Actor FindEnfeoffmentCandidate(Empire empire)
    {
        PersonalClanIdentity emperorIdentity = empire?.Emperor?.GetPersonalIdentity();
        return SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)
            .Select(item => item.Item2)
            .Where(identity => identity != null && IsEligibleSibling(identity._actor, empire))
            .OrderBy(identity => identity.rank)
            .Select(identity => identity._actor)
            .FirstOrDefault();
    }

    private static bool IsEligibleSibling(Actor actor, Empire empire)
    {
        if (actor == null || actor.isRekt() || actor.isKing() || empire?.Emperor == null)
            return false;
        PersonalClanIdentity emperorIdentity = empire.Emperor.GetPersonalIdentity();
        PersonalClanIdentity actorIdentity = actor.GetPersonalIdentity();
        if (emperorIdentity == null || actorIdentity == null || !actorIdentity.CanHeir(emperorIdentity)) return false;
        if (actorIdentity._specificClan != empire.EmpireSpecificClan) return false;
        if (actor.id == (empire.CoreKingdom?.GetHeir()?.id ?? -1L)) return false;
        if (actor.HasVirtualEnfeoff(empire)) return false;
        if (actor.kingdom?.GetEmpire() != empire) return false;
        return SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)
            .Any(item => item.Item2?.id == actorIdentity.id);
    }
}

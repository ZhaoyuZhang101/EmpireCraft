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
        EnfeoffmentHelper.TryEnfeoffActor(empire, actor, 10);
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
        return EnfeoffmentHelper.IsEligibleSibling(GetActorTarget(), empire) &&
            EnfeoffmentHelper.FindEnfeoffableCity(empire, EmpireCoreManager.Get(empire)) != null;
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null || empire.CoreKingdom == null) return false;
        Regime regime = empire.CoreKingdom.GetRegime();
        if (regime == null || regime.enfeoff_virtual_only) return false;
        if (EnfeoffmentHelper.FindEnfeoffableCity(empire, EmpireCoreManager.Get(empire)) == null) return false;

        Actor candidate = EnfeoffmentHelper.FindEnfeoffmentCandidate(empire);
        if (candidate == null) return false;
        SetActorTarget(candidate);
        return true;
    }
}

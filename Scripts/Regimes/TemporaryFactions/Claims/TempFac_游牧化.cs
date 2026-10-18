using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_游牧化 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_游牧化();
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
        var target = GetKingdomTarget();
        if (target != null && Eligible(empire, target))
        {
            target.SetRegimeType(RegimeType.YouMu);
            target.LoadRegimeKeepingRole();
            target.SystemChange();
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.kingdoms_list == null) return false;
        foreach (var kingdom in empire.kingdoms_list)
        {
            if (!Eligible(empire, kingdom)) continue;
            SetKingdomTarget(kingdom);
            return true;
        }
        return false;
    }

    // 只改游牧文化(有游牧特性或走游牧一线)的成员；手选、锁定政体的、共和与现代、原始政体、羁縻的不动
    // (以前不看文化，华夏文化的成员也被改成游牧政体)
    private static bool Eligible(Empire empire, Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom == empire.CoreKingdom) return false;
        RegimeType? current = kingdom.GetRegime()?.type;
        if (current == null || current == RegimeType.YouMu || !RegimeManager.IsMonarchy(current) ||
            RegimeManager.IsPrimitive(current)) return false;
        if (kingdom.GetOrCreate().regime_manually_selected || GeneralSystems.RepublicSystem.IsRegimeLocked(kingdom) ||
            GeneralSystems.JimiSystem.IsJimiAdministration(kingdom)) return false;
        string culture = GeneralSystems.CultureService.GetRealmCulture(kingdom);
        return EmpireCraft.Scripts.Data.OnomasticsRule.IsNomadCulture(culture) ||
               GeneralSystems.InstitutionSystem.GetCultureLine(culture) == "Youmu";
    }
}

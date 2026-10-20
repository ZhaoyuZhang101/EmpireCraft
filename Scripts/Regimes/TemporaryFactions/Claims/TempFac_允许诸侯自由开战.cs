using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_允许诸侯自由开战 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_允许诸侯自由开战();
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
        Regime regime = kingdom?.GetRegime();
        if (regime != null)
        {
            regime.SetAllowDiplomacy(true);
            regime.SetAllowSupportCenterArmy(false);
            // 封建制帝国的治安令施行期间：这一家记为豁免(见 HolyRomanSystem「帝国治安令」)
            HolyRomanSystem.Exempt(GetEmpire(), kingdom);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || empire.kingdoms_list == null) return false;
        if (empire.HasEmperor())
        {
            if (!empire.Emperor.hasTrait("ambitious")) return false;
        }
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            // 自由城市、主教领本就不开战，不在此列
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == empire.CoreKingdom ||
                !HolyRomanSystem.SecularPrince(kingdom)) continue;
            Regime regime = kingdom.GetRegime();
            if (regime != null && !regime.IsAllowDiplomacy())
            {
                SetKingdomTarget(kingdom);
                return true;
            }
        }
        return false;
    }
}

using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class RealmDiplomacySystem
{
    // 托管行政区不拥有出让国家主权的资格；地方自行称臣不能顺便将其从原帝国中解绑。
    public static bool CanChooseOverlord(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return false;
        if (kingdom.IsEmpire()) return true;
        if (kingdom.IsAdministrativeKingdomType()) return false;
        var empire = kingdom.GetEmpire();
        if (empire?.CoreKingdom?.GetRegime()?.type == RegimeType.LvLing) return false;
        return empire == null || kingdom.GetRegime()?.IsAllowDiplomacy() == true;
    }
}

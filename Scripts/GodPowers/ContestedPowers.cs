using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 争议地带(见 ContestedLandSystem)：先点要标的城，再点宣称方帝国核心里的任一座城，记一份宣称(已有这一家就撤掉它)；
// 第二下点回本城或同一核心的城：撤掉这座城的全部宣称。冻结法理时照样能用，手动标的不过期
public static class ContestedPowers
{
    public const string Id = "contested_land";
    private static City _pending;

    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = Id,
            name = Id,
            click_action = Click
        });
    }

    private static bool Click(WorldTile pTile, string pPower)
    {
        City city = pTile?.zone?.city;
        if (city?.data == null || city.isRekt())
        {
            ActionLibrary.showWhisperTip("contested_invalid_target");
            return false;
        }
        if (_pending?.data == null || _pending.isRekt())
        {
            _pending = city;
            Tip(string.Format(LM.Get("contested_selected"), city.GetCityName()));
            return false;
        }
        City target = _pending;
        _pending = null;
        EmpireCore core = city.GetEmpireCore();
        if (city == target || core == null || core.id == target.GetEmpireCoreID())
        {
            if (ContestedLandSystem.Clear(target)) Tip(string.Format(LM.Get("contested_cleared"), target.GetCityName()));
            else ActionLibrary.showWhisperTip("city_cancelled");
            return true;
        }
        string name = EmpireCoreManager.GetStatusDisplayName(core);
        if (ContestedLandSystem.Claimants(target).Contains(core))
        {
            ContestedLandSystem.RemoveClaim(target, core);
            Tip(string.Format(LM.Get("contested_removed"), target.GetCityName(), name));
            return true;
        }
        ContestedLandSystem.AddClaim(target, core, manual: true);
        Tip(string.Format(LM.Get("contested_added"), target.GetCityName(), name));
        return true;
    }

    private static void Tip(string text) => WorldTip.showNow(text, false, "top", 3f);
}

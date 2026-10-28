using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 外交：点地图上的国家，打开外交界面并以它为焦点(见 DiplomacyWindow)
public static class DiplomacyPower
{
    public const string Id = "diplomacy";

    public static void Init()
    {
        if (AssetManager.powers.get(Id) != null) return;
        AssetManager.powers.add(new GodPower
        {
            id = Id,
            name = Id,
            click_action = (tile, power) =>
            {
                Kingdom kingdom = tile?.zone?.city?.kingdom;
                if (kingdom?.data == null || kingdom.isRekt())
                {
                    WorldTip.showNow(LM.Get("diplomacy_pick_hint"), false, "top", 3f);
                    return true;
                }
                DiplomacyWindow.Open(kingdom);
                return true;
            }
        });
    }
}

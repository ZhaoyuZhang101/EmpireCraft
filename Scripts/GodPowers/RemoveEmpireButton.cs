using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class RemoveEmpireButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "remove_empire",
            name = "remove_empire",
            click_action = empire_remove_action
        });
    }

    private static bool empire_remove_action(WorldTile pTile, string pPower)
    {
        if (pTile.hasCity())
        {
            if (pTile.zone_city.hasKingdom())
            {
                if(pTile.zone_city.kingdom.IsInEmpire())
                {
                    Empire empire = pTile.zone_city.kingdom.GetEmpire();
                    Kingdom kingdom = pTile.zone_city.kingdom;
                    bool member = kingdom != empire.CoreKingdom;
                    // 神力放出：十五年内原帝国不得收回，它自己也不主动回去(见 MembershipCooldown)
                    MembershipCooldown.ByGod(() => empire.leave(kingdom));
                    // 放出来就是独立之国：行省、羁縻州之类不再托管法理，自办外交、自募军队、不供中央军
                    // (同据城自立，见 CityStabilitySystem.ReviewWithdrawal)；以前仍是没兵没外交的行省，邻国一打就并回去
                    if (member && !kingdom.isRekt() && !kingdom.IsInEmpire())
                    {
                        kingdom.ClearAdministrativeTitle();
                        var regime = kingdom.GetRegime();
                        regime?.SetAllowDiplomacy(true);
                        regime?.SetAllowArmy(true);
                        regime?.SetAllowSupportCenterArmy(false);
                        kingdom.GetOrCreate().last_kingdom_status_ts = -1d;
                        AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
                    }
                }
            }
        }
        return true;
    }
}

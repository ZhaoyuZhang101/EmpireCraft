using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GodPowers;

// 加冕称帝(神力)：玩家强制，不受"同文化已有帝国"的限制。
//   · 君主制国家：直接称帝；
//   · 现代国家：直接组建政府并成为正常国家——标记为革命政府，不会在军阀时期被降为军阀、撤销政府；
//     已组建政府却只算军阀的，同样直接转为正常国家。
public static class EmpireFormButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "empire_form",
            name = "empire_form",
            click_action = crown_become_action
        });
    }

    private static bool crown_become_action(WorldTile pTile, string pPower)
    {
        if (!pTile.hasCity()) return true;
        Kingdom kingdom = pTile.zone_city.kingdom;
        if (kingdom == null || kingdom.isRekt() || kingdom.isNeutral()) return true;
        bool modern = kingdom.GetRegime()?.type == RegimeType.Modern;

        if (kingdom.IsEmpire())
        {
            if (!modern) return true;
            Empire existing = kingdom.GetEmpire();
            if (!MakeNormalGovernment(existing)) return true;
            ActionLibrary.showWhisperTip("empire_form_modern");
            Refresh();
            return true;
        }

        if (kingdom.IsInEmpire())
            kingdom.GetEmpire().leave(kingdom);

        Empire empire = modern
            ? ModernStateFormationSystem.FormGovernment(kingdom)
            : ModClass.EMPIRE_MANAGER.NewEmpire(kingdom, allowCultureRival: true);
        if (empire == null)
        {
            ActionLibrary.showWhisperTip("empire_form_failed");
            return true;
        }
        if (modern) MakeNormalGovernment(empire);
        ActionLibrary.showWhisperTip(modern ? "empire_form_modern" : "empire_form");
        Refresh();
        return true;
    }

    private static bool MakeNormalGovernment(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null) return false;
        state.revolutionary_government = true;
        return true;
    }

    private static void Refresh()
    {
        WarlordEraSystem.UpdateWorld(force: true);
        World.world.zone_calculator.dirtyAndClear();
    }
}

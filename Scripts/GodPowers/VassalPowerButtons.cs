using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 附庸神力：
//   强制附庸(force_vassal)：先点宗主国，再点目标国家，无视外交条件直接建立封建附庸关系；
//     选中期间画出宗主首都到光标的指示线和现有的附庸关系线(见 EmpireCraftQuantumSpriteLibrary.DrawVassalLines)。
//   解除附庸(release_vassal)：点附庸解除它与宗主的关系；点宗主则解除它的全部直属附庸。
public static class VassalPowerButtons
{
    public static long SelectedLordId = -1L;

    public static Kingdom SelectedLord()
    {
        Kingdom lord = SelectedLordId > 0 ? World.world?.kingdoms?.get(SelectedLordId) : null;
        if (lord == null || lord.isRekt()) { SelectedLordId = -1L; return null; }
        return lord;
    }

    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = "force_vassal",
            name = "force_vassal",
            click_action = ClickForceVassal
        });
        AssetManager.powers.add(new GodPower
        {
            id = "release_vassal",
            name = "release_vassal",
            click_action = ClickReleaseVassal
        });
    }

    private static bool ClickForceVassal(WorldTile pTile, string pPower)
    {
        Kingdom kingdom = pTile?.zone?.city?.kingdom;
        if (kingdom == null || kingdom.wild || kingdom.isRekt())
        {
            WorldTip.showNow(LM.Get("force_vassal_no_kingdom"), false, "top", 3f);
            return false;
        }
        Kingdom lord = SelectedLord();
        if (lord == null)
        {
            SelectedLordId = kingdom.id;
            WorldTip.showNow(string.Format(LM.Get("force_vassal_pick_subject"), kingdom.GetKingdomName()), false, "top", 3f);
            return true;
        }
        if (lord == kingdom)
        {
            SelectedLordId = -1L;
            WorldTip.showNow(LM.Get("force_vassal_cancelled"), false, "top", 3f);
            return true;
        }
        bool bound = FeudalVassalService.ForceBind(lord, kingdom);
        SelectedLordId = -1L;
        WorldTip.showNow(string.Format(LM.Get(bound ? "force_vassal_done" : "force_vassal_failed"),
            kingdom.GetKingdomName(), lord.GetKingdomName()), false, "top", 3f);
        return bound;
    }

    private static bool ClickReleaseVassal(WorldTile pTile, string pPower)
    {
        Kingdom kingdom = pTile?.zone?.city?.kingdom;
        if (kingdom == null || kingdom.wild || kingdom.isRekt())
        {
            WorldTip.showNow(LM.Get("force_vassal_no_kingdom"), false, "top", 3f);
            return false;
        }
        if (FeudalVassalService.GetOverlord(kingdom) != null)
        {
            FeudalVassalService.Break(kingdom);
            WorldTip.showNow(string.Format(LM.Get("release_vassal_done"), kingdom.GetKingdomName()), false, "top", 3f);
            return true;
        }
        int released = 0;
        foreach (Kingdom subject in global::System.Linq.Enumerable.ToList(FeudalVassalService.GetDirectVassals(kingdom)))
        {
            FeudalVassalService.Break(subject);
            released++;
        }
        WorldTip.showNow(released > 0
            ? string.Format(LM.Get("release_vassal_all_done"), kingdom.GetKingdomName(), released)
            : LM.Get("release_vassal_none"), false, "top", 3f);
        return released > 0;
    }
}

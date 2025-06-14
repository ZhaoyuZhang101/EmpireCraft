using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using System;
using UnityEngine.Events;

// Token: 0x0200069B RID: 1691
public class EmpireWindow : WindowMetaGeneric<Empire, EmpireData>
{
    // Token: 0x170003FC RID: 1020
    // (get) Token: 0x06003294 RID: 12948 RVA: 0x0017DDC6 File Offset: 0x0017BFC6
    public override MetaType meta_type
    {
        get
        {
            return MetaType.Kingdom;
        }
    }

    // Token: 0x170003FD RID: 1021
    // (get) Token: 0x06003295 RID: 12949 RVA: 0x0017DDCA File Offset: 0x0017BFCA
    public override Empire meta_object
    {
        get
        {
            return ConfigData.CURRENT_SELECTED_EMPIRE;
        }
    }

    // Token: 0x06003296 RID: 12950 RVA: 0x0017DDD1 File Offset: 0x0017BFD1
    public override void initNameInput()
    {
        base.initNameInput();
        this.mottoInput.addListener(new UnityAction<string>(this.applyInputMotto));
    }

    // Token: 0x06003297 RID: 12951 RVA: 0x0017DDF0 File Offset: 0x0017BFF0
    private void applyInputMotto(string pInput)
    {
        if (pInput == null)
        {
            return;
        }
        if (this.meta_object == null)
        {
            return;
        }
        this.meta_object.data.motto = pInput;
    }

    // Token: 0x06003298 RID: 12952 RVA: 0x0017DE10 File Offset: 0x0017C010
    public override void showTopPartInformation()
    {
        base.showTopPartInformation();
        Empire tEmpire = this.meta_object;
        if (tEmpire == null)
        {
            return;
        }
        this.mottoInput.setText(tEmpire.getMotto());
        this.mottoInput.textField.color = tEmpire.getColor().getColorText();
    }

    // Token: 0x06003299 RID: 12953 RVA: 0x0017DE5C File Offset: 0x0017C05C
    public override void showStatsRows()
    {
        Empire tAlliance = this.meta_object;
        base.showStatRow("founded", tAlliance.getFoundedDate(), MetaType.None, -1L, "iconAge", null, null);
        base.tryToShowActor("alliance_founder", tAlliance.data.founder_actor_id, tAlliance.data.founder_actor_name, null, "actor_traits/iconStupid");
        base.tryToShowMetaKingdom("alliance_founder_kingdom", tAlliance.data.founder_kingdom_id, tAlliance.data.founder_kingdom_name, null);
        base.tryToShowMetaKingdom("Empire", tAlliance.data.empire, tAlliance.data.name, tAlliance.empire);
        using (ListPool<Actor> tList = new ListPool<Actor>())
        {
            tList.AddRange(tAlliance.getUnits());
            base.showSplitPopulationBySubspecies(tList);
        }
    }

    // Token: 0x0600329A RID: 12954 RVA: 0x0017DF0C File Offset: 0x0017C10C
    public override void OnDisable()
    {
        base.OnDisable();
        this.mottoInput.inputField.DeactivateInputField();
    }

    // Token: 0x04003887 RID: 14471
    public NameInput mottoInput;

    // Token: 0x04003888 RID: 14472
    public StatBar bar_experience;
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// Naming events only; never scan the population or infer paternity from a surname.
public static class SurnameInheritanceSystem
{
    public static Actor SelectParent(IEnumerable<Actor> parents) => parents?
        .Where(parent => parent?.data != null &&
            !string.IsNullOrWhiteSpace(parent.GetModName()?.familyName))
        .Distinct().OrderByDescending(ParentPriority).ThenBy(parent => parent.getID()).FirstOrDefault();

    // 在位的君主、储君(女主也一样)：配偶嫁入或入赘皇族，子女随君主姓、入皇族宗族，帝系不断
    public static bool Reigns(Actor actor)
    {
        if (actor?.data == null || actor.isRekt()) return false;
        if (actor.isKing()) return true;
        Kingdom kingdom = actor.kingdom;
        return kingdom?.data != null && !kingdom.isRekt() && kingdom.GetOrCreate().HeirID == actor.getID();
    }

    private static int ParentPriority(Actor parent)
    {
        // 君主、储君的子女随君主姓(女帝与皇夫所生的也是皇族，以前跟了皇夫的姓，帝系断开、姓氏跳来跳去)
        if (Reigns(parent)) return 1000 + (parent.isSexMale() ? 10 : 0);
        var identity = parent.GetPersonalIdentity();
        // Respect the existing marriage/concubinage and maternal/paternal clan rules.
        int priority = identity == null ? 0 : !identity.is_main ? -100
            : identity.hasLover() || identity.is_concubine ? 100 : 0;
        if (identity?._specificClan != null && identity.IsHeirPriority()) priority += 20;
        if (parent.isSexMale()) priority += 10;
        return priority;
    }

    public static bool TryInheritSurname(Actor child, bool overwrite = false)
    {
        if (child?.data == null) return false;
        Name name = child.GetModName();
        if (name == null || !overwrite && name.hasFamilyName(child)) return false;
        Actor parent = SelectParent(child.getParents());
        if (parent == null) return false;
        child.SetFamilyName(parent.GetModName().familyName);
        return true;
    }
}

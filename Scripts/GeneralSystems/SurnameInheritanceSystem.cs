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

    private static int ParentPriority(Actor parent)
    {
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

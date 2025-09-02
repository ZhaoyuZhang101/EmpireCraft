using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.WarExtension;

namespace EmpireCraft.Scripts.GameClassExtensions;
public static class FamilyExtension
{
    public class FamilyExtraData : ExtraDataBase
    {
        public bool is_culture_applied { get; set; } = false;
    }
    public static void ApplyCulture(this Family family)
    {
        family.GetOrCreate().is_culture_applied = true;
    }

    public static bool HasCultureApplied(this Family family)
    {
        return family.GetOrCreate().is_culture_applied;
    }

    public static FamilyExtraData GetOrCreate(this Family family, bool isSave=false)
    {
        return family.GetOrCreate<Family, FamilyExtraData>(isSave);
    }
}

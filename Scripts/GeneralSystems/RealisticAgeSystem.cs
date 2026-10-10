using System;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.GeneralSystems;

// Read the current laws at death checks: toggling never changes native stats or
// leaves an age-related death mark/risk behind after the law is disabled.
public static class RealisticAgeSystem
{
    public static bool Enabled => EmpireCraftWorldLawLibrary.empirecraft_law_realistic_age?.isEnabled() == true;
    public static bool OldAgeEnabled => WorldLawLibrary.world_law_old_age?.isEnabled() == true;

    public static int HumanDeathAge(long id, int recordedAge = -1)
    {
        if (recordedAge >= 60 && recordedAge <= 80) return recordedAge;
        // Stable across frames and saves; no repeated random draw can extend life.
        return 60 + (int)((ulong)id % 21UL);
    }

    public static bool CheckNaturalDeath(Actor actor, ref bool result)
    {
        if (!Enabled || actor?.data == null || !actor.isSapient() ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(actor)) return true;
        result = false;
        if (!OldAgeEnabled || !actor.isAlive() || actor.hasTrait("immortal")) return false;
        int limit = HumanDeathAge(actor.id, actor.GetPersonalIdentity()?.virtual_death_age ?? -1);
        if (actor.getAge() < limit) return false;
        actor.getHitFullHealth(AttackType.Age);
        result = true;
        return false;
    }

    public static void CaptureNativeLifetime(Actor actor, PersonalClanIdentity person)
    {
        person.virtual_native_lifespan = actor.stats?["lifespan"] ?? 0f;
        person.virtual_immortal = actor.hasTrait("immortal");
        person.virtual_death_age = HumanDeathAge(actor.id, person.virtual_death_age);
    }

    public static bool IsVirtualAgeDeathDue(PersonalClanIdentity person)
    {
        if (!OldAgeEnabled || person.virtual_immortal) return false;
        if (Enabled)
        {
            person.virtual_death_age = HumanDeathAge(person.id, person.virtual_death_age);
            return person.age >= person.virtual_death_age;
        }
        // Old snapshots have no native lifetime; resolve their species once.
        float lifespan = person.virtual_native_lifespan ??=
            AssetManager.actor_library.get(person.species)?.base_stats?["lifespan"] ?? 0f;
        return NativeDeathChance(person.age, lifespan) > UnityEngine.Random.value;
    }

    public static float NativeDeathChance(int age, float lifespan)
    {
        if (lifespan <= 0f || float.IsNaN(lifespan) || age <= lifespan) return 0f;
        // Same age curve as Actor.checkNaturalDeath, sampled in the existing
        // annual genealogy queue rather than adding a per-frame census.
        return Math.Min(0.9f, (float)(1d / (1d + Math.Exp(-5d * ((age - lifespan) / lifespan - 0.5d)))));
    }
}

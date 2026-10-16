using System;
using System.Collections.Generic;
using System.Linq;
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
        if (actor.getAge() < DeathAge(actor)) return false;
        actor.getHitFullHealth(AttackType.Age);
        result = true;
        return false;
    }

    // 君主、储君按古代的天年(见 RoyalHouseSystem.RoyalDeathAge)，其余六十到八十
    public static int DeathAge(Actor actor) => RoyalHouseSystem.IsRoyal(actor)
        ? RoyalHouseSystem.RoyalDeathAge(actor)
        : HumanDeathAge(actor.id, actor.GetPersonalIdentity()?.virtual_death_age ?? -1);

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
        // 旧存档、虚拟出生的子女没记下寿命：按物种补上。以前读物种资产上的 lifespan，原版寿命其实写在亚种基因里、
        // 物种资产上是 0，按原版 0 即不老死，关了真实年龄时这些虚拟族人活到两三百岁还在议立新君的名单里
        if (!(person.virtual_native_lifespan > 0f)) person.virtual_native_lifespan = SpeciesLifespan(person.species);
        return NativeDeathChance(person.age, person.virtual_native_lifespan ?? 0f) > UnityEngine.Random.value;
    }

    private const float FallbackLifespan = 100f;
    private static readonly Dictionary<string, float> LifespanBySpecies = new();
    private static object _lifespanWorld;

    // 物种的天年：先看一个在世的同物种单位(含亚种基因、特质)，没有就看物种基因里的寿命，再没有按一百年
    public static float SpeciesLifespan(string species)
    {
        if (!ReferenceEquals(_lifespanWorld, World.world))
        {
            _lifespanWorld = World.world;
            LifespanBySpecies.Clear();
        }
        string key = species ?? "";
        if (LifespanBySpecies.TryGetValue(key, out float known)) return known;
        float lifespan = 0f;
        try
        {
            foreach (Actor actor in World.world?.units ?? Enumerable.Empty<Actor>())
            {
                if (actor?.data == null || actor.asset?.id != key || !actor.isAlive()) continue;
                lifespan = actor.stats?["lifespan"] ?? 0f;
                if (lifespan > 0f) break;
            }
        }
        catch
        {
            // 遍历时单位列表变了：按物种基因算
        }
        if (!(lifespan > 0f))
            foreach (GenomePart part in AssetManager.actor_library.get(key)?.genome_parts ?? new HashSet<GenomePart>())
                if (string.Equals(part.id, "lifespan", StringComparison.OrdinalIgnoreCase)) lifespan = part.value;
        if (!(lifespan > 0f) || float.IsNaN(lifespan)) lifespan = FallbackLifespan;
        LifespanBySpecies[key] = lifespan;
        return lifespan;
    }

    public static float NativeDeathChance(int age, float lifespan)
    {
        if (lifespan <= 0f || float.IsNaN(lifespan) || age <= lifespan) return 0f;
        // Same age curve as Actor.checkNaturalDeath, sampled in the existing
        // annual genealogy queue rather than adding a per-frame census.
        return Math.Min(0.9f, (float)(1d / (1d + Math.Exp(-5d * ((age - lifespan) / lifespan - 0.5d)))));
    }
}

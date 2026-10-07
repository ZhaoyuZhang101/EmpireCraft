using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckHeir : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom kingdom)
    {
        if (kingdom?.data == null || EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(kingdom))
            return BehResult.Continue;
        if (!EmpireCraftKingdomBehCheckKing.NeedSuccession(kingdom))
        {
            kingdom.RecoverToDefaultHeir();
            return BehResult.Continue;
        }
        bool vacancy = kingdom.king == null || kingdom.king.isRekt() || !kingdom.king.isAlive();
        if (vacancy && kingdom.IsEmpire() && ConstitutionalSuccessionSystem.IsProtected(kingdom.GetEmpire()))
        {
            ConstitutionalSuccessionSystem.TryHandleVacancy(kingdom);
            return BehResult.Continue;
        }
        // 在位时从本继承法起点复查，不保留以前级联到旁系的结果。
        InstallHeir(kingdom, ResolveHeir(kingdom, vacancy));
        return BehResult.Continue;
    }

    private static void InstallHeir(Kingdom kingdom, (Actor actor, string relation) heir)
    {
        kingdom.RecoverToDefaultHeir();
        if (!IsFitCandidate(heir.actor, null))
        {
            kingdom.RemoveHeir();
            kingdom.StartToChooseHeir();
            return;
        }
        bool changed = kingdom.GetHeir() != heir.actor;
        kingdom.SetHeir(heir.actor);
        kingdom.ChooseHeirFinished();
        if (changed) TranslateHelper.LogKingChooseHeir(kingdom, heir.relation, heir.actor);
    }

    // 死亡、退位和真正的王位空缺才允许兄弟→孙辈→宗族→官员的原有兜底。
    public static void PrepareForSuccession(Kingdom kingdom, PersonalClanIdentity predecessor = null)
    {
        if (kingdom?.data == null || EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(kingdom) ||
            !EmpireCraftKingdomBehCheckKing.NeedSuccession(kingdom)) return;
        InstallHeir(kingdom, ResolveHeir(kingdom, true, predecessor));
    }

    public static PersonalClanIdentity PredecessorOf(Kingdom kingdom)
    {
        if (kingdom?.king != null)
        {
            kingdom.king.CheckSpecificClan(false);
            PersonalClanIdentity current = kingdom.king.GetPersonalIdentity();
            if (current != null) return current;
        }
        return kingdom == null ? null : SpecificClanManager.getPerson(kingdom.GetOrCreate().last_ruler_identity_id);
    }

    public static (Actor actor, string relation) ResolveHeir(Kingdom kingdom, bool allowFallback,
        PersonalClanIdentity predecessor = null)
    {
        if (kingdom?.data == null) return (null, "");
        PersonalClanIdentity parent = predecessor ?? PredecessorOf(kingdom);
        if (parent == null) return (null, "");
        SuccessionLawType law = kingdom.GetSuccessionLaw();
        if (law == SuccessionLawType.强者继承法)
            return CheckStrongestHeir(kingdom, parent, !allowFallback);
        EmpireHeirLawType start = KingdomExtension.ResolveHeirLawStart(law);
        // 兄终弟及仍在交接时按兄弟顺序继承，在位时不预立旁系。
        if (!allowFallback && start == EmpireHeirLawType.siblings) return (null, "");
        var result = CheckHeir(kingdom, start, law, parent);
        if (result.actor != null || !allowFallback) return result;
        foreach (EmpireHeirLawType step in new[] { EmpireHeirLawType.siblings, EmpireHeirLawType.grand_child_generation,
                     EmpireHeirLawType.random, EmpireHeirLawType.officer })
        {
            if (step == start || step == EmpireHeirLawType.officer && kingdom.IsEmpire()) continue;
            result = CheckHeir(kingdom, step, law, parent);
            if (result.actor != null) return result;
        }
        return (null, "");
    }

    private static List<PersonalClanIdentity> ChildrenOf(PersonalClanIdentity parent)
    {
        var children = new Dictionary<long, PersonalClanIdentity>();
        foreach (var pair in SpecificClanManager.getChildren(parent))
            if (pair.Item2 != null) children[pair.Item2.id] = pair.Item2;
        // 旧档可能有原版亲子关系，却没有完整的模组索引；只补实际在世单位的缺失链接。
        Actor actor = parent?._actor;
        if (actor != null)
            foreach (Actor child in actor.getChildren(pOnlyCurrentFamily: false))
            {
                if (child == null || child.isRekt() || !child.isAlive()) continue;
                child.CheckSpecificClan(false);
                PersonalClanIdentity identity = child.GetPersonalIdentity();
                if (identity == null) continue;
                if (!parent.children.Contains(identity.id)) identity.setParent(parent, recordHistory: false);
                foreach (Actor otherParent in child.getParents())
                {
                    if (otherParent == null || otherParent == actor) continue;
                    otherParent.CheckSpecificClan(false);
                    PersonalClanIdentity other = otherParent.GetPersonalIdentity();
                    if (other != null && (other.sex == ActorSex.Male ? identity.father <= 0 : identity.mother <= 0))
                        identity.setParent(other, recordHistory: false);
                }
                children[identity.id] = identity;
            }
        return children.Values.ToList();
    }

    private static bool IsEligibleRelative(PersonalClanIdentity person, PersonalClanIdentity parent, bool allowFemale) =>
        person != null && person.id != parent?.id && person.is_alive && !person.is_concubine &&
        person._specificClan != null && (person.CanHeir(parent) || allowFemale && person.sex == ActorSex.Female);

    private static bool IsFitCandidate(Actor actor, PersonalClanIdentity parent) =>
        actor != null && actor != parent?._actor && !actor.isRekt() && actor.isAlive() && actor.isUnitFitToRule();

    private static Actor Pick(Kingdom kingdom, IEnumerable<PersonalClanIdentity> candidates, PersonalClanIdentity parent,
        out PersonalClanIdentity selected)
    {
        bool protectedDynasty = kingdom.IsEmpire() && ConstitutionalSuccessionSystem.IsProtected(kingdom.GetEmpire());
        foreach (PersonalClanIdentity person in candidates)
        {
            Actor actor = person.Realize();
            if (!IsFitCandidate(actor, parent) || protectedDynasty && !ConstitutionalSuccessionSystem.CanInherit(kingdom.GetEmpire(), actor)) continue;
            selected = person;
            return actor;
        }
        selected = null;
        return null;
    }

    private static (Actor actor, string relation) CheckHeir(Kingdom kingdom, EmpireHeirLawType selection,
        SuccessionLawType law, PersonalClanIdentity parent)
    {
        bool allowFemale = PersonalUnionService.AllowsFemaleSuccession(kingdom);
        string relation = LM.Get(selection.ToString());
        IEnumerable<PersonalClanIdentity> candidates;
        switch (selection)
        {
            case EmpireHeirLawType.eldest_child:
            case EmpireHeirLawType.smallest_child:
                var children = ChildrenOf(parent).Where(person => IsEligibleRelative(person, parent, allowFemale));
                // 嫡子优先，嫡系没有合适儿子时仍在自己的子嗣中接续，不提前跳到叔伯。
                var ordered = children.OrderByDescending(person => person.IsHeirPriority())
                    .ThenByDescending(person => law == SuccessionLawType.嫡长子继承法 && IsBornOfPrimarySpouse(parent, person));
                candidates = selection == EmpireHeirLawType.smallest_child
                    ? ordered.ThenBy(person => person.age).ThenByDescending(person => person.rank).ThenBy(person => person.id)
                    : ordered.ThenByDescending(person => person.age).ThenBy(person => person.rank).ThenBy(person => person.id);
                Actor child = Pick(kingdom, candidates, parent, out PersonalClanIdentity selected);
                if (child != null)
                    relation = string.Format(LM.Get($"rank_child_{(kingdom.IsEmpire() ? "empire" : "kingdom")}_{(selected.sex == ActorSex.Female ? "female" : "male")}"), selected.rank);
                return (child, relation.ColorString(pColor: new Color(0.9f, 0.3f, 0.2f)));
            case EmpireHeirLawType.siblings:
                candidates = SpecificClanManager.GetSiblingsWithRelation(parent).Select(pair => pair.Item2)
                    .Where(person => IsEligibleRelative(person, parent, allowFemale))
                    .OrderByDescending(person => person.IsHeirPriority()).ThenByDescending(person => person.age).ThenBy(person => person.id);
                break;
            case EmpireHeirLawType.grand_child_generation:
                candidates = SpecificClanManager.GetGrandChildren(parent).Select(pair => pair.Item2)
                    .Where(person => IsEligibleRelative(person, parent, allowFemale))
                    .OrderByDescending(person => person.IsHeirPriority()).ThenByDescending(person => person.age).ThenBy(person => person.id);
                break;
            case EmpireHeirLawType.random:
                if (kingdom.IsEmpire() && ConstitutionalSuccessionSystem.IsProtected(kingdom.GetEmpire()))
                    return (ConstitutionalSuccessionSystem.SelectRoyalHeir(kingdom.GetEmpire()), ConstitutionalSuccessionSystem.SelectionRelation(kingdom.GetEmpire()));
                candidates = parent._specificClan?.SnapshotPeople() ?? Enumerable.Empty<PersonalClanIdentity>();
                candidates = candidates.Where(person => IsEligibleRelative(person, parent, allowFemale))
                    .OrderByDescending(person => person.age).ThenBy(person => person.id);
                break;
            case EmpireHeirLawType.officer:
                if (kingdom.IsEmpire()) return (null, relation);
                Actor officer = kingdom.cities.Select(city => city?.leader).FirstOrDefault(actor => IsFitCandidate(actor, parent));
                return (officer, relation);
            default: return (null, relation);
        }
        return (Pick(kingdom, candidates, parent, out _), relation.ColorString(pColor: new Color(0.3f, 0.3f, 0.9f)));
    }

    private static bool IsBornOfPrimarySpouse(PersonalClanIdentity parent, PersonalClanIdentity child)
    {
        long otherId = parent.sex == ActorSex.Male ? child.mother : child.father;
        if (otherId <= 0) return false;
        if (parent.lover.identity == otherId) return true;
        // 再婚不能把前任正妻所生的孩子全部降为非嫡。
        PersonalClanIdentity other = SpecificClanManager.getPerson(otherId);
        return other != null && !other.is_concubine && !parent.concubines.Any(pair => pair.identity == otherId);
    }

    private static (Actor actor, string relation) CheckStrongestHeir(Kingdom kingdom, PersonalClanIdentity parent, bool childrenOnly)
    {
        IEnumerable<PersonalClanIdentity> pool = ChildrenOf(parent);
        if (!childrenOnly) pool = pool.Concat(SpecificClanManager.GetSiblingsWithRelation(parent).Select(pair => pair.Item2));
        var candidates = pool.Where(person => IsEligibleRelative(person, parent, false))
            .OrderByDescending(person => person._actor?.GetIdentity()?.TotalPerformance ?? -1d)
            .ThenByDescending(person => person.age).ThenBy(person => person.id);
        return (Pick(kingdom, candidates, parent, out _),
            LM.Get(SuccessionLawType.强者继承法.ToString()).ColorString(pColor: new Color(0.6f, 0.05f, 0.05f)));
    }
}

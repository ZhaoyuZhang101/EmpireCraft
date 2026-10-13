using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
    private sealed class ChildRepairState { public float NextScan; }
    private static readonly ConditionalWeakTable<Actor, ChildRepairState> ChildRepairs = new();
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
        // 真正交接时不能受十秒修复间隔影响而漏掉异地子女、误选旁系。
        if (allowFallback && CityPopulationSystem.AbstractPopulationEnabled) ChildrenOf(parent, true);
        SuccessionLawType law = kingdom.GetSuccessionLaw();
        if (law == SuccessionLawType.强者继承法)
            return CheckStrongestHeir(kingdom, parent, !allowFallback);
        Actor designated = DesignatedHeir(kingdom, parent);
        if (designated != null)
            return (designated, LM.Get("designated_heir_relation").ColorString(pColor: new Color(0.9f, 0.3f, 0.2f)));
        EmpireHeirLawType start = KingdomExtension.ResolveHeirLawStart(law);
        // 兄终弟及仍在交接时按兄弟顺序继承，在位时不预立旁系。
        if (!allowFallback && start == EmpireHeirLawType.siblings) return (null, "");
        var result = CheckHeir(kingdom, start, law, parent);
        if (result.actor != null || !allowFallback) return result;
        // 帝国皇位：先君的孙辈仍是直系；子孙都没有、生前又未立储的，交群臣议立(见 SuccessionCouncilSystem)
        if (SuccessionCouncilSystem.Applies(kingdom))
        {
            if (start != EmpireHeirLawType.grand_child_generation)
            {
                result = CheckHeir(kingdom, EmpireHeirLawType.grand_child_generation, law, parent);
                if (result.actor != null) return result;
            }
            var council = SuccessionCouncilSystem.Resolve(kingdom, parent);
            if (council.handled) return (council.actor, council.relation);
        }
        foreach (EmpireHeirLawType step in new[] { EmpireHeirLawType.siblings, EmpireHeirLawType.grand_child_generation,
                     EmpireHeirLawType.random, EmpireHeirLawType.officer })
        {
            // 西方封建制(女性可继承)：本宗旁支之前先按血缘远近找外嫁宗亲的后代入继(见 RoyalMarriageSystem)
            if (step == EmpireHeirLawType.random && PersonalUnionService.AllowsFemaleSuccession(kingdom))
            {
                var cognate = CognaticHeir(kingdom, parent);
                if (cognate.actor != null) return cognate;
            }
            if (step == start || step == EmpireHeirLawType.officer && kingdom.IsEmpire()) continue;
            result = CheckHeir(kingdom, step, law, parent);
            if (result.actor != null) return result;
        }
        return (null, "");
    }

    private static List<PersonalClanIdentity> ChildrenOf(PersonalClanIdentity parent, bool forceRepair = false)
    {
        var children = new Dictionary<long, PersonalClanIdentity>();
        foreach (var pair in SpecificClanManager.getChildren(parent))
            if (pair.Item2 != null) children[pair.Item2.id] = pair.Item2;
        // 旧档可能有原版亲子关系，却没有完整的模组索引；只补实际在世单位的缺失链接。
        Actor actor = parent?._actor;
        if (actor != null)
        {
            bool virtualPopulation = CityPopulationSystem.AbstractPopulationEnabled;
            IEnumerable<Actor> nativeChildren = virtualPopulation ? actor.getChildren() :
                actor.getChildren(pOnlyCurrentFamily: false);
            int knownLiving = children.Values.Count(person => person._actor != null && !person._actor.isRekt() && person._actor.isAlive());
            if (virtualPopulation && (forceRepair || knownLiving < actor.current_children_count))
            {
                ChildRepairState repair = ChildRepairs.GetValue(actor, _ => new ChildRepairState());
                if (forceRepair || Time.unscaledTime >= repair.NextScan)
                {
                    repair.NextScan = Time.unscaledTime + 10f;
                    nativeChildren = nativeChildren.Concat(actor.getChildren(pOnlyCurrentFamily: false));
                }
            }
            foreach (Actor child in nativeChildren)
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
        }
        return children.Values.ToList();
    }

    private static bool IsEligibleRelative(PersonalClanIdentity person, PersonalClanIdentity parent, bool allowFemale) =>
        person != null && person.id != parent?.id && person.is_alive && !person.is_concubine &&
        person._specificClan != null && (person.CanHeir(parent) || allowFemale && person.sex == ActorSex.Female);

    // 钦定的储君：在世、可继位、仍是先君子嗣(新君即位后自然失效)
    private static Actor DesignatedHeir(Kingdom kingdom, PersonalClanIdentity parent)
    {
        var data = kingdom.GetOrCreate();
        if (data.designated_heir_id < 0) return null;
        Actor actor = World.world.units.get(data.designated_heir_id);
        actor?.CheckSpecificClan(false);
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        bool valid = identity != null && actor != kingdom.king && IsFitCandidate(actor, parent) &&
                     ChildrenOf(parent).Any(child => child.id == identity.id) &&
                     (!kingdom.IsEmpire() || !ConstitutionalSuccessionSystem.IsProtected(kingdom.GetEmpire()) ||
                      ConstitutionalSuccessionSystem.CanInherit(kingdom.GetEmpire(), actor));
        if (!valid) data.designated_heir_id = -1L;
        return valid ? actor : null;
    }

    // 册立储君的候选：先君(在位皇帝)在世、可继位的子嗣
    public static List<Actor> HeirCandidates(Kingdom kingdom)
    {
        PersonalClanIdentity parent = PredecessorOf(kingdom);
        if (parent == null) return new List<Actor>();
        return ChildrenOf(parent).Select(person => person._actor)
            .Where(actor => IsFitCandidate(actor, parent) && actor != kingdom.king).Distinct().ToList();
    }

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
                // 嫡长子继承：嫡长子早逝而身后有子，嫡长孙承重，先于诸叔
                if (selection == EmpireHeirLawType.eldest_child && law == SuccessionLawType.嫡长子继承法)
                {
                    PersonalClanIdentity first = ChildrenOf(parent).Where(person => person.IsHeirPriority() && !person.is_concubine)
                        .OrderByDescending(person => IsBornOfPrimarySpouse(parent, person)).ThenBy(BirthKey).FirstOrDefault();
                    if (first != null && !first.is_alive)
                    {
                        var grandsons = ChildrenOf(first).Where(person => IsEligibleRelative(person, first, allowFemale))
                            .OrderByDescending(person => person.IsHeirPriority())
                            .ThenByDescending(person => IsBornOfPrimarySpouse(first, person)).ThenBy(BirthKey);
                        Actor grandson = Pick(kingdom, grandsons, parent, out _);
                        if (grandson != null)
                            return (grandson, LM.Get("heir_relation_eldest_grandson").ColorString(pColor: new Color(0.9f, 0.3f, 0.2f)));
                    }
                }
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
                // 绝嗣：从宗室里按亲疏找人入继(隔几代找到共同的祖先)，同样亲疏的先立成年的、三十岁上下的
                // (帝国皇位另由群臣议立，见 SuccessionCouncilSystem)
                candidates = parent._specificClan?.SnapshotPeople() ?? Enumerable.Empty<PersonalClanIdentity>();
                Dictionary<long, int> ancestors = Ancestors(parent);
                candidates = candidates.Where(person => IsEligibleRelative(person, parent, allowFemale))
                    .OrderBy(person => Kinship(person, ancestors)).ThenByDescending(person => person.age >= 18)
                    .ThenBy(person => Math.Abs(person.age - 30)).ThenBy(person => person.id);
                relation = LM.Get("heir_relation_collateral");
                break;
            case EmpireHeirLawType.officer:
                if (kingdom.IsEmpire()) return (null, relation);
                Actor officer = kingdom.cities.Select(city => city?.leader).FirstOrDefault(actor => IsFitCandidate(actor, parent));
                return (officer, relation);
            default: return (null, relation);
        }
        return (Pick(kingdom, candidates, parent, out _), relation.ColorString(pColor: new Color(0.3f, 0.3f, 0.9f)));
    }

    // 外家入继：沿父母、子女找先君六步以内的血亲(含外嫁女子的后代)，近的在前；入继者本是别国君主的两国共主
    private static (Actor actor, string relation) CognaticHeir(Kingdom kingdom, PersonalClanIdentity parent)
    {
        bool protectedDynasty = kingdom.IsEmpire() && ConstitutionalSuccessionSystem.IsProtected(kingdom.GetEmpire());
        int realized = 0;
        foreach ((PersonalClanIdentity person, int _) in RoyalMarriageSystem.Cognates(parent, 6))
        {
            if (person.is_virtual && realized >= 6) continue;
            if (person.is_virtual) realized++;
            Actor actor = person.Realize();
            if (!IsFitCandidate(actor, parent) || actor.IsEmperor() ||
                protectedDynasty && !ConstitutionalSuccessionSystem.CanInherit(kingdom.GetEmpire(), actor)) continue;
            return (actor, LM.Get("heir_relation_cognatic").ColorString(pColor: new Color(0.3f, 0.6f, 0.9f)));
        }
        return (null, "");
    }

    // 议立新君的宗室人选池：先君以外、可以继位的宗亲(兄弟、叔侄、族人)，附亲疏(隔几代)
    public static List<(PersonalClanIdentity person, int kinship)> CollateralPool(Kingdom kingdom, PersonalClanIdentity parent)
    {
        var result = new List<(PersonalClanIdentity, int)>();
        if (kingdom == null || parent == null) return result;
        bool allowFemale = PersonalUnionService.AllowsFemaleSuccession(kingdom);
        Dictionary<long, int> ancestors = Ancestors(parent);
        var people = new Dictionary<long, PersonalClanIdentity>();
        foreach (var pair in SpecificClanManager.GetSiblingsWithRelation(parent))
            if (pair.Item2 != null) people[pair.Item2.id] = pair.Item2;
        IEnumerable<PersonalClanIdentity> clan = parent._specificClan?.SnapshotPeople() ?? Enumerable.Empty<PersonalClanIdentity>();
        SpecificClan royal = kingdom.IsEmpire() ? kingdom.GetEmpire()?.EmpireSpecificClan : null;
        if (royal != null && royal != parent._specificClan) clan = clan.Concat(royal.SnapshotPeople());
        foreach (PersonalClanIdentity person in clan)
            if (person != null) people[person.id] = person;
        foreach (PersonalClanIdentity person in people.Values)
            if (IsEligibleRelative(person, parent, allowFemale) && person._actor?.IsEmperor() != true)
                result.Add((person, Kinship(person, ancestors)));
        return result;
    }

    // 出生先后(生日"11,062年5月20日")；解析不了按编号
    private static long BirthKey(PersonalClanIdentity person)
    {
        var match = global::System.Text.RegularExpressions.Regex.Match(person?.birthday ?? "", @"([\d,]+)年(\d+)月(\d+)日");
        if (!match.Success || !long.TryParse(match.Groups[1].Value.Replace(",", ""), out long year)) return long.MaxValue / 2 + (person?.id ?? 0) % 1000000;
        return year * 10000 + int.Parse(match.Groups[2].Value) * 100 + int.Parse(match.Groups[3].Value);
    }

    // 往上八代的祖先 → 隔几代
    private static Dictionary<long, int> Ancestors(PersonalClanIdentity person)
    {
        var result = new Dictionary<long, int>();
        PersonalClanIdentity current = person;
        for (int generation = 0; current != null && generation <= 8; generation++)
        {
            result[current.id] = generation;
            current = current.father > 0 ? SpecificClanManager.getPerson(current.father) : null;
        }
        return result;
    }

    // 亲疏：两人各自往上几代到共同的祖先，合起来的代数(找不到算很远)
    private static int Kinship(PersonalClanIdentity person, Dictionary<long, int> ancestors)
    {
        PersonalClanIdentity current = person;
        for (int up = 0; current != null && up <= 8; up++)
        {
            if (ancestors.TryGetValue(current.id, out int down)) return up + down;
            current = current.father > 0 ? SpecificClanManager.getPerson(current.father) : null;
        }
        return 99;
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

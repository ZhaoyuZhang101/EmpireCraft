using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckHeir : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        if (!EmpireCraftKingdomBehCheckKing.NeedSuccession(pKingdom) || (pKingdom.HasHeir()&&!pKingdom.IsNeedToChooseHeir()))
        {
            pKingdom.RecoverToDefaultHeir();
            return BehResult.Continue;
        }

        var successionLaw = pKingdom.GetSuccessionLaw();
        var heir = successionLaw == SuccessionLawType.强者继承法
            ? CheckStrongestHeir(pKingdom)
            : CheckHeir(pKingdom, pKingdom.GetHeirLaw(), successionLaw);
        if (heir.actor.isRekt()||!heir.actor.isUnitFitToRule())
        {
            pKingdom.GoToNextHeirLaw();
        }
        else
        {
            pKingdom.SetHeir(heir.actor);
            TranslateHelper.LogKingChooseHeir(pKingdom, heir.relation, heir.actor);
            pKingdom.RecoverToDefaultHeir();
            pKingdom.ChooseHeirFinished();
        }
        
        return BehResult.Continue;
    }
    private static (Actor actor, string relation) CheckHeir(Kingdom k, EmpireHeirLawType secondSelection=EmpireHeirLawType.eldest_child, SuccessionLawType successionLaw = SuccessionLawType.嫡长子继承法, PersonalClanIdentity pActor = null)
    {
        if (k == null) return (null, "");
        Actor actor = null;
        var flag = k.IsEmpire();
        var logPreText = flag ? "empire" : "kingdom";
        PersonalClanIdentity pci = pActor??k.king?.GetPersonalIdentity();
        List<(ClanRelation, PersonalClanIdentity)> children = SpecificClanManager.getChildren(pci).FindAll(a=>a.Item2.CanHeir(pci));
        // 西方封建制允许女性继承(仍男性优先)：没有符合男女优先的子女/兄弟时，放宽到所有在世子女/兄弟姐妹，
        // 已外嫁的女儿也算——联姻继承多国正是靠这一点
        bool allowFemale = PersonalUnionService.AllowsFemaleSuccession(k);
        if (allowFemale && !children.Any())
            children = SpecificClanManager.getChildren(pci).FindAll(a => a.Item2 != null && a.Item2.is_alive && a.Item2.id != pci.id);
        var relationText = secondSelection.ToString();
        switch (secondSelection)
        {
            case EmpireHeirLawType.eldest_child:
                // 嫡长子继承法收紧为必须是儿子且出自正妻(非侍妾),失败时留空以进入级联回退
                var eligibleChildren = successionLaw == SuccessionLawType.嫡长子继承法
                    ? children.FindAll(c => c.Item2.sex == ActorSex.Male && IsBornOfPrimarySpouse(pci, c.Item2))
                    : children;
                if (allowFemale && !eligibleChildren.Any() && successionLaw == SuccessionLawType.嫡长子继承法)
                    eligibleChildren = children.FindAll(c => IsBornOfPrimarySpouse(pci, c.Item2));
                if (eligibleChildren.Any())
                {
                    var actor_pci = eligibleChildren.First().Item2; // Assuming eldest is the last after sorting by age
                    actor = actor_pci._actor;
                    relationText =
                        string.Format(
                            LM.Get($"rank_child_{logPreText}_{(actor_pci.sex == ActorSex.Female ? "female" : "male")}"),
                            actor_pci.rank).ColorString(pColor:new Color(0.9f, 0.3f, 0.2f));
                }
                break;
            case EmpireHeirLawType.smallest_child:
                if (children.Any())
                {
                    var actor_pci = children.Last().Item2;
                    actor = actor_pci._actor; // Assuming youngest is the first after sorting by age
                    relationText =
                        string.Format(
                            LM.Get(
                                $"rank_child_{logPreText}_{(actor_pci.sex == ActorSex.Female ? "female" : "male")}"),
                            actor_pci.rank).ColorString(pColor: new Color(0.5f, 0.1f, 0.7f));
                }
                break;
            case EmpireHeirLawType.siblings:
                // Logic for selecting a brother heir can be added here
                List<(ClanRelation, PersonalClanIdentity)> brothers = SpecificClanManager.GetSiblingsWithRelation(pci).FindAll(a=>a.Item2.CanHeir(pci));
                if (allowFemale && !brothers.Any())
                    brothers = SpecificClanManager.GetSiblingsWithRelation(pci)
                        .FindAll(a => a.Item2 != null && a.Item2.is_alive && a.Item2.id != pci.id);
                brothers.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>
                    .Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
                if (brothers.Any())
                {
                    actor = brothers.Last().Item2._actor;
                    relationText = LM.Get(relationText).ColorString(pColor:new Color(0.2f, 0.3f, 0.9f));
                }
                break;
            case EmpireHeirLawType.grand_child_generation:
                List<(ClanRelation, PersonalClanIdentity)> grandChildren = SpecificClanManager.GetGrandChildren(pci);
                grandChildren = grandChildren.FindAll(c=>c.Item2.CanHeir(pci));
                grandChildren.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>
                    .Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
                if (grandChildren.Any())
                {
                    actor = grandChildren.Last().Item2._actor;
                    relationText = LM.Get(relationText).ColorString(pColor:new Color(0.9f, 0.1f, 0.9f));
                }
                break;
            case EmpireHeirLawType.random:
                List<Actor> randomClanMember = pci?._specificClan?.AllAliveMembers??new List<Actor>();
                randomClanMember = randomClanMember.FindAll(c=>c.GetPersonalIdentity()?.CanHeir(pci)??false).OrderByDescending(a=>a.age).ToList();
                if (randomClanMember.Any())
                {
                    actor = randomClanMember.First();
                    relationText = LM.Get(relationText).ColorString(pColor:new Color(0.9f, 0.6f, 0.9f));
                }
                break;
            case EmpireHeirLawType.officer:
                if (flag)
                {
                    Empire empire = k.GetEmpire();
                    List<long> officeIDs = new List<long>();
                    officeIDs.AddRange(empire.data.centerOffice.CoreOffices);
                    officeIDs.AddRange(empire.data.centerOffice.Divisions);
                    officeIDs.AddRange(empire.kingdoms_list?.ToList().Select(pKingdom=>pKingdom.GetOfficeID()) ?? Array.Empty<long>());
                    officeIDs.Add(k.capital.GetOfficeID());
                    var office = officeIDs.Select(id=>OfficeManager.Offices.TryGetValue(id, out var value)?value:null).ToList().Find(o=>o!=null&&o.GetActor()!=null);
                    actor = office.GetActor();
                    var officeName = office.GetName();
                    relationText = LM.Get(officeName).ColorString(pColor:new Color(1.0f, 1.0f, 1.0f));
                }
                else
                {
                    if (k.cities.Any())
                    {
                        actor = k.cities.ToList().Find(c => c?.hasLeader()??false)?.leader;
                        relationText = LM.Get(relationText).ColorString(pColor:new Color(1.0f, 1.0f, 1.0f));
                    }
                }
                break;
        }
        return (actor, relationText);
    }

    private static bool IsBornOfPrimarySpouse(PersonalClanIdentity parent, PersonalClanIdentity child)
    {
        if (parent == null || child == null) return false;
        long otherParentIdentityId = parent.sex == ActorSex.Male ? child.mother : child.father;
        if (otherParentIdentityId <= 0) return false;
        return parent.lover.identity == otherParentIdentityId;
    }

    //强者继承法不走 EmpireHeirLawType 级联,在子嗣与兄弟候选池里按绩效值(沿用内阁选拔同款指标)选最强者
    private static (Actor actor, string relation) CheckStrongestHeir(Kingdom k)
    {
        if (k == null) return (null, "");
        PersonalClanIdentity pci = k.king?.GetPersonalIdentity();
        List<PersonalClanIdentity> candidates = SpecificClanManager.getChildren(pci).FindAll(a => a.Item2.CanHeir(pci))
            .Concat(SpecificClanManager.GetSiblingsWithRelation(pci).FindAll(a => a.Item2.CanHeir(pci)))
            .Select(a => a.Item2)
            .Where(identity => identity?._actor != null)
            .ToList();
        if (!candidates.Any()) return (null, "");
        PersonalClanIdentity strongest = candidates
            .OrderByDescending(identity => identity._actor.GetIdentity()?.TotalPerformance ?? 0d)
            .First();
        string relationText = LM.Get(SuccessionLawType.强者继承法.ToString()).ColorString(pColor: new Color(0.6f, 0.05f, 0.05f));
        return (strongest._actor, relationText);
    }
}
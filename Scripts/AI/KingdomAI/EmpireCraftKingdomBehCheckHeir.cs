using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
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
            return BehResult.Continue;
        }

        var heir = CheckHeir(pKingdom, pKingdom.GetHeirLaw());
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
    private static (Actor actor, string relation) CheckHeir(Kingdom k, EmpireHeirLawType secondSelection=EmpireHeirLawType.eldest_child, PersonalClanIdentity pActor = null)
    {
        if (k == null) return (null, "");
        Actor actor = null;
        var flag = k.IsEmpire();
        var logPreText = flag ? "Empire: " : "Kingdom: ";
        PersonalClanIdentity pci = pActor??k.king?.GetPersonalIdentity();
        List<(ClanRelation, PersonalClanIdentity)> children = SpecificClanManager.getChildren(pci).FindAll(a=>a.Item2.CanHeir(pci));
        var relationText = secondSelection.ToString();
        switch (secondSelection)
        {
            case EmpireHeirLawType.eldest_child:
                if (children.Any())
                {
                    actor = children.First().Item2._actor; // Assuming eldest is the last after sorting by age
                    relationText = LM.Get(relationText).ColorString(pColor:new Color(0.9f, 0.3f, 0.2f));
                }
                break;
            case EmpireHeirLawType.smallest_child:
                if (children.Any())
                {
                    actor = children.Last().Item2._actor; // Assuming youngest is the first after sorting by age
                    relationText = LM.Get(relationText).ColorString(pColor:new Color(0.5f, 0.1f, 0.7f));
                }
                break;
            case EmpireHeirLawType.siblings:
                // Logic for selecting a brother heir can be added here
                List<(ClanRelation, PersonalClanIdentity)> brothers = SpecificClanManager.GetSiblingsWithRelation(pci).FindAll(a=>a.Item2.CanHeir(pci));
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
}
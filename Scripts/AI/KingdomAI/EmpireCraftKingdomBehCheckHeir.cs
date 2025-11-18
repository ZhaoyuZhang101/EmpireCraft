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
using UnityEngine;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckHeir : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        Regime regime = pKingdom.GetRegime();
        if (((!pKingdom.IsEmpire()&&regime.GetLeaderSelectMethod()!=LeaderSelectMethod.Succession)||(pKingdom.IsEmpire()&&regime.leader_select_method != LeaderSelectMethod.Succession))| pKingdom.HasHeir()&&!pKingdom.IsNeedToChooseHeir())
        {
            return BehResult.Continue;
        }

        if (pKingdom.CalcHeirFinished())
        {
            pKingdom.SetCalcHeirTask(ScheduleCalcHeirAsync(pKingdom));
            return BehResult.Continue;
        }
        
        if (!pKingdom.GetCalcHeirTask().IsCompleted)
            return BehResult.Continue;
        
        try
        {
            var (actor, relation) = pKingdom.GetCalcHeirTask().Result;
            if (actor != null)
            {
                pKingdom.ChooseHeirFinished();
                if (pKingdom.GetHeir() == actor)
                {
                    return BehResult.Continue;
                }

                if (pKingdom.king == actor)
                {
                    return BehResult.Continue;
                }
                if (!actor.isUnitFitToRule()) return BehResult.Continue;
                pKingdom.SetHeir(actor);
                // 这时肯定在主线程里，UI 调用安全
                TranslateHelper.LogKingChooseHeir(pKingdom, relation, actor);
            }
        }
        finally
        {
            pKingdom.RemoveCalcHeirStatus();
        }
        return BehResult.Continue;
    }
    public static Task<(Actor actor, string relation)> CheckHeirAsync(Kingdom k, EmpireHeirLawType? secondSelection = null)
    {
        // 如果 CheckHeir 本身是 CPU 密集型，就用 Task.Run 包裹
        return Task.Run(() => secondSelection == null
            ? CheckHeir(k)
            : CheckHeir(k, secondSelection: secondSelection.Value));
    }

    public static async Task<(Actor actor, string relation)> ScheduleCalcHeirAsync(Kingdom k)
    {
        // 并发限流
        await KingdomExtension._sem.WaitAsync().ConfigureAwait(false);
        try
        {
            // 按优先级把所有策略打包成 Func<Task<…>>
            var strategies = new Func<Task<(Actor actor, string relation)>>[]
            {
                () => CheckHeirAsync(k),                                           // 默认策略
                () => CheckHeirAsync(k,EmpireHeirLawType.siblings),                 // 兄弟优先
                () => CheckHeirAsync(k,EmpireHeirLawType.grand_child_generation),   // 孙辈
                () => CheckHeirAsync(k,EmpireHeirLawType.random),                   // 随机
                () => CheckHeirAsync(k,EmpireHeirLawType.officer),                  // 军官
            };

            // 依次跑，每次 await 完看 actor，是不是非 null，就立刻返回
            foreach (var strat in strategies)
            {
                var result = await strat().ConfigureAwait(false);
                if (result.actor != null)
                    return result;
            }

            // 都没找到
            return (null, null);
        }
        finally
        {
            KingdomExtension._sem.Release();
        }
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
                List<(ClanRelation, PersonalClanIdentity)> randomClanMember = SpecificClanManager.FindAllRelations(pci);
                randomClanMember = randomClanMember.FindAll(c=>c.Item2.CanHeir(pci));
                randomClanMember.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>
                    .Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
                if (randomClanMember.Any())
                {
                    actor = randomClanMember.Last().Item2._actor;
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
                    var actorID = officeIDs.Select(id=>OfficeManager.Offices.TryGetValue(id, out var value)?value.actor_id:-1L).ToList().Find(aid=>aid != -1L);
                    actor = world.units.get(actorID);
                    OfficeIdentity identity = actor?.GetIdentity();
                    var officeName = string.Join("_", actor?.GetPersonalIdentity()?.culture, identity?.officialLevel);
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
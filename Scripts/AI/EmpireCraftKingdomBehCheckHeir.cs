using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.AI;
public class EmpireCraftKingdomBehCheckHeir: BehaviourActionKingdom
{
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.isEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (empire.Heir==null)
            {
                if (empire.emperor!=null)
                {
                    Actor son = CheckHeir(empire);
                    if (son != null)
                    {
                        empire.Heir = son;
                        LogService.LogInfo($"当前皇室继承人:{son.data.name}");
                    }
                }
            }
            return BehResult.Continue;
        }
        return BehResult.Continue;
    }

    public Actor CheckHeir(Empire empire)
    {
        Actor actor = null;
        EmpireHeirLawType type = empire.data.heir_type;
        List<Actor> children = empire.emperor.getChildren().ToList();
        if (children.Count <= 0) return actor;
        children.Sort(Comparer<Actor>.Create((a, b) => a.getAge().CompareTo(b.getAge())));
        switch (type)
        {
            case EmpireHeirLawType.eldest_son:
                if (children.Count > 0)
                {
                    actor = children.Last(); // Assuming eldest is the last after sorting by age
                }
                break;
            case EmpireHeirLawType.smallest_son:
                if (children.Count > 0)
                {
                    actor = children.First(); // Assuming youngest is the first after sorting by age
                }
                break;
            case EmpireHeirLawType.brother:
                // Logic for selecting a brother heir can be added here
                break;
        }
        return actor;
    }
}

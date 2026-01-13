using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GameLibrary;

public class LegalProperty
{
    public string id;
    public LegalDelegateCalc calc;
}

public delegate int LegalDelegateCalc(Empire empire);

public static class EmpireCraftLegalityLibrary
{
    public static Dictionary<string, LegalProperty> properties = new();
    public static int GetSum(Empire empire)
    {
        return properties.Select(p => p.Value.calc(empire)).Sum();
    }

    public static void Add(LegalProperty property)
    {
        properties[property.id] = property;
    }
    
    public static void init()
    {
        Add(new LegalProperty()
        {
            id = "顺位继承",
            calc = delegate(Empire empire)
            {
                return 20;
            }
        });
    }
}

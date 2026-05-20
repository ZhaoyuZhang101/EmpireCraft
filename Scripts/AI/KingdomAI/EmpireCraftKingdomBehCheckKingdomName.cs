using System;
using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckKingdomName:GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        if (!pKingdom.isRekt())
        {
            if (pKingdom.isEmpire()) return BehResult.Continue;

            if (pKingdom.HasTitle())
            {
                string culture = ConfigData.speciesCulturePair.TryGetValue(pKingdom.getSpecies(), out var a) ? a : "Western";
                string kingdomBack = LM.Get($"{culture}_" + pKingdom.GetCountryLevel());
                pKingdom.data.name = String.Join("\u200A", pKingdom.GetMainTitle().name, kingdomBack);
            }
        }
        return BehResult.Continue;
    }
}
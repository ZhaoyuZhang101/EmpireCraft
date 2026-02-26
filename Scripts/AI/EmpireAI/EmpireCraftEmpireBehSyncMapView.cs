using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI.EmpireAI;

public class EmpireCraftEmpireBehSyncMapView:GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Stop;
        Empire empire = pKingdom.GetEmpire();
        if (empire == null) return BehResult.Continue;
        empire.cities_list = empire.kingdoms_list.SelectMany(k => k.cities).ToList();
        return BehResult.Continue;
    }
}
using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckLaw : GameAICityBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(City pCity)
    {
        if (pCity == null || pCity.isRekt() || !pCity.hasKingdom())
        {
            return BehResult.Continue;
        }

        if (!pCity.IsLawScanDue(1f))
        {
            return BehResult.Continue;
        }

        pCity.RecordLawScan();
        EmpireLawSystem.CheckAutomaticLawTriggersForCity(pCity);
        return BehResult.Continue;
    }
}

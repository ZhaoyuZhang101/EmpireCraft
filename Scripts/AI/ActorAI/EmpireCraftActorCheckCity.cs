using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckCity:GameAIActorBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Actor pActor)
    {
        if (!pActor.isUnitFitToRule()) return BehResult.Continue;
        if (pActor.kingdom.isNeutral()) return BehResult.Continue;
        if (!EmpireCraftWorldLawLibrary.empirecraft_law_prevent_city_destroy.isEnabled()) return BehResult.Continue;
        if (pActor.current_zone.hasCity())
        {
            City city = pActor.current_zone.city;
            if (pActor.hasKingdom() && city != null && city.getPopulationPeople()<=0)
            {
                LogService.LogInfo("开始移民空城市");
                Kingdom kingdom = pActor.kingdom;
                city.joinAnotherKingdom(kingdom);
                if (pActor.hasFamily())
                {
                    foreach (var fActor in pActor.family.units)
                    {
                        fActor.joinCity(city);
                        fActor.goTo(city._city_tile);
                    }
                }
                else
                {
                    pActor.joinCity(city);
                    pActor.goTo(city._city_tile);
                }
            }
        }
        return BehResult.Continue;
    }
}
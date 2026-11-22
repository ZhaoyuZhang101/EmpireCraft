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
                Kingdom kingdom = pActor.kingdom;
                // 空城换主只在无主、或两国正在交战时(群友 2026-11-22 "城市永不毁灭好像出了点问题")：以前不看城是谁的，
                // 任何路过的人都把别国(盟友、同一帝国的也算)的空城划进本国，太平年月城就这样易了主。本国的空城照旧迁进去住
                Kingdom owner = city.kingdom;
                if (owner != kingdom)
                {
                    if (owner != null && !owner.isNeutral() && !owner.wild && !kingdom.isInWarWith(owner)) return BehResult.Continue;
                    city.joinAnotherKingdom(kingdom);
                }
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

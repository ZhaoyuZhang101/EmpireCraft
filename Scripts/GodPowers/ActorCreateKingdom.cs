using EmpireCraft.Scripts.GameClassExtensions;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class ActorCreateKingdom
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "actor_create_kingdom",
            name = "actor_create_kingdom",
            click_action = kingdom_create_action
        });
    }

    private static bool kingdom_create_action(WorldTile pTile, string pPower)
    {
        Actor actor = null;
        actor = ((pTile != null) ? getActorFromTile(pTile) : World.world.getActorNearCursor());
        if (actor == null)
        {
            return false;
        }
        Kingdom obj = World.world.kingdoms.makeNewCivKingdom(actor);
        City city = World.world.cities.buildFirstCivilizationCity(actor);
        actor.createDefaultCultureAndLanguageAndClan(city.name);
        obj.setUnitMetas(actor);
        city.setUnitMetas(actor);
        return true;
    }

    public static Actor getActorFromTile(WorldTile pTile = null)
    {
        if (pTile == null)
        {
            return null;
        }
        Actor result = null;
        float num = float.MaxValue;
        List<Actor> simpleList = World.world.units.getSimpleList();
        for (int i = 0; i < simpleList.Count; i++)
        {
            Actor actor = simpleList[i];
            if (actor.isAlive() && actor.asset.can_be_inspected && !actor.isInsideSomething())
            {
                float num2 = Toolbox.DistTile(actor.current_tile, pTile);
                if (!(num2 > 3f) && num2 < num)
                {
                    result = actor;
                    num = num2;
                }
            }
        }
        return result;
    }
}

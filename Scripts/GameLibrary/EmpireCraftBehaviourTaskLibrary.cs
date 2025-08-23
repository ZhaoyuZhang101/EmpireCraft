using ai.behaviours;
using EmpireCraft.Scripts.AI;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftBehaviourTaskLibrary
{
    public static void init()
    {
        LogService.LogInfo("初始化帝国模组角色逻辑");
        BehaviourTaskActor obj = new BehaviourTaskActor()
        {
            id = "do_mod_actor_beh"
        };
        obj.ActorAIs(AssetManager.tasks_actor);
        obj.addBeh(new BehRandomWait(pMax: 1f));
        AssetManager.tasks_actor.add(obj);
        
        LogService.LogInfo("初始化帝国模组城市逻辑");
        BehaviourTaskCity obj2 = new BehaviourTaskCity()
        {
            id = "do_mod_city_beh"
        };
        obj2.CityAIs(AssetManager.tasks_city);
        obj2.addBeh(new CityBehRandomWait(0.1f));
        AssetManager.tasks_city.add(obj2);
        
        LogService.LogInfo("初始化帝国模组王国逻辑");
        BehaviourTaskKingdom obj3 = new BehaviourTaskKingdom
        {
            id = "do_mod_kingdom_beh"
        };
        obj3.KingdomAIs(AssetManager.tasks_kingdom);
        obj3.addBeh(new KingdomBehRandomWait(0.1f));
        AssetManager.tasks_kingdom.add(obj3);
    }
}
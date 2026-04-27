using ai.behaviours;
using EmpireCraft.Scripts.AI;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftBehaviourTaskLibrary
{
    public static void init()
    {
        //角色
        BehaviourTaskActor obj = new BehaviourTaskActor()
        {
            id = "do_mod_actor_beh",
        };
        obj.ActorAIs(AssetManager.tasks_actor);
        obj.addBeh(new BehRandomWait(0.3f));
        AssetManager.tasks_actor.add(obj);
        AssetManager.job_actor.t.addTask("do_mod_actor_beh");
        
        //城市
        BehaviourTaskCity obj2 = new BehaviourTaskCity()
        {
            id = "do_mod_city_beh"
        };
        obj2.CityAIs(AssetManager.tasks_city);
        obj2.addBeh(new CityBehRandomWait(1f));
        AssetManager.tasks_city.add(obj2);
        AssetManager.job_city.t.addTask("do_mod_city_beh");
        
        //国家
        BehaviourTaskKingdom obj3 = new BehaviourTaskKingdom
        {
            id = "do_mod_kingdom_beh"
        };
        obj3.KingdomAIs(AssetManager.tasks_kingdom);
        obj3.addBeh(new KingdomBehRandomWait(1f));
        AssetManager.tasks_kingdom.add(obj3);
        AssetManager.job_kingdom.t.addTask("do_mod_kingdom_beh");
        
        //国家2
        BehaviourTaskKingdom obj4 = new BehaviourTaskKingdom
        {
            id = "do_mod_kingdom_mind_beh"
        };
        obj4.KingdomMindAIs(AssetManager.tasks_kingdom);
        AssetManager.tasks_kingdom.add(obj4);
        AssetManager.job_kingdom.t.addTask("do_mod_kingdom_mind_beh");
        
        //国家2
        BehaviourTaskKingdom obj5 = new BehaviourTaskKingdom
        {
            id = "do_mod_empire_beh"
        };
        obj5.EmpireAIs(AssetManager.tasks_kingdom);
        AssetManager.tasks_kingdom.add(obj5);
        AssetManager.job_kingdom.t.addTask("do_mod_empire_beh");
    }
}

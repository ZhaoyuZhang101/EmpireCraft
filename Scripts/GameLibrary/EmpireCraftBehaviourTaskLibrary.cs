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
        GameAIMain.ActorAIs(AssetManager.tasks_actor);
        
        //城市
        GameAIMain.CityAIs(AssetManager.tasks_city);
        
        //国家
        GameAIMain.KingdomAIs(AssetManager.tasks_kingdom);
        
        //国家2
        GameAIMain.KingdomMindAIs(AssetManager.tasks_kingdom);
        
        //国家2
        GameAIMain.EmpireAIs(AssetManager.tasks_kingdom);
    }
}

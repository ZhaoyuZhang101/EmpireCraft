using ai.behaviours;
using EmpireCraft.Scripts.AI;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftBehaviourTaskLibrary
{
    public static void init()
    {
        AssetManager._instance._dict.Remove("beh_kingdom");
        AssetManager._instance.add(AssetManager.tasks_kingdom = new ModBehaviourTaskKingdomLibrary(), "beh_kingdom");

        AssetManager._instance._dict.Remove("beh_city");
        AssetManager._instance.add(AssetManager.tasks_city = new ModBehaviourTaskCityLibrary(), "beh_city");
        
        AssetManager._instance._dict.Remove("beh_actor");
        AssetManager._instance.add(AssetManager.tasks_actor = new ModBehaviourTaskActorLibrary(), "beh_actor");
        LogService.LogInfo("覆盖原版的AI逻辑");
    }
}
public class ModBehaviourTaskKingdomLibrary : BehaviourTaskKingdomLibrary
{
    public override void init()
    {
        base.init();
        LogService.LogInfo("初始化帝国模组王国逻辑");
        BehaviourTaskKingdom obj = new BehaviourTaskKingdom
        {
            id = "do_mod_kingdom_beh"
        };
        t = obj;
        t.KingdomAIs(this);
        t.addBeh(new KingdomBehRandomWait(0.1f));
        add(obj);
    }
}

public class ModBehaviourTaskCityLibrary : BehaviourTaskCityLibrary
{
    public override void init()
    {
        base.init();
        LogService.LogInfo("初始化帝国模组城市逻辑");
        BehaviourTaskCity obj = new BehaviourTaskCity()
        {
            id = "do_mod_city_beh"
        };
        t = obj;
        t.CityAIs(this);
        t.addBeh(new CityBehRandomWait(0.1f));
        add(obj);
    }
}
public class ModBehaviourTaskActorLibrary : BehaviourTaskActorLibrary
{
    public override void init()
    {
        base.init();
        LogService.LogInfo("初始化帝国模组角色逻辑");
        BehaviourTaskActor obj = new BehaviourTaskActor()
        {
            id = "do_mod_actor_beh"
        };
        t = obj;
        t.ActorAIs(this);
        t.addBeh(new BehRandomWait(0.1f));
        add(obj);
    }
}
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
public static class EmpireCraftBehaviourTaskKingdomLibrary
{
    public static void init()
    {
        AssetManager._instance._dict.Remove("beh_kingdom");
        AssetManager._instance.add(AssetManager.tasks_kingdom = new ModBehaviourTaskKingdomLibrary(), "beh_kingdom");

        AssetManager._instance._dict.Remove("beh_city");
        AssetManager._instance.add(AssetManager.tasks_city = new ModBehaviourTaskCityLibrary(), "beh_city");
        LogService.LogInfo("覆盖原版的AI逻辑");
    }
}
public class ModBehaviourTaskKingdomLibrary : BehaviourTaskKingdomLibrary
{
    public override void init()
    {
        LogService.LogInfo("初始化帝国模组王国逻辑");
        _ = Application.version;
        BehaviourTaskKingdom obj = new BehaviourTaskKingdom
        {
            id = "nothing"
        };
        BehaviourTaskKingdom pAsset = obj;
        t = obj;
        add(pAsset);
        BehaviourTaskKingdom obj2 = new BehaviourTaskKingdom
        {
            id = "wait1"
        };
        pAsset = obj2;
        t = obj2;
        add(pAsset);
        t.addBeh(new KingdomBehRandomWait());
        BehaviourTaskKingdom obj3 = new BehaviourTaskKingdom
        {
            id = "wait_random"
        };
        pAsset = obj3;
        t = obj3;
        add(pAsset);
        t.addBeh(new KingdomBehRandomWait(0f, 5f));
        BehaviourTaskKingdom obj4 = new BehaviourTaskKingdom
        {
            id = "do_checks"
        };
        pAsset = obj4;
        t = obj4;
        add(pAsset);
        t.addBeh(new KingdomBehCheckCapital());
        t.addBeh(new EmpireCraftKingdomBehCheckKing());
        t.addBeh(new EmpireCraftKingdomBehCheckOfficer());
        t.addBeh(new EmpireCraftKingdomBehCheckExam());
        t.addBeh(new EmpireCraftKingdomBehCheckHeir());
        t.addBeh(new EmpireCraftKingdomBehCheckInerOffice());
        t.addBeh(new KingdomBehRandomWait());
    }

    public override void editorDiagnosticLocales()
    {
    }
}
public class ModBehaviourTaskCityLibrary : BehaviourTaskCityLibrary
{
    public override void init()
    {
        LogService.LogInfo("初始化帝国模组城市逻辑");
        _ = Application.version;
        BehaviourTaskCity obj = new BehaviourTaskCity
        {
            id = "nothing"
        };
        BehaviourTaskCity pAsset = obj;
        t = obj;
            add(pAsset);
        BehaviourTaskCity obj2 = new BehaviourTaskCity
        {
            id = "wait1"
        };
        pAsset = obj2;
        t = obj2;
        add(pAsset);
        t.addBeh(new CityBehRandomWait());
            BehaviourTaskCity obj3 = new BehaviourTaskCity
            {
                id = "wait5"
            };
        pAsset = obj3;
        t = obj3;
        add(pAsset);
        t.addBeh(new CityBehRandomWait(5f, 5f));
        BehaviourTaskCity obj4 = new BehaviourTaskCity
        {
            id = "random_wait_test"
        };
        pAsset = obj4;
        t = obj4;
        add(pAsset);
        t.addBeh(new CityBehRandomWait(5f, 10f));
        t.addBeh(new CityBehRandomWait(5f, 10f));
        t.addBeh(new CityBehRandomWait(5f, 10f));
        BehaviourTaskCity obj5 = new BehaviourTaskCity
        {
            id = "do_checks"
        };
        pAsset = obj5;
        t = obj5;
        add(pAsset);
        t.addBeh(new EmpireCraftCityBehCheckLeader());
        t.addBeh(new CityBehRandomWait(0.1f));
        t.addBeh(new CityBehCheckAttackZone());
        t.addBeh(new CityBehRandomWait(0.1f));
        t.addBeh(new CityBehCheckCitizenTasks());
        t.addBeh(new CityBehRandomWait(0.1f));
        BehaviourTaskCity obj6 = new BehaviourTaskCity
        {
            id = "do_initial_load_check"
        };
        pAsset = obj6;
        t = obj6;
        add(pAsset);
        t.addBeh(new CityBehCheckCitizenTasks());
        BehaviourTaskCity obj7 = new BehaviourTaskCity
        {
            id = "check_farms"
        };
        pAsset = obj7;
        t = obj7;
        add(pAsset);
        t.addBeh(new CityBehCheckFarms());
        BehaviourTaskCity obj8 = new BehaviourTaskCity
        {
            id = "produce_boat"
        };
        pAsset = obj8;
        t = obj8;
        add(pAsset);
        t.addBeh(new CityBehProduceBoat());
        BehaviourTaskCity obj9 = new BehaviourTaskCity
        {
            id = "border_shrink"
        };
        pAsset = obj9;
        t = obj9;
        add(pAsset);
        t.addBeh(new CityBehBorderShrink());
        BehaviourTaskCity obj10 = new BehaviourTaskCity
        {
            id = "build",
            single_interval = 0f
        };
        pAsset = obj10;
        t = obj10;
        add(pAsset);
        t.addBeh(new CityBehBuild());
        BehaviourTaskCity obj11 = new BehaviourTaskCity
        {
            id = "supply_kingdom_cities"
        };
        pAsset = obj11;
        t = obj11;
        add(pAsset);
        t.addBeh(new CityBehSupplyKingdomCities());
        BehaviourTaskCity obj12 = new BehaviourTaskCity
        {
            id = "produce_resources"
        };
        pAsset = obj12;
        t = obj12;
        add(pAsset);
        t.addBeh(new CityBehProduceResources());
        BehaviourTaskCity obj13 = new BehaviourTaskCity
        {
            id = "check_army"
        };
        pAsset = obj13;
        t = obj13;
        add(pAsset);
        t.addBeh(new CityBehCheckArmy());
    }

    public override void editorDiagnosticLocales()
    {
    }
}

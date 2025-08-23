using System;
using System.Linq;
using System.Reflection;
using ai.behaviours;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.AI.CityAI;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameLibrary;
using HarmonyLib;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI;
//此处为模组AI的统一接口。通过继承ActorAI/CityAI/KingdomAI/目录下的基类并填充自己的逻辑来控制国家/城市/角色的行为
public static class GameAIMain
{
    public static void KingdomAIs(this BehaviourTaskKingdom t, BehaviourTaskKingdomLibrary lib)
    {
        t.addBeh(new KingdomBehCheckCapital());
        var asm = Assembly.GetExecutingAssembly();
        var types = asm.GetTypes()
            .Where(ty =>
                    !ty.IsAbstract
                    && typeof(GameAIKingdomBase).IsAssignableFrom(ty)
                    && ty.GetConstructor(Type.EmptyTypes) != null 
            );
        foreach (var type in types)
        {
            var beh = (GameAIKingdomBase) Activator.CreateInstance(type);
            var id = beh.OriginalBeh.ToString().Split('.').Last();
            LogService.LogInfo("载入模组国家AI: " + beh.GetType().ToString().Split('.').Last());
            foreach (var bt in lib.list)
            {
                foreach (var action in bt.list.ToList())
                {
                    if (action.id == id)
                    {
                        LogService.LogInfo($"存在原版同类逻辑{id}，已覆盖");
                        bt.list.Remove(action);
                    }
                }
            }
            beh.create();
            t.addBeh(beh);
        }
    }
    public static void CityAIs(this BehaviourTaskCity t, BehaviourTaskCityLibrary lib)
    {
        var asm = Assembly.GetExecutingAssembly();
        var types = asm.GetTypes()
            .Where(ty =>
                !ty.IsAbstract
                && typeof(GameAICityBase).IsAssignableFrom(ty)
                && ty.GetConstructor(Type.EmptyTypes) != null
            );

        foreach (var type in types)
        {
            var beh = (GameAICityBase) Activator.CreateInstance(type);
            var id = beh.OriginalBeh.ToString().Split('.').Last();
            LogService.LogInfo("载入模组城市AI: " + beh.GetType().ToString().Split('.').Last());
            foreach (var bt in lib.list)
            {
                foreach (var action in bt.list.ToList())
                {
                    if (action.id == id)
                    {
                        LogService.LogInfo($"存在原版同类逻辑{id}，已覆盖");
                        bt.list.Remove(action);
                    }
                }
            }
            beh.create();
            t.addBeh(beh);
        }
    }
    public static void ActorAIs(this BehaviourTaskActor t, BehaviourTaskActorLibrary lib)
    {
        var asm = Assembly.GetExecutingAssembly();
        var types = asm.GetTypes()
            .Where(ty =>
                !ty.IsAbstract
                && typeof(GameAIActorBase).IsAssignableFrom(ty)
                && ty.GetConstructor(Type.EmptyTypes) != null
            );

        foreach (var type in types)
        {
            var beh = (GameAIActorBase) Activator.CreateInstance(type);
            var id = beh.OriginalBeh.ToString().Split('.').Last();
            LogService.LogInfo("载入模组角色AI: " + beh.GetType().ToString().Split('.').Last());
            foreach (var bt in lib.list)
            {
                foreach (var action in bt.list.ToList())
                {
                    if (action.id == id)
                    {
                        LogService.LogInfo($"存在原版同类逻辑{id}，已覆盖");
                        bt.list.Remove(action);
                    }
                }
            }
            beh.create();
            t.addBeh(beh);
        }
    }
}
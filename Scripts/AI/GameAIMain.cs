using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ai.behaviours;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.AI.CityAI;
using EmpireCraft.Scripts.AI.EmpireAI;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.AI.KingdomMindAI;
using EmpireCraft.Scripts.GameLibrary;
using HarmonyLib;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI;
//此处为模组AI的统一接口。通过继承ActorAI/CityAI/KingdomAI/目录下的基类并填充自己的逻辑来控制国家/城市/角色的行为
public static class GameAIMain
{
    public static List<GameAIKingdomBase> KingdomAis = new();
    public static List<GameAIEmpireBase> EmpireAis = new();
    public static List<GameAIKingdomMindBase> KingdomMindAis = new();
    public static List<GameAICityBase> CityAis = new();
    public static List<GameAIActorBase> ActorAis = new();
    public static void KingdomAIs(BehaviourTaskKingdomLibrary lib)
    {
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
            foreach (var bt in lib.list)
            {
                foreach (var action in bt.list.ToList())
                {
                    if (action.id == id)
                    {
                        bt.list.Remove(action);
                    }
                }
            }
            beh.create();
            KingdomAis.Add(beh);
        }
    }
    public static void KingdomMindAIs(BehaviourTaskKingdomLibrary lib)
    {
        var asm = Assembly.GetExecutingAssembly();
        var types = asm.GetTypes()
            .Where(ty =>
                    !ty.IsAbstract
                    && typeof(GameAIKingdomMindBase).IsAssignableFrom(ty)
                    && ty.GetConstructor(Type.EmptyTypes) != null 
            );
        foreach (var type in types)
        {
            var beh = (GameAIKingdomMindBase) Activator.CreateInstance(type);
            var id = beh.OriginalBeh.ToString().Split('.').Last();
            foreach (var bt in lib.list)
            {
                foreach (var action in bt.list.ToList())
                {
                    if (action.id == id)
                    {
                        bt.list.Remove(action);
                    }
                }
            }
            beh.create();
            KingdomMindAis.Add(beh);
        }
    }
    public static void EmpireAIs(BehaviourTaskKingdomLibrary lib)
    {
        var asm = Assembly.GetExecutingAssembly();
        var types = asm.GetTypes()
            .Where(ty =>
                    !ty.IsAbstract
                    && typeof(GameAIEmpireBase).IsAssignableFrom(ty)
                    && ty.GetConstructor(Type.EmptyTypes) != null 
            );
        foreach (var type in types)
        {
            var beh = (GameAIEmpireBase) Activator.CreateInstance(type);
            var id = beh.OriginalBeh.ToString().Split('.').Last();
            foreach (var bt in lib.list)
            {
                foreach (var action in bt.list.ToList())
                {
                    if (action.id == id)
                    {
                        bt.list.Remove(action);
                    }
                }
            }
            beh.create();
            EmpireAis.Add(beh);
        }
    }
    public static void CityAIs(BehaviourTaskCityLibrary lib)
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
            foreach (var bt in lib.list)
            {
                foreach (var action in bt.list.ToList())
                {
                    if (action.id == id)
                    {
                        bt.list.Remove(action);
                    }
                }
            }
            beh.create();
            CityAis.Add(beh);
        }
    }
    public static void ActorAIs(BehaviourTaskActorLibrary lib)
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
            bool isReplace = false;
            var beh = (GameAIActorBase) Activator.CreateInstance(type);
            var id = beh.OriginalBeh.ToString().Split('.').Last();
            foreach (var bt in lib.list)
            {
                foreach (var action in bt.list.ToList())
                {
                    if (action.id == id)
                    {
                        bt.list.Remove(action);
                        beh.create();
                        beh.id = id;
                        bt.list.Add(beh);
                        isReplace = true;
                    }
                }
            }

            if (isReplace) continue;
            beh.create();
            ActorAis.Add(beh);
        }
    }
}

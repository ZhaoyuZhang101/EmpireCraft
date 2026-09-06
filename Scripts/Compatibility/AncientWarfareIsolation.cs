using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ai.behaviours;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.AI.CityAI;
using EmpireCraft.Scripts.AI.EmpireAI;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.AI.KingdomMindAI;
using HarmonyLib;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Compatibility
{
    public static class AncientWarfareIsolation
    {
        private static readonly Assembly OwnAssembly = typeof(AncientWarfareIsolation).Assembly;
        private static readonly Harmony Guard = new("EmpireCraft.AncientWarfareIsolation");
        private static readonly Dictionary<MethodBase, Func<object, BehResult>> OriginalBehaviours = new();
        private static readonly Dictionary<string, Delegate> OriginalCallbacks = new();
        private static readonly List<PlotAsset> RemovedPlotFallbacks = new();
        private static readonly string[] Libraries = { "plots_library", "opinion_library", "loyalty", "traits", "powers" };
        private static bool _installed;
        private static bool _ready;

        public static void EnableWhenAvailable()
        {
            _ready = true;
            AncientWarfareCompatibility.Refresh();
            InstallWhenReady();
        }

        public static void InstallWhenReady()
        {
            if (!_ready || !AncientWarfareCompatibility.Loaded) return;
            AncientWarfareNaming.Install();
            AncientWarfareNameplates.Install();
            Install();
        }

        private static IEnumerable<(string key, object asset, FieldInfo field)> Callbacks()
        {
            foreach (string libraryName in Libraries)
            {
                object library = AccessTools.Field(typeof(AssetManager), libraryName)?.GetValue(null);
                if (library == null) continue;
                if (AccessTools.Field(library.GetType(), "list")?.GetValue(library) is not IEnumerable assets) continue;
                foreach (object asset in assets)
                {
                    if (asset == null) continue;
                    string id = AccessTools.Field(asset.GetType(), "id")?.GetValue(asset) as string;
                    foreach (FieldInfo field in asset.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
                        yield return (libraryName + "/" + id + "/" + field.Name, asset, field);
                    }
                }
            }
        }

        public static void CaptureOriginalCallbacks()
        {
            RemovedPlotFallbacks.AddRange(AssetManager.plots_library.list);
            foreach (var callback in Callbacks())
                if (callback.field.GetValue(callback.asset) is Delegate original)
                    OriginalCallbacks[callback.key] = original;
        }

        public static void Install()
        {
            if (_installed) return;
            InstallBehaviourGuards();
            // Country patches contain direct guards; never Harmony-patch a Harmony prefix/postfix.
            foreach (var callback in Callbacks())
            {
                if (callback.field.GetValue(callback.asset) is not Delegate current ||
                    current.Method.DeclaringType?.Assembly != OwnAssembly) continue;
                OriginalCallbacks.TryGetValue(callback.key, out Delegate original);
                callback.field.SetValue(callback.asset, WrapCallback(current, original));
            }
            // EC renames/removes some vanilla plots (notably new_war). AW kingdoms still need them.
            foreach (PlotAsset plot in RemovedPlotFallbacks)
            {
                if (AssetManager.plots_library.list.Any(p => p.id == plot.id)) continue;
                var originalCheck = plot.check_is_possible;
                plot.check_is_possible = actor => AncientWarfareCompatibility.Owns(actor) &&
                    (originalCheck == null || originalCheck(actor));
                AssetManager.plots_library.add(plot);
                if (plot.is_basic_plot && !AssetManager.plots_library.basic_plots.Contains(plot))
                    AssetManager.plots_library.basic_plots.Add(plot);
            }
            _installed = true;
            LogService.LogInfo("[EmpireCraft] AW compatibility installation completed; normal kingdoms and EC nameplates remain enabled.");
        }

        private static Delegate WrapCallback(Delegate current, Delegate original)
        {
            MethodInfo invoke = current.GetType().GetMethod("Invoke");
            ParameterInfo[] signature = invoke.GetParameters();
            if (signature.Any(p => p.ParameterType.IsByRef)) return current;
            var parameters = signature.Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            Expression bypass = Expression.AndAlso(
                Expression.Property(null, typeof(AncientWarfareCompatibility), nameof(AncientWarfareCompatibility.Loaded)),
                Expression.Call(typeof(AncientWarfareCompatibility), nameof(AncientWarfareCompatibility.OwnsAny), null,
                    Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object))))));
            Expression fallback = original != null && original.GetType() == current.GetType()
                ? Expression.Invoke(Expression.Constant(original), parameters)
                : Expression.Default(invoke.ReturnType);
            Expression body = Expression.Condition(bypass, fallback,
                Expression.Invoke(Expression.Constant(current), parameters));
            return Expression.Lambda(current.GetType(), body, parameters).Compile();
        }

        private static void InstallBehaviourGuards()
        {
            Type[] bases = { typeof(GameAIActorBase), typeof(GameAICityBase), typeof(GameAIKingdomBase),
                typeof(GameAIEmpireBase), typeof(GameAIKingdomMindBase) };
            foreach (Type type in OwnAssembly.GetTypes().Where(t => !t.IsAbstract && bases.Any(b => b.IsAssignableFrom(t))))
            {
                MethodInfo execute = type.GetMethod("execute", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (execute == null) continue;
                object behaviour = Activator.CreateInstance(type);
                Type originalType = (Type)type.GetProperty("OriginalBeh").GetValue(behaviour);
                if (originalType != type && originalType.Assembly != OwnAssembly)
                {
                    object original = Activator.CreateInstance(originalType);
                    AccessTools.Method(originalType, "create", Type.EmptyTypes)?.Invoke(original, null);
                    MethodInfo method = originalType.GetMethod("execute", new[] { execute.GetParameters()[0].ParameterType });
                    var target = Expression.Parameter(typeof(object), "target");
                    OriginalBehaviours[execute] = Expression.Lambda<Func<object, BehResult>>(
                        Expression.Call(Expression.Constant(original), method,
                            Expression.Convert(target, method.GetParameters()[0].ParameterType)), target).Compile();
                }
                Guard.Patch(execute, prefix: new HarmonyMethod(typeof(AncientWarfareIsolation), nameof(BehaviourPrefix)));
            }
        }

        private static bool BehaviourPrefix(MethodBase __originalMethod, object __0, ref BehResult __result)
        {
            if (!AncientWarfareCompatibility.OwnsObject(__0)) return true;
            __result = OriginalBehaviours.TryGetValue(__originalMethod, out var original)
                ? original(__0) : BehResult.Continue;
            return false;
        }

    }
}

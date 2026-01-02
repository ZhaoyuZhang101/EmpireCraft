using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

public class ArmyPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(Dispose)).Patch(AccessTools.Method(typeof(Army), nameof(Army.Dispose)),
            postfix: new HarmonyMethod(GetType(), nameof(Dispose)));
    }

    public static void Dispose(Army __instance)
    {
        var kingdom = World.world.kingdoms.ToList().Find(k => k.GetCenterArmy() == __instance);
        kingdom?.RemoveCenterArmy();
    }
}
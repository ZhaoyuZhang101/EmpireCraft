using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;
public class MetaTypeLibraryPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(EmpireCraftMetaType)).Patch(AccessTools.Method(typeof(MetaTypeLibrary), nameof(MetaTypeLibrary.init)),
            postfix: new HarmonyMethod(GetType(), nameof(EmpireCraftMetaType)));
    }

    public static void EmpireCraftMetaType(MetaTypeLibrary __instance)
    {
    }
}

using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches
{
    public class SuccessionToolPatch : GamePatch
    {
        public ModDeclare declare { get; set; }

        public void Initialize()
        {
            //new Harmony(nameof(findNextHeir)).Patch(
            //    AccessTools.Method(typeof(SaveManager), nameof(SuccessionTool.findNextHeir)),
            //    prefix: new HarmonyMethod(GetType(), nameof(findNextHeir)));
        }

        public static bool findNextHeir(Kingdom pKingdom, Actor pExculdeActor, ref Actor __result)
        {
            if (pKingdom.isEmpire())
            {
                __result = pKingdom.GetEmpire().Heir;
                return false;
            }
            return true;
        }
    }
}

using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class ClanWindowPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(applyInputName)).Patch(
            AccessTools.Method(typeof(ClanWindow), nameof(ClanWindow.applyInputName)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(applyInputName))
        );
    }

    public static void applyInputName(ClanWindow __instance, string pInput)
    {
        if (pInput != null && __instance.meta_object != null)
        {
            if (__instance.meta_object.meta_type==MetaType.Clan)
            {
                Clan clan = __instance.meta_object;
                string[] namePart;
                if (pInput.Contains("\u200A"))
                {
                    namePart = pInput.Split('\u200A');
                }
                else
                {
                    namePart = pInput.Split(' ');
                }
                if (namePart.Length >= 1)
                {
                    clan.data.name = namePart[0] + "\u200A" + LM.Get("Clan");
                    foreach (Actor actor in clan.units)
                    {
                        actor.initializeActorName();
                        actor.SetFamilyName(namePart[0]);
                        actor.GetModName().SetName(actor);
                    }
                }
                else
                {
                    clan.data.name = pInput;

                }
                __instance._name_input.setText(clan.data.name);
            }
        }
        __instance.updateStats();
    }
}
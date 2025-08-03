using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

public class BehWaitPatch:GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        // new Harmony(nameof(execute)).Patch(
        //     AccessTools.Method(typeof(BehWait), nameof(BehWait.execute)),
        //     postfix: new HarmonyMethod(GetType(), nameof(execute))
        // );
    }

    public static void execute(BehWait __instance, Actor pActor)
    {
        if (pActor.getParents().Any())
        {
            foreach (var parent in pActor.getParents().ToList())
            {
                if (parent.HasSpecificClan())
                {
                    PersonalClanIdentity pci = parent.GetPersonalIdentity();
                    if (pci.is_main)
                    {
                        pActor.setClan(parent.clan);
                        pActor.initializeActorName();
                        pActor.SetFamilyName(parent.clan.GetClanName());
                        pActor.GetModName().SetName(pActor);
                    }
                }
            }
        }
    }
}
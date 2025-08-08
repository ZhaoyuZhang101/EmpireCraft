using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftActorTraitGroupLibrary
{
    public static void init()
    {
        ActorTraitGroupLibrary lib = AssetManager.trait_groups;
        lib.add(new ActorTraitGroupAsset
        {
            id = "SampleTraits",
            name = "SampleTraitsGroup",
            color = "#5EFFFF"
        });
    }
}

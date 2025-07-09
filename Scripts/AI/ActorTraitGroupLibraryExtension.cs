using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.AI;
public static class ActorTraitGroupLibraryExtension
{
    public static void init()
    {
        ActorTraitGroupLibrary lib = AssetManager.trait_groups;
        lib.add(new ActorTraitGroupAsset
        {
                id = "EmpireExam",
                name = "EmpireExamGroup",
                color = "#5EFFFF"
        });
    }
}

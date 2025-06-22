using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class ActorTextureSubAssetPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        //new Harmony(nameof(setEmperorCrown)).Patch(
        //    AccessTools.Method(typeof(ActorTextureSubAsset), nameof(ActorTextureSubAsset.getUnitTexturePath)),
        //    prefix: new HarmonyMethod(GetType(), nameof(setEmperorCrown))
        //);
    }

    public static bool setEmperorCrown(ActorTextureSubAsset __instance, Actor pActor, ref string __result) 
    {
        Subspecies subspecies = pActor.subspecies;
        if (pActor.isEgg())
        {
            __result = subspecies.egg_sprite_path;
            return false;
        }
        if (pActor.isBaby())
        {
            __result = __instance.texture_path_baby;
            return false;
        }
        string result = __instance.texture_path_main;
        ProfessionAsset profession_asset = pActor.profession_asset;
        if (profession_asset == null || profession_asset.profession_id == UnitProfession.Nothing)
        {
            __result = result;
            return false;
        }
        if (!__instance.has_advanced_textures)
        {
            __result = result;
            return false;
        }
        switch (profession_asset.profession_id)
        {
            case UnitProfession.Warrior:
                {
                    string text = __instance.texture_path_warrior;
                    if (pActor.hasSubspecies())
                    {
                        text = pActor.subspecies.getSkinWarrior();
                    }
                    if (subspecies.has_mutation_reskin)
                    {
                        List<string> skin_warrior = subspecies.mutation_skin_asset.skin_warrior;
                        int index = Toolbox.loopIndex(pActor.asset.skin_warrior.IndexOf(text), skin_warrior.Count);
                        text = skin_warrior[index];
                    }
                    __result = __instance.texture_path_base + text;
                    return false;
                }
            case UnitProfession.King:
                if (pActor.isEmperor())
                {
                    result = "ChineseCrown";
                    return false;
                }
                __result = __instance.texture_path_king;
                return false;
            case UnitProfession.Leader:
                __result = __instance.texture_path_leader;
                return false;
            default:
                __result = __instance.getTextureSkinBasedOnSex(pActor);
                return false;
        }
    }
}

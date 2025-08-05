using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class ActorTextureSubAssetPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public static string texture_path_emperor {  get; set; }
    public static string texture_path_officer {  get; set; }
    public static string texture_path_general {  get; set; }
    public static string texture_path_minister {  get; set; }

    public void Initialize()
    {
        new Harmony(nameof(getUnitTexturePath)).Patch(AccessTools.Method(typeof(ActorTextureSubAsset), nameof(ActorTextureSubAsset.getUnitTexturePath)),
            prefix: new HarmonyMethod(GetType(), nameof(getUnitTexturePath)));
    }

    public static bool getUnitTexturePath(ActorTextureSubAsset __instance, Actor pActor, ref string __result)
    {
        if (string.IsNullOrEmpty(texture_path_emperor))
        {
            texture_path_emperor = __instance._base_path + "emperor";
        }
        if (string.IsNullOrEmpty(texture_path_officer))
        {
            texture_path_officer = __instance._base_path + "officer";
        }
        if (string.IsNullOrEmpty(texture_path_general))
        {
            texture_path_general = __instance._base_path + "general";
        }
        if (string.IsNullOrEmpty(texture_path_minister))
        {
            texture_path_minister = __instance._base_path + "minister";
        }
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
                __result = __instance.texture_path_king;
                return false;
            case UnitProfession.Leader:
                __result = __instance.texture_path_leader;
                return false;
            case UnitProfessionExtension.minister:
                __result = texture_path_minister;
                return false;
            case UnitProfessionExtension.Emperor:
                __result = texture_path_emperor;
                return false;
            case UnitProfessionExtension.General:
                __result = texture_path_general;
                return false;
            case UnitProfessionExtension.Officer:
                __result = texture_path_officer;
                return false;
            default:
                __result = __instance.getTextureSkinBasedOnSex(pActor);
                return false;
        }
    }
}

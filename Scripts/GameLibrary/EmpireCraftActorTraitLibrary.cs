using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameLibrary;
public static class ModActorTraitLibrary
{
    public static void init()
    {
        ActorTraitLibrary lib = AssetManager.traits;
        lib.add(new ActorTrait
        {
            id = "sample_trait", //特质id
            path_icon = "ui/icons/actor_traits/iconEmpireEliteArmy", //特质图标位置
            group_id = "SampleTraits", //特质类别
            action_on_add = SampleTraitAddedAction
        });
        //此处设置添加特之后给人物的属性
        lib.t.base_stats["damage"] = 40f;
        lib.t.base_stats["speed"] = 40f;
        lib.t.base_stats["armor"] = 50f;
        lib.t.base_stats["critical_chance"] = 0.8f;
        lib.t.base_stats["critical_damage_multiplier"] = 0.5f;
    }

    private static bool SampleTraitAddedAction(NanoObject pTarget, BaseAugmentationAsset pTrait)
    {
        //todo: 在此处添加获得特质后触发的逻辑
        return true;
    }
}

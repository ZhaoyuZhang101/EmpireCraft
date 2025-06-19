using ai.behaviours;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class AssetManagerPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(add_empire_banners_lib_init)).Patch(
            AccessTools.Method(typeof(AssetManager), nameof(AssetManager.initLibs)),
            prefix: new HarmonyMethod(GetType(), nameof(add_empire_banners_lib_init))
        );
        LogService.LogInfo("资源管理器补丁加载成功");
    }

    public static void add_empire_banners_lib_init(AssetManager __instance)
    {
    }
}

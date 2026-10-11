using System;
using EmpireCraft.Scripts.GameLibrary;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 王国名统一：原版各处(外交权能提示、战争、世界日志、窗口……)读国名都经过 kingdom.name，
// 而它直接返回存档里的原始名(data.name)，可能是当年称帝时的旧名(如转成现代政府后仍是"越帝国")。
// 这里让王国的 name 返回与地图铭牌一致的显示名(EmpireCraftNamePlateLibrary.GetDisplayName)。
// 存档里的 data.name 保持不变：模组以它判断玩家是否改名、从中提取核心地名(KingdomExtension.EnsureKingdomCoreName)，
// 不能把显示名写回去。setter 不动，玩家在原版窗口改名照样写入 data.name。
public class KingdomNamePatch : GamePatch
{
    public ModDeclare declare { get; set; }

    [ThreadStatic] private static bool _resolving;

    public void Initialize()
    {
        // name 定义在泛型基类 CoreSystemObject<T> 上，引用类型参数共享同一份实现，城市、文化等也会走到这里
        new Harmony(nameof(KingdomNamePatch)).Patch(
            AccessTools.PropertyGetter(typeof(CoreSystemObject<KingdomData>), "name"),
            postfix: new HarmonyMethod(typeof(KingdomNamePatch), nameof(DisplayName)));
    }

    private static void DisplayName(object __instance, ref string __result)
    {
        if (_resolving || __instance is not Kingdom kingdom || kingdom.data == null) return;
        _resolving = true;
        try
        {
            string display = EmpireCraftNamePlateLibrary.GetDisplayName(kingdom);
            if (!string.IsNullOrWhiteSpace(display)) __result = display;
        }
        catch
        {
            // 显示名算不出来时保持原始名
        }
        finally
        {
            _resolving = false;
        }
    }
}

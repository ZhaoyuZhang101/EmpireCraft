using ai.behaviours;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 城市 AI 限流(帧数忽高忽低的大头)：
//   · 忠诚：原版城市 AI 每零点几秒就强制重算一次忠诚(绕过它自己三秒的缓存)，每次把忠诚表逐项算一遍，
//     全图几十座城下来每帧好几毫秒；改为每座城至多每个游戏月(五个游戏秒)强制重算一次——游戏秒随倍速变快，
//     以前两秒一次，四百座城开二十倍速时每秒重算四千次(性能日志里城市忠诚检查每帧近 3 毫秒)，
//     忠诚要的模组各项也都是按月结算的；
//   · 建造(无小人模式)：开工由模组的城市建设按月推进(CityConstructionSystem.StartNext)，原版每五秒一次的建造检查
//     是重复的，而一次要挑可建的建筑、找空地，十几到三十毫秒，各城轮流冒尖峰——跳过；
//   · 建筑连路(纯净地图)：路是模拟的(见 RoadNetwork)，原版建造前给随机一座建筑寻路连路，白算，跳过。
public class CityAIThrottlePatch : GamePatch
{
    public ModDeclare declare { get; set; }
    private const float LoyaltySeconds = 5f;

    public void Initialize()
    {
        var harmony = new Harmony(nameof(CityAIThrottlePatch));
        harmony.Patch(AccessTools.Method(typeof(CityBehCheckLoyalty), nameof(CityBehCheckLoyalty.execute)),
            prefix: new HarmonyMethod(typeof(CityAIThrottlePatch), nameof(BeforeCheckLoyalty)));
        harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.execute)),
            prefix: new HarmonyMethod(typeof(CityAIThrottlePatch), nameof(BeforeBuild)));
        harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.makeRoadsBuildings)),
            prefix: new HarmonyMethod(typeof(CityAIThrottlePatch), nameof(BeforeRoads)));
    }

    private static bool BeforeCheckLoyalty(City pCity, ref BehResult __result)
    {
        if (pCity?.data == null || World.world == null) return true;
        if (World.world.getWorldTimeElapsedSince(pCity._loyalty_last_time) >= LoyaltySeconds) return true;
        __result = BehResult.Continue;
        return false;
    }

    private static bool BeforeBuild(City pCity, ref BehResult __result)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || pCity == null || AncientWarfareCompatibility.OwnsObject(pCity))
            return true;
        __result = BehResult.Continue;
        return false;
    }

    private static bool BeforeRoads() => !PureMapMode.Enabled;
}

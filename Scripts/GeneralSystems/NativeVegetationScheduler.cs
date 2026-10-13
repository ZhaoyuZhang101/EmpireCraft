using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace EmpireCraft.Scripts.GeneralSystems;

// FruitGrowth 在果实已满时只会返回。让这类单组件植物休眠，采收时恢复原版的 90 秒计时。
// 不移除组件，不改变扩散、火灾、动画、麦田或其他模组添加的组件。
public static class NativeVegetationScheduler
{
    private static readonly HashSet<Building> Sleeping = new();
    private static object _world;
    // 只在主线程改休眠表与原版组件列表(原版并行建筑作业打开时，果实生长的回调会跑在工作线程上)
    private static int _mainThread = -1;
    public static int SleepingCount => Sleeping.Count;

    private static bool OffMainThread => _mainThread >= 0 && Thread.CurrentThread.ManagedThreadId != _mainThread;

    public static void Tick()
    {
        _mainThread = Thread.CurrentThread.ManagedThreadId;
        if (!ReferenceEquals(_world, World.world))
        {
            Sleeping.Clear();
            _world = World.world;
        }
        if (!CityPopulationSystem.AbstractPopulationEnabled) Restore();
    }

    public static void Reconcile(Building building)
    {
        if (OffMainThread) return;
        if (!CityPopulationSystem.AbstractPopulationEnabled && Sleeping.Count == 0) return;
        if (!ReferenceEquals(_world, World.world))
        {
            Sleeping.Clear();
            _world = World.world;
        }
        if (building?.batch == null || building.data == null) return;

        var components = (IList)building.components_list;

        bool idle = CityPopulationSystem.AbstractPopulationEnabled &&
                    building.isUsable() &&
                    building.isNormal() &&
                    building.component_fruit_growth != null &&
                    components.Count == 1 &&
                    ReferenceEquals(
                        components[0],
                        building.component_fruit_growth
                    ) &&
                    building.hasResourcesToCollect();

        if (idle)
        {
            // ObjectContainer 延迟执行增删，允许在组件更新中调用。
            Sleeping.Add(building);
            building.batch.c_components.Remove(building);
        }
        else if (Sleeping.Remove(building) && building.isUsable())
        {
            building.batch.c_components.Add(building);
        }
    }

    public static void Forget(Building building)
    {
        if (!OffMainThread) Sleeping.Remove(building);
    }

    public static void Restore()
    {
        foreach (Building building in Sleeping)
            if (building?.data != null && building.batch != null && building.isUsable())
                building.batch.c_components.Add(building);
        Sleeping.Clear();
    }

    public static void Reset()
    {
        Restore();
        _world = World.world;
    }
}

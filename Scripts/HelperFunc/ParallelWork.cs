using System;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.HelperFunc;

// 多核计算(用户 2026-11-14 "服务终端请一定要用多核优化，因为是纯数据")：主线程先把要读的东西取成快照，
// 几个工作线程同时算(只读快照，不碰懒加载的缓存、不调 Unity 的接口)，算完主线程落地。
// 游戏里留核给画面、原版和模组自己的人口线程，至多用一半的核(上限 4)；独立服务器没有画面，开服时设成全部的核
public static class ParallelWork
{
    public static int MaxWorkers = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));

    // 一并算 count 件(主线程自己也算一份)；只有一件或只许一个线程时就地算
    public static void For(int count, Action<int> body)
    {
        if (count <= 0) return;
        if (count == 1 || MaxWorkers <= 1)
        {
            for (int i = 0; i < count; i++) body(i);
            return;
        }
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = MaxWorkers }, body);
    }
}

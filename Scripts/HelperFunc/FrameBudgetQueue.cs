using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.HelperFunc;

// 分帧处理：全图级的年度结算(理念交往、藏书影响……)一帧做完会卡住，排成队列每帧只花一点时间。
// 换世界(读档/新图)时自动作废队列。
public sealed class FrameBudgetQueue<T>
{
    private readonly double _budgetMilliseconds;
    private readonly Action<T> _work;
    private readonly string _label;
    private List<T> _items;
    private int _index;
    private object _world;

    public FrameBudgetQueue(double budgetMilliseconds, Action<T> work, string label)
    {
        _budgetMilliseconds = budgetMilliseconds;
        _work = work;
        _label = label;
    }

    public bool Active => _items != null;

    public void Start(IEnumerable<T> items)
    {
        // 高速游戏进入下一年度时，不能把尚未完成的旧队列反复清零，导致尾部城市永远轮不到。
        if (EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.AbstractPopulationEnabled &&
            Active && ReferenceEquals(_world, World.world)) return;
        _items = items?.ToList() ?? new List<T>();
        _index = 0;
        _world = World.world;
    }

    public void Cancel()
    {
        _items = null;
        _world = null;
        _index = 0;
    }

    // 处理到时间预算用完为止；本次调用把队列处理完时返回 true
    public bool Tick(Func<bool> canContinue = null)
    {
        if (_items == null) return false;
        if (!ReferenceEquals(_world, World.world))
        {
            Cancel();
            return false;
        }
        using var frameWork = SimulationFrameBudget.Measure();
        long started = Stopwatch.GetTimestamp();
        while (_index < _items.Count && SimulationFrameBudget.HasTime && (canContinue?.Invoke() ?? true))
        {
            T item = _items[_index++];
            try
            {
                _work(item);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] {_label}失败: {exception.Message}");
            }
            if ((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency >= _budgetMilliseconds) return false;
        }
        if (_index < _items.Count) return false;
        Cancel();
        return true;
    }
}

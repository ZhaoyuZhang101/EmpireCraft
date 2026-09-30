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
        _items = items?.ToList() ?? new List<T>();
        _index = 0;
        _world = World.world;
    }

    public void Cancel() => _items = null;

    // 处理到时间预算用完为止；本次调用把队列处理完时返回 true
    public bool Tick()
    {
        if (_items == null) return false;
        if (!ReferenceEquals(_world, World.world))
        {
            _items = null;
            return false;
        }
        long started = Stopwatch.GetTimestamp();
        while (_index < _items.Count)
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
        _items = null;
        return true;
    }
}

using System.Collections.Generic;

namespace EmpireCraft.Scripts.HelperFunc;

// 小顶堆(最短路用)：按 key 从小到大弹出
public sealed class MinHeap<T>
{
    private readonly List<(float key, T item)> _items = new();

    public int Count => _items.Count;

    public void Clear() => _items.Clear();

    public void Push(float key, T item)
    {
        _items.Add((key, item));
        int i = _items.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (_items[parent].key <= key) break;
            _items[i] = _items[parent];
            i = parent;
        }
        _items[i] = (key, item);
    }

    public (float key, T item) Pop()
    {
        (float key, T item) top = _items[0];
        (float key, T item) last = _items[_items.Count - 1];
        _items.RemoveAt(_items.Count - 1);
        if (_items.Count == 0) return top;
        int i = 0;
        while (true)
        {
            int left = i * 2 + 1;
            if (left >= _items.Count) break;
            int right = left + 1;
            int child = right < _items.Count && _items[right].key < _items[left].key ? right : left;
            if (_items[child].key >= last.key) break;
            _items[i] = _items[child];
            i = child;
        }
        _items[i] = last;
        return top;
    }
}

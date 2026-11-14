// 原版的对象池列表：模组里当"用完就还的临时列表"用，支持 using、foreach(含 foreach ref)、LINQ。这里就是个普通的可增长数组。
using System;
using System.Collections;
using System.Collections.Generic;

public class ListPool<T> : IDisposable, IEnumerable<T>
{
    public const int MinimumCapacity = 16;
    private T[] _items;
    private int _count;

    public ListPool()
    {
        _items = new T[MinimumCapacity];
    }

    public ListPool(int capacity)
    {
        _items = new T[Math.Max(capacity, MinimumCapacity)];
    }

    public ListPool(ICollection<T> collection) : this(collection?.Count ?? 0)
    {
        if (collection == null) return;
        collection.CopyTo(_items, 0);
        _count = collection.Count;
    }

    public ListPool(IEnumerable<T> source) : this()
    {
        AddRange(source);
    }

    public int Count => _count;
    public ref T this[int index] => ref _items[index];

    public void Add(T item)
    {
        if (_count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
        _items[_count++] = item;
    }

    public void AddRange(IEnumerable<T> source)
    {
        if (source == null) return;
        foreach (T item in source) Add(item);
    }

    public void Clear()
    {
        Array.Clear(_items, 0, _count);
        _count = 0;
    }

    public bool Any() => _count > 0;
    public int IndexOf(T item) => Array.IndexOf(_items, item, 0, _count);
    public bool Contains(T item) => IndexOf(item) >= 0;

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _count) return;
        _count--;
        if (index < _count) Array.Copy(_items, index + 1, _items, index, _count - index);
        _items[_count] = default;
    }

    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    public void Sort() => Array.Sort(_items, 0, _count);
    public void Sort(Comparison<T> comparison) => Array.Sort(_items, 0, _count, Comparer<T>.Create(comparison));
    public void Sort(IComparer<T> comparer) => Array.Sort(_items, 0, _count, comparer);

    public T[] ToArray()
    {
        var copy = new T[_count];
        Array.Copy(_items, copy, _count);
        return copy;
    }

    // 从随便一处起头走一圈
    public IEnumerable<T> LoopRandom()
    {
        if (_count == 0) yield break;
        int start = System.Random.Shared.Next(_count);
        for (int i = 0; i < _count; i++) yield return _items[(start + i) % _count];
    }

    public void Dispose() => Clear();
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        for (int i = 0; i < _count; i++) yield return _items[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    public struct Enumerator
    {
        private readonly ListPool<T> _pool;
        private int _index;

        internal Enumerator(ListPool<T> pool)
        {
            _pool = pool;
            _index = -1;
        }

        public ref T Current => ref _pool._items[_index];
        public bool MoveNext() => ++_index < _pool._count;
    }
}

using EmpireCraft.Scripts;
using NeoModLoader.General.Game.extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

public static class ExtensionManager<TKey, TData>
    where TKey : NanoObject
    where TData : class, new()
{
    public static ConditionalWeakTable<TKey, TData> _table = new();

    public static TData GetOrCreate(TKey key, bool isSave=false)
    {
        if (isSave)
        {
            if (key == null) return null;
            if (_table.TryGetValue(key, out TData data))
            {
                return data;
            } else
            {
                return null;
            }
        }
        if (key == null) return default;
        TData d = _table.GetOrCreateValue(key);
        return d;

    }
    public static void Update(TKey key, TData data)
    {
        _table.Remove(key);
        _table.Add(key, data);
    }

    public static bool Remove(TKey key)
    {
        return _table.Remove(key);
    }

    public static void Clear()
    {
        _table = new();
    }
}
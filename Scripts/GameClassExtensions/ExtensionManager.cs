using EmpireCraft.Scripts;
using NeoModLoader.General.Game.extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

public static class ExtensionManager<TKey, TData>
    where TKey : class
    where TData : class, new()
{
    private static ConditionalWeakTable<TKey, TData> _table = new();

    public static TData GetOrCreate(TKey key)
    {
        if (ModClass.IS_CLEAR) return default;
        var d = _table.GetOrCreateValue(key);
        return d;
    }

    public static bool Remove(TKey key)
    {
        if (ModClass.IS_CLEAR) return true;
        return _table.Remove(key);
    }

    public static void Clear()
    {
        _table = new();
    }
}
using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems;

// 套在原版图层委托外面的几层(图层补洞 EnclaveFill、无人区并进国土 UI.Components.WildernessTerritory)各自每帧对一遍：
// 自己套的那层还在不在眼下的委托里头(一层层往里找)。不在(被别处整个换掉了，或头一回)才再套——
// 只比"是不是自己套的"的话，两处互相套在对方外面，每帧各多套一层
internal static class LayerHooks
{
    // 套上去的委托 → 套在它里面的那个
    private static readonly Dictionary<Delegate, Delegate> Inner = new();

    internal static void Add(Delegate hook, Delegate inner)
    {
        if (hook != null) Inner[hook] = inner;
    }

    internal static bool Contains(Delegate current, Delegate mine)
    {
        if (mine == null) return false;
        for (Delegate d = current; d != null; d = Inner.TryGetValue(d, out Delegate next) ? next : null)
            if (ReferenceEquals(d, mine)) return true;
        return false;
    }
}

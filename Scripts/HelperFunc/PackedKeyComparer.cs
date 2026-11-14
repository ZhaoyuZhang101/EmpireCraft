using System.Collections.Generic;

namespace EmpireCraft.Scripts.HelperFunc;

// 两个编号拼成的 long 键((long)a << 32 | b)用的比较器：.NET 的 long 取哈希是高低两半异或，拼出来的键就成了 a ^ b，
// 相邻地块、相近的编号异或出来的数大量撞在一起，哈希表退化成长链(2026-11-14 实测 RoadNetwork.Spans 一次要半微秒多，
// 战线段地形层九成时间花在这里)。这里先乘一个大奇数把位打散再取高位
public sealed class PackedKeyComparer : IEqualityComparer<long>
{
    public static readonly PackedKeyComparer Instance = new();

    public bool Equals(long x, long y) => x == y;

    public int GetHashCode(long key) => unchecked((int)((ulong)(key * -7046029254386353131L) >> 32));
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EmpireCraft.Server;

// 替身层的公共部分。生成的空壳(Shim/Generated)里，还没手写实现的方法都落到 Miss：
// 记一笔是谁被调用了、调了多少回，回一个默认值，程序接着跑。按这本账的次数从多到少补实现。
public static class Shim
{
    private static readonly ConcurrentDictionary<string, int> Missing = new();
    // 头一回碰到就在控制台说一声(排查时打开)
    public static bool Verbose;

    public static void Miss(string member)
    {
        if (Missing.AddOrUpdate(member, 1, (_, count) => count + 1) == 1 && Verbose)
            Console.WriteLine("[替身] 还没实现: " + member);
    }

    public static T Miss<T>(string member)
    {
        Miss(member);
        return default;
    }

    // 壳里"可遍历"的类型：手写的 partial 实现这个接口给出真东西；没实现就是空的
    public interface IShimItems<T>
    {
        IEnumerable<T> ShimItems();
    }

    public static IEnumerator<T> Items<T>(object owner, string member)
    {
        if (owner is IShimItems<T> source) return source.ShimItems().GetEnumerator();
        Miss(member);
        return Enumerable.Empty<T>().GetEnumerator();
    }

    // 壳里那些"原版一上来就建好的帮手对象"(world.city_zone_helper.city_place_finder 之类)：公共字段是空的、
    // 类型又是原版的普通类(不是人、城、国、地块、资产这些实体)，就给它建一个，往下再走两层
    public static void FillHelpers(object owner, int depth = 2)
    {
        if (owner == null || depth <= 0) return;
        foreach (System.Reflection.FieldInfo field in owner.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            Type type = field.FieldType;
            // 数组空着的给个零长的(原版多半在别处填好，模组拿来就遍历)
            if (type.IsArray && type.GetArrayRank() == 1 && field.GetValue(owner) == null)
            {
                field.SetValue(owner, Array.CreateInstance(type.GetElementType(), 0));
                continue;
            }
            if (!type.IsClass || type.IsAbstract || type.IsArray || type == typeof(string) || !string.IsNullOrEmpty(type.Namespace) || type.IsGenericType) continue;
            // 装备：服务器不造物件(传国玉玺这类宝物要真的物件)，人身上不配装备栏，模组见了没有装备栏就不办
            if (type == typeof(ActorEquipment)) continue;
            if (typeof(NanoObject).IsAssignableFrom(type) || typeof(Asset).IsAssignableFrom(type) || typeof(UnityEngine.Object).IsAssignableFrom(type) ||
                typeof(Delegate).IsAssignableFrom(type) || type == typeof(WorldTile) || type == typeof(TileZone) || type.GetConstructor(Type.EmptyTypes) == null) continue;
            object value = field.GetValue(owner);
            if (value == null)
            {
                try
                {
                    value = Activator.CreateInstance(type);
                }
                catch (Exception)
                {
                    continue;
                }
                field.SetValue(owner, value);
            }
            FillHelpers(value, depth - 1);
        }
    }

    public static int MissingKinds => Missing.Count;

    // 账本：调用最多的在前
    public static string Report(int top = 60)
    {
        var text = new StringBuilder();
        foreach (KeyValuePair<string, int> pair in Missing.OrderByDescending(pair => pair.Value).Take(top))
            text.Append(pair.Value.ToString().PadLeft(9)).Append("  ").AppendLine(pair.Key);
        return text.ToString();
    }
}

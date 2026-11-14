// 资产库：模组往里登记自己的内容(法则、特质、世界提示、地块……)，也按名字来取原版的内容。
// 原版自带的那些内容服务器里没有：按名字取不到时给一个只有名字的空资产并记一笔，免得处处空引用；has 照实回没有。
//# hand AssetLibrary`1: a add clone get has t
//# hand BaseAssetLibrary: getList linkAssets
//# hand Asset: create
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EmpireCraft.Server;

public partial class Asset
{
    public virtual void create()
    {
    }

    // 复制一份：自己的集合、数组另起一份，免得改了副本连原件一起变
    internal Asset ShimClone()
    {
        var copy = (Asset)MemberwiseClone();
        for (Type type = GetType(); type != null && type != typeof(object); type = type.BaseType)
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object value = field.GetValue(this);
                if (value == null) continue;
                if (value is Array array) field.SetValue(copy, array.Clone());
                else if (value is ICollection && field.FieldType.IsGenericType && field.FieldType.Namespace == "System.Collections.Generic")
                {
                    try
                    {
                        field.SetValue(copy, Activator.CreateInstance(field.FieldType, value));
                    }
                    catch (Exception)
                    {
                        // 没有复制构造的集合：共用原件
                    }
                }
            }
        return copy;
    }
}

public partial class BaseAssetLibrary
{
    public virtual IEnumerable<Asset> getList() => Array.Empty<Asset>();

    public virtual void linkAssets()
    {
    }
}

public partial class AssetLibrary<T>
{
    private readonly Dictionary<string, T> _phantoms = new();
    private T _last;

    // 库里最近登记的那一个；原版自带内容的库在服务器里是空的，取它时给个占位的
    public T t
    {
        get => _last ??= get("_");
        set => _last = value;
    }

    public static TA[] a<TA>(params TA[] pArgs) => pArgs;
    public override IEnumerable<Asset> getList() => list;

    public virtual T add(T pAsset)
    {
        if (pAsset == null) return null;
        string id = pAsset.id ?? "";
        if (dict.TryGetValue(id, out T old)) list.Remove(old);
        dict[id] = pAsset;
        list.Add(pAsset);
        _phantoms.Remove(id);
        t = pAsset;
        pAsset.create();
        return pAsset;
    }

    public virtual bool has(string pID) => pID != null && dict.ContainsKey(pID);

    public virtual T get(string pID)
    {
        if (pID == null) return null;
        if (dict.TryGetValue(pID, out T found)) return found;
        if (_phantoms.TryGetValue(pID, out found)) return found;
        Shim.Miss("资产 " + GetType().Name + " [" + pID + "]");
        try
        {
            found = Activator.CreateInstance<T>();
            found.id = pID;
        }
        catch (Exception)
        {
            found = null;
        }
        return _phantoms[pID] = found;
    }

    public virtual T clone(string pNew, string pFrom)
    {
        T source = get(pFrom);
        T copy = source == null ? Activator.CreateInstance<T>() : (T)source.ShimClone();
        copy.id = pNew;
        return add(copy);
    }
}

public partial class AssetManager
{
    // 各个库一上来就都建好(空的)，模组初始化时往里登记
    static AssetManager()
    {
        foreach (FieldInfo field in typeof(AssetManager).GetFields(BindingFlags.Static | BindingFlags.Public))
        {
            if (field.GetValue(null) != null || field.FieldType.IsAbstract || field.FieldType.GetConstructor(Type.EmptyTypes) == null) continue;
            field.SetValue(null, Activator.CreateInstance(field.FieldType));
        }
        // 地块表是服务器自己的(见 ShimTiles)
        ShimTiles.Init();
        // 种族与王国类型也是(见 ShimSpecies)
        ShimSpecies.Init();
        // 货物表(粮、木、石、金……；见 ShimResources)
        ShimResources.Init();
        // 城市忠诚的底子(见 ShimLoyalty)
        ShimLoyalty.Init();
        // 各库里以静态字段点名的原版内容(PlotsLibrary.alliance_create、TileLibrary.hills……)：先各放一个只有名字的，
        // 模组初始化时会去改它们；真正的数值由服务器自己的表后补
        foreach (FieldInfo field in typeof(AssetManager).GetFields(BindingFlags.Static | BindingFlags.Public))
        {
            if (field.GetValue(null) is not BaseAssetLibrary library) continue;
            MethodInfo add = library.GetType().GetMethod("add");
            foreach (FieldInfo named in library.GetType().GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (!typeof(Asset).IsAssignableFrom(named.FieldType) || named.FieldType.IsAbstract || named.GetValue(null) != null) continue;
                try
                {
                    var asset = (Asset)Activator.CreateInstance(named.FieldType);
                    asset.id = named.Name;
                    named.SetValue(null, asset);
                    if (add != null && add.GetParameters()[0].ParameterType.IsInstanceOfType(asset)) add.Invoke(library, new object[] { asset });
                }
                catch (Exception)
                {
                    // 建不出来的留空
                }
            }
        }
    }
}

//# hand T:BaseStats
//# hand T:CustomDataContainer`1
// 一组按名字存的数值(特质、装备等给的加成)
public class BaseStats
{
    private readonly Dictionary<string, float> _values = new();
    internal Dictionary<string, float> ShimValues => _values;

    public float this[string pID]
    {
        get => pID != null && _values.TryGetValue(pID, out float value) ? value : 0f;
        set
        {
            if (pID != null) _values[pID] = value;
        }
    }

    public float get(string pID) => this[pID];
    public bool hasStat(string pID) => pID != null && _values.ContainsKey(pID);
    public bool hasStats() => _values.Count > 0;
    public void clear() => _values.Clear();
    public IEnumerable<KeyValuePair<string, float>> getList() => _values;
}

public class CustomDataContainer<TType>
{
    public Dictionary<string, TType> dict = new();

    public TType this[string pKey]
    {
        get => pKey != null && dict.TryGetValue(pKey, out TType value) ? value : default;
        set
        {
            if (pKey != null) dict[pKey] = value;
        }
    }

    public IEnumerable<string> Keys => dict.Keys;
}

// 货物表：哪种货、是不是吃的、价钱和仓储上限(数是从原版表里抽的，见 Resources.g.cs)；模组自己加的货照它自己登记
internal static partial class ShimResources
{
    private static void Add(ResourceAsset asset)
    {
        if (!AssetManager.resources.dict.ContainsKey(asset.id)) AssetManager.resources.add(asset);
    }
}

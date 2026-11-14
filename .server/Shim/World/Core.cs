// 世界核心的底座：对象、数据、管理器、日期、随机数、加载步骤队列。
// 名字与签名照模组用到的原版接口，实现是服务器自己的：只管数据，不画图、不驱动小人。
//# hand NanoObject: Dispose getColor getID hasDied isAlive setAlive setDefaultValues setHash setName
//# hand NanoObjectExtensions: isRekt
//# hand CoreSystemObject`1: Dispose getAge isFavorite loadData save updateColor name
//# hand BaseSystemData: get
//# hand MetaObjectData: setColorID
//# hand BaseSystemManager: Count checkLists clear hasAny startCollectHistoryData
//# hand SystemManager`2: addObject clear get newObjectFromID removeObject update
//# hand CoreSystemManager`2: GetEnumerator addObject clear removeObject
//# hand MetaSystemManager`2: addObject startCollectHistoryData updateDirtyUnits
//# hand Date: getCurrentMonth getCurrentYear getDate getMonth getMonthTime getMonthsSince getYear getYearsSince
//# hand Randy: LoopRandom randomChance randomFloat randomInt resetSeed
//# hand SmoothLoader: add isLoading prepare
//# hand Toolbox: SquaredDistTile SquaredDist DistTile makeColor DistVec2Float loopIndex
//# hand ListExtensions: GetRandom Any First Last Shuffle Swap
//# hand ArrayExtensions: Contains Last First
using System;
using System.Collections.Generic;
using EmpireCraft.Server;
using UnityEngine;

public partial class NanoObject
{
    // 一建出来就算在世(同原版)：模组读档时帝国、法理是直接 new 出来登记的，不经管理器的"新建"
    private bool _alive = true;
    private int _hash;

    public bool isAlive() => _alive;
    public virtual void setAlive(bool pValue) => _alive = pValue;
    public virtual bool hasDied() => !_alive;
    public virtual long getID() => id;
    public virtual ColorAsset getColor() => null;
    public void setHash(int pHash) => _hash = pHash;
    public void setName(string pName, bool pTrack = true) => name = pName;

    public virtual void setDefaultValues()
    {
    }

    public virtual void Dispose() => _alive = false;
    public override int GetHashCode() => _hash != 0 ? _hash : base.GetHashCode();
}

public static partial class NanoObjectExtensions
{
    // 模组里到处用它判"这东西还在不在"
    public static bool isRekt(this NanoObject pObject) => pObject == null || !pObject.isAlive();
}

// 对象怎么接上它的数据(管理器新建对象时用)
public partial interface ILoadable<TData>
{
    void loadData(TData pData)
    {
    }
}

public partial class CoreSystemObject<TData>
{
    // 名字存在数据里，对象上的 name 只是转过去
    public override string name
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        get => data?.name;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        set
        {
            if (data != null) data.name = value;
        }
    }

    public virtual void loadData(TData pData)
    {
        data = pData;
        if (pData != null) id = pData.id;
    }

    public virtual int getAge() => data == null ? 0 : Date.getYearsSince(data.created_time);
    public bool isFavorite() => false;

    public virtual void save()
    {
    }

    public virtual bool updateColor(ColorAsset pColor) => true;
    public override void Dispose() => setAlive(false);
}

public partial class BaseSystemData
{
    // 原版的自定义键值；服务器里只按默认值回
    public void get(string pKey, out int pResult, int pDefault = 0) => pResult = pDefault;
    public void get(string pKey, out long pResult, long pDefault = 0L) => pResult = pDefault;
    public void get(string pKey, out float pResult, float pDefault = 0f) => pResult = pDefault;
}

public partial class MetaObjectData
{
    public void setColorID(int pColorID)
    {
        color_id = pColorID;
        if (original_color_id == 0) original_color_id = pColorID;
    }
}

public partial class BaseSystemManager
{
    public virtual int Count => 0;
    public virtual bool hasAny() => Count > 0;

    public virtual void checkLists()
    {
    }

    public virtual void clear()
    {
    }

    public virtual void startCollectHistoryData()
    {
    }
}

public partial class SystemManager<TObject, TData>
{
    protected readonly Dictionary<long, TObject> _byId = new();
    protected long _nextId = 1;

    public override int Count => _byId.Count;
    public TObject get(long pID) => _byId.TryGetValue(pID, out TObject found) ? found : null;

    // 新建一个对象并登记(原版的 newObject)
    public TObject newObject() => newObjectFromID(_nextId);

    // 新建时先做一点准备(接上数据之前)：模组挂在"接上数据"这一步的补丁要看到备好的东西
    public TObject newObject(Action<TObject> prepare) => newObjectFromID(_nextId, prepare);

    public TObject newObjectFromID(long pID) => newObjectFromID(pID, null);

    private TObject newObjectFromID(long pID, Action<TObject> prepare)
    {
        if (pID >= _nextId) _nextId = pID + 1;
        var created = new TObject();
        prepare?.Invoke(created);
        var data = new TData { id = pID, created_time = World.world?.getCurWorldTime() ?? 0d };
        created.id = pID;
        // 原版一上来就建好的那些帮手(国的 ai、城的 status 之类)
        Shim.FillHelpers(created, 1);
        created.loadData(data);
        created.setAlive(true);
        addObject(created);
        return created;
    }

    // 读档：照存下来的数据把对象建回来(编号不变；模组挂在"接上数据"这一步的补丁照常跑)
    internal TObject ShimRestore(TData data, Action<TObject> prepare = null)
    {
        if (data.id >= _nextId) _nextId = data.id + 1;
        var created = new TObject();
        prepare?.Invoke(created);
        created.id = data.id;
        Shim.FillHelpers(created, 1);
        created.loadData(data);
        created.setAlive(true);
        addObject(created);
        return created;
    }

    // 下一个新对象的编号：随档存着，读档后不把已故之人、已亡之国的编号再发一遍(族谱、史书里还记着它们)
    internal long ShimNextId
    {
        get => _nextId;
        set => _nextId = Math.Max(_nextId, value);
    }

    public virtual void addObject(TObject pObject)
    {
        if (pObject != null) _byId[pObject.id] = pObject;
    }

    public virtual void removeObject(TObject pObject)
    {
        if (pObject == null) return;
        _byId.Remove(pObject.id);
        pObject.setAlive(false);
    }

    public override void clear()
    {
        foreach (TObject item in _byId.Values) item.setAlive(false);
        _byId.Clear();
        _nextId = 1;
    }

    public virtual void update(float pElapsed)
    {
    }
}

public partial class CoreSystemManager<TObject, TData> : Shim.IShimItems<TObject>
{
    public IEnumerator<TObject> GetEnumerator() => list.GetEnumerator();
    IEnumerable<TObject> Shim.IShimItems<TObject>.ShimItems() => list;

    public override void addObject(TObject pObject)
    {
        if (pObject == null || _byId.ContainsKey(pObject.id)) return;
        base.addObject(pObject);
        list.Add(pObject);
    }

    public override void removeObject(TObject pObject)
    {
        if (pObject == null) return;
        list.Remove(pObject);
        base.removeObject(pObject);
    }

    public override void clear()
    {
        list.Clear();
        base.clear();
    }
}

public partial class MetaSystemManager<TObject, TData>
{
    public override void addObject(TObject pObject) => base.addObject(pObject);

    public override void startCollectHistoryData()
    {
    }

    // 各国各城的人口名单是随增随减的，没有"脏了再重算"这回事
    public virtual void updateDirtyUnits()
    {
    }
}

// 历法：世界时间以秒计，五秒一月、六十秒一年(模组各处也是按这个数算的)
public static partial class Date
{
    public const double SecondsPerMonth = 5d, SecondsPerYear = 60d;
    private static double Now => World.world?.getCurWorldTime() ?? 0d;

    public static int getYearsSince(double pFrom) => (int)((Now - pFrom) / SecondsPerYear);
    public static int getMonthsSince(double pFrom) => (int)((Now - pFrom) / SecondsPerMonth);
    public static float getMonthTime() => (float)SecondsPerMonth;
    public static int getYear(double pTime) => (int)(pTime / SecondsPerYear) + 1;
    public static int getMonth(double pTimestamp) => (int)(pTimestamp % SecondsPerYear / SecondsPerMonth) + 1;
    public static int getCurrentYear() => getYear(Now);
    public static int getCurrentMonth() => getMonth(Now);

    public static string getDate(double pTime) =>
        NeoModLoader.General.LM.Language == "en" ? "Y" + getYear(pTime) + " M" + getMonth(pTime) : getYear(pTime) + "年" + getMonth(pTime) + "月";
}

public static partial class Randy
{
    private static System.Random _random = new();
    private static readonly object Gate = new();

    public static void resetSeed(int pIntValue)
    {
        lock (Gate) _random = new System.Random(pIntValue);
    }

    public static bool randomChance(float pVal)
    {
        lock (Gate) return _random.NextDouble() < pVal;
    }

    public static int randomInt(int pMinInclusive, int pMaxExclusive)
    {
        if (pMaxExclusive <= pMinInclusive) return pMinInclusive;
        lock (Gate) return _random.Next(pMinInclusive, pMaxExclusive);
    }

    public static float randomFloat(float pMinInclusive, float pMaxExclusive)
    {
        lock (Gate) return pMinInclusive + (pMaxExclusive - pMinInclusive) * (float)_random.NextDouble();
    }

    // 从随便一处起头把列表走一圈
    public static IEnumerable<T> LoopRandom<T>(this List<T> list)
    {
        int count = list?.Count ?? 0;
        if (count == 0) yield break;
        int start = randomInt(0, count);
        for (int i = 0; i < count && i < list.Count; i++) yield return list[(start + i) % list.Count];
    }
}

// 加载步骤队列：模组开局(WorldMapGenerator、WorldMapScenario)把要办的事一步步排进来，服务器的主循环一步步执行。
// 执行当中再加的步骤排在当前这步后面(同原版)
public static partial class SmoothLoader
{
    private static readonly List<(MapLoaderAction action, string id)> Steps = new();
    private static int _cursor, _insertAt = -1;
    public static string CurrentStep { get; private set; } = "";
    public static int Done => _cursor;
    public static int Total => Steps.Count;

    public static void prepare()
    {
        Steps.Clear();
        _cursor = 0;
        _insertAt = -1;
        CurrentStep = "";
    }

    public static void add(MapLoaderAction pAction, string pId, bool pSkipFrame = false, float pNewWaitTimerValue = 0.001f, bool pToEnd = false)
    {
        if (pAction == null) return;
        if (_insertAt >= 0 && !pToEnd) Steps.Insert(_insertAt++, (pAction, pId));
        else Steps.Add((pAction, pId));
    }

    public static bool isLoading() => _cursor < Steps.Count;

    // 执行下一步；回的是"还有没有"
    internal static bool RunNext()
    {
        if (_cursor >= Steps.Count) return false;
        (MapLoaderAction action, string id) = Steps[_cursor];
        CurrentStep = id ?? "";
        _insertAt = _cursor + 1;
        try
        {
            action();
        }
        finally
        {
            _insertAt = -1;
            _cursor++;
        }
        return _cursor < Steps.Count;
    }
}

public partial class Toolbox
{
    static Toolbox()
    {
        color_log_good = new Color(0.36f, 0.83f, 0.36f);
        color_log_warning = new Color(0.93f, 0.33f, 0.28f);
        color_log_neutral = new Color(0.95f, 0.87f, 0.45f);
    }

    public static int SquaredDistTile(WorldTile pT1, WorldTile pT2)
    {
        int dx = pT1.x - pT2.x, dy = pT1.y - pT2.y;
        return dx * dx + dy * dy;
    }

    public static float SquaredDist(float x1, float y1, float x2, float y2) => (x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1);
    public static float DistTile(WorldTile pT1, WorldTile pT2) => Mathf.Sqrt(SquaredDistTile(pT1, pT2));
    public static float DistVec2Float(Vector2 pT1, Vector2 pT2) => Vector2.Distance(pT1, pT2);
    public static int loopIndex(int pIndex, int pLength) => pLength <= 0 ? 0 : (pIndex % pLength + pLength) % pLength;

    public static Color makeColor(string pHex)
    {
        ColorUtility.TryParseHtmlString(pHex, out Color color);
        return color;
    }
}

public static partial class ListExtensions
{
    public static T GetRandom<T>(this List<T> list) => list == null || list.Count == 0 ? default : list[Randy.randomInt(0, list.Count)];
    public static bool Any<T>(this List<T> list) => list != null && list.Count > 0;
    public static T First<T>(this List<T> list) => list[0];
    public static T Last<T>(this List<T> list) => list[list.Count - 1];

    public static void Shuffle<T>(this List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--) Swap(list, i, Randy.randomInt(0, i + 1));
    }

    public static void Swap<T>(this List<T> list, int i, int j) => (list[i], list[j]) = (list[j], list[i]);
}

public static partial class ArrayExtensions
{
    public static bool Contains<T>(this T[] pArray, T pValue) => pArray != null && Array.IndexOf(pArray, pValue) >= 0;
    public static T Last<T>(this T[] pArray) => pArray[pArray.Length - 1];
    public static T First<T>(this T[] pArray) => pArray[0];
}

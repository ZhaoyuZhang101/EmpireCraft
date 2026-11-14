// 原版给数组、列表、数字、文字加的那些小扩展。它们同标准库里的重名，空着会把标准库的遮住，所以都得有真东西。
//# hand ArrayExtensions: GetRandom IndexOf
//# hand HashSetExtensions: ToArray
//# hand IntExtensions: ToText
//# hand ListExtensions: Pop
//# hand ListPoolExtensions: Any GetRandom Where
//# hand LongExtension: hasValue
//# hand MetaTypeExtensions: AsString getAsset
//# hand StringExtension: Localize Underscore
//# hand TransformExtensions: FindAllRecursive FindRecursive
//# hand ButtonExtensions: OnHover OnHoverOut
//# hand WorldLogMessageExtensions: add getAsset
//# hand WorldLogMessage: ctor
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

public static partial class ArrayExtensions
{
    public static T GetRandom<T>(this T[] pArray) => pArray == null || pArray.Length == 0 ? default : pArray[Randy.randomInt(0, pArray.Length)];
    public static int IndexOf<T>(this T[] pArray, T pValue) => pArray == null ? -1 : Array.IndexOf(pArray, pValue);
}

public static partial class HashSetExtensions
{
    public static T[] ToArray<T>(this HashSet<T> pHashSet)
    {
        if (pHashSet == null) return Array.Empty<T>();
        var copy = new T[pHashSet.Count];
        pHashSet.CopyTo(copy);
        return copy;
    }
}

public static partial class IntExtensions
{
    public static string ToText(this int pInt) => pInt.ToString("N0", CultureInfo.InvariantCulture);
}

public static partial class ListExtensions
{
    public static T Pop<T>(this List<T> list)
    {
        if (list == null || list.Count == 0) return default;
        T last = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        return last;
    }
}

public static partial class ListPoolExtensions
{
    public static bool Any<T>(this ListPool<T> list) => list != null && list.Count > 0;
    public static T GetRandom<T>(this ListPool<T> list) => list == null || list.Count == 0 ? default : list[Randy.randomInt(0, list.Count)];

    public static IEnumerable<T> Where<T>(this ListPool<T> pList, Func<T, bool> pPredicate)
    {
        if (pList == null) yield break;
        for (int i = 0; i < pList.Count; i++)
            if (pPredicate(pList[i])) yield return pList[i];
    }
}

public static partial class LongExtension
{
    // 编号"有没有"：没有记作 -1
    public static bool hasValue(this long pLong) => pLong != -1L;
}

public static partial class MetaTypeExtensions
{
    [MethodImpl(P.N)]
    public static string AsString(this MetaType pType) => pType.ToString().ToLowerInvariant();

    public static MetaTypeAsset getAsset(this MetaType pType) => AssetManager.meta_type_library.get(pType.AsString());
}

public static partial class StringExtension
{
    [MethodImpl(P.N)]
    public static string Localize(this string pString) => NeoModLoader.General.LM.Get(pString);

    public static string Underscore(this string pString)
    {
        if (string.IsNullOrEmpty(pString)) return pString ?? "";
        var text = new StringBuilder();
        for (int i = 0; i < pString.Length; i++)
        {
            char c = pString[i];
            if (char.IsUpper(c) && i > 0 && pString[i - 1] != '_') text.Append('_');
            text.Append(char.ToLowerInvariant(c));
        }
        return text.ToString();
    }
}

public static partial class TransformExtensions
{
    public static T[] FindAllRecursive<T>(this Transform pTransform) => Array.Empty<T>();
    public static Transform FindRecursive(this Transform pTransform, string pName) => pTransform?.Find(pName);
}

public static partial class ButtonExtensions
{
    public static void OnHover(this UnityEngine.UI.Button button, UnityEngine.Events.UnityAction call)
    {
    }

    public static void OnHoverOut(this UnityEngine.UI.Button button, UnityEngine.Events.UnityAction call)
    {
    }
}

// 世界提示：模组各系统报事都从这里出去；屏幕上的提示条服务器里没有，
// 但模组挂在"上屏"这一步的补丁照常跑(手机上的奏报就是从那里接的)
public partial class WorldLogMessage
{
    public WorldLogMessage()
    {
    }

    public WorldLogMessage(WorldLogAsset pAsset, string pSpecial1 = null, string pSpecial2 = null, string pSpecial3 = null)
    {
        asset_id = pAsset?.id;
        special1 = pSpecial1;
        special2 = pSpecial2;
        special3 = pSpecial3;
        timestamp = (int)(World.world?.getCurWorldTime() ?? 0d);
        kingdom_id = -1L;
    }
}

public static partial class WorldLogMessageExtensions
{
    private static HistoryHud _hud;

    [MethodImpl(P.N)]
    public static void add(this WorldLogMessage pMessage)
    {
        if (pMessage == null) return;
        (_hud ??= new HistoryHud()).newHistory(pMessage);
    }

    public static WorldLogAsset getAsset(this WorldLogMessage pMessage) => pMessage?.asset_id == null ? null : AssetManager.world_log_library.get(pMessage.asset_id);
}

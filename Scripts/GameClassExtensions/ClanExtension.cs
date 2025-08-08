using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;
namespace EmpireCraft.Scripts.GameClassExtensions;
public static class ClanExtension
{
    public class ClanExtraData:ExtraDataBase
    {
        // todo: 添加需要存储的氏族数据
    }

    public static ClanExtraData GetOrCreate(this Clan a, bool isSave = false)
    {
        var ed = a.GetOrCreate<Clan, ClanExtraData>(isSave);
        return ed;
    }
    public static void Clear()
    {
        ExtensionManager<Clan, ClanExtraData>.Clear();
    }
}
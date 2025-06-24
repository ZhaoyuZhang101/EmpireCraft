using EmpireCraft.Scripts.Enums;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;

namespace EmpireCraft.Scripts.GameClassExtensions;
public static class WarExtension
{
    public class WarExtraData
    {
        public long id = -1L;
        public EmpireWarType empireWarType = EmpireWarType.None;

    }
    public static bool syncData(this War a, WarExtraData actorExtraData)
    {
        var ed = GetOrCreate(a);
        ed.id = actorExtraData.id;
        ed.empireWarType = actorExtraData.empireWarType;
        return true;
    }

    public static void RemoveExtraData(this War a)
    {
        if (a == null) return;
        ExtensionManager<War, WarExtraData>.Remove(a);
    }

    public static WarExtraData getExtraData(this War a, bool isSave = false)
    {
        if (GetOrCreate(a, isSave) == null) return null;
        WarExtraData data = new WarExtraData();
        data.id = a.getID();
        data.empireWarType = a.GetEmpireWarType();
        return data;
    }

    public static void SetEmpireWarType(this War w, EmpireWarType type)
    {
        GetOrCreate(w).empireWarType = type;
    }
    public static EmpireWarType GetEmpireWarType(this War w)
    {
        return GetOrCreate(w).empireWarType;
    }
    public static void Clear()
    {
        ExtensionManager<War, WarExtraData>.Clear();
    }
    public static WarExtraData GetOrCreate(this War a, bool isSave = false)
    {
        var ed = ExtensionManager<War, WarExtraData>.GetOrCreate(a, isSave);
        return ed;
    }
}

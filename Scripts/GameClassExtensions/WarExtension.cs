using EmpireCraft.Scripts.Enums;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Layer;
using NCMS.Extensions;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;

namespace EmpireCraft.Scripts.GameClassExtensions;
public static class WarExtension
{
    public class WarExtraData: ExtraDataBase
    {
        public EmpireWarType empireWarType = EmpireWarType.None;
        public MetaType metaType = MetaType.None;
        public long metaID = -1L;
    }

    public static void SetEmpireWarType(this War w, EmpireWarType type, string pre="", NanoObject nanoObject = null)
    {
        GetOrCreate(w).empireWarType = type;
        Empire empire = w.main_attacker.GetEmpire();
        if (empire != null)
        {
            w.data.name = empire.name + type + "战争";
        }
        else
        {
            w.data.name = (string.IsNullOrEmpty(pre)?w.main_attacker?.name:pre) + type + "战争";
        }

        if (nanoObject != null)
        {
            w.GetOrCreate().metaType = nanoObject.meta_type;
            w.GetOrCreate().metaID = nanoObject.id;
        }
        switch (type)
        {
            case EmpireWarType.索取法理:
                var title = (KingdomTitle)nanoObject;
                w.data.name = $"{w.getMainAttacker()?.name}索取{title?.name}法理战争";
                break;
        }
    }

    public static KingdomTitle GetTitleTarget(this War w)
    {
        var metaType = w.GetOrCreate().metaType;
        switch (metaType)
        {
            case MetaTypeExtension.KingdomTitle:
                return ModClass.KINGDOM_TITLE_MANAGER.get(w.GetOrCreate().metaID);
        }
        return null;
    }
    public static EmpireWarType GetEmpireWarType(this War w)
    {
        return GetOrCreate(w).empireWarType;
    }
    public static WarExtraData GetOrCreate(this War a, bool isSave = false)
    {
        var ed = a.GetOrCreate<War, WarExtraData>(isSave);
        return ed;
    }
}

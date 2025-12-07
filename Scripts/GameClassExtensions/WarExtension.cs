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

    }

    public static void SetEmpireWarType(this War w, EmpireWarType type)
    {
        GetOrCreate(w).empireWarType = type;
        Empire empire = w.main_attacker.GetEmpire();
        if (empire != null)
        {
            switch (type)
            {
                case EmpireWarType.攘夷:
                    w.data.name = empire.name + type.ToString() + "战争";
                    break;
                case EmpireWarType.统一:
                    w.data.name = empire.name + type.ToString() + "战争";
                    break;
                case EmpireWarType.迫使朝贡:
                    w.data.name = empire.name + type.ToString() + "战争";
                    break;
                case EmpireWarType.伐不臣:
                    w.data.name = empire.name + type.ToString() + "战争";
                    break;
            }
        }
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

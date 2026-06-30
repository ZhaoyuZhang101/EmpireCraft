using EmpireCraft.Scripts.Enums;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
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
        public FixedFaction belongingFaction = null;
        public long linkedWarId = -1L;
    }

    public static void SetEmpireWarType(this War w, EmpireWarType type, string pre="", NanoObject nanoObject = null, bool isRebelling = false, FixedFaction belongingFaction = null)
    {
        GetOrCreate(w).empireWarType = type;
        Empire empire = w.main_attacker.GetEmpire();
        if (empire != null)
        {
            w.data.name = empire.name + type + (!isRebelling?"战争":"");
        }
        else
        {
            w.data.name = (string.IsNullOrEmpty(pre)?w.main_attacker?.name:pre) + type + (!isRebelling?"战争":"");
        }
        if (belongingFaction != null)
        {
            w.GetOrCreate().belongingFaction = belongingFaction;
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

    public static Empire GetEmpireTarget(this War w)
    {
        var metaType = w.GetOrCreate().metaType;
        switch (metaType)
        {
            case MetaTypeExtension.Empire:
                return ModClass.EMPIRE_MANAGER.get(w.GetOrCreate().metaID);
        }
        return null;
    }
    public static EmpireWarType GetEmpireWarType(this War w)
    {
        return GetOrCreate(w).empireWarType;
    }
    public static FixedFaction GetEmpireFaction(this War w)
    {
        return GetOrCreate(w).belongingFaction;
    }

    public static void SetLinkedWar(this War war, War linkedWar)
    {
        if (war == null)
        {
            return;
        }

        war.GetOrCreate().linkedWarId = linkedWar == null ? -1L : linkedWar.getID();
    }

    public static War GetLinkedWar(this War war)
    {
        if (war == null)
        {
            return null;
        }

        long linkedWarId = war.GetOrCreate().linkedWarId;
        if (linkedWarId < 0 || World.world?.wars == null)
        {
            return null;
        }

        return World.world.wars.get(linkedWarId);
    }

    public static bool HasAliveLinkedWar(this War war)
    {
        War linkedWar = war.GetLinkedWar();
        return linkedWar != null && !linkedWar.isRekt() && linkedWar.isAlive() && !linkedWar.hasEnded();
    }
    public static WarExtraData GetOrCreate(this War a, bool isSave = false)
    {
        var ed = a.GetOrCreate<War, WarExtraData>(isSave);
        return ed;
    }
}

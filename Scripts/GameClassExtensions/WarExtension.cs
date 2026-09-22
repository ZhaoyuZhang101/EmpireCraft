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
        public bool history_declaration_recorded;
        public bool history_end_recorded;
        public List<DefeatedEmpireHouse> attacker_empire_houses = new List<DefeatedEmpireHouse>();
        public List<DefeatedEmpireHouse> defender_empire_houses = new List<DefeatedEmpireHouse>();
        public List<long> defender_realm_title_ids = new List<long>();
        public string defender_kingdom_name = "";
        public bool royal_houses_recorded;
        public long legitimacy_challenger_empire_id = -1L;
        public long legitimacy_challenger_core_kingdom_id = -1L;
        public long legitimacy_challenger_emperor_id = -1L;
        public string legitimacy_challenger_empire_name = "";
        public long legitimacy_defender_empire_id = -1L;
        public long legitimacy_defender_core_id = -1L;
        public long legitimacy_defender_core_kingdom_id = -1L;
        public long legitimacy_defender_emperor_id = -1L;
        public string legitimacy_defender_empire_name = "";
        public List<long> legitimacy_defender_kingdom_ids = new List<long>();
        public List<long> legitimacy_defender_city_ids = new List<long>();
        public int legitimacy_initial_zone_count;
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
                Kingdom defender = w.getMainDefender();
                defender?.SyncRealmTitlesFromRuler();
                w.GetOrCreate().defender_realm_title_ids = defender?.GetRealmTitleIds()
                    .Distinct().ToList() ?? new List<long>();
                w.GetOrCreate().defender_kingdom_name = defender?.GetKingdomName() ?? defender?.name ?? "";
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

    public static bool IsMainDefenderEliminated(this War war)
    {
        Kingdom defender = war?.getMainDefender();
        return defender == null || defender.isRekt() || defender.cities == null || defender.cities.Count == 0;
    }

    public static bool AreAllDeJureTitleZonesControlledByAttackers(this War war,
        KingdomTitle title)
    {
        if (war == null || title == null || title.isRekt()) return false;
        int controlled = 0;
        int total = 0;
        HashSet<int> seenZones = new HashSet<int>();
        foreach (City city in title.getCities())
        {
            if (city == null || city.isRekt() || city.zones == null) continue;
            foreach (TileZone zone in city.zones)
            {
                if (zone == null || zone.world_edge || zone.city != city || !seenZones.Add(zone.id)) continue;
                total++;
                Kingdom controller = city.kingdom;
                if (controller == null || !war._list_attackers.Contains(controller))
                    controller = city.GetTileZoneOccupier(zone);
                if (controller != null && war._list_attackers.Contains(controller)) controlled++;
            }
        }
        return DeJureTitleClaimRules.ControlsAll(controlled, total);
    }

    public static void InitializeLegitimacyChallenge(this War war, Empire challenger, Empire defender)
    {
        if (war == null || challenger == null || defender == null) return;
        WarExtraData data = war.GetOrCreate();
        data.legitimacy_challenger_empire_id = challenger.id;
        data.legitimacy_challenger_core_kingdom_id = challenger.CoreKingdom?.id ?? -1L;
        data.legitimacy_challenger_emperor_id = challenger.Emperor?.id ?? -1L;
        data.legitimacy_challenger_empire_name = challenger.GetEmpireFullName();
        data.legitimacy_defender_empire_id = defender.id;
        data.legitimacy_defender_core_id = EmpireCoreManager.Get(defender)?.id ?? -1L;
        data.legitimacy_defender_core_kingdom_id = defender.CoreKingdom?.id ?? -1L;
        data.legitimacy_defender_emperor_id = defender.Emperor?.id ?? -1L;
        data.legitimacy_defender_empire_name = defender.GetEmpireFullName();
        data.legitimacy_defender_kingdom_ids = defender.kingdoms_list
            .Where(kingdom => kingdom != null && !kingdom.isRekt())
            .Select(kingdom => kingdom.id).Distinct().ToList();
        data.legitimacy_defender_city_ids = defender.kingdoms_list
            .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom.cities != null)
            .SelectMany(kingdom => kingdom.cities)
            .Where(city => city != null && !city.isRekt())
            .Select(city => city.id)
            .Distinct().ToList();

        var zoneIds = new HashSet<int>();
        foreach (long cityId in data.legitimacy_defender_city_ids)
        {
            City city = World.world?.cities?.get(cityId);
            if (city?.zones == null || city.isRekt()) continue;
            foreach (TileZone zone in city.zones)
                if (zone != null && !zone.world_edge && zone.city == city) zoneIds.Add(zone.id);
        }
        data.legitimacy_initial_zone_count = zoneIds.Count;
    }

    public static bool HasLegitimacyOccupationThreshold(this War war)
    {
        if (war == null) return false;
        WarExtraData data = war.GetOrCreate();
        if (data.legitimacy_initial_zone_count <= 0) return war.IsMainDefenderEliminated();

        int controlled = 0;
        var seenZones = new HashSet<int>();
        foreach (long cityId in data.legitimacy_defender_city_ids ?? new List<long>())
        {
            City city = World.world?.cities?.get(cityId);
            if (city?.zones == null || city.isRekt()) continue;
            foreach (TileZone zone in city.zones)
            {
                if (zone == null || zone.world_edge || zone.city != city || !seenZones.Add(zone.id)) continue;
                Kingdom controller = city.kingdom;
                if (controller == null || !war._list_attackers.Contains(controller))
                    controller = city.GetTileZoneOccupier(zone);
                if (controller != null && war._list_attackers.Contains(controller)) controlled++;
            }
        }
        return ImperialLegitimacyRules.HasOccupiedThird(controlled, data.legitimacy_initial_zone_count);
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
    public static WarExtraData GetOrCreate(this War a, bool isSave = false)
    {
        var ed = a.GetOrCreate<War, WarExtraData>(isSave);
        return ed;
    }
}

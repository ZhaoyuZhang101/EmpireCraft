// 国与城。城的辖地以区块计；国由城组成。这里只管"谁属于谁"这层数据：
// 粮食、房屋、守军、忠诚这些由模组自己的账本与系统算(它们的补丁挂在这里留着的空方法上)。
//# hand Kingdom: Dispose clearKingData copyMetasFromOtherKingdom countBuildings countCities countTotalFood countTotalWarriors countUnhappyCities countWarriorsMax countWeapons countZones distanceBetweenKingdom getAlliance getColor getEnemiesKingdoms getFounderSpecies getKingClan getMaxCities getMotto getPopulationPeople getPopulationTotal getPopulationTotalPossible getReligion getSpecies getSpeciesIcon getWars hasAlliance hasCapital hasCulture hasEnemies hasKing hasLanguage hasNearbyKingdoms hasReligion isAttacker isCiv isEnemy isInWarOnSameSide isInWarWith isNeutral isOpinionTowardsKingdomGood isSupreme kingFledCity removeKing setCapital setCityMetas setCulture setKing setReligion setUnitMetas
//# hand KingdomManager: beginChecksCities makeNewCivKingdom removeObject setDirtyCities
//# hand City: findKingdomToJoinAfterCapture newForceKingdomEvent Dispose addZone removeZone canGrowZones countZones hasZones destroyCity finishCapture clearCapture getActorAsset getCulture getFounderSpecies getPopulationPeople getPopulationMaximum getReligion getSpecies getTile hasCulture hasKingdom hasLeader hasReligion isCapitalCity isGettingCaptured isHappy isNeutral joinAnotherKingdom makeOwnKingdom reachableFrom recalculateCityTile recalculateNeighbourCities removeFromCurrentKingdom removeLeader setKingdom setLanguage setLeader setReligion setStatusDirty setUnitMetas switchedKingdom turnCityToNeutral updateCityCenter
//# hand CityManager: buildFirstCivilizationCity buildNewCity removeObject
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Server;
using UnityEngine;

public partial class Kingdom
{
    internal Alliance ShimAlliance;

    public override IEnumerable<City> getCities() => cities;
    public override bool hasCities() => cities.Count > 0;
    protected override bool ShimHasCity(City city) => city.kingdom == this;

    [MethodImpl(P.N)]
    public override ColorAsset getColor() => base.getColor();

    public bool isCiv() => !wild;
    public bool isNeutral() => false;
    public bool hasKing() => king != null && king.isAlive();
    public bool hasCapital() => capital != null && capital.isAlive();
    public bool hasCulture() => culture != null;
    public bool hasLanguage() => language != null;
    public bool hasReligion() => religion != null;
    public Religion getReligion() => religion;
    public void setCulture(Culture pCulture) => culture = pCulture;
    public void setReligion(Religion pReligion) => religion = pReligion;
    public int countCities() => cities.Count;
    public int countZones() => cities.Sum(city => city.zones.Count);
    public int countBuildings() => 0;
    public int countTotalFood() => 0;
    public int countUnhappyCities() => 0;
    public int countWeapons() => 0;
    public int countWarriorsMax() => cities.Sum(city => city.getMaxWarriors());

    [MethodImpl(P.N)]
    public int countTotalWarriors() => cities.Sum(city => city.countWarriors());

    // 能有几座城：服务器不设限(模组另有自己的规矩)
    [MethodImpl(P.N)]
    public int getMaxCities() => 9999;

    [MethodImpl(P.N)]
    public override int getPopulationPeople() => units.Count;

    public int getPopulationTotal() => getPopulationPeople();
    public int getPopulationTotalPossible() => cities.Sum(city => city.getPopulationMaximum());
    public string getSpecies() => data?.original_actor_asset ?? king?.asset?.id ?? "";
    public ActorAsset getFounderSpecies() => AssetManager.actor_library.get(getSpecies());
    public override ActorAsset getActorAsset() => getFounderSpecies();
    public Sprite getSpeciesIcon() => null;
    public virtual string getMotto() => "";
    public Clan getKingClan() => king?.clan ?? (data != null && data.royal_clan_id > 0 ? World.world.clans.get(data.royal_clan_id) : null);

    [MethodImpl(P.N)]
    public void setKing(Actor pActor, bool pFromLoad = false)
    {
        king = pActor;
        if (pActor == null) return;
        pActor.setProfession(UnitProfession.King);
        if (pActor.clan != null) data.royal_clan_id = pActor.clan.id;
        if (!pFromLoad) data.timestamp_king_rule = World.world.getCurWorldTime();
    }

    [MethodImpl(P.N)]
    public void removeKing()
    {
        Actor old = king;
        king = null;
        if (old != null && old.isAlive()) old.setProfession(UnitProfession.Unit);
    }

    public void clearKingData() => king = null;

    // 君主身故：君位空出来(谁来继位由月度核对与模组的继承规矩定)
    internal void ShimKingDied(Actor dead)
    {
        if (king == dead) king = null;
    }

    [MethodImpl(P.N)]
    public void setCapital(City pCity)
    {
        capital = pCity;
        if (pCity != null) location = new Vector3(pCity.city_center.x, pCity.city_center.y);
    }

    // 国家的文化、语言、宗教照这个人的来
    public void setUnitMetas(Actor pActor)
    {
        if (pActor == null) return;
        if (pActor.culture != null) culture = pActor.culture;
        if (pActor.language != null) language = pActor.language;
        if (pActor.religion != null) religion = pActor.religion;
    }

    public void setCityMetas(City pCity)
    {
        if (pCity == null) return;
        culture ??= pCity.culture;
        language ??= pCity.language;
        religion ??= pCity.religion;
    }

    public void copyMetasFromOtherKingdom(Kingdom pKingdom)
    {
        if (pKingdom == null) return;
        culture = pKingdom.culture;
        language = pKingdom.language;
        religion = pKingdom.religion;
    }

    public void kingFledCity()
    {
    }

    // ── 战与盟 ──
    public IEnumerable<War> getWars(bool pRandom = false) => World.world.wars.getWars(this, pRandom);
    public bool isInWarWith(Kingdom pKingdom) => pKingdom != null && pKingdom != this && World.world.wars.ShimAtWar(this, pKingdom);
    public bool isEnemy(Kingdom pKingdomTarget) => pKingdomTarget != null && pKingdomTarget != this && (wild || pKingdomTarget.wild || isInWarWith(pKingdomTarget));
    public bool hasEnemies() => World.world.wars.hasWars(this);

    public bool isInWarOnSameSide(Kingdom pKingdom)
    {
        if (pKingdom == null || pKingdom == this) return false;
        if (World.world.wars.ShimSameSide(this, pKingdom)) return true;
        return false;
    }

    public bool isAttacker() => World.world.wars.list.Any(war => war._list_attackers.Contains(this));

    public ListPool<Kingdom> getEnemiesKingdoms()
    {
        var enemies = new ListPool<Kingdom>();
        foreach (War war in World.world.wars.list)
        {
            List<Kingdom> other = war._list_attackers.Contains(this) ? war._list_defenders : war._list_defenders.Contains(this) ? war._list_attackers : null;
            if (other == null) continue;
            foreach (Kingdom enemy in other)
                if (!enemies.Contains(enemy)) enemies.Add(enemy);
        }
        return enemies;
    }

    public Alliance getAlliance() => ShimAlliance != null && ShimAlliance.isAlive() ? ShimAlliance : null;
    public bool hasAlliance() => getAlliance() != null;

    public bool isOpinionTowardsKingdomGood(Kingdom pKingdom)
    {
        KingdomOpinion opinion = World.world.diplomacy.getOpinion(this, pKingdom);
        return opinion == null || opinion.total >= 0;
    }

    public bool isSupreme() => DiplomacyManager.kingdom_supreme == this;
    public bool hasNearbyKingdoms() => cities.Any(city => city.neighbours_kingdoms.Count > 0);

    public static float distanceBetweenKingdom(Kingdom pKingdom, Kingdom pTarget) =>
        pKingdom?.capital == null || pTarget?.capital == null ? float.MaxValue : Vector2.Distance(pKingdom.capital.city_center, pTarget.capital.city_center);

    [MethodImpl(P.N)]
    public override void Dispose()
    {
        setAlive(false);
        cities = new List<City>();
        units.Clear();
        king = null;
        capital = null;
    }
}

public partial class KingdomManager
{
    private readonly Dictionary<string, Kingdom> _wild = new();

    // 没入国的人挂在这里：每个种族一个"野处"，不算国
    internal Kingdom ShimWild(string assetId)
    {
        assetId ??= "";
        if (_wild.TryGetValue(assetId, out Kingdom found)) return found;
        ShimSpecies.EnsureRealmKinds(assetId);
        found = new Kingdom { wild = true, asset = AssetManager.kingdoms.get("nomads_" + assetId) };
        found.loadData(new KingdomData { id = -1 - _wild.Count, name = assetId, original_actor_asset = assetId });
        found.id = found.data.id;
        found.setAlive(true);
        return _wild[assetId] = found;
    }

    public override void clear()
    {
        _wild.Clear();
        base.clear();
    }

    // 各种族的"野处"，照建的先后(存档按这个次序记，读档照样建回来，编号就对得上)
    internal IEnumerable<string> ShimWildSpecies => _wild.OrderByDescending(pair => pair.Value.id).Select(pair => pair.Key);

    // 立国：这个人为君
    public Kingdom makeNewCivKingdom(Actor pActor, string pID = null, bool pLog = true)
    {
        string species = pActor?.asset?.id ?? "";
        string kind = pID ?? pActor?.asset?.kingdom_id_civilization ?? species;
        ShimSpecies.EnsureRealmKinds(kind);
        Kingdom kingdom = newObject(fresh => fresh.asset = AssetManager.kingdoms.get(kind));
        kingdom.wild = false;
        kingdom.data.original_actor_asset = species;
        kingdom.data.name = pActor?.getName();
        kingdom.generateColor();
        kingdom.newCivKingdom(pActor);
        if (pActor != null)
        {
            pActor.setKingdom(kingdom);
            kingdom.setKing(pActor);
        }
        return kingdom;
    }

    [MethodImpl(P.N)]
    public override void removeObject(Kingdom pKingdom)
    {
        if (pKingdom == null) return;
        base.removeObject(pKingdom);
        pKingdom.Dispose();
    }

    public void beginChecksCities()
    {
    }

    public void setDirtyCities() => ShimCities.MarkDirty();
}

public partial class City
{
    public override string name
    {
        [MethodImpl(P.N)] get => data?.name;
        [MethodImpl(P.N)]
        set
        {
            if (data != null) data.name = value;
        }
    }

    public bool hasKingdom() => kingdom != null;
    public bool hasLeader() => leader != null && leader.isAlive();
    public bool hasCulture() => culture != null;
    public bool hasReligion() => religion != null;
    public bool hasZones() => zones.Count > 0;
    public int countZones() => zones.Count;
    public bool isCapitalCity() => kingdom != null && kingdom.capital == this;
    public bool isNeutral() => false;
    public bool isHappy() => true;
    public bool isGettingCaptured() => being_captured_by != null;
    public Culture getCulture() => culture;
    public Religion getReligion() => religion;
    public void setReligion(Religion pReligion) => religion = pReligion;
    public void setLanguage(Language pLanguage) => language = pLanguage;
    public string getSpecies() => kingdom?.getSpecies() ?? "";
    public ActorAsset getFounderSpecies() => AssetManager.actor_library.get(getSpecies());
    [MethodImpl(P.N)]
    public override ActorAsset getActorAsset() => getFounderSpecies();

    [MethodImpl(P.N)]
    public bool canGrowZones() => true;

    [MethodImpl(P.N)]
    public override int getPopulationPeople() => units.Count;

    [MethodImpl(P.N)]
    public int getPopulationMaximum() => Math.Max(status?.housing_total ?? 0, units.Count);

    // 城心那一格(城心区块中央是水时取这块里的头一格陆地)
    public WorldTile getTile(bool pForceRecalc = false)
    {
        if (_city_tile == null || pForceRecalc) recalculateCityTile();
        return _city_tile;
    }

    [MethodImpl(P.N)]
    public void recalculateCityTile()
    {
        TileZone seat = zones.Count > 0 ? zones[0] : null;
        if (seat == null) return;
        WorldTile tile = seat.centerTile;
        if (tile.Type == null || !tile.Type.ground)
            foreach (WorldTile candidate in seat.tiles)
                if (candidate.Type != null && candidate.Type.ground)
                {
                    tile = candidate;
                    break;
                }
        _city_tile = tile;
        updateCityCenter();
    }

    [MethodImpl(P.N)]
    public void updateCityCenter()
    {
        if (_city_tile == null) return;
        last_city_center = city_center;
        city_center = new Vector2(_city_tile.x, _city_tile.y);
    }

    [MethodImpl(P.N)]
    public void addZone(TileZone pZone)
    {
        if (pZone == null || pZone.city == this) return;
        pZone.city?.removeZone(pZone);
        zones.Add(pZone);
        pZone.setCity(this);
        ShimCities.MarkDirty();
    }

    [MethodImpl(P.N)]
    public void removeZone(TileZone pZone)
    {
        if (pZone == null || !zones.Remove(pZone)) return;
        if (pZone.city == this) pZone.setCity(null);
        ShimCities.MarkDirty();
    }

    public void recalculateNeighbourCities()
    {
        ShimCities.MarkDirty();
        ShimCities.Refresh();
    }

    public void setStatusDirty()
    {
    }

    // 原版改隶时国家的城市名单到下一帧才重排，模组常一边遍历本国的城一边让城改隶(继承并国、分封……)；
    // 替身当场改名单，就换一份新的，正在遍历的旧名单不动
    private static void ShimDetach(Kingdom owner, City city)
    {
        if (owner == null) return;
        int index = owner.cities.IndexOf(city);
        if (index < 0) return;
        var remaining = new List<City>(owner.cities);
        remaining.RemoveAt(index);
        owner.cities = remaining;
    }

    [MethodImpl(P.N)]
    public void setKingdom(Kingdom pKingdom, bool pFromLoad = false)
    {
        if (kingdom == pKingdom) return;
        ShimDetach(kingdom, this);
        kingdom = pKingdom;
        if (pKingdom != null && !pKingdom.cities.Contains(this)) pKingdom.cities = new List<City>(pKingdom.cities) { this };
        if (!pFromLoad && data != null) data.timestamp_kingdom = World.world.getCurWorldTime();
        ShimCities.MarkDirty();
    }

    public void removeFromCurrentKingdom()
    {
        Kingdom old = kingdom;
        if (old == null) return;
        ShimDetach(old, this);
        if (old.capital == this) old.setCapital(old.cities.Count > 0 ? old.cities[0] : null);
        kingdom = null;
        ShimCities.MarkDirty();
    }

    public void switchedKingdom()
    {
    }

    // 一批人整体改隶别国
    public void newForceKingdomEvent(List<Actor> pUnits, List<Actor> pBoats, Kingdom pKingdom, string pHappinessEvent)
    {
        if (pUnits == null || pKingdom == null) return;
        foreach (Actor unit in pUnits.ToArray())
            if (unit != null && unit.isAlive() && unit.kingdom != pKingdom) unit.setKingdom(pKingdom);
    }

    // 改隶别国：城里的人随城走
    [MethodImpl(P.N)]
    public void joinAnotherKingdom(Kingdom pNewSetKingdom, bool pCaptured = false, bool pRebellion = false)
    {
        if (pNewSetKingdom == null || kingdom == pNewSetKingdom) return;
        removeFromCurrentKingdom();
        setKingdom(pNewSetKingdom);
        foreach (Actor unit in units.ToArray())
            if (unit.kingdom != pNewSetKingdom) unit.setKingdom(pNewSetKingdom);
        if (pNewSetKingdom.capital == null) pNewSetKingdom.setCapital(this);
        clearCapture();
        switchedKingdom();
    }

    // 自立一国：这个人为君，本城作都城
    [MethodImpl(P.N)]
    public Kingdom makeOwnKingdom(Actor pActor, bool pRebellion = false, bool pFellApart = false)
    {
        Kingdom created = World.world.kingdoms.makeNewCivKingdom(pActor, null, false);
        joinAnotherKingdom(created, false, pRebellion);
        created.setCapital(this);
        return created;
    }

    [MethodImpl(P.N)]
    public void turnCityToNeutral() => removeFromCurrentKingdom();

    [MethodImpl(P.N)]
    public void finishCapture(Kingdom pNewKingdom) => joinAnotherKingdom(pNewKingdom, true);

    // 这座城被打下来归谁：归打下它的那一国
    public Kingdom findKingdomToJoinAfterCapture(Kingdom pKingdom, ListPool<War> pWars) => pKingdom != null && pKingdom.isAlive() ? pKingdom : null;

    public void clearCapture()
    {
        being_captured_by = null;
        _capture_ticks = 0f;
        _capturing_units.Clear();
    }

    [MethodImpl(P.N)]
    public void setLeader(Actor pActor, bool pNew)
    {
        leader = pActor;
        pActor?.setProfession(UnitProfession.Leader);
    }

    [MethodImpl(P.N)]
    public void removeLeader()
    {
        Actor old = leader;
        leader = null;
        if (old != null && old.isAlive() && !old.isKing()) old.setProfession(UnitProfession.Unit);
    }

    internal void ShimLeaderDied(Actor dead)
    {
        if (leader == dead) leader = null;
    }

    // 城的文化、语言、宗教照这个人的来
    public void setUnitMetas(Actor pActor)
    {
        if (pActor == null) return;
        if (pActor.culture != null) culture = pActor.culture;
        if (pActor.language != null) language = pActor.language;
        if (pActor.religion != null) religion = pActor.religion;
    }

    public bool reachableFrom(City pCity) => pCity != null && getTile() != null && pCity.getTile() != null && getTile().isSameIsland(pCity.getTile());

    // 撤城：辖地放掉，人散为无城之民
    [MethodImpl(P.N)]
    public void destroyCity()
    {
        foreach (TileZone zone in zones.ToArray()) removeZone(zone);
        foreach (Actor unit in units.ToArray()) unit.setCity(null);
        removeFromCurrentKingdom();
        World.world.cities.removeObject(this);
    }

    [MethodImpl(P.N)]
    public override void Dispose()
    {
        setAlive(false);
        units.Clear();
        leader = null;
    }
}

public partial class CityManager
{
    public City buildFirstCivilizationCity(Actor pActor)
    {
        City city = buildNewCity(pActor, pActor?.current_tile?.zone);
        if (city != null) pActor.joinCity(city);
        return city;
    }

    // 在这块地上建城：只领城心这一块，其余辖地由模组自己铺
    [MethodImpl(P.N)]
    public City buildNewCity(Actor pActor, TileZone pZone)
    {
        if (pZone == null) return null;
        City city = newObject();
        city.status = new CityStatus();
        city.data.name = "City " + city.id;
        if (pActor?.kingdom != null && !pActor.kingdom.wild) city.setKingdom(pActor.kingdom);
        city.addZone(pZone);
        city.recalculateCityTile();
        if (city.kingdom != null && city.kingdom.capital == null) city.kingdom.setCapital(city);
        city.newCityEvent(pActor);
        return city;
    }

    [MethodImpl(P.N)]
    public override void removeObject(City pObject)
    {
        if (pObject == null) return;
        base.removeObject(pObject);
        pObject.Dispose();
    }
}

// 城与城、城与国的邻接：辖地一动就记脏，主循环每一步重算一遍(模组直接读各城上的这几个集合)
internal static class ShimCities
{
    private static bool _dirty;
    internal static void MarkDirty() => _dirty = true;

    internal static void Refresh()
    {
        if (!_dirty) return;
        _dirty = false;
        MapBox world = World.world;
        if (world == null) return;
        foreach (City city in world.cities.list)
        {
            city.neighbours_cities.Clear();
            city.neighbours_cities_kingdom.Clear();
            city.neighbours_kingdoms.Clear();
            city.border_zones.Clear();
        }
        foreach (TileZone zone in world.zone_calculator.zones)
        {
            City city = zone.city;
            if (city == null) continue;
            if (zone.world_edge) city.border_zones.Add(zone);
            foreach (TileZone near in zone.neighbours)
            {
                City other = near.city;
                if (other == city) continue;
                city.border_zones.Add(zone);
                if (other == null) continue;
                city.neighbours_cities.Add(other);
                if (other.kingdom == city.kingdom) city.neighbours_cities_kingdom.Add(other);
                else if (other.kingdom != null) city.neighbours_kingdoms.Add(other.kingdom);
            }
        }
    }
}

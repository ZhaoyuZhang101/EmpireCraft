// 人物，与人物归属的那几样"集体"(宗族、文化、宗教、语言、家族、亚种)。
// 服务器里的人物只是数据：姓名、性别、生年、所属、特质、几项才干。没有走路、打架、吃饭这些小人的事。
// 标着不许内联的方法是模组补丁的挂载点：这里只做"原版在这一步改了哪些数据"，模组挂上来的前后逻辑照常跑。
//# hand MetaObject`1: units Dispose addRenown clearCursorOver clearListUnits countAdults countChildren countFamilies countFemales countHappyUnits countHomeless countHoused countHungry countMales countSick countStarving countTotalMoney countUnits generateBanner generateColor generateNewMetaObject getActorAsset getCities getColor getColorLibrary getFamilies getPopulationPeople getRandomUnit getRenown getSpriteIcon getTotalBirths getTotalDeaths getTotalKills getUnits hasCities hasFamilies isReadyForRemoval preserveAlive setDefaultValues
//# hand MetaObjectWithTraits`2: Dispose addTrait hasTrait
//# hand ColorAsset: getColorBanner getColorBorderInsideAlpha32 getColorBorderOut_capture getColorMain getColorMain32 getColorMainSecond getColorMainSecond32 getColorText initColor tryMakeNewColorAsset
//# hand Actor: generateName age renown money loot Dispose addMoney addRenown spendMoney hasEnoughMoney addTrait removeTrait hasTrait becomeLoversWith createDefaultCultureAndLanguageAndClan die dieAndDestroy getActorAsset getAge getChildren getParents getName getMetaObjectOfType hasArmy hasClan hasCulture hasFamily hasLanguage hasLover hasPlot hasReligion hasSubspecies isAdult isBaby isBreedingAge isCityLeader isEgg isFavorite isFighting isKing isParentOf isSapient isSexFemale isSexMale isUnitFitToRule isWarrior isImportantPerson isCameraFollowingUnit joinCity joinKingdom joinLanguage leavePlot removeFromArmy setArmy setCity setClan setCulture setCurrentTile setCurrentTilePosition setFamily setKingdom setLanguage setLover setParent1 setParent2 setPlot setProfession setReligion stopBeingWarrior
//# hand BaseSimObject: getCity hasCity hasKingdom isActor isKingdomCiv getHealth getMaxHealth setHealth hasStatus
//# hand SimSystemManager`2: GetEnumerator getSimpleArray getSimpleList
//# hand ActorManager: createNewUnit
//# hand ClanManager: newClan
//# hand Clan: getChief
//# hand CultureManager: newCulture setDirtyCities
//# hand ReligionManager: newReligion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Server;
using UnityEngine;

internal static class P
{
    public const MethodImplOptions N = MethodImplOptions.NoInlining;
}

// ───────── 颜色 ─────────
public partial class ColorAsset
{
    private Color? _main, _second, _text, _banner;

    private static Color Parse(string hex, Color fallback) => hex != null && ColorUtility.TryParseHtmlString(hex, out Color color) ? color : fallback;
    public Color getColorMain() => _main ??= Parse(color_main, Color.gray);
    public Color32 getColorMain32() => getColorMain();
    public Color getColorMainSecond() => _second ??= Parse(color_main_2 ?? color_main, getColorMain());
    public Color32 getColorMainSecond32() => getColorMainSecond();
    public Color getColorText() => _text ??= Parse(color_text ?? color_main, getColorMain());
    public Color getColorBanner() => _banner ??= Parse(color_banner ?? color_main, getColorMain());

    public Color32 getColorBorderInsideAlpha32()
    {
        Color32 color = getColorMain();
        color.a = 120;
        return color;
    }

    public Color getColorBorderOut_capture() => getColorMainSecond();

    public void initColor()
    {
        _main = _second = _text = _banner = null;
        _color_text = getColorText();
    }

    public static ColorAsset tryMakeNewColorAsset(string pColorMain)
    {
        if (pColorMain == null || !ColorUtility.TryParseHtmlString(pColorMain, out _)) return null;
        return ShimColors.Make(pColorMain);
    }
}

// 调色板：服务器自己的一套(各国、各文化、各宗族按编号轮着取；模组另有自己定颜色的规矩)
internal static class ShimColors
{
    private static readonly string[] Hex =
    {
        "#3f9e75", "#d85a30", "#378add", "#ba7517", "#8a5fc2", "#c94f6d", "#4fa8a0", "#a3a23a", "#6d7fd0", "#d08a3c",
        "#5a9e3f", "#b84a4a", "#3c7ea8", "#9c6a3a", "#7a52a8", "#c26a9a", "#3f8f8a", "#8a8f3a", "#5566b8", "#b8742e",
        "#2f8f5a", "#a83f2a", "#2a6fb8", "#8f5a12", "#6a48a0", "#a83f5a", "#3a8a84", "#7f7e2a", "#4a5aa0", "#a8662a",
        "#66b88a", "#e07a52", "#5aa0e8", "#d0933a", "#a47fd8", "#de6f8a", "#6fc0b8", "#bdbc58", "#8a9ae0", "#e0a45a"
    };

    private static readonly List<ColorAsset> Palette = new();

    internal static ColorAsset Make(string hex)
    {
        var asset = new ColorAsset { id = "color_" + Palette.Count, index_id = Palette.Count, color_main = hex, color_main_2 = hex, color_text = hex, color_banner = hex };
        asset.initColor();
        Palette.Add(asset);
        return asset;
    }

    internal static int Count
    {
        get
        {
            if (Palette.Count == 0)
                foreach (string hex in Hex) Make(hex);
            return Palette.Count;
        }
    }

    internal static ColorAsset ById(int id) => Palette[((id % Count) + Count) % Count];
}

// ───────── 集体的共同部分 ─────────
public partial class MetaObject<TData>
{
    // 属于它的人：随加入、离开实时增减
    public List<Actor> units { get; set; } = new();

    public override ColorAsset getColor() => _cached_color ??= ShimColors.ById(data?.color_id ?? 0);
    public virtual ColorLibrary getColorLibrary() => null;

    public virtual void generateColor()
    {
        data?.setColorID(Randy.randomInt(0, ShimColors.Count));
        _cached_color = null;
    }

    public override bool updateColor(ColorAsset pColor)
    {
        if (pColor == null) return false;
        data?.setColorID(pColor.index_id);
        _cached_color = pColor;
        return true;
    }

    public virtual IEnumerable<Actor> getUnits() => units;

    [MethodImpl(P.N)]
    public virtual int countUnits() => units.Count;

    [MethodImpl(P.N)]
    public virtual int getPopulationPeople() => units.Count;

    public virtual Actor getRandomUnit() => units.Count == 0 ? null : units[Randy.randomInt(0, units.Count)];
    public virtual int countAdults() => units.Count(unit => unit.isAdult());
    public virtual int countChildren() => units.Count(unit => !unit.isAdult());
    public virtual int countMales() => units.Count(unit => unit.isSexMale());
    public virtual int countFemales() => units.Count(unit => unit.isSexFemale());
    public virtual int countTotalMoney() => units.Sum(unit => unit.money);
    public virtual int countHappyUnits() => units.Count;
    public virtual int countHomeless() => 0;
    public virtual int countHoused() => units.Count;
    public virtual int countHungry() => 0;
    public virtual int countSick() => 0;
    public virtual int countStarving() => 0;
    public virtual int countFamilies() => getFamilies().Count();
    public virtual IEnumerable<Family> getFamilies() => units.Where(unit => unit.family != null).Select(unit => unit.family).Distinct();
    public virtual bool hasFamilies() => units.Any(unit => unit.family != null);

    // 它名下的城：各类集体自己说(见 ShimHasCity)
    public virtual IEnumerable<City> getCities()
    {
        List<City> all = World.world?.cities?.list;
        if (all == null) yield break;
        for (int i = 0; i < all.Count; i++)
            if (ShimHasCity(all[i])) yield return all[i];
    }

    protected virtual bool ShimHasCity(City city) => false;
    public virtual bool hasCities() => getCities().Any();
    public virtual ActorAsset getActorAsset() => null;
    public int getRenown() => data?.renown ?? 0;

    public void addRenown(int pAmount)
    {
        if (data != null) data.renown += pAmount;
    }

    public Sprite getSpriteIcon() => null;
    public long getTotalBirths() => 0L;
    public long getTotalDeaths() => 0L;
    public long getTotalKills() => 0L;

    // 没人了也不自动撤销：国亡、族绝由服务器的月度核对来办
    [MethodImpl(P.N)]
    public virtual bool isReadyForRemoval() => false;

    public virtual void generateBanner()
    {
    }

    public virtual void generateNewMetaObject()
    {
    }

    public void clearCursorOver()
    {
    }

    public virtual void clearListUnits() => units.Clear();

    public void preserveAlive()
    {
    }

    public override void setDefaultValues()
    {
    }

    public override void Dispose()
    {
        setAlive(false);
        units.Clear();
    }
}

public partial class MetaObjectWithTraits<TData, TBaseTrait>
{
    private readonly HashSet<string> _traits = new();
    internal HashSet<string> ShimTraitIds => _traits;

    public virtual bool addTrait(string pTraitID, bool pRemoveOpposites = false) => pTraitID != null && _traits.Add(pTraitID);
    public bool hasTrait(string pTrait) => pTrait != null && _traits.Contains(pTrait);
    public void removeTrait(string pTraitID) => _traits.Remove(pTraitID ?? "");
    public override void Dispose() => base.Dispose();
}

// ───────── 人物 ─────────
public partial class BaseSimObject
{
    public virtual City getCity() => null;
    public bool hasCity() => getCity() != null;
    public bool hasKingdom() => kingdom != null;
    public bool isActor() => this is Actor;
    public bool isKingdomCiv() => kingdom != null && kingdom.isCiv();
    public int getHealth() => isAlive() ? 100 : 0;
    public int getMaxHealth() => 100;

    public void setHealth(int pValue, bool pClamp = true)
    {
    }

    public bool hasStatus(string pID) => false;
}

public partial class Actor
{
    private readonly HashSet<string> _traitIds = new();
    private readonly List<Actor> _children = new();
    private UnitProfession _profession = UnitProfession.Unit;

    public void loadData(ActorData pData)
    {
        data = pData;
        if (pData != null) id = pData.id;
    }

    public override string name
    {
        get => data?.name;
        set
        {
            if (data != null) data.name = value;
        }
    }

    public override City getCity() => city;
    // 人的颜色随他的国
    public override ColorAsset getColor() => kingdom?.getColor() ?? ShimColors.ById(0);
    public string getName() => string.IsNullOrEmpty(data?.name) ? "#" + id : data.name;
    public ActorAsset getActorAsset() => asset;

    // 年纪按生年算(生年存在数据里的 created_time)，再加上额外添的岁数
    public int getAge() => data == null ? 0 : Math.Max(0, Date.getYearsSince(data.created_time) + data.age_overgrowth);
    public int age
    {
        get => getAge();
        set { }
    }

    public int renown
    {
        get => data?.renown ?? 0;
        set
        {
            if (data != null) data.renown = value;
        }
    }

    public int money
    {
        [MethodImpl(P.N)] get => data?.money ?? 0;
        [MethodImpl(P.N)]
        set
        {
            if (data != null) data.money = value;
        }
    }

    public int loot
    {
        get => data?.loot ?? 0;
        set
        {
            if (data != null) data.loot = value;
        }
    }

    [MethodImpl(P.N)]
    public void addMoney(int pValue) => money += pValue;
    public void addRenown(int pValue) => renown += pValue;
    public void spendMoney(int pCost) => money = Math.Max(0, money - pCost);
    public bool hasEnoughMoney(int pCost) => money >= pCost;

    public bool isAdult() => getAge() >= 18;
    public bool isBaby() => getAge() < 5;
    public bool isEgg() => false;
    public bool isBreedingAge() => getAge() >= 18 && getAge() < 50;
    public bool isSexMale() => data != null && data.sex == ActorSex.Male;
    public bool isSexFemale() => data != null && data.sex == ActorSex.Female;
    public bool isSapient() => asset == null || asset.civ;
    public bool isKing() => kingdom != null && kingdom.king == this;
    public bool isCityLeader() => city != null && city.leader == this;
    public bool isImportantPerson() => isKing() || isCityLeader();
    public bool isUnitFitToRule() => isAlive() && isAdult();
    public bool isWarrior() => _profession == UnitProfession.Warrior;
    public bool isFavorite() => false;
    public bool isFighting() => false;
    public bool isCameraFollowingUnit() => false;
    public bool hasClan() => clan != null;
    public bool hasCulture() => culture != null;
    public bool hasReligion() => religion != null;
    public bool hasLanguage() => language != null;
    public bool hasFamily() => family != null;
    public bool hasSubspecies() => subspecies != null;
    public bool hasLover() => lover != null && lover.isAlive();
    public bool hasArmy() => army != null;
    public bool hasPlot() => plot != null;

    public bool hasTrait(string pTraitID) => pTraitID != null && _traitIds.Contains(pTraitID);

    public bool addTrait(string pTraitID, bool pRemoveOpposites = false)
    {
        if (pTraitID == null || !_traitIds.Add(pTraitID)) return false;
        ActorTrait trait = AssetManager.traits.get(pTraitID);
        if (trait != null)
        {
            traits.Add(trait);
            try
            {
                trait.action_on_augmentation_add?.Invoke(this, trait);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning("[人物] 特质 " + pTraitID + " 的回调出错: " + exception.Message);
            }
        }
        return true;
    }

    public void removeTrait(string pTraitID)
    {
        if (pTraitID == null || !_traitIds.Remove(pTraitID)) return;
        traits.RemoveWhere(trait => trait.id == pTraitID);
    }

    // 存读档用的几处入口：特质照名字放回去(不当作"新得的特质"，回调不跑)、身份、子女名单
    internal IEnumerable<string> ShimTraitIds => _traitIds;

    internal void ShimRestoreTrait(string traitId)
    {
        if (traitId == null || !_traitIds.Add(traitId)) return;
        ActorTrait trait = AssetManager.traits.get(traitId);
        if (trait != null) traits.Add(trait);
    }

    internal UnitProfession ShimProfession
    {
        get => _profession;
        set => _profession = value;
    }

    internal void ShimRestoreChild(Actor child)
    {
        if (child != null && !_children.Contains(child)) _children.Add(child);
    }

    public void setProfession(UnitProfession pType, bool pCancelBeh = true) => _profession = pType;

    // 照本人文化的模板起一个名字(人名、族名、城名、国名各有一套)
    public string generateName(MetaType pType, long pSeed, ActorSex pSex = ActorSex.None) =>
        culture?.getOnomasticData(pType)?.generateName(pSex == ActorSex.None && data != null ? data.sex : pSex, 0, pSeed) ?? "";
    public void stopBeingWarrior() => _profession = UnitProfession.Unit;

    public void setCurrentTile(WorldTile pTile)
    {
        current_tile = pTile;
        current_zone = pTile?.zone;
        if (pTile != null) current_position = new Vector2(pTile.x, pTile.y);
    }

    public void setCurrentTilePosition(WorldTile pTile) => setCurrentTile(pTile);


    [MethodImpl(P.N)]
    public void setKingdom(Kingdom pKingdomToSet)
    {
        if (kingdom == pKingdomToSet) return;
        kingdom?.units.Remove(this);
        kingdom = pKingdomToSet;
        if (pKingdomToSet != null && !pKingdomToSet.units.Contains(this)) pKingdomToSet.units.Add(this);
    }

    public void joinKingdom(Kingdom pKingdom) => setKingdom(pKingdom);

    [MethodImpl(P.N)]
    public void setCity(City pCity)
    {
        if (city == pCity) return;
        city?.units.Remove(this);
        city = pCity;
        if (pCity != null && !pCity.units.Contains(this)) pCity.units.Add(this);
    }

    // 入城：随城入国
    public void joinCity(City pCity)
    {
        setCity(pCity);
        if (pCity?.kingdom != null && kingdom != pCity.kingdom) setKingdom(pCity.kingdom);
    }

    [MethodImpl(P.N)]
    public void setClan(Clan pObject)
    {
        if (clan == pObject) return;
        clan?.units.Remove(this);
        clan = pObject;
        if (pObject != null && !pObject.units.Contains(this)) pObject.units.Add(this);
    }

    [MethodImpl(P.N)]
    public void setCulture(Culture pCulture)
    {
        if (culture == pCulture) return;
        culture?.units.Remove(this);
        culture = pCulture;
        if (pCulture != null && !pCulture.units.Contains(this)) pCulture.units.Add(this);
    }

    [MethodImpl(P.N)]
    public void setReligion(Religion pObject)
    {
        if (religion == pObject) return;
        religion?.units.Remove(this);
        religion = pObject;
        if (pObject != null && !pObject.units.Contains(this)) pObject.units.Add(this);
    }

    public void setLanguage(Language pObject)
    {
        if (language == pObject) return;
        language?.units.Remove(this);
        language = pObject;
        if (pObject != null && !pObject.units.Contains(this)) pObject.units.Add(this);
    }

    public void joinLanguage(Language pLanguage) => setLanguage(pLanguage);

    [MethodImpl(P.N)]
    public void setFamily(Family pObject)
    {
        if (family == pObject) return;
        family?.units.Remove(this);
        family = pObject;
        if (pObject != null && !pObject.units.Contains(this)) pObject.units.Add(this);
    }

    [MethodImpl(P.N)]
    public void setLover(Actor pActor) => lover = pActor;

    public void becomeLoversWith(Actor pTarget)
    {
        if (pTarget == null) return;
        setLover(pTarget);
        pTarget.setLover(this);
    }

    [MethodImpl(P.N)]
    public void setParent1(Actor pParentActor, bool pIncreaseChildren = true)
    {
        if (data == null || pParentActor == null) return;
        data.parent_id_1 = pParentActor.id;
        pParentActor.AddChild(this, pIncreaseChildren);
    }

    [MethodImpl(P.N)]
    public void setParent2(Actor pActor, bool pIncreaseChildren = true)
    {
        if (data == null || pActor == null) return;
        data.parent_id_2 = pActor.id;
        pActor.AddChild(this, pIncreaseChildren);
    }

    private void AddChild(Actor child, bool count)
    {
        if (_children.Contains(child)) return;
        _children.Add(child);
        if (count) current_children_count++;
    }

    // 在世的父母
    public IEnumerable<Actor> getParents()
    {
        if (data == null) yield break;
        ActorManager all = World.world?.units;
        Actor first = data.parent_id_1 > 0 ? all?.get(data.parent_id_1) : null, second = data.parent_id_2 > 0 ? all?.get(data.parent_id_2) : null;
        if (first != null) yield return first;
        if (second != null) yield return second;
    }

    // 在世的子女
    public IEnumerable<Actor> getChildren(bool pOnlyCurrentFamily = true)
    {
        for (int i = 0; i < _children.Count; i++)
            if (_children[i].isAlive()) yield return _children[i];
    }

    public bool isParentOf(Actor pActor) => pActor?.data != null && (pActor.data.parent_id_1 == id || pActor.data.parent_id_2 == id);

    [MethodImpl(P.N)]
    public void setArmy(Army pObject) => army = pObject;

    [MethodImpl(P.N)]
    public void removeFromArmy() => army = null;

    // 才干与等级：原版由种族、特质、等级算出来，替身新造人时掷一回(各 1~20)，随档存着(见 WorldSave)；
    // 模组有的读字段(actor.intelligence)，有的读 stats["intelligence"]，两处一起写
    internal void ShimSetAbilities(int pStewardship, int pWarfare, int pIntelligence, int pDiplomacy)
    {
        stewardship = pStewardship;
        warfare = pWarfare;
        intelligence = pIntelligence;
        diplomacy = pDiplomacy;
        stats["stewardship"] = pStewardship;
        stats["warfare"] = pWarfare;
        stats["intelligence"] = pIntelligence;
        stats["diplomacy"] = pDiplomacy;
    }

    internal void ShimRollAbilities()
    {
        ShimSetAbilities(Randy.randomInt(1, 21), Randy.randomInt(1, 21), Randy.randomInt(1, 21), Randy.randomInt(1, 21));
        if (level < 1) level = 1;
    }

    public void setPlot(Plot pObject) => plot = pObject;
    public void leavePlot() => plot = null;

    public NanoObject getMetaObjectOfType(MetaType pType) => pType switch
    {
        MetaType.Kingdom => kingdom, MetaType.City => city, MetaType.Clan => clan, MetaType.Culture => culture, MetaType.Religion => religion,
        MetaType.Language => language, MetaType.Family => family, MetaType.Subspecies => subspecies, MetaType.Alliance => kingdom?.getAlliance(),
        MetaType.Army => army, _ => null
    };

    // 还没有文化、语言、宗族的(开国之君)：各立一个
    [MethodImpl(P.N)]
    public void createDefaultCultureAndLanguageAndClan(string pCultureName = null)
    {
        MapBox world = World.world;
        if (culture == null)
        {
            Culture created = world.cultures.newCulture(this, true);
            if (!string.IsNullOrEmpty(pCultureName)) created.data.name = pCultureName;
        }
        if (language == null) world.languages.newLanguage(this, true);
        if (clan == null) world.clans.newClan(this, true);
    }

    // 人没了：撤出各处的名册。君位、城主空出来的事由国与城自己接着办(模组挂在这里的身后事照常跑)
    [MethodImpl(P.N)]
    public void die(bool pDestroy = false, AttackType pType = (AttackType)5, bool pCountDeath = true, bool pLogFavorite = true)
    {
        if (!isAlive()) return;
        if (data != null) data.died_time = World.world?.getCurWorldTime() ?? 0d;
        setAlive(false);
        Kingdom realm = kingdom;
        City home = city;
        if (realm != null && realm.king == this) realm.ShimKingDied(this);
        if (home != null && home.leader == this) home.ShimLeaderDied(this);
        realm?.units.Remove(this);
        home?.units.Remove(this);
        clan?.units.Remove(this);
        culture?.units.Remove(this);
        religion?.units.Remove(this);
        language?.units.Remove(this);
        family?.units.Remove(this);
        subspecies?.units.Remove(this);
        if (lover != null && lover.lover == this) lover.lover = null;
        World.world?.units.removeObject(this);
    }

    public void dieAndDestroy(AttackType pType) => die(true, pType);

    [MethodImpl(P.N)]
    public override void Dispose() => setAlive(false);
}

public partial class SimSystemManager<TObject, TData> : Shim.IShimItems<TObject>
{
    private readonly List<TObject> _simple = new();

    public List<TObject> getSimpleList() => _simple;
    public TObject[] getSimpleArray() => _simple.ToArray();
    public IEnumerator<TObject> GetEnumerator() => _simple.GetEnumerator();
    IEnumerable<TObject> Shim.IShimItems<TObject>.ShimItems() => _simple;

    public override void addObject(TObject pObject)
    {
        if (pObject == null || _byId.ContainsKey(pObject.id)) return;
        base.addObject(pObject);
        _simple.Add(pObject);
    }

    public override void removeObject(TObject pObject)
    {
        if (pObject == null) return;
        _simple.Remove(pObject);
        base.removeObject(pObject);
    }

    public override void clear()
    {
        _simple.Clear();
        base.clear();
    }
}

public partial class ActorAssetLibrary
{
    // 种族表服务器里没有：按名字取不到的都当作会建国的人形种族，现登记一份(连同它立国用的王国类型)
    public override ActorAsset get(string pID)
    {
        if (pID == null) return null;
        return dict.TryGetValue(pID, out ActorAsset found) ? found : ShimSpecies.Register(pID);
    }
}

public partial class ActorManager
{
    private static readonly Dictionary<string, Subspecies> SpeciesByAsset = new();

    internal static Subspecies SpeciesOf(string assetId)
    {
        assetId ??= "";
        if (SpeciesByAsset.TryGetValue(assetId, out Subspecies found) && found.isAlive()) return found;
        found = new Subspecies();
        found.loadData(new SubspeciesData { id = SpeciesByAsset.Count + 1, name = assetId, species_id = assetId });
        found.id = found.data.id;
        found.setAlive(true);
        return SpeciesByAsset[assetId] = found;
    }

    public override void clear()
    {
        SpeciesByAsset.Clear();
        base.clear();
    }

    // 造一个人：落在某格上，带种族、性别和几项才干；国、城、族这些由调用的一方接着安排
    [MethodImpl(P.N)]
    public Actor createNewUnit(string pStatsID, WorldTile pTile, bool pMiracleSpawn = false, float pSpawnHeight = 0f, Subspecies pSubspecies = null,
        Subspecies pSubspeciesMutateFrom = null, bool pSpawnWithItems = true, bool pAdultAge = false, bool pGiveOwnerlessItems = false, bool pSapientSubspecies = false)
    {
        Actor actor = newObject();
        actor.asset = AssetManager.actor_library.get(pStatsID);
        actor.data.asset_id = pStatsID;
        actor.data.sex = Randy.randomChance(0.5f) ? ActorSex.Male : ActorSex.Female;
        actor.data.health = 100;
        // 成年人：岁数记在"额外添的岁数"里(模组也是往这里写年纪的)
        if (pAdultAge) actor.data.age_overgrowth = Randy.randomInt(18, 41);
        actor.setCurrentTile(pTile);
        actor.kingdom = World.world.kingdoms.ShimWild(pStatsID);
        actor.subspecies = pSubspecies ?? SpeciesOf(pStatsID);
        if (!actor.subspecies.units.Contains(actor)) actor.subspecies.units.Add(actor);
        actor.ShimRollAbilities();
        actor.is_visible = true;
        return actor;
    }
}

// ───────── 宗族、文化、宗教、语言 ─────────
public partial class Clan
{
    // 族长：族里在世的成年人中最年长的(君主优先)
    public Actor getChief()
    {
        Actor best = null;
        foreach (Actor member in units)
        {
            if (!member.isAlive() || !member.isAdult()) continue;
            if (member.isKing()) return member;
            if (best == null || member.getAge() > best.getAge()) best = member;
        }
        return best;
    }
}

public partial class ClanManager
{
    public Clan newClan(Actor pFounder, bool pAddDefaultTraits)
    {
        Clan clan = newObject();
        clan.data.original_actor_asset = pFounder?.asset?.id;
        clan.data.name = pFounder?.getName();
        clan.generateColor();
        clan.newClan(pFounder, pAddDefaultTraits);
        pFounder?.setClan(clan);
        return clan;
    }
}

public partial class CultureManager
{
    public Culture newCulture(Actor pFounder, bool pAddDefaultTraits)
    {
        Culture culture = newObject();
        culture.data.original_actor_asset = culture.data.creator_species_id = pFounder?.asset?.id;
        culture.data.creator_city_name = pFounder?.city?.name;
        culture.data.creator_kingdom_name = pFounder?.kingdom?.name;
        culture.data.name = pFounder?.city?.name ?? pFounder?.getName();
        culture.generateColor();
        culture.createCulture(pFounder, pAddDefaultTraits);
        pFounder?.setCulture(culture);
        return culture;
    }

    public void setDirtyCities()
    {
    }
}

public partial class LanguageManager
{
    public Language newLanguage(Actor pFounder, bool pAddDefaultTraits)
    {
        Language language = newObject();
        language.data.creator_species_id = pFounder?.asset?.id;
        language.data.name = pFounder?.culture?.name ?? pFounder?.getName();
        language.generateColor();
        language.newLanguage(pFounder, pAddDefaultTraits);
        pFounder?.setLanguage(language);
        return language;
    }
}

public partial class ReligionManager
{
    public Religion newReligion(Actor pFounder, bool pAddDefaultTraits)
    {
        Religion religion = newObject();
        religion.data.creator_species_id = pFounder?.asset?.id;
        religion.data.creator_kingdom_id = pFounder?.kingdom?.id ?? -1;
        religion.data.name = pFounder?.culture?.name ?? pFounder?.getName();
        religion.generateColor();
        religion.newReligion(pFounder, pFounder?.current_tile, pAddDefaultTraits);
        pFounder?.setReligion(religion);
        return religion;
    }
}

public partial class Culture
{
    protected override bool ShimHasCity(City city) => city.culture == this;
}

public partial class Religion
{
    protected override bool ShimHasCity(City city) => city.religion == this;
}

public partial class Language
{
    protected override bool ShimHasCity(City city) => city.language == this;
}

public partial class Clan
{
    protected override bool ShimHasCity(City city) => city.leader?.clan == this;
}

// 各类集体"是哪个种族的"：都记在各自的数据里
public partial class Clan
{
    public override string species_id
    {
        get => data?.original_actor_asset;
        set
        {
            if (data != null) data.original_actor_asset = value;
        }
    }
}

public partial class Culture
{
    public override string species_id
    {
        get => data?.original_actor_asset;
        set
        {
            if (data != null) data.original_actor_asset = value;
        }
    }
}

public partial class Religion
{
    public override string species_id
    {
        get => data?.creator_species_id;
        set
        {
            if (data != null) data.creator_species_id = value;
        }
    }
}

public partial class Language
{
    public override string species_id
    {
        get => data?.creator_species_id;
        set
        {
            if (data != null) data.creator_species_id = value;
        }
    }
}

public partial class Subspecies
{
    public override string species_id
    {
        get => data?.species_id;
        set
        {
            if (data != null) data.species_id = value;
        }
    }
}

public partial class Kingdom
{
    public override string species_id
    {
        get => data?.original_actor_asset;
        set
        {
            if (data != null) data.original_actor_asset = value;
        }
    }
}

// 种族与王国类型：服务器自己的最小一份。模组判"这人能不能立国"(设行政区、分封、自立都要过这一关)
// 查的是种族写着的王国类型在不在、是不是文明国家
internal static class ShimSpecies
{
    internal static void Init()
    {
        foreach (string id in new[] { "human", "elf", "orc", "dwarf" }) Register(id);
    }

    internal static ActorAsset Register(string id)
    {
        if (AssetManager.actor_library.dict.TryGetValue(id, out ActorAsset known)) return known;
        var asset = new ActorAsset { id = id, civ = true, kingdom_id_civilization = id, kingdom_id_wild = "nomads_" + id };
        EnsureRealmKinds(id);
        return AssetManager.actor_library.add(asset);
    }

    internal static void EnsureRealmKinds(string id)
    {
        if (!AssetManager.kingdoms.dict.ContainsKey(id)) AssetManager.kingdoms.add(new KingdomAsset { id = id, civ = true });
        if (!AssetManager.kingdoms.dict.ContainsKey("nomads_" + id)) AssetManager.kingdoms.add(new KingdomAsset { id = "nomads_" + id, civ = false });
    }
}

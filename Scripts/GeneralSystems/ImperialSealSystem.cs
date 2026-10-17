using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 传国玉玺(用户 2026-10-16："把传国玉玺做成装备吧""皇帝会下令打造或者搜索遗落的传国玉玺""只有华夏有玉玺")：
// 只有华夏一线的文化有，每个天下一方(华夏、中国、山海共用；复查 2026-10-17 前是每个文化一方)，是一件护符栏装备
// (带"永恒"词缀，不坏、不会凭空消失)，由持有它的帝国的皇帝佩着；入主的外族受着天命也能得、能用——
//   · 打造：本文化还没有玉玺时，进入帝制的华夏帝国(不是原始部落联盟、周制天子)国库有三百以上，每年约四分之一的机会
//     命良工琢玉为玺，号传国玉玺；
//   · 持有：皇帝佩着玉玺的帝国每年法统 +2；皇位更替时传给新君；
//   · 易手：佩玉玺的人被杀，原版装备会被杀他的人夺去——夺去的人在哪个帝国，玉玺就归哪个帝国(献给本朝皇帝)；
//     落到不是帝国的国家手里、进了城库、或持有的帝国亡了没人接手，就算遗落在那座城(记下城)；
//   · 访求：玉玺遗落时，本文化没有它的帝国每年花三十访求，占着遗落之城的一成五机会得到，否则百分之二；
//   · 受命宝：玉玺遗落十年以上、或在别的帝国手里，没有玉玺也没有受命宝的皇帝国库有一百五十以上，每年一成机会另刻
//     "某某受命宝"(铜金印)，佩着每年法统 +1；后来得了传国玉玺，受命宝就不再用；
//   · 绝嗣裂土时，玉玺一半留在京城，一半流落到某家强藩手里(见 InterregnumSystem)。
// 玉玺与称帝、天命(用户 2026-10-17："我的玉玺装备呢""这个可以影响称帝的逻辑的")：
//   · 受了天命(见 TianxiaSystem)或处在开国气象里的帝国，本文化还没有玉玺就当年琢玉为玺，不必等国库——以前要国库三百，
//     穷困的朝廷(国库常年为零、国债违约)一直造不出来；
//   · 王国也能怀玺：佩玺的人被还没称帝的华夏独立国家的人杀了、或玉玺遗落在这样的国家的城里，就归这个国家
//     ("某国得传国玉玺")，由其君主佩着；它称了帝玉玺随之归帝国；
//   · 怀玺受命：怀玺的王国可以凭玉玺称帝(称帝路线 Seal，开国天命 75，见 EmpireFormationService)；
//   · 无玺称帝：华夏有别家怀着玉玺时称帝，开国天命 -10；
//   · 天命：怀玺的帝国据有天下六成即受天命(别家要七成五，见 TianxiaSystem.Settle)。
public static class ImperialSealSystem
{
    public const string GenuineId = "imperial_seal", LesserId = "imperial_seal_lesser";
    private const int CraftCost = 300, LesserCost = 150, SearchCost = 30, GenuineMandate = 2, LesserMandate = 1,
        LesserAfterYears = 10;
    private const float CraftChance = 0.25f, LesserChance = 0.1f, SearchHomeChance = 0.15f, SearchAwayChance = 0.02f;

    #region 装备定义

    public static void Init()
    {
        try
        {
            EquipmentAsset template = AssetManager.items?.get("amulet_silver");
            if (template == null) return;
            Define(GenuineId, template, "ui/icons/items/icon_imperial_seal", 6f, 4f);
            Define(LesserId, template, "ui/icons/items/icon_imperial_seal_lesser", 3f, 2f);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][传国玉玺] 装备定义失败: {exception}");
        }
    }

    private static void Define(string id, EquipmentAsset template, string icon, float diplomacy, float stewardship)
    {
        if (AssetManager.items.get(id) != null) return;
        EquipmentAsset asset = AssetManager.items.clone(id, template.id);
        asset.path_icon = icon;
        asset.translation_key = id;
        asset.material = "gold";
        // 价值极高：原版的人不会拿城库里的护符把它换下来
        asset.equipment_value = 999;
        asset.item_modifier_ids = null;
        asset.item_modifiers = null;
        asset.base_stats = new BaseStats();
        asset.base_stats["diplomacy"] = diplomacy;
        asset.base_stats["stewardship"] = stewardship;
    }

    #endregion

    #region 查询

    private static bool Huaxia(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return false;
        try
        {
            return InstitutionSystem.GetCultureLine(culture) == "Huaxia";
        }
        catch
        {
            return false;
        }
    }

    // 玉玺按天下存(复查 2026-10-17：华夏、中国、山海一个联合天下以前可能有三方真玺)：标了同一天下标识的华夏线文化共用一方，
    // 存在与标识同名的文化上(没有同名文化就存在本文化)；旧档里存在别的文化上的并过来
    private static CultureInstitutionState SealState(string culture)
    {
        if (!Huaxia(culture)) return null;
        string home = culture.GetTianxiaId();
        if (!Huaxia(home)) home = culture;
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(home);
        if (state == null) return null;
        // 同一天下的各文化一次并齐(不论先问到的是哪个文化，免得先在主文化上新造一方、再把别的文化的旧玺抹掉)
        foreach (string other in CultureService.CultureKeys)
            if (other != home && Huaxia(other) && other.GetTianxiaId() == home)
                Merge(InstitutionSystem.GetOrCreateCultureState(other), state);
        return state;
    }

    private static void Merge(CultureInstitutionState from, CultureInstitutionState into)
    {
        if (from == null || from == into || from.imperial_seal_item_id <= 0 && !from.imperial_seal_made) return;
        if (into.imperial_seal_item_id <= 0 && !into.imperial_seal_made)
        {
            into.imperial_seal_item_id = from.imperial_seal_item_id;
            into.imperial_seal_made = from.imperial_seal_made;
            into.imperial_seal_empire_id = from.imperial_seal_empire_id;
            into.imperial_seal_kingdom_id = from.imperial_seal_kingdom_id;
            into.imperial_seal_city_id = from.imperial_seal_city_id;
            into.imperial_seal_lost_since = from.imperial_seal_lost_since;
        }
        from.imperial_seal_item_id = -1L;
        from.imperial_seal_made = false;
        from.imperial_seal_empire_id = -1L;
        from.imperial_seal_kingdom_id = -1L;
        from.imperial_seal_city_id = -1L;
        from.imperial_seal_lost_since = -1d;
    }

    // 帝国用哪一方玉玺：本文化是华夏线的用本天下的；外族受着华夏线天下的天命的(入主，用户 2026-10-17 选"外族入主可受天命"，
    // 能用玉玺)也用那个天下的
    private static CultureInstitutionState StateOf(Empire empire, out string culture)
    {
        culture = CultureService.GetEmpireDefaultCulture(empire);
        if (Huaxia(culture)) return SealState(culture);
        foreach (EmpireCore lead in TianxiaSystem.MandatesOf(empire?.CoreKingdom))
        {
            if (!Huaxia(lead.default_culture)) continue;
            culture = lead.default_culture;
            return SealState(culture);
        }
        return null;
    }

    public static bool HeldBy(Empire empire)
    {
        CultureInstitutionState state = StateOf(empire, out _);
        return state != null && empire?.data != null && state.imperial_seal_empire_id == empire.data.id;
    }

    // 还没称帝的华夏王国怀着玉玺
    public static bool HeldByKingdom(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        CultureInstitutionState state = SealState(CultureService.GetRealmCulture(kingdom));
        return state != null && state.imperial_seal_empire_id <= 0 && state.imperial_seal_kingdom_id == kingdom.id;
    }

    // 怀玺(帝国或王国)：称帝、受天命时看这个
    public static bool Holds(Kingdom kingdom)
    {
        if (kingdom == null) return false;
        Empire empire = kingdom.IsEmpire() ? kingdom.GetEmpire() : null;
        return empire != null ? HeldBy(empire) : HeldByKingdom(kingdom);
    }

    // 开国天命的增减：怀玺称帝不另加(路线已给)，别家怀着玉玺时无玺称帝 -10
    public static int FoundingMandateAdjustment(Kingdom founder)
    {
        CultureInstitutionState state = SealState(CultureService.GetRealmCulture(founder));
        if (state == null || Holds(founder)) return 0;
        bool another = state.imperial_seal_empire_id > 0 || state.imperial_seal_kingdom_id >= 0 && state.imperial_seal_kingdom_id != founder.id;
        return another ? -10 : 0;
    }

    private static Item ItemOf(long id) => id > 0 ? World.world.items.get(id) : null;

    private static Empire RealmOf(Kingdom kingdom) =>
        kingdom == null || kingdom.isRekt() ? null : kingdom.IsEmpire() || kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;

    private static bool Alive(Empire empire) => empire?.data != null && !empire.isRekt() && !empire.IsArchived();

    // 进入帝制(不是原始部落联盟、周制天子)
    private static bool Imperial(Empire empire)
    {
        RegimeType? type = empire?.CoreKingdom?.GetRegime()?.type;
        return type != null && !RegimeManager.IsPrimitive(type) && type != RegimeType.ZhouFeudalism &&
               RegimeManager.IsMonarchy(type) && !RepublicSystem.IsRepublic(empire);
    }

    #endregion

    #region 佩戴

    // 给 actor 佩上一件玉玺/受命宝(没有就新造)，返回这件装备
    private static Item Wear(Actor actor, Item item, string assetId, string name)
    {
        if (actor?.data == null || actor.isRekt() || !actor.isAlive() || actor.equipment == null) return item;
        if (item == null || item.shouldbe_removed)
        {
            EquipmentAsset asset = AssetManager.items.get(assetId);
            if (asset == null) return null;
            item = World.world.items.generateItem(asset, actor.kingdom, actor.getName(), 0, actor);
            item.addMod("eternal");
            item.setName(name);
        }
        if (item.getActor() == actor) return item;
        item.getActor()?.equipment?.getSlot(EquipmentType.Amulet)?.takeAwayItem();
        if (item.getCity() != null) item.clearCity();
        actor.equipment.setItem(item, actor);
        return item;
    }

    // 把玉玺交给 empire 的皇帝(得玉玺)
    public static void Give(Empire empire, bool announce, string key = "imperial_seal_taken_history", string place = "")
    {
        CultureInstitutionState state = StateOf(empire, out _);
        Actor emperor = empire?.Emperor;
        if (state == null || emperor?.data == null) return;
        Item item = Wear(emperor, ItemOf(state.imperial_seal_item_id), GenuineId, LM.Get(GenuineId));
        if (item == null) return;
        state.imperial_seal_item_id = item.getID();
        state.imperial_seal_empire_id = empire.data.id;
        state.imperial_seal_kingdom_id = -1L;
        state.imperial_seal_city_id = empire.CoreKingdom?.capital?.data?.id ?? state.imperial_seal_city_id;
        state.imperial_seal_lost_since = -1d;
        // 得了传国玉玺，受命宝不再用
        Item lesser = ItemOf(empire.data.imperial_seal_lesser_item_id);
        if (lesser != null && lesser != item && lesser.getActor() != null && lesser.getActor() != emperor)
            lesser.getActor().equipment?.getSlot(EquipmentType.Amulet)?.takeAwayItem();
        if (announce) Announce(empire, string.Format(LM.Get(key), empire.GetEmpireFullName(), place));
    }

    // 玉玺归一个还没称帝的华夏王国，由其君主佩着
    private static bool GiveKingdom(CultureInstitutionState state, Kingdom kingdom, bool announce)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.IsEmpire() || kingdom.IsInEmpire() ||
            !Huaxia(CultureService.GetRealmCulture(kingdom))) return false;
        Actor king = kingdom.king;
        if (king?.data == null || !king.isAlive()) return false;
        Item item = Wear(king, ItemOf(state.imperial_seal_item_id), GenuineId, LM.Get(GenuineId));
        if (item == null) return false;
        bool changed = state.imperial_seal_kingdom_id != kingdom.id;
        state.imperial_seal_item_id = item.getID();
        state.imperial_seal_empire_id = -1L;
        state.imperial_seal_kingdom_id = kingdom.id;
        state.imperial_seal_city_id = kingdom.capital?.data?.id ?? state.imperial_seal_city_id;
        state.imperial_seal_lost_since = -1d;
        if (announce && changed)
        {
            string content = string.Format(LM.Get("imperial_seal_taken_history"), kingdom.GetKingdomName());
            LogService.LogInfo("[EmpireCraft][传国玉玺] " + content);
            TranslateHelper.LogEventMessage(content, kingdom);
        }
        return true;
    }

    // 玉玺遗落在某城(那城归一个还没称帝的华夏王国的，就归这个王国)
    private static void Lose(CultureInstitutionState state, City city, Empire former)
    {
        state.imperial_seal_empire_id = -1L;
        if (city?.kingdom != null && GiveKingdom(state, city.kingdom, announce: true)) return;
        state.imperial_seal_kingdom_id = -1L;
        if (city?.data != null) state.imperial_seal_city_id = city.data.id;
        if (state.imperial_seal_lost_since >= 0d) return;
        state.imperial_seal_lost_since = World.world.getCurWorldTime();
        City at = World.world.cities.get(state.imperial_seal_city_id);
        string content = string.Format(LM.Get("imperial_seal_lost_history"), at?.GetCityName() ?? "");
        LogService.LogInfo("[EmpireCraft][传国玉玺] " + content);
        if (Alive(former)) former.RecordHistory(directContent: content);
    }

    private static void Announce(Empire empire, string content)
    {
        LogService.LogInfo("[EmpireCraft][传国玉玺] " + content);
        empire.RecordHistory(directContent: content, actorId: empire.Emperor?.id ?? -1L);
        TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }

    #endregion

    #region 每年

    // 帝国例行检查(EmpireCraftEmpireBehCheckMandate)
    public static void Yearly(Empire empire)
    {
        CultureInstitutionState state = StateOf(empire, out string culture);
        if (state == null || !Alive(empire)) return;
        Reconcile(state);
        Actor emperor = empire.Emperor;
        if (emperor?.data == null || !emperor.isAlive()) return;
        if (state.imperial_seal_empire_id == empire.data.id)
        {
            Give(empire, announce: false);
            empire.AddMandate(GenuineMandate);
            return;
        }
        if (!Imperial(empire)) return;
        Kingdom core = empire.CoreKingdom;
        // 本文化还没有玉玺：命良工琢玉为玺。受了天命的当年就造，不看国库；否则国库三百、每年约四分之一机会
        if (state.imperial_seal_item_id <= 0 && !state.imperial_seal_made)
        {
            bool mandate = TianxiaSystem.MandateOf(core) != null || UnificationSystem.InGrace(core);
            if (mandate || UnityEngine.Random.value < CraftChance && TreasurySystem.TrySpend(core, CraftCost, TreasuryCategory.Rites))
            {
                state.imperial_seal_made = true;
                Give(empire, announce: true, "imperial_seal_crafted_history");
            }
            return;
        }
        // 玉玺遗落：访求
        if (state.imperial_seal_empire_id <= 0 && state.imperial_seal_lost_since >= 0d &&
            TreasurySystem.TrySpend(core, SearchCost, TreasuryCategory.Rites))
        {
            City at = World.world.cities.get(state.imperial_seal_city_id);
            bool home = at?.kingdom != null && RealmOf(at.kingdom) == empire;
            if (UnityEngine.Random.value < (home ? SearchHomeChance : SearchAwayChance))
            {
                Give(empire, announce: true, "imperial_seal_found_history", at?.GetCityName() ?? "");
                return;
            }
        }
        WearLesser(empire, state, emperor);
    }

    // 受命宝：玉玺遗落十年以上或在别人手里时另刻；已有的随皇位传
    private static void WearLesser(Empire empire, CultureInstitutionState state, Actor emperor)
    {
        Item lesser = ItemOf(empire.data.imperial_seal_lesser_item_id);
        if (lesser == null || lesser.shouldbe_removed)
        {
            empire.data.imperial_seal_lesser_item_id = -1L;
            bool longLost = state.imperial_seal_lost_since >= 0d &&
                            Date.getYearsSince(state.imperial_seal_lost_since) >= LesserAfterYears;
            bool rivalHolds = state.imperial_seal_empire_id > 0 && state.imperial_seal_empire_id != empire.data.id;
            if (!longLost && !rivalHolds || UnityEngine.Random.value >= LesserChance ||
                !TreasurySystem.TrySpend(empire.CoreKingdom, LesserCost, TreasuryCategory.Rites)) return;
            lesser = Wear(emperor, null, LesserId, string.Format(LM.Get("imperial_seal_lesser_name"), empire.GetEmpireName()));
            if (lesser == null) return;
            empire.data.imperial_seal_lesser_item_id = lesser.getID();
            Announce(empire, string.Format(LM.Get("imperial_seal_lesser_history"), empire.GetEmpireFullName(), lesser.getName()));
        }
        else Wear(emperor, lesser, LesserId, lesser.getName());
        empire.AddMandate(LesserMandate);
    }

    // 对账(每个文化每年一次)：玉玺在谁身上就归谁的朝廷
    private static readonly Dictionary<string, double> Reconciled = new();
    private static object _world;

    private static void Reconcile(CultureInstitutionState state)
    {
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            Reconciled.Clear();
        }
        if (state.imperial_seal_item_id <= 0) return;
        double now = World.world.getCurWorldTime();
        string key = state.imperial_seal_item_id.ToString();
        if (Reconciled.TryGetValue(key, out double at) && now >= at && Date.getYearsSince(at) < 1) return;
        Reconciled[key] = now;
        Item item = ItemOf(state.imperial_seal_item_id);
        Empire holder = state.imperial_seal_empire_id > 0 ? ModClass.EMPIRE_MANAGER?.get(state.imperial_seal_empire_id) : null;
        // 玉玺在哪：佩着它的人所在的国、存着它的城库所在的国；都没有就是原来的持有者，持有者亡了看它最后的都城
        Actor owner = item?.getActor();
        City place;
        Empire realm;
        if (owner?.data != null && owner.isAlive() && !owner.isRekt())
        {
            place = owner.city ?? owner.kingdom?.capital;
            realm = RealmOf(owner.kingdom);
        }
        else if (item?.getCity() != null)
        {
            place = item.getCity();
            realm = RealmOf(place.kingdom);
        }
        else if (Alive(holder))
        {
            place = holder.CoreKingdom?.capital;
            realm = holder;
        }
        else
        {
            place = World.world.cities.get(state.imperial_seal_city_id);
            realm = RealmOf(place?.kingdom);
        }
        if (Alive(realm) && StateOf(realm, out _) == state && realm.Emperor?.data != null && realm.Emperor.isAlive())
            Give(realm, announce: realm != holder);
        // 佩着它的人、存着它的城归一个还没称帝的华夏王国
        else if (!GiveKingdom(state, owner?.data != null && owner.isAlive() ? owner.kingdom : place?.kingdom,
                     announce: true))
            Lose(state, place, holder);
    }

    #endregion
}

using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 建城、营宫殿(用户 2026-10-17 "手动放置村/镇/城/宫殿/城市"，问答"城市=新城，归所点地方的国家"；
// "官府也能手动升级成皇宫，但是这意味着这个地方会成为默认的帝国核心首都(给个提示)""一个帝国只能有一个皇宫")：
//   · 建城：点一处平地，在那里建一座新城，归那块地所属的国家；无主之地归挨着的国家里都城最近的。
//     从本国最近的城落成一位官员做建城人、迁一成人口过去；新城不独立为城邦、不分出新部落(见 CityPatch.CityBuilt)；
//   · 营宫殿：皇宫就是帝国核心的默认首都(EmpireCore 的核心首都，迁都、还于旧都都以它为准)，一个帝国只有一座。
//     点帝国核心之国的一座城：头一回只提示，五秒内再点同一座城才营建——这座城成为帝国核心的默认首都、当即迁都，
//     平面图上宫城地标随之移过来(见 PureMapPatch.Layout)。
// 手动不设限(用户 2026-10-20 "手动放置建筑时候不要有任何限制")：
//   · 建城不看离别的城多近、离厂区多近、敌军占没占着(占着的那块地建城时占领记录清掉)，丘陵也行；无主之地四周没有国家就归全图都城最近的国家；
//     只留：水、岩浆、山、高峰、城墙上不建(原版城心要人走得到、能放篝火)，别城城心那块地不拆(那是原城本身)，
//     建城要一位本国人物做建城人(原版建城离不开)，全图一个国家都没有就建不了；
//   · 营宫殿不限帝国核心之国：别的国家(王国、帝国成员、城邦)的城营宫殿就成为本国都城(迁都)，宫城一直留着(palace_kept)，
//     不论城大小；帝国核心之国照旧是皇宫(默认首都)。两次点击确认照旧。
public static class CityPlacementSystem
{
    private const float ConfirmSeconds = 5f;
    private static long _armedPalace = -1L;
    private static float _armedAt = -100f;

    // 建城期间：新城不独立为城邦、不分出新部落
    public static bool Placing { get; private set; }

    // 先选国家再建城：纯净地图、无小人模式下点不到小人，原版"点小人建城"用不了。点一国的地先选定它，
    // 再点它的地或无主的平地给它建城，城名、主流文化照它的国家文化；点别国的地改选那一国。
    // 没选过时，点无主之地照旧归挨着的国家里都城最近的
    private static long _chosen = -1L;

    public static string PlaceCity(WorldTile tile)
    {
        TileZone zone = tile?.zone;
        if (zone == null || !UrbanFabric.ManualGround(tile)) return LM.Get("city_place_no_land");
        City host = zone.city;
        Kingdom chosen = _chosen > 0 ? World.world.kingdoms.get(_chosen) : null;
        if (chosen != null && (chosen.isRekt() || chosen.wild)) chosen = null;
        Kingdom clicked = host?.kingdom;
        if (clicked != null && !clicked.wild && !clicked.isRekt() && clicked != chosen)
        {
            _chosen = clicked.getID();
            return string.Format(LM.Get("city_place_chosen"), clicked.GetKingdomName());
        }
        Kingdom owner = chosen ?? clicked;
        if (host == null && owner == null)
        {
            // 无主之地：挨着的国家里都城最近的；四周没有国家就是全图都城最近的
            owner = NearestKingdom(zone.neighbours_all?.Select(neighbour => neighbour?.city?.kingdom), tile) ??
                    NearestKingdom(World.world.kingdoms.list, tile);
        }
        if (owner == null || owner.wild || owner.isRekt()) return LM.Get("city_place_no_kingdom");
        // 别城的城心那块地不拆：那是原城本身(城心、篝火、平面图都在这里)；城区别的地块可以，那块划给新城
        if (host != null && zone == host.getTile()?.zone) return string.Format(LM.Get("settlement_place_urban"), host.GetCityName());
        City source = host ?? owner.cities?.Where(city => city?.getTile() != null && !city.isRekt())
            .OrderBy(city => Toolbox.SquaredDistTile(city.getTile(), tile)).FirstOrDefault();
        if (source == null) return LM.Get("city_place_no_kingdom");
        string culture = CultureService.GetRealmCulture(owner);
        if (!CultureService.IsValidCulture(culture)) culture = CultureService.GetMainCulture(source);
        Actor founder = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.SpawnOfficial(source, owner) : null;
        // 新落成的建城人随国家文化，原版按他的文化给新城起名
        if (founder != null && !founder.isRekt() && CultureService.GetNativeCultureObject(culture) is Culture native &&
            founder.culture != native)
            founder.setCulture(native);
        // 建城人：落成的官员，没有就本城城主、本国君主、本国随便一位活着的人
        bool Fits(Actor actor) => actor != null && !actor.isRekt() && actor.isAlive() && actor.kingdom == owner;
        if (!Fits(founder)) founder = source.leader;
        if (!Fits(founder)) founder = owner.king;
        if (!Fits(founder)) founder = owner.units?.FirstOrDefault(Fits);
        if (!Fits(founder)) return LM.Get("city_place_no_founder");
        if (host != null)
        {
            // 敌军占着的地：占领记在原城名下，建了新城就对不上，先清掉
            if (host.GetTileZoneOccupier(zone) != null) host.RemoveOccupiedTileZoneFromAll(zone);
            host.removeZone(zone);
            if (zone.city != null) return string.Format(LM.Get("settlement_place_urban"), host.GetCityName());
        }
        City city;
        Placing = true;
        try
        {
            city = World.world.cities.buildNewCity(founder, zone);
        }
        finally
        {
            Placing = false;
        }
        if (city == null) return LM.Get("city_place_no_land");
        city.GetOrCreate().placed_by_hand = true;
        // 主流文化照国家文化(与起名的文化不同时按它改名)
        CultureService.SetCityMainCulture(city, culture);
        CityPopulationSystem.TransferBackground(source, city, 0.1f);
        string content = string.Format(LM.Get("city_placed"), owner.GetKingdomName(), city.GetCityName());
        TranslateHelper.LogEventOnly(content, owner);
        return content;
    }

    // 无主之地归哪国：这些国家里都城离 tile 最近的(没有都城的按它的头一座城)
    private static Kingdom NearestKingdom(global::System.Collections.Generic.IEnumerable<Kingdom> kingdoms, WorldTile tile)
    {
        Kingdom best = null;
        float bestDistance = float.MaxValue;
        foreach (Kingdom kingdom in kingdoms ?? Enumerable.Empty<Kingdom>())
        {
            if (kingdom?.data == null || kingdom.wild || kingdom.isRekt()) continue;
            WorldTile seat = kingdom.capital?.getTile() ?? kingdom.cities?.FirstOrDefault(city => city?.getTile() != null)?.getTile();
            if (seat == null) continue;
            float d = Toolbox.SquaredDistTile(seat, tile);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = kingdom;
        }
        return best;
    }

    // 营宫殿：返回提示文字
    public static string BuildPalace(WorldTile tile)
    {
        City city = tile?.zone?.city;
        if (city?.data == null || city.isRekt()) return LM.Get("palace_no_city");
        Empire empire = city.kingdom?.GetEmpire();
        Kingdom core = empire == null || empire.isRekt() || empire.IsArchived() ? null : empire.CoreKingdom;
        EmpireCore empireCore = core == null ? null : EmpireCoreManager.Get(empire);
        if (core == null || city.kingdom != core || empireCore == null) return BuildRoyalPalace(city);
        City current = empireCore.GetCoreCapital();
        if (current == city && core.capital == city) return string.Format(LM.Get("palace_already"), city.GetCityName());
        bool confirmed = _armedPalace == city.data.id && UnityEngine.Time.unscaledTime - _armedAt <= ConfirmSeconds;
        if (!confirmed)
        {
            _armedPalace = city.data.id;
            _armedAt = UnityEngine.Time.unscaledTime;
            return current != null && current != city
                ? string.Format(LM.Get("palace_confirm_move"), city.GetCityName(), empire.GetEmpireName(), current.GetCityName())
                : string.Format(LM.Get("palace_confirm"), city.GetCityName(), empire.GetEmpireName());
        }
        _armedPalace = -1L;
        if (current != null && current != city) empireCore.former_capital = current.data.id;
        else if (empireCore.former_capital == city.data.id) empireCore.former_capital = -1L;
        empireCore.SetCoreCapital(city);
        core.setCapital(city);
        PureMapPatch.Redraw(city);
        if (current != null) PureMapPatch.Redraw(current);
        string content = string.Format(LM.Get("palace_built"), empire.GetEmpireFullName(), city.GetCityName());
        EventRecorder.Record(empire, content, empire.Emperor);
        return content;
    }

    // 不是帝国核心之国的城营宫殿(王国、帝国成员、城邦)：头一回只提示，五秒内再点同一座城才营建——成为本国都城(迁都)，
    // 宫城一直留着(不论城大小，见 PureMapPatch.HasPalace)；本来就是都城的只营宫城。没有国家的(无主废城)只营宫城
    private static string BuildRoyalPalace(City city)
    {
        Kingdom kingdom = city.kingdom;
        bool usable = kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;
        var data = city.GetOrCreate();
        if (data.palace_kept && (!usable || kingdom.capital == city)) return string.Format(LM.Get("palace_royal_already"), city.GetCityName());
        bool confirmed = _armedPalace == city.data.id && UnityEngine.Time.unscaledTime - _armedAt <= ConfirmSeconds;
        if (!confirmed)
        {
            _armedPalace = city.data.id;
            _armedAt = UnityEngine.Time.unscaledTime;
            City current = usable ? kingdom.capital : null;
            return current != null && current != city
                ? string.Format(LM.Get("palace_royal_confirm_move"), city.GetCityName(), kingdom.GetKingdomName(), current.GetCityName())
                : string.Format(LM.Get("palace_royal_confirm"), city.GetCityName());
        }
        _armedPalace = -1L;
        City previous = usable ? kingdom.capital : null;
        data.palace_kept = true;
        data.palace_owner = usable ? kingdom.id : -1L;
        if (usable && previous != city) kingdom.setCapital(city);
        PureMapPatch.Redraw(city);
        if (previous != null && previous != city) PureMapPatch.Redraw(previous);
        string content = string.Format(LM.Get("palace_royal_built"), usable ? kingdom.GetKingdomName() : "", city.GetCityName());
        if (usable) TranslateHelper.LogEventOnly(content, kingdom);
        return content;
    }
}

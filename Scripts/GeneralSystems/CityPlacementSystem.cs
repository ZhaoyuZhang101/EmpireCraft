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
public static class CityPlacementSystem
{
    private const float ConfirmSeconds = 5f;
    private const int MinCityDistance = 16;
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
        if (zone == null || tile.Type == null || tile.Type.liquid || tile.Type.mountains || MapWallSystem.Contains(tile))
            return LM.Get("city_place_no_land");
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
            // 无主之地：挨着的国家里都城最近的
            float best = float.MaxValue;
            foreach (TileZone neighbour in zone.neighbours_all ?? global::System.Array.Empty<TileZone>())
            {
                Kingdom kingdom = neighbour?.city?.kingdom;
                WorldTile capital = kingdom?.capital?.getTile();
                if (kingdom == null || kingdom.wild || kingdom.isRekt() || capital == null) continue;
                float d = Toolbox.SquaredDistTile(capital, tile);
                if (d >= best) continue;
                best = d;
                owner = kingdom;
            }
        }
        if (owner == null || owner.wild || owner.isRekt()) return LM.Get("city_place_no_kingdom");
        if (host != null && (zone == host.getTile()?.zone || PureMapPatch.BuiltZones(host)?.Contains(zone.id) == true))
            return string.Format(LM.Get("settlement_place_urban"), host.GetCityName());
        // 不压厂区；敌军占着的地不建(占领记在原城名下，建了城就对不上)
        if (FactorySystem.Near(tile.x, tile.y, 4)) return LM.Get("factory_place_no_room");
        if (host != null && host.GetTileZoneOccupier(zone) != null) return LM.Get("city_place_occupied");
        foreach (City other in World.world.cities)
        {
            WorldTile center = other?.getTile();
            if (center == null || other.isRekt()) continue;
            if (Toolbox.SquaredDistTile(center, tile) < MinCityDistance * MinCityDistance)
                return string.Format(LM.Get("settlement_place_crowded"), other.GetCityName());
        }
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
        if (founder == null || founder.isRekt()) founder = source.leader;
        if (founder == null || founder.isRekt() || !founder.isAlive() || founder.kingdom != owner)
            return LM.Get("city_place_no_founder");
        if (host != null)
        {
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

    // 营宫殿：返回提示文字
    public static string BuildPalace(WorldTile tile)
    {
        City city = tile?.zone?.city;
        if (city?.data == null || city.isRekt()) return LM.Get("palace_no_city");
        Empire empire = city.kingdom?.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived()) return LM.Get("palace_no_empire");
        Kingdom core = empire.CoreKingdom;
        if (city.kingdom != core) return string.Format(LM.Get("palace_not_core"), core?.GetKingdomName() ?? "");
        EmpireCore empireCore = EmpireCoreManager.Get(empire);
        if (empireCore == null) return LM.Get("palace_no_empire");
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
        empireCore.SetCoreCapital(city);
        core.setCapital(city);
        PureMapPatch.Redraw(city);
        if (current != null) PureMapPatch.Redraw(current);
        string content = string.Format(LM.Get("palace_built"), empire.GetEmpireFullName(), city.GetCityName());
        EventRecorder.Record(empire, content, empire.Emperor);
        return content;
    }
}

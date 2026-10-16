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

    public static string PlaceCity(WorldTile tile)
    {
        TileZone zone = tile?.zone;
        if (zone == null || tile.Type == null || tile.Type.liquid || tile.Type.mountains || MapWallSystem.Contains(tile))
            return LM.Get("city_place_no_land");
        City host = zone.city;
        Kingdom owner = host?.kingdom;
        if (host == null)
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
        Actor founder = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.SpawnOfficial(source, owner) : null;
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
        CultureService.SetCityMainCulture(city, CultureService.GetMainCulture(source));
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

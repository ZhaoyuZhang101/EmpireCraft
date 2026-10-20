using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 迁城(用户 2026-10-20 "允许城市中心城池重新定位"，问答定"神力手动迁，不花钱、不花时间""原来的城池变成一处镇")：
//   · 营建页「迁城」：先点一座城(它地界里任一块地)选中它，再点本城地界里的另一块地(不是水、山、城墙)，
//     城池当场迁过去；点到别城的地改选那座城，点到无主之地划进本城再迁；半分钟不点作罢。不做 AI 自动迁；
//   · 城心记在 CityExtraData.center_tile_x/y(见 CityCenterPatch)：迁城就是改记这一格；那块地丢了照原版重新定。
//     城市平面图、城墙、宫城、街名、铭牌、政令路程、驻军都以城心为准，跟着过去；
//   · 新城心那块地上的村镇(关城除外)并进城里；点在本城一处村镇上就以它为新城心(镇升为城)；
//   · 旧城址留一处镇(纯净地图，见 RuralSettlementSystem.LeaveOldSeat)：有城墙的城留有墙的大镇，游牧营地留毡帐镇；
//     人口、村镇、地界照旧，只是城心换地方；离新城太近、被新城区盖住的照村镇的老规矩不画，城退回去又露出来；
//   · 连着本城的官道、航线照新城心重新找路(修路的等级、进度不动，见 RoadNetwork.ReplanCity)，政令路程当即重算；
//   · 开小人(原版建筑真实存在)时篝火跟着搬：新城心附近放得下就点一座新的、拆掉旧的，放不下就留在原处(城心照样迁)；
//   · 都城迁城心仍是都城，皇宫(帝国核心默认首都)随城走。
// 手动不设限(用户 2026-10-20 "手动放置建筑时候不要有任何限制")：选中一座城后点无主之地也迁(那块地当场划进本城)；
//   丘陵也行，不看离厂区多近、那块地有没有敌军占着。只留：水、岩浆、山、高峰、城墙上不迁(原版城心要人走得到、能放篝火)；
//   点到别城的地是改选那座城(别城的地不能拿来当本城城心)。
public static class CityRelocationSystem
{
    private const float PickSeconds = 30f;
    // 篝火离新城心至多几格
    private const int BonfireReach = 6;
    private static long _picked = -1L;
    private static float _pickedAt = -100f;

    // 神力点一下：返回提示文字
    public static string Click(WorldTile tile)
    {
        City clicked = tile?.zone?.city;
        if (clicked != null && (clicked.data == null || clicked.isRekt())) clicked = null;
        City picked = _picked >= 0L && Time.unscaledTime - _pickedAt <= PickSeconds ? World.world.cities.get(_picked) : null;
        if (picked != null && (picked.data == null || picked.isRekt())) picked = null;
        // 选中了城、点到无主之地：那块地划进本城再迁
        if (clicked == null && picked != null && tile?.zone != null && tile.zone.city == null)
        {
            _pickedAt = Time.unscaledTime;
            string moved = Relocate(picked, tile, out bool claimed);
            if (claimed) _picked = -1L;
            return moved;
        }
        if (clicked == null)
            return picked != null ? string.Format(LM.Get("relocate_city_outside"), picked.GetCityName()) : LM.Get("relocate_city_no_city");
        if (picked != clicked)
        {
            _picked = clicked.data.id;
            _pickedAt = Time.unscaledTime;
            return string.Format(LM.Get("relocate_city_pick"), clicked.GetCityName());
        }
        // 没迁成(点错了地方)仍选着这座城，再点一处就行
        _pickedAt = Time.unscaledTime;
        string result = Relocate(clicked, tile, out bool done);
        if (done) _picked = -1L;
        return result;
    }

    // 把 city 的城池迁到 tile(本城地界里，或无主之地：先划进本城)；done 为迁成没有
    public static string Relocate(City city, WorldTile tile, out bool done)
    {
        done = false;
        if (city?.data == null || city.isRekt()) return LM.Get("relocate_city_no_city");
        string cityName = city.GetCityName();
        if (tile?.zone == null || tile.zone.city != null && tile.zone.city != city)
            return string.Format(LM.Get("relocate_city_outside"), cityName);
        if (tile.zone.city == null)
        {
            if (!UrbanFabric.ManualGround(tile)) return LM.Get("relocate_city_no_land");
            using (CityExpansionPatch.Manual())
                city.addZone(tile.zone);
            if (tile.zone.city != city) return string.Format(LM.Get("relocate_city_outside"), cityName);
            CityDistricts.Reset();
        }
        bool pure = PureMapMode.Enabled;
        // 点在本城一处村镇上：以它为新城心
        RuralSettlement seat = pure ? RuralSettlementSystem.Near(tile) : null;
        WorldTile seatTile = seat == null ? null : World.world.GetTile(seat.x, seat.y);
        if (seat != null && !seat.fort && seatTile?.zone?.city == city && UrbanFabric.ManualGround(seatTile)) tile = seatTile;
        else seat = null;
        if (!UrbanFabric.ManualGround(tile)) return LM.Get("relocate_city_no_land");
        TileZone zone = tile.zone;
        WorldTile old = city.getTile();
        TileZone oldZone = old?.zone;
        if (zone == oldZone) return string.Format(LM.Get("relocate_city_same"), cityName);

        // 旧城有没有城墙(按迁之前的平面图)；新城址的名字(点的是村镇)
        bool walled = pure && oldZone != null && PureMapPatch.Walled(city);
        string site = seat != null ? RuralSettlementSystem.Label(seat) : null;
        if (pure) RuralSettlementSystem.Absorb(zone);

        CityExtension.CityExtraData data = city.GetOrCreate();
        data.center_tile_x = tile.x;
        data.center_tile_y = tile.y;
        city.recalculateCityTile();
        city.updateCityCenter();
        city.setStatusDirty();
        MoveBonfire(city, tile);

        RuralSettlement town = pure && oldZone != null ? RuralSettlementSystem.LeaveOldSeat(city, oldZone, walled) : null;
        RuralSettlementSystem.Touch();
        CityDistricts.Reset();
        // 本城当即重画；邻城接过来的官道换了走向，指纹跟着变(见 PureMapPatch.Stamp)，按轮重建时(约十秒一轮)重画
        PureMapPatch.Redraw(city);
        RoadNetwork.ReplanCity(city);
        AdministrativeControlSystem.Refresh(city.kingdom);
        done = true;

        string where = site != null ? string.Format(LM.Get("relocate_city_site_town"), site)
            : string.Format(LM.Get("relocate_city_site_bearing"), LM.Get("relocate_dir_" + Bearing(old ?? tile, tile)));
        string content = string.Format(LM.Get("relocate_city_done"), city.kingdom?.GetKingdomName() ?? "", cityName, where);
        if (town != null) content += string.Format(LM.Get("relocate_city_old_town"), RuralSettlementSystem.Label(town));
        if (city.kingdom != null) EventRecorder.Record(city.kingdom, content);
        return content;
    }

    // 新城址在旧城的哪一方：0 东、1 东北、2 北、3 西北、4 西、5 西南、6 南、7 东南(地图上 y 往上是北)
    private static int Bearing(WorldTile from, WorldTile to)
    {
        float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;
        return Mathf.RoundToInt(angle / 45f) % 8;
    }

    // 开小人时原版篝火是城的心(人在那里闲坐、聚会，篝火产粮)：在新城心附近本城的地上找一处放得下的空地点一座新的，
    // 再拆掉旧的；放不下就不搬(城心照样迁，见 CityCenterPatch)。纯净地图下的虚拟城市多半没有篝火，没有就不管
    private static void MoveBonfire(City city, WorldTile target)
    {
        try
        {
            Building old = city.getBuildingOfType("type_bonfire", pCountOnlyFinished: false);
            if (old?.asset == null || old.isOnRemove() || old.current_tile?.zone == target.zone) return;
            BuildingAsset asset = old.asset;
            WorldTile spot = null;
            int best = BonfireReach * BonfireReach + 1;
            var zones = new List<TileZone> { target.zone };
            if (target.zone.neighbours_all != null) zones.AddRange(target.zone.neighbours_all);
            foreach (TileZone zone in zones)
            {
                if (zone?.city != city || zone.tiles == null) continue;
                foreach (WorldTile tile in zone.tiles)
                {
                    int d = Toolbox.SquaredDistTile(tile, target);
                    if (d >= best || !tile.canBuildOn(asset) || !World.world.buildings.canBuildFrom(tile, asset, city)) continue;
                    best = d;
                    spot = tile;
                }
            }
            if (spot == null) return;
            Building fresh = World.world.buildings.addBuilding(asset, spot);
            if (fresh == null) return;
            old.startRemove();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][迁城] 篝火没搬成: {exception.Message}");
        }
    }
}

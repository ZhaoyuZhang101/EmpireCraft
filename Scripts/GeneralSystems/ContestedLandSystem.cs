using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 一份故地宣称：哪个帝国核心、从什么时候起、是不是神力手动标的(手动的不过期)
public class ContestClaim
{
    public long core = -1L;
    public double since = -1d;
    public bool manual;
}

// 争议地带(用户 2026-10-22 问"让城市拥有两个帝国核心和天下"，问答选"争议地带"；
// "这种城市一般出现在边界，帝国图层和天下图层应该显示为两种颜色的叠加"；"玩家也可以自己用权能设定，固定法理也生效")：
//   · 城仍只归一个帝国核心(法理照旧)，另记至多两家"故地宣称"的核心(CityExtraData.contested_claims)；
//   · 来由：每年看一遍，城的核心换了(法理被别的核心吸收、校正)而原核心就在隔壁(相邻地块有原核心的城)，原核心留一份宣称；
//     神力「争议地带」手动标、撤(先点城，再点宣称方核心里的任一座城；再点本城或同核心的城就撤销)——冻结法理时照样能用；
//   · 效果：算进宣称方所在天下的城(统一占比、统一战争的目标，见 TianxiaSystem.Cities)；宣称方帝国开战时这座城算收复
//     (见 WarGoalSystem.Claimed)；法理图层的帝国核心显示、天下图层上画两家颜色相间的斜纹(见 PureMapPatch.ShadeTerrain)；
//     帝国核心的悬停说明写"另有某核心宣称"；
//   · 失效：宣称方核心没了、城回到宣称方核心手里；自动记下的宣称满百年淡忘(手动标的不过期)；
//     开着冻结法理时一律不淡忘，永远挂着两家的法理(用户：开启冻结法理后争议地带也永远不会转正永远挂着双法理)。
public static class ContestedLandSystem
{
    public const int MaxClaims = 2;
    private const int ForgetYears = 100;
    private static double _lastScan = -1d;

    public static void ResetWorldState() => _lastScan = -1d;

    public static bool HasClaims(City city) => city?.data != null && city.GetOrCreate().contested_claims?.Count > 0;

    // 宣称这座城的别家帝国核心(还在的、不是本城自己的核心)
    public static List<EmpireCore> Claimants(City city)
    {
        var result = new List<EmpireCore>();
        if (!HasClaims(city)) return result;
        long own = city.GetEmpireCoreID();
        foreach (ContestClaim claim in city.GetOrCreate().contested_claims)
        {
            if (claim == null || claim.core == own) continue;
            if (EmpireCoreManager.EmpireCores.TryGetValue(claim.core, out EmpireCore core) && core != null && !result.Contains(core))
                result.Add(core);
        }
        return result;
    }

    // 宣称方帝国里有攻方一方的(帝国按整个帝国算)，城又不在攻方手里：开战时算收复
    public static bool ClaimedBy(City city, Kingdom realm)
    {
        if (realm == null || !HasClaims(city) || city.kingdom != null && FrontSystem.Realm(city.kingdom) == realm) return false;
        foreach (EmpireCore core in Claimants(city))
            foreach (Empire empire in EmpireCoreManager.GetEmpires(core))
                if (empire?.CoreKingdom != null && !empire.isRekt() && !empire.IsArchived() && FrontSystem.Realm(empire.CoreKingdom) == realm)
                    return true;
        return false;
    }

    // 记一份宣称(已有就不重记；满两家时挤掉最早的)
    public static bool AddClaim(City city, EmpireCore core, bool manual)
    {
        if (city?.data == null || core == null || city.GetEmpireCoreID() == core.id) return false;
        var data = city.GetOrCreate();
        data.contested_claims ??= new List<ContestClaim>();
        ContestClaim existing = data.contested_claims.Find(claim => claim?.core == core.id);
        if (existing != null)
        {
            existing.manual |= manual;
            return false;
        }
        while (data.contested_claims.Count >= MaxClaims) data.contested_claims.RemoveAt(0);
        data.contested_claims.Add(new ContestClaim { core = core.id, since = World.world.getCurWorldTime(), manual = manual });
        Touch(city);
        return true;
    }

    public static bool RemoveClaim(City city, EmpireCore core)
    {
        if (!HasClaims(city) || core == null) return false;
        if (city.GetOrCreate().contested_claims.RemoveAll(claim => claim?.core == core.id) == 0) return false;
        Touch(city);
        return true;
    }

    public static bool Clear(City city)
    {
        if (!HasClaims(city)) return false;
        city.GetOrCreate().contested_claims.Clear();
        Touch(city);
        return true;
    }

    // 每年(挂在全图年度扫描上)：核心换了、原核心就在隔壁的留一份宣称；过期、失效的清掉
    public static void TryYearlyScan()
    {
        if (World.world?.cities == null) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan < 0d || now < _lastScan)
        {
            _lastScan = now;
            return;
        }
        if (Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (City city in World.world.cities.list.ToList())
        {
            if (city?.data == null || city.isRekt()) continue;
            var data = city.GetOrCreate();
            long current = city.GetEmpireCoreID();
            long last = data.contested_last_core;
            if (current >= 0) data.contested_last_core = current;
            if (last >= 0 && current >= 0 && last != current &&
                EmpireCoreManager.EmpireCores.TryGetValue(last, out EmpireCore previous) && previous != null && Borders(city, previous))
                AddClaim(city, previous, false);
            Prune(city, data, now);
        }
    }

    private static void Prune(City city, CityExtension.CityExtraData data, double now)
    {
        if (data.contested_claims == null || data.contested_claims.Count == 0) return;
        long own = city.GetEmpireCoreID();
        bool frozen = ModClass.KINGDOM_TITLE_FREEZE;
        int removed = data.contested_claims.RemoveAll(claim => claim == null || claim.core == own ||
                                                              !EmpireCoreManager.EmpireCores.ContainsKey(claim.core) ||
                                                              !frozen && !claim.manual && claim.since >= 0d && now >= claim.since &&
                                                              Date.getYearsSince(claim.since) >= ForgetYears);
        if (removed > 0) Touch(city);
    }

    // 本城有地块挨着这个核心的城
    private static bool Borders(City city, EmpireCore core)
    {
        if (city?.zones == null) return false;
        foreach (TileZone zone in city.zones)
            foreach (TileZone next in new[] { zone.zone_up, zone.zone_down, zone.zone_left, zone.zone_right })
            {
                City other = next?.city;
                if (other != null && other != city && !other.isRekt() && other.GetEmpireCoreID() == core.id) return true;
            }
        return false;
    }

    // 宣称变了：本城各地块下一轮重画(斜纹)
    private static void Touch(City city)
    {
        foreach (TileZone zone in city?.zones ?? new List<TileZone>()) zone?.resetRenderHelpers();
    }

    // ───────── 地图上的第二种颜色 ─────────
    // 法理图层的帝国核心显示(选项 0)：头一家宣称方的颜色(它的帝国还在就用帝国的颜色，否则用核心的代表色)；
    // 天下图层(选项 0)：头一家不在本城天下的宣称方所在天下的颜色。别的图层、没有宣称的为空
    public static Color32? StripeColor(City city)
    {
        if (!HasClaims(city) || World.world == null) return null;
        MetaTypeAsset layer = World.world.zone_calculator?._mode_asset ?? World.world.getCachedMapMetaAsset();
        if (layer == null || layer.getZoneOptionState() != 0) return null;
        if (layer.map_mode == MetaTypeExtension.KingdomTitle)
        {
            EmpireCore core = Claimants(city).FirstOrDefault();
            if (core == null) return null;
            Empire empire = ModClass.EMPIRE_MANAGER?.get(core.empire_id);
            object meta = empire != null && !empire.isRekt() && !empire.IsArchived() ? empire : EmpireCoreManager.GetColorTitle(core);
            ColorAsset color = CustomColorSystem.Of(meta) ?? (meta as IMetaObject)?.getColor();
            return Opaque(color);
        }
        if (layer.map_mode == MetaTypeExtension.Tianxia)
        {
            EmpireCore own = TianxiaSystem.LeadOf(city);
            foreach (EmpireCore claimant in Claimants(city))
            {
                EmpireCore lead = TianxiaSystem.Lead(claimant);
                if (lead == null || lead == own) continue;
                return ColorUtility.TryParseHtmlString(TianxiaMap.HexOf(lead), out Color color) ? (Color32)color : null;
            }
        }
        return null;
    }

    private static Color32? Opaque(ColorAsset color)
    {
        if (color == null) return null;
        Color32 fill = color.getColorBorderInsideAlpha32();
        fill.a = byte.MaxValue;
        return fill;
    }

    // 平滑国界的上色缓存键(见 SmoothBorders.KeyOf)：斜纹的颜色变了要重画
    public static int StripeKey(City city)
    {
        Color32? color = StripeColor(city);
        return color is Color32 c ? c.r | c.g << 8 | c.b << 16 | 1 << 24 : 0;
    }

    // 斜纹：两格本色、两格宣称方的颜色相间
    public static bool StripeAt(WorldTile tile) => (tile.x + tile.y) % 4 < 2;
}

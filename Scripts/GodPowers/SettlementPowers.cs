using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

// 营建神力(用户 2026-10-17 "手动把村子划入城市势力范围""手动放置村/镇……和销毁他们"，见 RuralSettlementSystem)：
//   立村、设镇、筑关：点城郊平地放一处，小路顺着地势自动连上；建城、营宫殿见 CityPlacementSystem；
//   迁城：先点一座城，再点本城地界里的新城址，城池当场迁过去、旧城址留一处镇(见 CityRelocationSystem)；
//   毁弃：点一处村镇，完好的毁为废墟，对废墟再点一下彻底抹去；
//   改隶：先点一处村镇，再点一座接壤的城，村镇连同它那一片地改归那座城(点别处取消，半分钟不点也取消)；
//   小、中、大厂区：点城的地放下一整片成品厂区(见 FactorySystem.Place)；毁弃也能拆厂区；
//   城貌：点一座城打开城貌小窗口，从各文化风格里任选一种(见 CityStyleWindow)；纯净地图开着时打开城池全图(见 CityPlanScreen)；
//   中立区：点一片无人区设为永久中立区，再点取消(见 WildernessSystem.ToggleNeutral)；
//   开发：按住刷雨林、大泽无人区，刷到的地块开发度涨一截，格子一批批改成草地，开满退出无人区(见 WildernessDevelopmentSystem.Brush)；
//   无人区法理(用户 10-30 "给个划分无人区法理归属的权能""可以允许多个法理归属同一个无人区，但是类似于笔刷上色那种""一个法理拿一部分")：
//   先在一座城上按一下取它的王国法理，再按住拖过无人区，刷到的地块挂到这个法理上，归取得这个法理的国家、跟着那座城的行政区，
//   自动重挂不再动它(见 WildernessSystem.Paint)；一片无人区可以几个法理各刷一部分。「还给法理」笔刷把刷过的地块还给自动的规矩。
// 这些都是手动的，上帝说了算(用户 2026-10-20 "手动放置建筑时候不要有任何限制")：不看科技、文化、地形类别、钱、战争、法则、数量、距离，
// 只留真做不到的(各系统开头写着)；AI 自己营建照旧守规矩。
public static class SettlementPowers
{
    public const string Village = "place_village", Town = "place_town", Fort = "place_fort", Raze = "raze_settlement", Assign = "assign_settlement";
    public const string NewCity = "place_city", Palace = "build_palace", Relocate = "relocate_city", Restyle = "restyle_city";
    public const string FactorySmall = "factory_small", FactoryMedium = "factory_medium", FactoryLarge = "factory_large";
    // 中立区：点一片无人区设为永久中立区，再点取消(见 WildernessSystem.ToggleNeutral)
    public const string WildNeutral = "wild_neutral";
    // 开发：按住刷雨林、大泽无人区(笔刷大小用原版的)，不看科技与归属(见 WildernessDevelopmentSystem.Brush)
    public const string WildDevelop = "wild_develop";
    // 无人区法理：刷给取的王国法理 / 还给自动的规矩
    public const string WildTitle = "wild_title", WildTitleClear = "wild_title_clear";
    public static readonly string[] Ids =
    {
        Village, Town, Fort, NewCity, Relocate, Palace, Restyle, FactorySmall, FactoryMedium, FactoryLarge, Raze, Assign, WildNeutral,
        WildDevelop, WildTitle, WildTitleClear
    };
    // 无人区法理笔刷取的城与法理；这一笔还在取的那座城上(拖出城的地才刷)、这一笔刷不了
    private static EmpireCraft.Scripts.Layer.KingdomTitle _brushTitle;
    private static City _brushCity;
    private static bool _strokeHolding, _strokeDead;
    private const float PickSeconds = 30f, DevelopTipSeconds = 0.25f;
    private static readonly HashSet<TileZone> Brushed = new();
    private static float _developTipAt = -100f;
    private static int _picked = -1;
    private static float _pickedAt = -100f;

    public static void Init()
    {
        if (AssetManager.powers.get(Village) != null) return;
        AssetManager.powers.add(new GodPower { id = Village, name = Village, click_action = (tile, _) => Tip(Build(tile, "village")) });
        AssetManager.powers.add(new GodPower { id = Town, name = Town, click_action = (tile, _) => Tip(Build(tile, "town")) });
        AssetManager.powers.add(new GodPower { id = Fort, name = Fort, click_action = (tile, _) => Tip(Build(tile, "fort")) });
        AssetManager.powers.add(new GodPower { id = NewCity, name = NewCity, click_action = (tile, _) => Tip(CityPlacementSystem.PlaceCity(tile)) });
        AssetManager.powers.add(new GodPower { id = Palace, name = Palace, click_action = (tile, _) => Tip(CityPlacementSystem.BuildPalace(tile)) });
        AssetManager.powers.add(new GodPower { id = Relocate, name = Relocate, click_action = (tile, _) => Tip(CityRelocationSystem.Click(tile)) });
        AssetManager.powers.add(new GodPower { id = Restyle, name = Restyle, click_action = ClickRestyle });
        AssetManager.powers.add(new GodPower { id = Raze, name = Raze, click_action = (tile, _) => Tip(RazeAt(tile)) });
        AssetManager.powers.add(new GodPower { id = WildNeutral, name = WildNeutral, click_action = (tile, _) => Tip(ToggleNeutral(tile)) });
        AssetManager.powers.add(new GodPower
        {
            id = WildDevelop,
            name = WildDevelop,
            hold_action = true,
            show_tool_sizes = true,
            unselect_when_window = true,
            click_power_action = CollectWild,
            click_power_brush_action = StrokeWild
        });
        foreach (string id in new[] { WildTitle, WildTitleClear })
            AssetManager.powers.add(new GodPower
            {
                id = id,
                name = id,
                hold_action = true,
                show_tool_sizes = true,
                unselect_when_window = true,
                click_power_action = CollectWild,
                click_power_brush_action = StrokeTitle
            });
        foreach (string id in new[] { FactorySmall, FactoryMedium, FactoryLarge })
            AssetManager.powers.add(new GodPower { id = id, name = id, click_action = (tile, power) => Tip(Build(tile, id)) });
        AssetManager.powers.add(new GodPower { id = Assign, name = Assign, click_action = ClickAssign });
        // 城貌小窗口随营建神力一起注册(标题用 "<窗口id> Title" 约定 key；返回按钮显示城市图标)
        CityStyleWindow.CreateAndInit(nameof(CityStyleWindow), new Vector2(480f, 200f));
        WindowAsset window = AssetManager.window_library.get(nameof(CityStyleWindow));
        if (window != null && SpriteTextureLoader.getSprite("ui/Icons/iconCity") != null) window.icon_path = "iconCity";
    }

    // 城貌：点一座城(它地界里任一块地)打开城貌小窗口；纯净地图开着时打开城池全图
    // (用户 2026-10-28 "窗口全屏，整座城显示完整"，见 CityPlanScreen)
    private static bool ClickRestyle(WorldTile tile, string power)
    {
        City city = tile?.zone?.city;
        if (city?.data == null || city.isRekt()) return Tip(LM.Get("city_style_no_city"));
        if (PureMapMode.Enabled) CityPlanScreen.Open(city);
        else CityStyleWindow.Open(city);
        return true;
    }

    // 立村、设镇、筑关、放小中大厂区(神力与城池全图共用，见 CityPlanWorks.PlaceSettlement)：
    // kind 为 village、town、fort、factory_small、factory_medium、factory_large；返回提示文字，认不得的 kind 返回 null
    internal static string Build(WorldTile tile, string kind) => kind switch
    {
        "village" => RuralSettlementSystem.Place(tile, false),
        "town" => RuralSettlementSystem.Place(tile, true),
        "fort" => RuralSettlementSystem.Place(tile, false, true),
        FactorySmall => FactorySystem.Place(tile, 1, out _, true),
        FactoryMedium => FactorySystem.Place(tile, 2, out _, true),
        FactoryLarge => FactorySystem.Place(tile, 3, out _, true),
        _ => null
    };

    // 毁弃(神力与城池全图共用，见 CityPlanWorks.Raze)：点到的镇村完好的毁为废墟、废墟彻底抹去，点到的不是镇村就拆厂区；
    // only 给了就只认落在这座城地上的(点到别城的报不归本城)
    internal static string RazeAt(WorldTile tile, City only = null)
    {
        RuralSettlement settlement = RuralSettlementSystem.Near(tile);
        FactorySite site = settlement == null ? FactorySystem.At(tile) : null;
        if (only != null && (settlement != null && RuralSettlementSystem.CityOf(settlement) != only ||
                             site != null && FactorySystem.CityOf(site) != only))
            return LM.Get("city_plan_works_not_own");
        return RuralSettlementSystem.Raze(settlement) ?? FactorySystem.Remove(site) ?? LM.Get("settlement_none");
    }

    // 中立区(用户 2026-10-29 "也可以手动将他设置成永久中立区")：点一片无人区，设为永久中立区——不归任何国家，法理、周边的城都不管；
    // 再点一次取消，照常按法理、周边的城定归属(见 WildernessSystem.OwnerOf)
    private static string ToggleNeutral(WorldTile tile)
    {
        WildRegion region = WildernessSystem.ToggleNeutral(tile?.zone);
        if (region == null) return LM.Get("wild_neutral_none");
        return string.Format(LM.Get(region.Neutral ? "wild_neutral_on" : "wild_neutral_off"), WildernessSystem.DisplayName(region));
    }

    // 开发(用户 2026-10-29 要雨林、大泽有开发度，神力手动刷——"手动放置建筑时候不要有任何限制")：笔刷盖到的地块记下，
    // 按住时每帧各刷一次(同一块隔一会儿才再涨，见 WildernessDevelopmentSystem.Brush)；提示笔刷中心那块的开发度
    private static bool CollectWild(WorldTile tile, GodPower power)
    {
        if (tile?.zone != null) Brushed.Add(tile.zone);
        return true;
    }

    private static bool StrokeWild(WorldTile center, GodPower power)
    {
        if (center == null || power == null || World.world == null) return false;
        Brushed.Clear();
        World.world.loopWithBrush(center, global::Brush.get(Config.current_brush), CollectWild, power);
        bool any = false;
        foreach (TileZone zone in Brushed) any |= WildernessDevelopmentSystem.Brush(zone);
        Brushed.Clear();
        float now = Time.unscaledTime;
        if (now - _developTipAt < DevelopTipSeconds && now >= _developTipAt) return any;
        _developTipAt = now;
        WildRegion region = WildernessSystem.RegionOf(center.zone);
        string text = region != null && WildernessDevelopmentSystem.Develops(region.Kind)
            ? string.Format(LM.Get("wild_develop_brush"), WildernessSystem.DisplayName(region),
                Mathf.FloorToInt(WildernessDevelopmentSystem.DevelopmentOf(center.zone)))
            : any ? null : LM.Get("wild_develop_none");
        if (!string.IsNullOrEmpty(text)) WorldTip.showNow(text, false, "top", 1.5f);
        return any;
    }

    // 无人区法理笔刷(群友 2026-11-05 "为什么划不了那国家无人区法理，有时候能画，有时候不能划")：按下时落在城上就取这座城，
    // 拖出城的地才开始刷；落在别处照取的城刷。刷到的地块跟着这座城，归它所在的国、挂它的王国法理。一笔从按下到放开
    // (原版 PlayerControl.first_click)，中途经过别的城不换。以前隔 0.25 秒没刷就算新一笔，掉帧或停一下再落到城上就悄悄换了法理；
    // 按在城上拖出去不刷；刷不上也没有提示。擦除笔刷不用取
    private static bool StrokeTitle(WorldTile center, GodPower power)
    {
        if (center == null || power == null || World.world == null) return false;
        bool erase = power.id == WildTitleClear;
        if (World.world.player_control.first_click)
        {
            _strokeDead = _strokeHolding = false;
            City city = center.zone?.city;
            if (!erase && city?.data != null && !city.isRekt())
            {
                EmpireCraft.Scripts.Layer.KingdomTitle title = city.GetTitle();
                if (title == null || title.isRekt())
                {
                    _strokeDead = true;
                    return Tip(LM.Get("wild_title_no_title"));
                }
                _brushTitle = title;
                _brushCity = city;
                _strokeHolding = true;
                return Tip(string.Format(LM.Get("wild_title_picked"), city.name, title.data?.name, city.kingdom?.name));
            }
            if (!erase && (_brushTitle == null || _brushTitle.isRekt()))
            {
                _strokeDead = true;
                return Tip(LM.Get("wild_title_need"));
            }
        }
        if (_strokeDead || !erase && _brushTitle == null) return true;
        if (_strokeHolding)
        {
            if (center.zone?.city != null) return true;
            _strokeHolding = false;
        }
        Brushed.Clear();
        World.world.loopWithBrush(center, global::Brush.get(Config.current_brush), CollectWild, power);
        bool any = false;
        City brushCity = erase || _brushCity?.data == null || _brushCity.isRekt() ? null : _brushCity;
        foreach (TileZone zone in Brushed) any |= WildernessSystem.Paint(zone, erase ? null : _brushTitle, brushCity);
        Brushed.Clear();
        if (!erase) TitleStrokeTip(center.zone);
        return any;
    }

    // 刷着时提示光标下这块的结果，看得出刷上没有、归了谁
    private static void TitleStrokeTip(TileZone zone)
    {
        float now = Time.unscaledTime;
        if (now - _developTipAt < DevelopTipSeconds && now >= _developTipAt) return;
        _developTipAt = now;
        WildRegion region = WildernessSystem.RegionOf(zone);
        string text;
        if (region == null) text = LM.Get("wild_title_not_wild");
        else if (WildernessSystem.IsNeutral(zone)) text = string.Format(LM.Get("wild_title_neutral"), WildernessSystem.DisplayName(region));
        else
            text = string.Format(LM.Get("wild_title_brush"), WildernessSystem.DisplayName(region), WildernessSystem.TitleOf(zone)?.data?.name,
                WildernessSystem.OwnerOf(zone)?.name ?? LM.Get("wild_title_no_owner"));
        WorldTip.showNow(text, false, "top", 1.5f);
    }

    private static bool Tip(string text)
    {
        if (!string.IsNullOrEmpty(text)) WorldTip.showNow(text, false, "top", 3f);
        return true;
    }

    private static bool ClickAssign(WorldTile tile, string power)
    {
        RuralSettlement picked = _picked > 0 && Time.unscaledTime - _pickedAt <= PickSeconds ? RuralSettlementSystem.ById(_picked) : null;
        RuralSettlement clicked = RuralSettlementSystem.Near(tile);
        City target = tile?.zone?.city;
        City source = picked == null ? null : World.world.GetTile(picked.x, picked.y)?.zone?.city;
        // 还没选、或点到本城的另一处村镇：选中它
        if (picked == null || clicked != null && clicked != picked && target == source)
        {
            if (clicked == null)
            {
                _picked = -1;
                return Tip(LM.Get("settlement_none"));
            }
            _picked = clicked.id;
            _pickedAt = Time.unscaledTime;
            return Tip(string.Format(LM.Get("settlement_assign_pick"), RuralSettlementSystem.Label(clicked)));
        }
        _picked = -1;
        if (target == null) return Tip(LM.Get("settlement_assign_cancel"));
        return Tip(RuralSettlementSystem.Assign(picked, target));
    }
}

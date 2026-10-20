using EmpireCraft.Scripts.Data;
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
//   城貌：点一座城打开城貌小窗口，从各文化风格里任选一种(见 CityStyleWindow)。
// 这些都是手动的，上帝说了算(用户 2026-10-20 "手动放置建筑时候不要有任何限制")：不看科技、文化、地形类别、钱、战争、法则、数量、距离，
// 只留真做不到的(各系统开头写着)；AI 自己营建照旧守规矩。
public static class SettlementPowers
{
    public const string Village = "place_village", Town = "place_town", Fort = "place_fort", Raze = "raze_settlement", Assign = "assign_settlement";
    public const string NewCity = "place_city", Palace = "build_palace", Relocate = "relocate_city", Restyle = "restyle_city";
    public const string FactorySmall = "factory_small", FactoryMedium = "factory_medium", FactoryLarge = "factory_large";
    public static readonly string[] Ids =
        { Village, Town, Fort, NewCity, Relocate, Palace, Restyle, FactorySmall, FactoryMedium, FactoryLarge, Raze, Assign };
    private const float PickSeconds = 30f;
    private static int _picked = -1;
    private static float _pickedAt = -100f;

    public static void Init()
    {
        if (AssetManager.powers.get(Village) != null) return;
        AssetManager.powers.add(new GodPower { id = Village, name = Village, click_action = (tile, _) => Tip(RuralSettlementSystem.Place(tile, false)) });
        AssetManager.powers.add(new GodPower { id = Town, name = Town, click_action = (tile, _) => Tip(RuralSettlementSystem.Place(tile, true)) });
        AssetManager.powers.add(new GodPower { id = Fort, name = Fort, click_action = (tile, _) => Tip(RuralSettlementSystem.Place(tile, false, true)) });
        AssetManager.powers.add(new GodPower { id = NewCity, name = NewCity, click_action = (tile, _) => Tip(CityPlacementSystem.PlaceCity(tile)) });
        AssetManager.powers.add(new GodPower { id = Palace, name = Palace, click_action = (tile, _) => Tip(CityPlacementSystem.BuildPalace(tile)) });
        AssetManager.powers.add(new GodPower { id = Relocate, name = Relocate, click_action = (tile, _) => Tip(CityRelocationSystem.Click(tile)) });
        AssetManager.powers.add(new GodPower { id = Restyle, name = Restyle, click_action = ClickRestyle });
        AssetManager.powers.add(new GodPower
        {
            id = Raze, name = Raze,
            click_action = (tile, _) => Tip(RuralSettlementSystem.Raze(tile) ?? FactorySystem.Remove(FactorySystem.At(tile)) ??
                                             LM.Get("settlement_none"))
        });
        foreach ((string id, int size) in new[] { (FactorySmall, 1), (FactoryMedium, 2), (FactoryLarge, 3) })
            AssetManager.powers.add(new GodPower
            {
                id = id, name = id,
                click_action = (tile, power) => Tip(FactorySystem.Place(tile, size, out _, true))
            });
        AssetManager.powers.add(new GodPower { id = Assign, name = Assign, click_action = ClickAssign });
        // 城貌小窗口随营建神力一起注册(标题用 "<窗口id> Title" 约定 key；返回按钮显示城市图标)
        CityStyleWindow.CreateAndInit(nameof(CityStyleWindow), new Vector2(480f, 200f));
        WindowAsset window = AssetManager.window_library.get(nameof(CityStyleWindow));
        if (window != null && SpriteTextureLoader.getSprite("ui/Icons/iconCity") != null) window.icon_path = "iconCity";
    }

    // 城貌：点一座城(它地界里任一块地)打开城貌小窗口
    private static bool ClickRestyle(WorldTile tile, string power)
    {
        City city = tile?.zone?.city;
        if (city?.data == null || city.isRekt()) return Tip(LM.Get("city_style_no_city"));
        CityStyleWindow.Open(city);
        return true;
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

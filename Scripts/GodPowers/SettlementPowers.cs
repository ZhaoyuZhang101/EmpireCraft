using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

// 营建神力(用户 2026-10-17 "手动把村子划入城市势力范围""手动放置村/镇……和销毁他们"，见 RuralSettlementSystem)：
//   立村、设镇、筑关：点城郊平地放一处，小路顺着地势自动连上；
//   毁弃：点一处村镇，完好的毁为废墟，对废墟再点一下彻底抹去；
//   改隶：先点一处村镇，再点一座接壤的城，村镇连同它那一片地改归那座城(点别处取消，半分钟不点也取消)；
//   小、中、大厂区：点城的地放下一整片成品厂区(见 FactorySystem.Place)；毁弃也能拆厂区。
public static class SettlementPowers
{
    public const string Village = "place_village", Town = "place_town", Fort = "place_fort", Raze = "raze_settlement", Assign = "assign_settlement";
    public const string FactorySmall = "factory_small", FactoryMedium = "factory_medium", FactoryLarge = "factory_large";
    public static readonly string[] Ids = { Village, Town, Fort, FactorySmall, FactoryMedium, FactoryLarge, Raze, Assign };
    private const float PickSeconds = 30f;
    private static int _picked = -1;
    private static float _pickedAt = -100f;

    public static void Init()
    {
        if (AssetManager.powers.get(Village) != null) return;
        AssetManager.powers.add(new GodPower { id = Village, name = Village, click_action = (tile, _) => Tip(RuralSettlementSystem.Place(tile, false)) });
        AssetManager.powers.add(new GodPower { id = Town, name = Town, click_action = (tile, _) => Tip(RuralSettlementSystem.Place(tile, true)) });
        AssetManager.powers.add(new GodPower { id = Fort, name = Fort, click_action = (tile, _) => Tip(RuralSettlementSystem.Place(tile, false, true)) });
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
                click_action = (tile, power) => Tip(FactorySystem.Place(tile, size, out _))
            });
        AssetManager.powers.add(new GodPower { id = Assign, name = Assign, click_action = ClickAssign });
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

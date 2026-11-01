using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 外交(群管理 10-30 建议"先点击发起国然后再点击指定国家进入外交界面后就锁定这个国家")，两步：
//   · 第一下点城选发起国：屏幕提示，发起国国土描亮、从都城拉箭头到光标(见 EmpireCraftQuantumSpriteLibrary.DrawDiplomacyPick)；
//   · 第二下点另一国的城：打开外交界面，进这两国的双边页(操作页也对着这一国)；
//     再点发起国自己：打开它的总览(与各国一行，即以前点一下的用法，用户 10-30 定)；没点到国家：提示，发起国留着；
//   · 重新点一下权能按钮清掉已选的发起国(同原版同盟、挑拨战争权能)。
public static class DiplomacyPower
{
    public const string Id = "diplomacy";

    public static long SelectedId = -1L;

    public static Kingdom Selected()
    {
        Kingdom kingdom = SelectedId >= 0 ? World.world?.kingdoms?.get(SelectedId) : null;
        if (!Usable(kingdom)) SelectedId = -1L;
        return SelectedId >= 0 ? kingdom : null;
    }

    private static bool Usable(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;

    public static void Init()
    {
        if (AssetManager.powers.get(Id) != null) return;
        AssetManager.powers.add(new GodPower
        {
            id = Id,
            name = Id,
            select_button_action = _ =>
            {
                SelectedId = -1L;
                Tip(LM.Get("diplomacy_pick_first"));
                return false;
            },
            click_action = (tile, _) => Click(tile?.zone?.city?.kingdom)
        });
    }

    private static bool Click(Kingdom kingdom)
    {
        Kingdom first = Selected();
        if (!Usable(kingdom))
        {
            Tip(first == null ? LM.Get("diplomacy_pick_hint") : string.Format(LM.Get("diplomacy_pick_missed"), first.GetKingdomName()));
            return false;
        }
        if (first == null)
        {
            SelectedId = kingdom.id;
            Tip(string.Format(LM.Get("diplomacy_pick_second"), kingdom.GetKingdomName()));
            return true;
        }
        SelectedId = -1L;
        DiplomacyWindow.Open(first, kingdom == first ? null : kingdom);
        return true;
    }

    private static void Tip(string text) => WorldTip.showNow(text, false, "top", 4f);
}

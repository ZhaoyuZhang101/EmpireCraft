using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Components;

// 汗国主页的"草原形势"卡(用户 2026-10-27："游牧刚分裂就这么大是因为现在是冰封纪元吗……放在游牧帝国主页显示当前的状态，
// 不然玩家不知道什么原因")：游牧汗国才画——卡头是汗庭的草原压力，下面写当前纪元苦不苦寒、压力四项构成、
// 自愿依附来的兀鲁思几部、眼下的后果(依附强部、结盟劫掠、渐次散去)、兀鲁思离合的规矩(见 NomadRaidSystem、SteppeOrderSystem)
public static class SteppePanel
{
    private const float TitleWidth = 80f;

    public static void Render(AutoVertLayoutGroup parent, Empire empire, float width)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt() || !NomadRaidSystem.IsNomad(core)) return;
        SteppeState state = NomadRaidSystem.State(core);
        bool assessed = state.pressure_at >= 0d;
        float pressure = assessed ? state.pressure : 0f;
        string hex = pressure >= SteppeRules.RaidThreshold ? "#E8584F" : pressure >= SteppeRules.AttachPressure ? "#E8C35A" : "#9BD27A";
        var card = parent.BeginCard(width, "steppe_card_title", hex,
            assessed ? string.Format(LM.Get("steppe_card_header"), pressure.ToString("0")) : null);
        int index = 0;
        WorldAgeAsset age = World.world_era;
        if (age != null)
        {
            string name = LocalizedTextManager.getText(age.getLocaleID());
            string key = age.flag_winter ? "steppe_era_winter" : !age.flag_crops_grow ? "steppe_era_dark" : "steppe_era_calm";
            bool hard = SteppeRules.HardEra(age.flag_winter, age.flag_crops_grow);
            card.AddListRow(width, index++, null, LM.Get("steppe_row_era"), string.Format(LM.Get(key), name),
                titleHex: hard ? "#7FB8E8" : hex, titleWidth: TitleWidth);
        }
        card.AddListRow(width, index++, null, LM.Get("steppe_row_parts"),
            assessed
                ? string.Format(LM.Get("steppe_parts_value"), state.era.ToString("0"), state.herd.ToString("0"),
                    state.treasury.ToString("0"), state.market.ToString("0"))
                : LM.Get("steppe_parts_pending"),
            titleHex: hex, titleWidth: TitleWidth);
        int attached = SteppeOrderSystem.AttachedCount(empire);
        card.AddListRow(width, index++, null, LM.Get("steppe_row_uluses"),
            attached > 0 ? string.Format(LM.Get("steppe_uluses_value"), attached) : LM.Get("label_none"),
            titleHex: hex, titleWidth: TitleWidth);
        if (assessed)
        {
            string effect = pressure >= SteppeRules.RaidThreshold ? "steppe_effect_raid"
                : pressure >= SteppeRules.AttachPressure ? "steppe_effect_attach" : "steppe_effect_calm";
            card.AddListRow(width, index++, null, LM.Get("steppe_row_effect"), LM.Get(effect), titleHex: hex, titleWidth: TitleWidth);
        }
        card.AddListRow(width, index, null, LM.Get("steppe_row_rule"), LM.Get("steppe_rule_value"), titleHex: "#9BD27A",
            titleWidth: TitleWidth);
    }
}

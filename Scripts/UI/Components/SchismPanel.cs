using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Components;

// 帝国主页的"分裂进程"卡(见 SchismSystem，用户 2026-10-21 选"预警前兆、先割据后独立、争位内战代替集体出走、连锁观望")：
// 没有进程不画；有就写类别与阶段(卡头)、时限、离心/割据/观望的成员(全列，帝国窗口没有单独的成员列表，这里就是名单)、
// 争位者与争位之战、首义声势、怎样化解
public static class SchismPanel
{
    public static void Render(AutoVertLayoutGroup parent, Empire empire, float width)
    {
        if (!SchismSystem.Active(empire)) return;
        int stage = SchismSystem.StageOf(empire);
        string hex = stage >= SchismSystem.Breakaway ? "#E8584F" : "#E8C35A";
        var card = parent.BeginCard(width, "schism_card_title", hex,
            $"{SchismSystem.KindLabel(empire)} · {SchismSystem.StageLabel(empire)}");
        int index = 0;
        card.AddListRow(width, index++, null, LM.Get("schism_row_time"), SchismSystem.TimeLabel(empire), titleHex: hex, titleWidth: 80f);
        List<Kingdom> members = SchismSystem.Members(empire);
        string membersKey = stage == SchismSystem.Warning ? "schism_row_unstable"
            : stage == SchismSystem.Rising ? "schism_row_watching"
            : "schism_row_breakaway";
        string names = members.Count == 0 ? LM.Get("label_none")
            : string.Format(LM.Get("schism_members_value"), members.Count,
                string.Join(LM.Get("schism_name_separator"), members.Select(kingdom => kingdom.GetKingdomName())));
        card.AddListRow(width, index++, null, LM.Get(membersKey), names, titleHex: hex, titleWidth: 80f);
        if (SchismSystem.ClaimantLabel(empire) is string claimant)
            card.AddListRow(width, index++, null, LM.Get("schism_row_claimant"), claimant, titleHex: hex, titleWidth: 80f);
        if (stage == SchismSystem.Rising)
        {
            float momentum = SchismSystem.Momentum(empire, update: false);
            card.AddListRow(width, index++, null, LM.Get("schism_row_momentum"),
                string.Format(LM.Get("schism_momentum_value"), momentum.ToString("0.0#")),
                titleHex: momentum > 1f ? "#E8584F" : momentum < 1f ? "#9BD27A" : hex, titleWidth: 80f);
        }
        if (SchismSystem.ResolveHint(empire) is string hint)
            card.AddListRow(width, index, null, LM.Get("schism_row_resolve"), hint, titleHex: "#9BD27A", titleWidth: 80f);
    }
}

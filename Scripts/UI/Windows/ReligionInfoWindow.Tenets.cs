using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;
using static EmpireCraft.Scripts.GameClassExtensions.ReligionExtension;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 宗教窗口概况页的教义卡、融合进度卡(用户 2026-11-06 选"模组自己的教义表、按类型分槽位"，见 ReligionTenets、SyncretismSystem)：
    //   · 教义：本有的(金色)、融来的(紫色、标"融"，写效果按六成与融自某教某年)；卡头"本有 n · 融合 m/槽位"；
    //     下面一行本土加成：本天下、本土惰性倍数、外天下传教折扣、升格与启示宗教的标记；
    //   · 融合进度：接触度最高的三个来源(条满 = 可融；写接触度/门槛、接触年数/门槛)，点开来源宗教；槽满、没有槽的写明。
    public partial class ReligionInfoWindow
    {
        private const int MaxSyncretismSources = 3;

        private void DrawTenets(AutoVertLayoutGroup page)
        {
            Religion religion = _religion;
            List<string> innate = ReligionTenets.Innate(religion);
            List<SyncreticTenetData> absorbed = ReligionTenets.Absorbed(religion);
            bool tolerant = ReligionTenets.Tolerant(religion);
            int slots = ReligionTenets.Slots(religion, tolerant);
            var card = page.BeginCard(PageWidth, LM.Get("religion_card_tenets"), WideKit.Gold,
                string.Format(LM.Get("religion_tenets_header"), innate.Count, absorbed.Count, slots));
            int index = 0;
            foreach (string id in innate)
                card.AddListRow(PageWidth, index++, null, ReligionDisplay.TenetName(id, null), ReligionDisplay.TenetDetail(id, null, 1f, true),
                    titleHex: WideKit.Gold, titleWidth: 96f);
            foreach (SyncreticTenetData entry in absorbed)
                card.AddListRow(PageWidth, index++, null,
                    $"{ReligionDisplay.TenetName(entry.tenet, entry.sphere)} {LM.Get("religion_tenet_absorbed_tag")}",
                    ReligionDisplay.AbsorbedDetail(entry), titleHex: ReligionDisplay.Purple, titleWidth: 96f);
            if (index == 0) card.AddParagraph(PageWidth, LM.Get("religion_tenets_none"), hex: WideKit.Muted);
            card.AddParagraph(PageWidth, ReligionDisplay.HomeSummary(religion), 6, WideKit.Muted);

            var config = ReligionTenets.Syncretism;
            var progress = SyncretismSystem.Progress(religion, MaxSyncretismSources);
            var sync = page.BeginCard(PageWidth, LM.Get("religion_card_syncretism"), WideKit.Gold,
                string.Format(LM.Get("religion_syncretism_header"), ReligionTenets.Openness(religion, tolerant)));
            if (slots <= 0) sync.AddParagraph(PageWidth, LM.Get("religion_syncretism_closed"), hex: WideKit.Muted);
            else if (absorbed.Count >= slots) sync.AddParagraph(PageWidth, LM.Get("religion_syncretism_full"), hex: WideKit.Muted);
            if (progress.Count == 0)
            {
                if (slots > 0 && absorbed.Count < slots)
                    sync.AddParagraph(PageWidth, LM.Get("religion_syncretism_none"), hex: WideKit.Muted);
                return;
            }
            int row = 0;
            foreach ((Religion source, float exposure, int years) in progress)
            {
                Religion target = source;
                float share = config.min_exposure > 0f ? Mathf.Clamp01(exposure / config.min_exposure) : 1f;
                bool ready = exposure >= config.min_exposure && years >= config.min_years;
                sync.AddBarRow(PageWidth, row++, source.name, share,
                    string.Format(LM.Get("religion_syncretism_value"), exposure, config.min_exposure, years, config.min_years),
                    ready ? WideKit.Green : WideKit.Value, ReligionDisplay.Purple, "#3A3F44", () => ReligionInfoWindow.Open(target), 96f, 120f);
            }
        }
    }
}

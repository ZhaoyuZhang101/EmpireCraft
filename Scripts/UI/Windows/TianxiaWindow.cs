using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.api;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 天下窗口(见 TianxiaSystem、Layer/TianxiaMap)：宽窗口「卷册」排版——
    // 左栏：天下名(可改)、标识、天命、规模；右侧：天下诸家(各家据有几成)、构成的帝国法理、历代共主、天下一览(点了换看)
    public class TianxiaWindow : AbstractWideWindow<TianxiaWindow>
    {
        private static EmpireCore _selected;
        private AutoVertLayoutGroup _root, _rail, _content;
        private const float ContentWidth = UIHelper.PageMainWidth;

        public static void Open(EmpireCore core)
        {
            _selected = TianxiaSystem.Lead(core);
            if (_selected == null) return;
            ScrollWindow.showWindow(nameof(TianxiaWindow));
        }

        protected override void Init()
        {
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
            _rail = UIHelper.CreateWideWindowRail(BackgroundTransform, UIHelper.RailWidth, UIHelper.RailInset);
        }

        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
            Refresh();
        }

        private void Clear()
        {
            UIHelper.ClearRail(_rail);
            if (_content == null) return;
            _content.gameObject.SetActive(false);
            Destroy(_content.gameObject);
            _content = null;
        }

        private void Refresh()
        {
            Clear();
            _selected = TianxiaSystem.Lead(_selected);
            if (_selected == null || !EmpireCoreManager.EmpireCores.ContainsKey(_selected.id))
                _selected = TianxiaSystem.Leads().OrderByDescending(lead => TianxiaSystem.Cities(lead).Count).FirstOrDefault();
            if (_selected == null) return;
            DrawRail();
            DrawContent();
        }

        private void DrawRail()
        {
            EmpireCore lead = _selected;
            var identity = _rail.BeginRailCard("FactionFrame_dominate");
            identity.AddRailText(TianxiaSystem.Name(lead).ColorString(TianxiaMap.HexOf(lead)), 10, 14f);
            string id = TianxiaSystem.TianxiaId(lead);
            if (id != "") identity.AddRailText(string.Format(LM.Get("tianxia_id_label"), id).ColorString("#8FA0A8"), 6, 9f);
            identity.AddRailText(TianxiaSystem.MandateText(lead).ColorString("#FF8A7A"), 6, 9f);
            identity.AddRailInput(LM.Get("tianxia_name_input"), TianxiaSystem.Name(lead), value =>
            {
                TianxiaSystem.Rename(lead, value);
                Refresh();
            });

            (Dictionary<Kingdom, int> held, int total) = TianxiaSystem.Holdings(lead);
            var stats = _rail.BeginRailCard();
            float rowWidth = UIHelper.RailWidth - 8f;
            stats.AddRailRow(rowWidth, LM.Get("tianxia_cores_count"), TianxiaSystem.Members(lead).Count.ToString(), "#73D1FF", 0);
            stats.AddRailRow(rowWidth, LM.Get("empire_core_cities_count"), total.ToString(), "#73F28C", 1);
            stats.AddRailRow(rowWidth, LM.Get("tianxia_houses_count"), held.Count.ToString(), "#FFB852", 2);
            stats.AddRailRow(rowWidth, LM.Get("tianxia_dynasties_count"), (lead.mandate_history?.Count ?? 0).ToString(), "#F2EEE2", 3);
        }

        private void DrawContent()
        {
            EmpireCore lead = _selected;
            _content = _root.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 6));
            UIHelper.FixPanelWidth(_content.transform, ContentWidth);
            _content.AddPageHeader(ContentWidth, TianxiaSystem.Name(lead), TianxiaSystem.MandateText(lead));

            // 天下诸家：各家据有几成(天命：据有七成五受命，跌破一半失命)
            Section(LM.Get("tianxia_section_houses"));
            (Dictionary<Kingdom, int> held, int total) = TianxiaSystem.Holdings(lead);
            Kingdom holder = TianxiaSystem.Holder(lead);
            foreach (KeyValuePair<Kingdom, int> pair in held.OrderByDescending(pair => pair.Value).Take(8))
            {
                float share = total > 0 ? pair.Value / (float)total : 0f;
                string mark = pair.Key == holder ? " " + LM.Get("tianxia_holder_mark").ColorString("#F3C34A") : "";
                Line($"{pair.Key.GetKingdomName().ColorString(pair.Key.getColor()?.color_text ?? "#FFFFFF")}{mark}  " +
                     $"{share:P0} ({pair.Value}/{total})");
            }
            if (held.Count == 0) Line(LM.Get("empire_core_none"));
            PartyIdeology? school = CliqueSystem.OfficialSchool(holder?.GetEmpire());
            if (school != null) Line(string.Format(LM.Get("tianxia_official_school"), PartySystem.GetIdeologyName(school.Value)));
            Line(string.Format(LM.Get("tianxia_rule_hint"), $"{TianxiaSystem.ClaimShare:P0}", $"{TianxiaSystem.KeepShare:P0}")
                .ColorString("#8FA0A8"));

            // 来朝诸国(天下朝贡)
            List<Kingdom> tributaries = TianxiaSystem.Tributaries(lead);
            if (tributaries.Count > 0)
            {
                Section(LM.Get("tianxia_section_tributaries"));
                Line(string.Join("、", tributaries.Select(kingdom => kingdom.GetKingdomName())));
            }

            // 构成：各帝国法理，点了打开帝国法理窗口
            Section(LM.Get("tianxia_section_cores"));
            foreach (EmpireCore core in TianxiaSystem.Members(lead))
            {
                EmpireCore target = core;
                string label = $"{EmpireCoreManager.GetStatusDisplayName(core)} · {EmpireCoreManager.GetCities(core).Count}" +
                               LM.Get("tianxia_cities_suffix");
                _content.AddButtonIntoVertLayout("tianxia_open_core", label, () =>
                {
                    EmpireCraftMetaTypeLibrary.selected_empireCore = target;
                    ScrollWindow.showWindow(nameof(EmpireCoreWindow));
                }, size: new Vector2(ContentWidth - 20f, 14f), showTip: false);
            }

            // 历代共主
            Section(LM.Get("tianxia_section_history"));
            List<MandateRecord> history = lead.mandate_history ?? new List<MandateRecord>();
            if (history.Count == 0) Line(LM.Get("tianxia_mandate_never"));
            foreach (MandateRecord record in Enumerable.Reverse(history).Take(20))
            {
                string to = record.lost_at < 0d ? LM.Get("tianxia_until_now") : Date.getDate(record.lost_at);
                string dynasty = string.IsNullOrWhiteSpace(record.dynasty) ? "" : $" ({record.dynasty})";
                Line($"{record.kingdom_name}{dynasty}  {Date.getDate(record.claimed_at)} — {to}");
            }

            // 天下一览：点了换看别的天下
            Section(LM.Get("tianxia_section_all"));
            foreach (EmpireCore other in TianxiaSystem.Leads().OrderByDescending(item => TianxiaSystem.Cities(item).Count).Take(16))
            {
                EmpireCore target = other;
                Kingdom otherHolder = TianxiaSystem.Holder(other);
                string label = $"{TianxiaSystem.Name(other)} · {TianxiaSystem.Cities(other).Count}{LM.Get("tianxia_cities_suffix")} · " +
                               (otherHolder != null ? otherHolder.GetKingdomName() : LM.Get("tianxia_vacant_short"));
                _content.AddButtonIntoVertLayout("tianxia_switch", label, () =>
                {
                    _selected = target;
                    Refresh();
                }, size: new Vector2(ContentWidth - 20f, 14f), showTip: false);
            }
        }

        private void Section(string title) => _content.AddSectionHeader(ContentWidth, title, "#B3E6FF", false);

        private void Line(string text) =>
            HoverMarqueeText.Attach(_content.AddTextIntoVertLayout(text, true, TextAnchor.MiddleLeft, new Vector2(ContentWidth - 12f, 11f)));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class KingdomTitleWindow : AutoLayoutWindow<KingdomTitleWindow>
    {
        private static readonly Color SectionColor = new Color(0.7f, 0.9f, 1f);
        private static readonly Color CyanColor = new Color(0.35f, 0.85f, 1f);
        private static readonly Color GreenColor = new Color(0.25f, 0.9f, 0.55f);
        private static readonly Color GoldColor = new Color(1f, 0.78f, 0.28f);

        public KingdomTitle title { get; set; }
        public TextInput titleNameInput;
        public TextInput provinceNameInput;
        private AutoVertLayoutGroup _topPart;
        private AutoVertLayoutGroup _content;

        protected override void Init()
        {
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 80, 3);
        }

        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 80, 3);
            title = EmpireCraftMetaTypeLibrary.selected_kingdomTitle;
            Clear();
            if (title == null || title.isRekt() || title.data == null) return;
            title.checkActive();
            title.isBeenControlled();
            BuildContent();
        }

        private void Clear()
        {
            if (_topPart != null)
            {
                Destroy(_topPart.gameObject);
                _topPart = null;
            }
            if (_content != null)
            {
                Destroy(_content.gameObject);
                _content = null;
            }
            titleNameInput = null;
            provinceNameInput = null;
        }

        private void BuildContent()
        {
            BuildIdentityCard();

            _content = this.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.UpperCenter);
            AddChild(_content.gameObject);

            BuildMetrics();
            List<Kingdom> administrations = FindCurrentAdministrations();
            BuildCurrentRelations(administrations);
            BuildJurisdictionHistory(administrations);
            BuildCityGrid();
        }

        private void BuildIdentityCard()
        {
            _topPart = this.BeginVertGroup(pSize: new Vector2(205, 76), pSpacing: 2,
                pAlignment: TextAnchor.MiddleCenter);
            HoverMarqueeText.Attach(_topPart.AddTextIntoVertLayout(
                (title.data.name ?? LM.Get("label_none")).ColorString(pColor: GoldColor), true,
                TextAnchor.MiddleCenter, new Vector2(190, 16)));
            AddInputRow(_topPart, LM.Get("title_name"), title.data.name, NameChange, out titleNameInput);
            AddInputRow(_topPart, LM.Get("province_name"), title.data.province_name, ProvinceNameChange,
                out provinceNameInput);
            string capitalName = title.title_capital?.GetCityName() ?? LM.Get("label_none");
            _topPart.AddTextIntoVertLayout($"{LM.Get("kingdom_title_capital")}: {capitalName}", true,
                TextAnchor.MiddleCenter, new Vector2(190, 10));
            _topPart.transform.AddStretchBackground("clanFrame", new Vector2(205, 76));
            _topPart.gameObject.AdjustTopPart(transform.parent.transform, new Vector2(0, 1));
            RectTransform topRect = _topPart.GetComponent<RectTransform>();
            topRect.sizeDelta = new Vector2(205, 76);
            LayoutRebuilder.ForceRebuildLayoutImmediate(topRect);
        }

        private static void AddInputRow(AutoVertLayoutGroup parent, string label, string value,
            UnityEngine.Events.UnityAction<string> action, out TextInput input)
        {
            var row = parent.BeginHoriGroup(pSize: new Vector2(196, 19), pSpacing: 2,
                pAlignment: TextAnchor.MiddleCenter);
            var labelText = row.AddTextIntoHoriLayout(label, true, TextAnchor.MiddleRight, new Vector2(52, 15));
            labelText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            input = UnityEngine.Object.Instantiate(TextInput.Prefab, null);
            input.Setup(value ?? "", action);
            input.SetSize(new Vector2(138, 18));
            row.AddChild(input.gameObject);
        }

        private void BuildMetrics()
        {
            var metrics = _content.BeginHoriGroup(pSpacing: 3, pAlignment: TextAnchor.MiddleCenter);
            AddMetricCard(metrics, LM.Get("kingdom_title_cities"), title.city_list.Count.ToString(), CyanColor);
            AddMetricCard(metrics, LM.Get("i_population"), title.countPopulation().ToString(), GreenColor);
            AddMetricCard(metrics, LM.Get("kingdom_title_zones"), title.countZones().ToString(), GoldColor);
        }

        private static void AddMetricCard(AutoHoriLayoutGroup parent, string label, string value, Color color)
        {
            var card = parent.BeginVertGroup(pSize: new Vector2(62, 28), pSpacing: -2,
                pAlignment: TextAnchor.MiddleCenter);
            card.AddTextIntoVertLayout(value.ColorString(pColor: color), true, TextAnchor.MiddleCenter,
                new Vector2(30, 12));
            var labelText = card.AddTextIntoVertLayout(label, true, TextAnchor.MiddleCenter,
                new Vector2(58, 10));
            labelText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            card.transform.AddStretchBackground("FactionFrame", new Vector2(62, 28));
        }

        private void BuildCurrentRelations(List<Kingdom> administrations)
        {
            AddSectionTitle(LM.Get("kingdom_title_current_relations"));
            var grid = _content.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount,
                pCellSize: new Vector2(100, 62), pSpacing: new Vector2(2, 2));

            List<KingdomTitleHolderRelation> holders = ResolveCurrentHolders();
            if (holders.Count == 0)
            {
                AddHolderCard(grid, null, null, true);
            }
            else
            {
                foreach (KingdomTitleHolderRelation holder in holders)
                {
                    AddHolderCard(grid, holder.Holder, holder.Empire, holder.Unlanded);
                }
            }
            if (administrations.Count == 0)
            {
                AddKingdomCard(grid, LM.Get("kingdom_title_administration"), null,
                    LM.Get("kingdom_title_no_administration"));
            }
            else
            {
                foreach (Kingdom administration in administrations)
                {
                    AddKingdomCard(grid, LM.Get("kingdom_title_administration"), administration,
                        administration.GetKingdomType() == KingdomType.LvLing_jiedushi
                            ? LM.Get("LvLing_jiedushi") : LM.Get("LvLing_province"));
                }
            }

            Kingdom controller = title.control_kingdom;
            if (controller != null && !controller.isRekt() && administrations.All(k => k != controller) &&
                controller != title.main_kingdom)
            {
                AddKingdomCard(grid, LM.Get("kingdom_title_controller"), controller,
                    LM.Get("title_been_controlled"));
            }
        }

        private List<KingdomTitleHolderRelation> ResolveCurrentHolders()
        {
            return KingdomTitleRelationResolver.ResolveCurrentHolders(title);
        }

        private List<Kingdom> FindCurrentAdministrations()
        {
            return KingdomTitleRelationResolver.FindCurrentAdministrations(title);
        }

        private void AddHolderCard(AutoGridLayoutGroup parent, Actor holder, Empire empire, bool unlanded)
        {
            var card = this.BeginVertGroup(pSize: new Vector2(100, 62), pSpacing: 1,
                pAlignment: TextAnchor.MiddleCenter);
            string heading = unlanded
                ? LM.Get("kingdom_title_unlanded_holder")
                : LM.Get("kingdom_title_holder");
            card.AddTextIntoVertLayout(heading.ColorString(pColor: CyanColor), true,
                TextAnchor.MiddleCenter, new Vector2(94, 11));
            var body = card.BeginHoriGroup(pSize: new Vector2(94, 43), pSpacing: 3,
                pAlignment: TextAnchor.MiddleCenter);
            AddAvatarSlot(body, holder?.id ?? -1L);
            var details = body.BeginVertGroup(pSize: new Vector2(58, 40), pSpacing: 0,
                pAlignment: TextAnchor.MiddleLeft);
            string holderName = holder?.getName() ?? LM.Get("label_vacant");
            HoverMarqueeText.Attach(details.AddTextIntoVertLayout(holderName.ColorString(
                    pColor: holder == null ? Color.gray : GreenColor), true, TextAnchor.MiddleLeft,
                new Vector2(56, 12)));
            string status = holder == null ? LM.Get("label_vacant") :
                unlanded ? LM.Get("label_unlanded_fief") : LM.Get("label_landed_fief");
            details.AddTextIntoVertLayout(status, true, TextAnchor.MiddleLeft, new Vector2(56, 10));
            if (holder != null)
            {
                HoverMarqueeText.Attach(details.AddTextIntoVertLayout(FormatPeerageName(holder, empire), true,
                    TextAnchor.MiddleLeft, new Vector2(56, 10)));
            }
            parent.AddChild(card.gameObject);
            card.transform.AddStretchBackground(holder == null ? "FactionFrame" : "FactionFrame_dominate",
                new Vector2(100, 62));
        }

        private static string FormatPeerageName(Actor holder, Empire empire)
        {
            string peerageName = holder?.GetPeerageDisplayName() ?? "";
            string empireName = empire?.GetEmpireName() ?? "";
            if (string.IsNullOrWhiteSpace(empireName)) return peerageName;
            if (string.IsNullOrWhiteSpace(peerageName)) return empireName;
            return $"{empireName} · {peerageName}";
        }

        private void AddKingdomCard(AutoGridLayoutGroup parent, string heading, Kingdom kingdom, string status)
        {
            var card = this.BeginVertGroup(pSize: new Vector2(100, 62), pSpacing: 1,
                pAlignment: TextAnchor.MiddleCenter);
            card.AddTextIntoVertLayout(heading.ColorString(pColor: CyanColor), true,
                TextAnchor.MiddleCenter, new Vector2(94, 11));
            var body = card.BeginHoriGroup(pSize: new Vector2(94, 43), pSpacing: 3,
                pAlignment: TextAnchor.MiddleCenter);
            AddAvatarSlot(body, kingdom?.king?.id ?? -1L);
            var details = body.BeginVertGroup(pSize: new Vector2(58, 40), pSpacing: 0,
                pAlignment: TextAnchor.MiddleLeft);
            string kingdomName = kingdom?.GetKingdomFullName() ?? LM.Get("label_none");
            HoverMarqueeText.Attach(details.AddTextIntoVertLayout(kingdomName.ColorString(
                    pColor: kingdom == null ? Color.gray : GoldColor), true, TextAnchor.MiddleLeft,
                new Vector2(56, 12)));
            details.AddTextIntoVertLayout(status, true, TextAnchor.MiddleLeft, new Vector2(56, 10));
            if (kingdom != null)
            {
                details.AddButtonIntoVertLayout("open_kingdom", LM.Get("kingdom_title_details"), () =>
                    MetaType.Kingdom.getAsset().selectAndInspect(kingdom),
                    SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(48, 13), showTip: true);
            }
            parent.AddChild(card.gameObject);
            card.transform.AddStretchBackground(kingdom == null ? "FactionFrame" : "FactionFrame_dominate",
                new Vector2(100, 62));
        }

        private static void AddAvatarSlot(AutoHoriLayoutGroup parent, long actorId)
        {
            var avatarSlot = parent.BeginVertGroup(new Vector2(30, 40), pSpacing: 0,
                pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
            SimpleButton avatar = UIHelper.CreateAvatarView(actorId);
            avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(30, 30);
            avatarSlot.AddChild(avatar.gameObject);
            avatarSlot.transform.localPosition = Vector3.zero;
        }

        private void BuildJurisdictionHistory(List<Kingdom> administrations)
        {
            if (title.main_kingdom != null && !title.main_kingdom.isRekt())
                title.RecordJurisdiction(title.main_kingdom, KingdomTitle.JurisdictionHolder);
            foreach (Kingdom administration in administrations)
                title.RecordJurisdiction(administration, KingdomTitle.JurisdictionAdministration);

            AddSectionTitle(LM.Get("kingdom_title_history"));
            List<KingdomTitleJurisdictionRecord> records = title.data.jurisdiction_history?.Where(record =>
                    record != null).OrderByDescending(record => record.start_time).ToList()
                ?? new List<KingdomTitleJurisdictionRecord>();
            foreach (KingdomTitleJurisdictionRecord record in records)
            {
                AddHistoryCard(record);
            }

            bool founderAlreadyShown = records.Any(record => record.kingdom_id == title.data.founder_kingdom_id);
            if (!founderAlreadyShown && !string.IsNullOrWhiteSpace(title.data.founder_kingdom_name))
            {
                AddFounderCard();
            }
            if (records.Count == 0 && string.IsNullOrWhiteSpace(title.data.founder_kingdom_name))
            {
                _content.AddTextIntoVertLayout(LM.Get("label_none"), true, TextAnchor.MiddleCenter,
                    new Vector2(100, 12));
            }
        }

        private void AddHistoryCard(KingdomTitleJurisdictionRecord record)
        {
            Kingdom liveKingdom = World.world.kingdoms.get(record.kingdom_id);
            if (liveKingdom?.isRekt() == true) liveKingdom = null;
            bool current = record.end_time < 0;
            string relation = record.relation == KingdomTitle.JurisdictionAdministration
                ? LM.Get("kingdom_title_history_administration")
                : LM.Get("kingdom_title_history_holder");
            string kingdomName = liveKingdom?.GetKingdomFullName();
            if (string.IsNullOrWhiteSpace(kingdomName)) kingdomName = record.kingdom_name;
            if (string.IsNullOrWhiteSpace(kingdomName)) kingdomName = LM.Get("label_none");

            var card = _content.BeginHoriGroup(pSize: new Vector2(200, 36), pSpacing: 2,
                pAlignment: TextAnchor.MiddleCenter);
            var details = card.BeginVertGroup(pSize: new Vector2(176, 32), pSpacing: -1,
                pAlignment: TextAnchor.MiddleLeft);
            HoverMarqueeText.Attach(details.AddTextIntoVertLayout(
                $"{relation} · {kingdomName}".ColorString(pColor: current ? GoldColor : SectionColor), true,
                TextAnchor.MiddleLeft, new Vector2(172, 12)));
            details.AddTextIntoVertLayout(FormatPeriod(record.start_time, record.end_time), true,
                TextAnchor.MiddleLeft, new Vector2(172, 10));
            if (liveKingdom != null)
            {
                Kingdom target = liveKingdom;
                card.AddButtonIntoHoriLayout("open_history_kingdom", "", () =>
                    MetaType.Kingdom.getAsset().selectAndInspect(target),
                    SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(16, 16), showTip: true);
            }
            card.transform.AddStretchBackground(current ? "FactionFrame_dominate" : "FactionFrame",
                new Vector2(200, 36));
        }

        private void AddFounderCard()
        {
            var card = _content.BeginVertGroup(pSize: new Vector2(200, 30), pSpacing: -1,
                pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(5, 5, 1, 1));
            HoverMarqueeText.Attach(card.AddTextIntoVertLayout(
                $"{LM.Get("kingdom_title_history_founder")} · {title.data.founder_kingdom_name}"
                    .ColorString(pColor: SectionColor), true, TextAnchor.MiddleLeft, new Vector2(188, 12)));
            string date = title.data.timestamp_established_time > 0
                ? Date.getDate(title.data.timestamp_established_time)
                : LM.Get("kingdom_title_unknown_date");
            card.AddTextIntoVertLayout(date, true, TextAnchor.MiddleLeft, new Vector2(188, 10));
            card.transform.AddStretchBackground("FactionFrame", new Vector2(200, 30));
        }

        private void BuildCityGrid()
        {
            AddSectionTitle(LM.Get("kingdom_title_cities"));
            var grid = _content.BeginGridGroup(4, GridLayoutGroup.Constraint.FixedColumnCount,
                pCellSize: new Vector2(48, 40), pSpacing: new Vector2(2, 2));
            foreach (City city in title.city_list.Where(city => city != null && !city.isRekt())
                         .OrderByDescending(city => city == title.title_capital).ThenBy(city => city.GetCityName()))
            {
                var card = this.BeginVertGroup(pSize: new Vector2(48, 40), pSpacing: 0,
                    pAlignment: TextAnchor.MiddleCenter);
                HoverMarqueeText.Attach(card.AddTextIntoVertLayout(city.GetCityName().ColorString(
                        pColor: city == title.title_capital ? GoldColor : SectionColor), true,
                    TextAnchor.MiddleCenter, new Vector2(44, 11)));
                string status = city == title.title_capital
                    ? LM.Get("kingdom_title_capital")
                    : city.kingdom?.GetKingdomName() ?? LM.Get("label_none");
                HoverMarqueeText.Attach(card.AddTextIntoVertLayout(status, true, TextAnchor.MiddleCenter,
                    new Vector2(44, 9)));
                City target = city;
                card.AddButtonIntoVertLayout("open_title_city", "", () =>
                    MetaType.City.getAsset().selectAndInspect(target),
                    SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(14, 14), showTip: true);
                grid.AddChild(card.gameObject);
                card.transform.AddStretchBackground(city == title.title_capital ? "FactionFrame_dominate" : "FactionFrame",
                    new Vector2(48, 40));
            }
        }

        private void AddSectionTitle(string text)
        {
            _content.AddTextIntoVertLayout(text.ColorString(pColor: SectionColor), true,
                TextAnchor.MiddleCenter, new Vector2(150, 13));
        }

        private static string FormatPeriod(double start, double end)
        {
            string startText = start > 0 ? Date.getDate(start) : LM.Get("kingdom_title_unknown_date");
            string endText = end < 0 ? LM.Get("kingdom_title_current") : Date.getDate(end);
            return $"{startText} - {endText}";
        }

        private void NameChange(string value)
        {
            if (title?.data == null) return;
            title.data.name = value;
        }

        private void ProvinceNameChange(string value)
        {
            if (title?.data == null) return;
            title.data.province_name = value;
            title.RefreshAdministrativeDivisionNames();
        }
    }
}

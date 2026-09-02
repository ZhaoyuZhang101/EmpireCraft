using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.General.UI.Window;
using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using NeoModLoader.General.UI.Prefabs;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Data;

using UnityEngine.UI;
using EmpireCraft.Scripts.HelperFunc;

using NeoModLoader.General;
using System.Collections;
using DG.Tweening;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireWindow : AutoLayoutWindow<EmpireWindow>
    {
        private TextInput _empireNameInput;
        private Empire _empire;
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();

        private Dictionary<string, Text> _infosTrans = new Dictionary<string, Text>();

        protected override void Init()
        {
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 60, 3);
            _empireNameInput = Instantiate(TextInput.Prefab, this.transform.parent.transform.parent);
            _empireNameInput.Setup("", name_change);
        }
        public void Clear()
        {
            foreach (var container in _groups)
            {
                Destroy(container.Value);
            }
            _groups.Clear();
        }
        private void InitialTabButtons()
        {
            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_controlled_kingdoms"))
            {
                var kingdomsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                kingdomsWindowTab.Setup("empire_controlled_kingdoms", this.ScrollWindowComponent, action:ShowKingdomListHelp,
                    sprite: SpriteTextureLoader.getSprite("SplitAllUnderHeaven"));
            }

            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "past_emperors"))
            {
                var pastEmperorsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                pastEmperorsWindowTab.Setup("past_emperors", this.ScrollWindowComponent, action:ShowEmperors, sprite:SpriteTextureLoader.getSprite("ui/iconHistory"));
            }
            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_bureau"))
            {
                var bureauWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                bureauWindowTab.Setup("empire_bureau", this.ScrollWindowComponent, action:ShowBureau, sprite:SpriteTextureLoader.getSprite("ChineseCrown"));
            }
            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_setting"))
            {
                var settingWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                settingWindowTab.Setup("empire_setting", this.ScrollWindowComponent, action:OpenEmpireSettingWindow);
            }
        }

        private void ShowKingdomListHelp(WindowMetaTab pArg0)
        {
            StartCoroutine(ShowKingdomList());
        }
        private void OpenEmpireSettingWindow(WindowMetaTab pArg0)
        {
            ScrollWindow.showWindow(nameof(EmpireSettingWindow));
        }

        public void ShowTopPart()
        {
            InitialTextInput();
            InitialTopPartInfo();
        }
        //顶部信息栏
        private void InitialTopPartInfo()
        {
            //总容器
            var topSpace = this.BeginHoriGroup();
            topSpace.transform.AddStretchBackground("clanFrame", new Vector2(220, 55));
            
            //左侧信息栏
            var leftPart = topSpace.BeginVertGroup(pAlignment:TextAnchor.MiddleCenter);
            leftPart.AddTextIntoVertLayout($"{LM.Get("empire_clan")}: {(_empire.EmpireSpecificClan?.name??""+ " " + LM.Get("Clan")).ColorString(_empire.EmpireSpecificClan?.color??"#FFFFFF")}");
            leftPart.AddTextIntoVertLayout($"{"format_past_emperor".LocalFormat(_empire?.data?.history_emperrors?.Count??0)}");
            leftPart.AddTextIntoVertLayout($"{LM.Get("i_population")}: {_empire.CountPopulation()}/{_empire.countMaxPopulation()}");
            
            var leftPart2 = topSpace.BeginVertGroup(pAlignment:TextAnchor.LowerCenter, pPadding: new RectOffset(0,0,6,0));
            leftPart2.AddTextIntoVertLayout(LM.Get("empire_heir").ColorString(pColor:new Color(0.8f,0.0f,1f)),true, TextAnchor.MiddleCenter, size:new Vector2(15, 10));
            leftPart2.AddActorViewIntoVertLayout(_empire.CoreKingdom.GetHeir());
            
            //中央信息栏
            var centerPart = topSpace.BeginVertGroup(pAlignment:TextAnchor.UpperCenter, pPadding: new RectOffset(0,0,0,4));
            centerPart.AddTextIntoVertLayout(LM.Get(_empire.HasEmperor()?_empire.Emperor.isSexFemale()?"actor_emperor_G":"actor_emperor_B":"actor_emperor_B").ColorString(pColor:new Color(1,0.8f,0)),true, TextAnchor.MiddleCenter, size:new Vector2(15, 10));
            centerPart.AddActorViewIntoVertLayout(_empire.Emperor);
            
            var rightPart2 = topSpace.BeginVertGroup(pAlignment:TextAnchor.LowerCenter, pPadding: new RectOffset(0,0,6,0));
            rightPart2.AddTextIntoVertLayout(LM.Get(_empire.HasEmperor()?_empire.Emperor.hasLover()?_empire.Emperor.lover.isSexFemale()?"empire_lover_G":"empire_lover_B":"empire_lover_B":"empire_lover_B").ColorString(pColor:new Color(1f,0.1f,0.5f)),true, TextAnchor.MiddleCenter, size:new Vector2(15, 10));
            rightPart2.AddActorViewIntoVertLayout(_empire.Emperor?.lover);
            
            //右侧信息栏
            var rightPart = topSpace.BeginVertGroup(pAlignment:TextAnchor.MiddleCenter);
            rightPart.AddTextIntoVertLayout($"{LM.Get("official_students_num")}: " +
                                            $"{_empire.GetMembersWithTrait("jingshi").Count.ToString().ColorString("#E16A54")}/" +
                                            $"{_empire.GetMembersWithTrait("gongshi").Count.ToString().ColorString("#CB9DF0")}/" +
                                            $"{_empire.GetMembersWithTrait("juren").Count.ToString()}".ColorString("#A2D2DF"));
            rightPart.AddTextIntoVertLayout($"{_empire.GetYearNameWithTime().ColorString(pColor:_empire.getColor()._color_text)}");
            rightPart.AddTextIntoVertLayout($"{LM.Get("i_age")}: {_empire.CoreKingdom.getAge()}");
            
            topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 1));
            
            AddIntoGroup("top_space", topSpace.gameObject);
        }


        private void InitialTextInput()
        {
            string text = _empire.GetEmpireName();
            UIHelper.GenerateTextInput(this.transform.parent.transform.parent, offset:new Vector2(0, 152), default_text:text, input:_empireNameInput);
        }
        
        //显示势力范围
        public IEnumerator ShowKingdomList()
        {
            if (_empire?.CoreKingdom == null || _empire.CoreKingdom.isRekt()) yield break;
            Clear();
            InitialTopPartInfo();
            var parent = CommonInitial("empire_controlled_kingdoms");
            if (parent == null) yield break;
            yield return CoroutineHelper.wait_for_next_frame;
            if (parent == null || parent.gameObject == null) yield break;
            UIHelper.InitialFactionSpace(parent.BeginHoriGroup(), _empire.CoreKingdom);
            yield return CoroutineHelper.wait_for_next_frame;
            if (parent == null || parent.gameObject == null) yield break;
            parent.AddTextIntoVertLayout("", true, TextAnchor.MiddleCenter);
            parent.AddTextIntoVertLayout(LM.Get("kingdom_list"), true, TextAnchor.MiddleCenter);
            yield return CoroutineHelper.wait_for_next_frame;
            if (parent == null || parent.gameObject == null) yield break;
            StartCoroutine(ShowKingdoms(parent));
        }
        //显示君主世系
        public void ShowEmperors(WindowMetaTab pArg0)
        {
            Clear();
            InitialTopPartInfo();
            var parent = CommonInitial("past_emperors");
            parent.AddComponent<EmpireCraftStatsRow>();
            EmpireCraftStatsRow statsRow = parent.GetComponent<EmpireCraftStatsRow>();
            
            string text = "";
            var groups = new List<string>();
            foreach (var h in _empire.data.history)
            {
                var s = string.IsNullOrEmpty(h.royal_surname) ? "" : h.royal_surname;
                var g = string.IsNullOrEmpty(s) ? h.empire_name : string.Join("·", s, h.empire_name);
                if (!groups.Contains(g)) groups.Add(g);
            }
            foreach (var g in groups)
            {
                parent.AddTextIntoVertLayout(g, true, TextAnchor.MiddleCenter);
                foreach (var h in _empire.data.history)
                {
                    var s = string.IsNullOrEmpty(h.royal_surname) ? "" : h.royal_surname;
                    var k = string.IsNullOrEmpty(s) ? h.empire_name : string.Join("·", s, h.empire_name);
                    if (k == g)
                    {
                        ListPastEmperor(statsRow, h);
                    }
                }
            }
            text = _empire.GetEmpireName() + (_empire.data.has_year_name?_empire.data.year_name:"") + LM.Get("emperor");
            statsRow.IShowStatsRow("current_emperor", _empire.Emperor?.name??"无" , _empire.getColor().color_text, pIconPath: "iconKings", action: () => OpenHistoryWindow(_empire.data.currentHistory));
            if (_empire.Emperor != null)
            {
                statsRow.IShowStatsRow("title_name", text, _empire.getColor().color_text);
            }
            StartCoroutine(statsRow.showRows());
        }
        //显示个人历史
        public void ShowPersonalHistory()
        {
            Clear();
            InitialTopPartInfo();
            var currentHistory = ConfigData.CURRENT_SELECTED_HISTORY;
            if (currentHistory == null) return;

            var parent = this.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            AddIntoGroup("empire_reign_history", parent.gameObject);
            string eraName = string.IsNullOrWhiteSpace(currentHistory.year_name)
                ? LM.Get("waiting_for_naming")
                : currentHistory.year_name;
            string emperorName = string.IsNullOrWhiteSpace(currentHistory.emperor)
                ? _empire.Emperor?.getName() ?? ""
                : currentHistory.emperor;
            int reignYears = currentHistory == _empire.data.currentHistory
                ? _empire.GetEmperorYear()
                : Mathf.Max(1, currentHistory.total_time);

            var reignCard = parent.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleCenter,
                pSize: new Vector2(196, 34));
            AddReignInfoColumn(reignCard, LM.Get("year_name"), eraName, new Color(1f, 0.78f, 0.2f), new Vector2(52, 30), 9);
            AddReignInfoColumn(reignCard, LM.Get("emperor"), emperorName, _empire.getColor()._color_text, new Vector2(82, 30), 11);
            AddReignInfoColumn(reignCard, LM.Get("empire_reign_duration"), $"{reignYears}{LM.Get("Year")}",
                new Color(0.25f, 0.9f, 0.8f), new Vector2(52, 30), 9);
            reignCard.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(196, 34));

            if (currentHistory.descriptions != null)
            {
                HistoryDescription lasDes = new HistoryDescription()
                {
                    cities = currentHistory.initial_cities != null ? new List<string>(currentHistory.initial_cities) : new List<string>(),
                    description = "",
                    time = ""
                };
                foreach (var d in currentHistory.descriptions)
                {
                    EmpireHistoryWindow.ListHistoryDescriptions(lasDes, d, parent);
                    lasDes = d;
                }
            }
        }

        // Keep the ruler prominent while the era and reign length remain readable context.
        private static void AddReignInfoColumn(AutoHoriLayoutGroup parent, string label, string value, Color valueColor,
            Vector2 size, int valueFontSize)
        {
            var column = parent.BeginVertGroup(size, pSpacing: -2, pAlignment: TextAnchor.MiddleCenter,
                pPadding: new RectOffset(0, 0, 1, 1));
            var labelText = column.AddTextIntoVertLayout(label.ColorString(pColor: new Color(0.65f, 0.72f, 0.72f)), true,
                TextAnchor.MiddleCenter, new Vector2(size.x - 2, 8));
            labelText.UseFixedFontSize(5, HorizontalWrapMode.Overflow);
            var valueText = column.AddTextIntoVertLayout(value.ColorString(pColor: valueColor), true,
                TextAnchor.MiddleCenter, new Vector2(size.x - 2, 18));
            valueText.UseFixedFontSize(valueFontSize, HorizontalWrapMode.Overflow);
        }
        //显示行政窗口
        public void ShowBureau(WindowMetaTab pArg0)
        {
            
            ScrollWindow.showWindow(nameof(EmpireBeaurauWindow));
        }

        public IEnumerator ShowKingdoms(AutoVertLayoutGroup parent)
        {
            
            foreach (var e in _empire.kingdoms_list)
            {
                PrepareKingdom(e, parent);
                yield return CoroutineHelper.wait_for_next_frame;
            }
        }

        public void PrepareKingdom(Kingdom e, AutoVertLayoutGroup parent)
        {
            GameObject kingdomListElement = PrefabHelper.FindPrefabByName("list_element_kingdom");
            GameObject inst = GameObject.Instantiate(kingdomListElement);
            KingdomListElement kl = inst.GetComponent<KingdomListElement>();
            kl.kingdomName.text = e.name;
            kl.textAge._text.text = e.getAge().ToString();
            kl.textPopulation._text.text = e.countUnits().ToString();
            kl.textArmy._text.text = e.countTotalWarriors().ToString();
            kl.textCities._text.text = e.countCities().ToString();
            kl.textZones._text.text = e.countZones().ToString();
            kl.avatarLoader.load(e.king);
            kl.meta_object = e;
            kl.loadBanner();
            inst.name = "list_element_kingdom";
            inst.SetActive(true);
            parent.AddChild(inst);
        }

        public override void OnFirstEnable()
        {
            base.OnFirstEnable();
            this.DORestart();
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 60, 3);
            _empire = EmpireCraftMetaTypeLibrary.selected_empire;
            _empireNameInput.input.text = _empire.GetEmpireName();
            Clear();
            InitialTabButtons();
            ShowTopPart();
            StartCoroutine(ShowKingdomList());
        }
        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 60, 3);
            _empire = EmpireCraftMetaTypeLibrary.selected_empire;
            _empireNameInput.input.text = _empire.GetEmpireName();
            Clear();
            InitialTabButtons();
            ShowTopPart();
            StartCoroutine(ShowKingdomList());
        }

        public void ListPastEmperor(EmpireCraftStatsRow statsRow, EmpireCraftHistory history)
        {
            if (string.IsNullOrEmpty(history.emperor))
            {
                return;
            }
            var text1 = history.empire_name + (_empire.data.has_year_name?history.year_name:"") + LM.Get("emperor");
            var text2 = "";
            if (_empire.data.has_year_name)
            {
                if (!string.IsNullOrEmpty(history.miaohao_name))
                {
                    text2 =
                        history.empire_name + LM.Get(history.miaohao_name) + LM.Get(history.miaohao_suffix) + "-" +
                        history.empire_name + LM.Get(history.shihao_name) + LM.Get("emperor_suffix");
                }
                else
                {
                    text2 = LM.Get("waiting_for_naming");
                } 
            }
            statsRow.IShowStatsRow("past_emperor", history.emperor + $"(在位 {history.total_time}{LM.Get("Year")})", _empire.getColor().color_text, pIconPath: "iconKings", action: () => OpenHistoryWindow(history));
            statsRow.IShowStatsRow("title_name", text1, _empire.getColor().color_text);
            if (_empire.data.has_year_name)
            {
                statsRow.IShowStatsRow("post_humous_name", text2, _empire.getColor().color_text);
            }
            statsRow.IShowStatsRow("empty", "=======================================================================================", "#ffffff");
        }

        public void OpenHistoryWindow(EmpireCraftHistory history)
        {
            ConfigData.CURRENT_SELECTED_HISTORY = history;
            ShowPersonalHistory();
        }

        public void name_change(string name)
        {
            if (_empire != null)
            {
                _empire.SetEmpireName(name);
            }
        }

        public void AddIntoGroup(string title, GameObject obj)
        {
            if (!_groups.ContainsKey(title))
            {
                _groups.Add(title, obj);
            }
        }

        private AutoVertLayoutGroup CommonInitial(string titleName)
        {
            
            //通用显示区域
            var container = this.BeginVertGroup(pSpacing: 3, pAlignment:TextAnchor.UpperCenter);
            //标题区
            SimpleText title = Instantiate(SimpleText.Prefab, null);
            string empirePersonalHistory = LM.Get(titleName);
            title.Setup($"{empirePersonalHistory}", TextAnchor.MiddleCenter, new Vector2(40, 15));
            title.background.enabled = false;
            container.AddChild(title.gameObject);
            //内容区
            var content = this.BeginVertGroup(pSpacing: 3);
            container.AddChild(content.gameObject);
            
            AddIntoGroup(titleName, container.gameObject);
            return content;
        }
    }
}

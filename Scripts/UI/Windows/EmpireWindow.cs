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
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireWindow : AutoLayoutWindow<EmpireWindow>
    {
        private TextInput _empireNameInput;
        private Empire _empire;
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();
        public SimpleWindowTab kingdomsWindowTab;
        public SimpleWindowTab pastEmperorsWindowTab;
        public SimpleWindowTab bureauWindowTab;

        private List<string> _infos = new List<string>()
        {
            "i_cities", "i_kingdoms", "i_age", "i_renown", "i_deaths", "i_members"
        };

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
                kingdomsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                kingdomsWindowTab.Setup("empire_controlled_kingdoms", this.ScrollWindowComponent, action:ShowKingdomList, sprite:SpriteTextureLoader.getSprite("ui/specificClanIcon"));
            }
            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "past_emperors"))
            {
                pastEmperorsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                pastEmperorsWindowTab.Setup("past_emperors", this.ScrollWindowComponent, action:ShowEmperors, sprite:SpriteTextureLoader.getSprite("ui/specificClanIcon"));
            }
            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_bureau"))
            {
                bureauWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                bureauWindowTab.Setup("empire_bureau", this.ScrollWindowComponent, action:ShowBureau, sprite:SpriteTextureLoader.getSprite("ui/specificClanIcon"));
            }
            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_setting"))
            {
                bureauWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                bureauWindowTab.Setup("empire_setting", this.ScrollWindowComponent, action:OpenEmpireSettingWindow);
            }
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
        [Hotfixable]
        private void InitialTopPartInfo()
        {
            //总容器
            var topSpace = this.BeginHoriGroup();
            topSpace.transform.AddStretchBackground(SpriteTextureLoader.getSprite("ui/clanFrame"), new Vector2(220, 55));
            
            //左侧信息栏
            var leftPart = topSpace.BeginVertGroup(pAlignment:TextAnchor.MiddleCenter);
            leftPart.AddTextIntoVertLayout($"{LM.Get("empire_clan")}: {(_empire.EmpireSpecificClan.name+ " " + LM.Get("Clan")).ColorString(_empire.EmpireSpecificClan.color)}");
            leftPart.AddTextIntoVertLayout($"{"format_past_emperor".LocalFormat(_empire.data.history_emperrors.Count)}");
            leftPart.AddTextIntoVertLayout($"{LM.Get("i_population")}: {_empire.countPopulation()}/{_empire.countMaxPopulation()}");
            
            var leftPart2 = topSpace.BeginVertGroup(pAlignment:TextAnchor.LowerCenter, pPadding: new RectOffset(0,0,6,0));
            leftPart2.AddTextIntoVertLayout(LM.Get("empire_heir").ColorString(pColor:new Color(0.8f,0.0f,1f)),true, TextAnchor.MiddleCenter, size:new Vector2(15, 10));
            leftPart2.AddActorViewIntoVertLayout(_empire.Heir);
            
            //中央信息栏
            var centerPart = topSpace.BeginVertGroup(pAlignment:TextAnchor.UpperCenter, pPadding: new RectOffset(0,0,0,4));
            centerPart.AddTextIntoVertLayout(LM.Get("actor_emperor").ColorString(pColor:new Color(1,0.8f,0)),true, TextAnchor.MiddleCenter, size:new Vector2(15, 10));
            centerPart.AddActorViewIntoVertLayout(_empire.Emperor);
            
            var rightPart2 = topSpace.BeginVertGroup(pAlignment:TextAnchor.LowerCenter, pPadding: new RectOffset(0,0,6,0));
            rightPart2.AddTextIntoVertLayout(LM.Get("empire_lover").ColorString(pColor:new Color(1f,0.1f,0.5f)),true, TextAnchor.MiddleCenter, size:new Vector2(15, 10));
            rightPart2.AddActorViewIntoVertLayout(_empire.Emperor?.lover);
            
            //右侧信息栏
            var rightPart = topSpace.BeginVertGroup(pAlignment:TextAnchor.MiddleCenter);
            rightPart.AddTextIntoVertLayout($"{LM.Get("official_students_num")}: " +
                                            $"{_empire.GetMembersWithTrait("jingshi").Count.ToString().ColorString("#E16A54")}/" +
                                            $"{_empire.GetMembersWithTrait("gongshi").Count.ToString().ColorString("#CB9DF0")}/" +
                                            $"{_empire.GetMembersWithTrait("juren").Count.ToString()}".ColorString("#A2D2DF"));
            rightPart.AddTextIntoVertLayout($"{_empire.GetYearNameWithTime().ColorString(pColor:_empire.getColor()._colorText)}");
            rightPart.AddTextIntoVertLayout($"{LM.Get("i_age")}: {_empire.empire.getAge()}");
            
            topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 1));
            
            AddIntoGroup("top_space", topSpace.gameObject);
        }

        private void LeftEmperor()
        {
            throw new NotImplementedException();
        }

        private void InitialTextInput()
        {
            string text = _empire.GetEmpireName();
            UIHelper.GenerateTextInput(this.transform.parent.transform.parent, offset:new Vector2(0, 152), default_text:text, input:_empireNameInput);
        }
        
        //显示势力范围
        public void ShowKingdomList(WindowMetaTab pArg0=null)
        {
            Clear();
            InitialTopPartInfo();
            var parent = CommonInitial("empire_controlled_kingdoms");
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
            foreach (EmpireCraftHistory history in _empire.data.history)
            {
                ListPastEmperor(statsRow, history);
            }
            text = _empire.GetEmpireName() + _empire.data.year_name + LM.Get("emperor");
            statsRow.tryToShowActor("current_emperor", -1L, null, _empire.Emperor, "iconKings");
            statsRow.IShowStatsRow("title_name", text, _empire.getColor().color_text);
            StartCoroutine(statsRow.showRows());
        }
        //显示个人历史
        public void ShowPersonalHistory()
        {
            Clear();
            InitialTopPartInfo();
            var parent = CommonInitial("empire_personal_history");
            var currentHistory = ConfigData.CURRENT_SELECTED_HISTORY;
            string text1 = "";
            string text2 = "";
            SimpleText titleText = Instantiate(SimpleText.Prefab, null);
            text1 = currentHistory.emperor + "\n" + _empire.GetEmpireName() + currentHistory.year_name + LM.Get("emperor");
            if (!string.IsNullOrEmpty(currentHistory.miaohao_name))
            {
                text2 =
                    _empire.GetEmpireName() + LM.Get(currentHistory.miaohao_name) + LM.Get(currentHistory.miaohao_suffix) + "-" +
                    _empire.GetEmpireName() + LM.Get(currentHistory.shihao_name) + LM.Get("emperor_suffix");
            }
            else
            {
                text2 = LM.Get("waiting_for_naming");
            }
            string text = text1 + "\n" + text2;
            titleText.Setup(text, TextAnchor.MiddleCenter, new Vector2(50, 50));
            titleText.background.enabled = false;
            parent.AddChild(titleText.gameObject);
            foreach (var d in currentHistory.descriptions)
            {
                ListHistoryDescriptions(d, parent);
            }
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
            kl.textAge.text = e.getAge().ToString();
            kl.textPopulation.text = e.countUnits().ToString();
            kl.textArmy.text = e.countTotalWarriors().ToString();
            kl.textCities.text = e.countCities().ToString();
            kl.textZones.text = e.countZones().ToString();
            kl.avatarLoader.load(e.king);
            kl.meta_object = e;
            kl.loadBanner();
            inst.name = "list_element_kingdom";
            inst.SetActive(true);
            parent.AddChild(inst);
        }

        public override void OnFirstEnable()
        {
        }
        public override void OnNormalEnable()
        {
            _empire = ConfigData.CURRENT_SELECTED_EMPIRE;
            _empireNameInput.input.text = _empire.GetEmpireName();
            Clear();
            InitialTabButtons();
            ShowTopPart();
            ShowKingdomList();
        }

        public void ListPastEmperor(EmpireCraftStatsRow statsRow, EmpireCraftHistory history)
        {
            if (string.IsNullOrEmpty(history.emperor))
            {
                return;
            }
            var text1 = history.empire_name + history.year_name + LM.Get("emperor");
            var text2 = "";
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
            statsRow.IShowStatsRow("past_emperor", history.emperor + $"(在位 {history.total_time}{LM.Get("Year")})", _empire.getColor().color_text, pIconPath: "iconKings", action: () => OpenHistoryWindow(history));
            statsRow.IShowStatsRow("title_name", text1, _empire.getColor().color_text);
            statsRow.IShowStatsRow("post_humous_name", text2, _empire.getColor().color_text);
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

        public void ListHistoryDescriptions(string description, AutoVertLayoutGroup parent)
        {
            (string time, string describe) des = (description.Split('_')[0], description.Split('_')[1]);
            SimpleText timeText = Instantiate(SimpleText.Prefab, null);
            SimpleText describeText = Instantiate(SimpleText.Prefab, null);
            timeText.Setup(des.time, TextAnchor.MiddleLeft, new Vector2(40, 10));
            timeText.text.color = Color.blue;
            describeText.Setup(des.describe, TextAnchor.MiddleLeft, new Vector2(200, 10));
            parent.AddChild(timeText.gameObject);
            parent.AddChild(describeText.gameObject);
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
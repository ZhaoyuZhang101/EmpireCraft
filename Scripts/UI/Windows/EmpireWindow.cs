using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.General.UI.Window;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using NeoModLoader.General.UI.Prefabs;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Data;
using NeoModLoader.api.attributes;
using UnityEngine.Assertions;
using NeoModLoader.services;
using UnityEngine.UI;
using EmpireCraft.Scripts.HelperFunc;
using UnityEngine.Pool;
using NeoModLoader.General;
using System.Collections;
using EmpireCraft.Scripts.UI.Components;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireWindow : AutoLayoutWindow<EmpireWindow>
    {
        AutoHoriLayoutGroup TopLayout;
        AutoGridLayoutGroup GridLayout;
        ListPool<GameObject> ListPool;
        ListPool<GameObject> EmperorsPool;
        ListPool<GameObject> historyPool;
        TextInput empireNameInput;
        SimpleButton year_name_button;
        AutoVertLayoutGroup emperorsContainer;
        Empire empire;
        AutoVertLayoutGroup list_kingdom_container;
        AutoVertLayoutGroup list_kingdoms;
        AutoVertLayoutGroup EmperorsSpace;
        AutoVertLayoutGroup PersonalHistory;
        AutoVertLayoutGroup personalHistoryContainer;
        EmpireCraftStatsRow statsRow;

        List<string> infos = new List<string>()
        {
            "i_cities", "i_kingdoms", "i_age", "i_renown", "i_deaths", "i_members"
        };
        Dictionary<string, Text> infosTrans = new Dictionary<string, Text>();
        protected override void Init()
        {
            this.GetLayoutGroup().spacing = 10;

            ListPool = new ListPool<GameObject>();
            EmperorsPool = new ListPool<GameObject>();
            historyPool = new ListPool<GameObject>();
            TopLayout = this.BeginHoriGroup(new Vector2(150, 30), pSpacing: 3);

            AutoVertLayoutGroup vertLayout = this.BeginVertGroup(new Vector2(75, 30), pSpacing:3);
            //名称输入栏
            SimpleText empireText = Instantiate(SimpleText.Prefab, null);
            string empireName = LM.Get("empire_name");
            empireText.Setup($"{empireName}: ", TextAnchor.MiddleCenter, new Vector2(40, 15));
            empireText.background.enabled = false;
  
            empireNameInput = Instantiate(TextInput.Prefab, null);
            empireNameInput.Setup("", name_change);
            empireNameInput.SetSize(new Vector2(90, 18));
            vertLayout.AddChild(empireText.gameObject);
            vertLayout.AddChild(empireNameInput.gameObject);
            TopLayout.AddChild(vertLayout.gameObject);

            //年号按钮
            AutoVertLayoutGroup vertLayout2 = this.BeginVertGroup(new Vector2(75, 30), pSpacing: 3);
            SimpleText ToggleText = Instantiate(SimpleText.Prefab, null);
            string open_year_name = LM.Get("open_year_name");
            ToggleText.Setup($"{open_year_name}: ", TextAnchor.MiddleCenter, new Vector2(30, 15));
            ToggleText.background.enabled = false;
            year_name_button = Instantiate(SimpleButton.Prefab, null);
            year_name_button.Setup(ToggleYearName, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"));
            year_name_button.Background.enabled = false;
            year_name_button.SetSize(new Vector2(10, 10));
            vertLayout2.AddChild(ToggleText.gameObject);
            vertLayout2.AddChild(year_name_button.gameObject);
            TopLayout.AddChild(vertLayout2.gameObject);

            AddChild(TopLayout.gameObject);


            //信息查看选择区
            AutoHoriLayoutGroup selectGroup = this.BeginHoriGroup(new Vector2(150, 30), pSpacing: 3);
            //信息查看按钮
            Vector2 buttonSize = new Vector2(25, 15);
            SimpleButton infosButton = Instantiate(SimpleButton.Prefab, null);
            infosButton.Setup(ShowList, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"), LM.Get("empire_cotroled_kingdoms"), buttonSize);

            SimpleButton emperorsButton = Instantiate(SimpleButton.Prefab, null);
            emperorsButton.Setup(ShowEmperors, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"), LM.Get("past_emperors"), buttonSize);

            SimpleButton empireBeaurauButton = Instantiate(SimpleButton.Prefab, null);
            empireBeaurauButton.Setup(ShowBeaurau, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"), LM.Get("empire_beaurau"), buttonSize);

            selectGroup.AddChild(infosButton.gameObject);
            selectGroup.AddChild(emperorsButton.gameObject);
            selectGroup.AddChild(empireBeaurauButton.gameObject);
            AddChild(selectGroup.gameObject);


            //君主世系
            EmperorsSpace = this.BeginVertGroup(pSpacing: 3);
            //标题区
            SimpleText emperorsText = Instantiate(SimpleText.Prefab, null);
            string emperors = LM.Get("past_emperors");
            emperorsText.Setup($"{emperors}", TextAnchor.MiddleCenter, new Vector2(40, 15));
            emperorsText.background.enabled = false;
            EmperorsSpace.AddChild(emperorsText.gameObject);
            //查看区
            emperorsContainer = this.BeginVertGroup();
            emperorsContainer.AddComponent<EmpireCraftStatsRow>();
            statsRow = emperorsContainer.GetComponent<EmpireCraftStatsRow>();
            EmperorsSpace.AddChild(emperorsContainer.gameObject);
            AddChild(EmperorsSpace.gameObject);
            EmperorsSpace.gameObject.SetActive(false);


            //势力范围
            list_kingdoms = this.BeginVertGroup(pSpacing: 3);
            //标题区
            SimpleText kingdomsText = Instantiate(SimpleText.Prefab, null);
            string empire_cotroled_kingdoms = LM.Get("empire_cotroled_kingdoms");
            kingdomsText.Setup($"{empire_cotroled_kingdoms}", TextAnchor.MiddleCenter, new Vector2(40, 15));
            kingdomsText.background.enabled = false;
            list_kingdoms.AddChild(kingdomsText.gameObject);
            //国家列表
            list_kingdom_container = this.BeginVertGroup();
            list_kingdom_container.AddChild(kingdomsText.gameObject);
            list_kingdoms.AddChild(list_kingdom_container.gameObject);
            AddChild(list_kingdoms.gameObject);
            list_kingdoms.gameObject.SetActive(false);

            //个人历史
            PersonalHistory = this.BeginVertGroup(pSpacing: 3);
            //标题区
            SimpleText personalHistorysText = Instantiate(SimpleText.Prefab, null);
            string empire_personal_history = LM.Get("empire_personal_history");
            personalHistorysText.Setup($"{empire_personal_history}", TextAnchor.MiddleCenter, new Vector2(40, 15));
            personalHistorysText.background.enabled = false;
            PersonalHistory.AddChild(personalHistorysText.gameObject);
            //个人历史列表
            personalHistoryContainer = this.BeginVertGroup(pSpacing: 3);
            PersonalHistory.AddChild(PersonalHistory.gameObject);


        }

        private void ToggleYearName()
        {
            empire = ConfigData.CURRENT_SELECTED_EMPIRE;
            empire.data.has_year_name = !empire.data.has_year_name;
            setToggle(empire.data.has_year_name);
        }
        public void setToggle (bool toggle)
        {
            if (toggle)
            {
                year_name_button.Icon.sprite = SpriteTextureLoader.getSprite("ui/toggle_open");
            }
            else
            {
                year_name_button.Icon.sprite = SpriteTextureLoader.getSprite("ui/toggle_close");
            }
        }
        public void Clear()
        {
            if (ListPool == null) return;
            float deleteTime = 0.2f;
            foreach (GameObject go in ListPool)
            {
                go.SetActive(false);
                Destroy(go, deleteTime);
                deleteTime += 0.2f;
            }
            ListPool.Clear();

            if (EmperorsPool == null) return;
            deleteTime = 0.2f;
            foreach (GameObject go in EmperorsPool)
            {
                go.SetActive(false);
                Destroy(go, deleteTime);
                deleteTime += 0.2f;
            }
            EmperorsPool.Clear();

            if (historyPool == null) return;
            deleteTime = 0.2f;
            foreach (GameObject go in historyPool)
            {
                go.SetActive(false);
                Destroy(go, deleteTime);
                deleteTime += 0.2f;
            }
            historyPool.Clear();

            list_kingdoms.gameObject.SetActive(false);
            EmperorsSpace.gameObject.SetActive(false);
            PersonalHistory.gameObject.SetActive(false);
        }

        public void ShowList()
        {
            Clear();
            list_kingdoms.gameObject.SetActive(true);
            StartCoroutine(ShowKingdoms());
            list_kingdoms.GetLayoutGroup().CalculateLayoutInputVertical();
        }
        public void ShowBeaurau()
        {
            ScrollWindow.showWindow(nameof(EmpireBeaurauWindow));
        }

        public IEnumerator ShowKingdoms()
        {
            foreach (var e in empire.kingdoms_list)
            {
                prepareKingdom(e);
                yield return CoroutineHelper.wait_for_next_frame;
            }
        }

        public void prepareKingdom(Kingdom e)
        {
            GameObject KingdomListElement = PrefabHelper.FindPrefabByName("list_element_kingdom");
            GameObject inst = GameObject.Instantiate(KingdomListElement);
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
            list_kingdom_container.AddChild(inst);
            ListPool.Add(inst);
        }

        public override void OnFirstEnable()
        {
        }
        public override void OnNormalEnable()
        {
            empire = ConfigData.CURRENT_SELECTED_EMPIRE;
            empireNameInput.input.text = empire.GetEmpireName();
            setToggle(empire.data.has_year_name);
            Clear();
            ShowList();
        }

        public void ShowEmperors()
        {
            Clear();
            EmperorsSpace.gameObject.SetActive(true);
            string text = "";
            string text1 = "";
            string text2 = "";
            foreach (EmpireCraftHistory history in empire.data.history)
            {
                if (history.emperor == null || history.emperor == "")
                {
                    continue;
                }
                text1 = history.empire_name + history.year_name + LM.Get("emperor");
                if (history.miaohao_name != "" && history.miaohao_name != null)
                {
                    text2 =
                        history.empire_name + LM.Get(history.miaohao_name) + LM.Get(history.miaohao_suffix) + "-" +
                        history.empire_name + LM.Get(history.shihao_name) + LM.Get("emperor_suffix");
                }
                else
                {
                    text2 = LM.Get("waiting_for_naming");
                }
                statsRow.IShowStatsRow("past_emperor", history.emperor + $"(在位 {history.total_time}{LM.Get("Year")})", empire.getColor().color_text, pIconPath: "iconKings", action: () => OpenHistoryWindow(history));
                statsRow.IShowStatsRow("title_name", text1, empire.getColor().color_text);
                statsRow.IShowStatsRow("post_humous_name", text2, empire.getColor().color_text);
                statsRow.IShowStatsRow("empty", "=======================================================================================", "#ffffff");
            }
            text = empire.GetEmpireName() + empire.data.year_name + LM.Get("emperor");
            statsRow.tryToShowActor("current_emperor", -1L, null, empire.emperor, "iconKings");
            statsRow.IShowStatsRow("title_name", text, empire.getColor().color_text);
            StartCoroutine(statsRow.showRows());
            EmperorsSpace.GetLayoutGroup().CalculateLayoutInputVertical();
        }

        public void OpenHistoryWindow(EmpireCraftHistory history)
        {
            ConfigData.CURRENT_SELECTED_HISTORY = history;
            showPersonalHistory();
        }

        public void name_change(string name)
        {
            if (empire != null)
            {
                empire.SetEmpireName(name);
            }
        }

        public void showPersonalHistory()
        {
            Clear();
            var current_history = ConfigData.CURRENT_SELECTED_HISTORY;
            string text1 = "";
            string text2 = "";
            PersonalHistory.gameObject.SetActive(true);
            SimpleText titleText = Instantiate(SimpleText.Prefab, null);
            text1 = current_history.emperor + "\n" + empire.GetEmpireName() + current_history.year_name + LM.Get("emperor");
            if (current_history.miaohao_name != "" && current_history.miaohao_name != null)
            {
                text2 =
                    empire.GetEmpireName() + LM.Get(current_history.miaohao_name) + LM.Get(current_history.miaohao_suffix) + "-" +
                    empire.GetEmpireName() + LM.Get(current_history.shihao_name) + LM.Get("emperor_suffix");
            }
            else
            {
                text2 = LM.Get("waiting_for_naming");
            }
            string text = text1 + "\n" + text2;
            titleText.Setup(text, TextAnchor.MiddleCenter, new Vector2(50, 50));
            titleText.background.enabled = false;
            personalHistoryContainer.AddChild(titleText.gameObject);
            historyPool.Add(titleText.gameObject);
            foreach (var d in current_history.descriptions)
            {
                (string time, string describe) des = (d.Split('_')[0], d.Split('_')[1]);
                SimpleText timeText = Instantiate(SimpleText.Prefab, null);
                SimpleText describeText = Instantiate(SimpleText.Prefab, null);
                timeText.Setup(des.time, TextAnchor.MiddleLeft, new Vector2(40, 10));
                timeText.text.color = Color.blue;
                describeText.Setup(des.describe, TextAnchor.MiddleLeft, new Vector2(200, 10));
                personalHistoryContainer.AddChild(timeText.gameObject);
                personalHistoryContainer.AddChild(describeText.gameObject);
                historyPool.Add(timeText.gameObject);
                historyPool.Add(describeText.gameObject);
            }
        }
    }
}
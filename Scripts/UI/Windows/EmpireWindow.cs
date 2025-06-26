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

namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireWindow : AutoLayoutWindow<EmpireWindow>
    {
        AutoHoriLayoutGroup TopLayout;
        AutoGridLayoutGroup GridLayout;
        ListPool<GameObject> ListPool;
        TextInput empireNameInput;
        SimpleButton year_name_button;
        Empire empire;
        AutoVertLayoutGroup list_kingdom_container;

        List<string> infos = new List<string>()
        {
            "i_cities", "i_kingdoms", "i_age", "i_renown", "i_deaths", "i_members"
        };
        Dictionary<string, Text> infosTrans = new Dictionary<string, Text>();
        [Hotfixable]
        protected override void Init()
        {
            ListPool = new ListPool<GameObject>();
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

            SimpleText kingdomsText = Instantiate(SimpleText.Prefab, null);
            string empire_cotroled_kingdoms = LM.Get("empire_cotroled_kingdoms");
            kingdomsText.Setup($"{empire_cotroled_kingdoms}", TextAnchor.MiddleCenter, new Vector2(40, 15));
            kingdomsText.background.enabled = false;

            //国家列表
            list_kingdom_container = this.BeginVertGroup();
            list_kingdom_container.AddChild(kingdomsText.gameObject);
            AddChild(list_kingdom_container.gameObject);
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
        }

        public void ShowList()
        {
            foreach (var e in empire.kingdoms_list)
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
        }

        public override void OnFirstEnable()
        {
        }
        [Hotfixable]
        public override void OnNormalEnable()
        {
            empire = ConfigData.CURRENT_SELECTED_EMPIRE;
            empireNameInput.input.text = empire.GetEmpireName();
            setToggle(empire.data.has_year_name);
            Clear();
            ShowList();
        }

        public void name_change(string name)
        {
            if (empire != null)
            {
                empire.SetEmpireName(name);
            }
        }

        public void showContent()
        {
            //infosTrans["i_cities"].text = empire.countCities().ToString();
            //infosTrans["i_kingdoms"].text = empire.countKingdoms().ToString();
            //infosTrans["i_age"].text = empire.getAge().ToString();
            //infosTrans["i_renown"].text = empire.getRenown().ToString();
            //infosTrans["i_deaths"].text = empire.getTotalDeaths().ToString();
            //infosTrans["i_members"].text = empire.countPopulation().ToString();
        }
    }
}
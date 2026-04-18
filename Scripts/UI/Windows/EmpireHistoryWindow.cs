using System.Collections.Generic;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.General;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI;
using EmpireCraft.Scripts.UI.Components;
using UnityEngine.UI;
namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireHistoryWindow : AutoLayoutWindow<EmpireHistoryWindow>
    {
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();
        private Empire _empire;
        protected override void Init()
        {
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 60, 3);
        }
        public void Clear()
        {
            foreach (var container in _groups)
            {
                GameObject.Destroy(container.Value);
            }
            _groups.Clear();
        }
        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 60, 3);
            _empire = EmpireCraftMetaTypeLibrary.selected_empire;
            Clear();
            ShowPersonalHistory();
        }
        private AutoVertLayoutGroup CommonInitial(string titleName)
        {
            var container = this.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            SimpleText title = UnityEngine.Object.Instantiate(SimpleText.Prefab, null);
            string t = LM.Get(titleName);
            title.Setup($"{t}", TextAnchor.MiddleCenter, new Vector2(40, 15));
            title.background.enabled = false;
            container.AddChild(title.gameObject);
            var content = this.BeginVertGroup(pSpacing: 3);
            container.AddChild(content.gameObject);
            if (!_groups.ContainsKey(titleName)) _groups.Add(titleName, container.gameObject);
            return content;
        }
        public void ShowPersonalHistory()
        {
            Clear();
            var parent = CommonInitial("empire_personal_history");
            var currentHistory = ConfigData.CURRENT_SELECTED_HISTORY;
            if (currentHistory == null) return;
            string text1 = "";
            string text2 = "";
            SimpleText titleText = UnityEngine.Object.Instantiate(SimpleText.Prefab, null);
            text1 = currentHistory.emperor + "\n" + currentHistory.empire_name + currentHistory.year_name + LM.Get("emperor");
            if (!string.IsNullOrEmpty(currentHistory.miaohao_name))
            {
                text2 = currentHistory.empire_name + LM.Get(currentHistory.miaohao_name) + LM.Get(currentHistory.miaohao_suffix) + "-" +
                        currentHistory.empire_name + LM.Get(currentHistory.shihao_name) + LM.Get("emperor_suffix");
            }
            else
            {
                text2 = LM.Get("waiting_for_naming");
            }
            string text = text1 + "\n" + text2;
            titleText.Setup(text, TextAnchor.MiddleCenter, new Vector2(50, 50));
            titleText.background.enabled = false;
            parent.AddChild(titleText.gameObject);
            if (currentHistory.descriptions != null)
            {
                HistoryDescription lasDes = new HistoryDescription()
                {
                    cities = new  List<string>(),
                    description = "",
                    time = ""
                };
                foreach (var d in currentHistory.descriptions)
                {
                    ListHistoryDescriptions(lasDes, d, parent);
                    lasDes = d;
                }
            }
        }
        public static void ListHistoryDescriptions(HistoryDescription lastDesc, HistoryDescription desc, AutoVertLayoutGroup parent)
        {
            if (lastDesc.time != desc.time)
            {
                parent.AddTextIntoVertLayout(desc.time.ColorString(pColor:new Color(0.5f, 0.8f, 1.0f)), false, TextAnchor.MiddleCenter, new Vector2(50, 23));
                string content = "";
                string com = "";
                int init = 0;
                foreach (var city in desc.cities)
                {
                    init++;
                    com = init<desc.cities.Count? ", ": "";
                    if (!lastDesc.cities.Contains(city))
                    {
                        content+= city.ColorString(pColor:new Color(0.4f, 0.8f, 0.4f)) +$"({LM.Get("obtain_city")})"+ com;
                    }
                    else
                    {
                        content += city +　com;
                    }
                }
                
                foreach (var lCity in lastDesc.cities)
                {
                    com = string.IsNullOrEmpty(content) ? "" : ", ";
                    if (!desc.cities.Contains(lCity))
                    {
                        content += com + lCity.ColorString(pColor:new Color(1.0f, 0.3f, 0.3f)) +$"({LM.Get("lost_city")})";
                    }
                }
                var cityChangeText = parent.AddTextIntoVertLayout(content, false, TextAnchor.MiddleCenter, new Vector2(200, 15));
                cityChangeText.UseFixedFontSize(8);
                cityChangeText.RefreshAutoHeight(15);
            }
            parent.AddTextIntoVertLayout(desc.description, false, TextAnchor.MiddleLeft, new Vector2(200, 10));
        }
    }
}

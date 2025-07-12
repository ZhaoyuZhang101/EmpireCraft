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
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class ProvinceWindow : AutoLayoutWindow<ProvinceWindow>
    {
        public Province province { get; set; }
        public TextInput provinceNameInput;
        public AutoVertLayoutGroup layout1;
        public AutoVertLayoutGroup layout2;
        public PrefabUnitElement pl;
        protected override void Init()
        {
            layout1 = this.BeginVertGroup(pSpacing: 5, pPadding: new RectOffset(0, 0, 0, 0));
            //名称输入栏
            SimpleText empireText = Instantiate(SimpleText.Prefab, null);
            string province_name = LM.Get("province_name");
            empireText.Setup($"{province_name}: ", TextAnchor.MiddleCenter, new Vector2(40, 15));
            empireText.background.enabled = false;

            provinceNameInput = Instantiate(TextInput.Prefab, null);
            provinceNameInput.Setup("", name_change);
            provinceNameInput.SetSize(new Vector2(90, 18));
            layout1.AddChild(empireText.gameObject);
            layout1.AddChild(provinceNameInput.gameObject);
            AddChild(layout1.gameObject);

            layout2 = this.BeginVertGroup();
            SimpleText provinceOfficer = Instantiate(SimpleText.Prefab);
            provinceOfficer.Setup("province_leader");
            GameObject unitListElement = PrefabHelper.FindPrefabByName("list_element_favorites");
            GameObject inst = GameObject.Instantiate(unitListElement);
            pl = inst.GetComponent<PrefabUnitElement>();


        }

        private void name_change(string arg0)
        {
            province.data.name = arg0;
        }

        public override void OnNormalEnable()
        {
            pl.gameObject.SetActive(false);
            province = ConfigData.CURRENT_SELECTED_PROVINCE;
            provinceNameInput.input.text = province.data.name;
            if (province.HasOfficer())
            {
                pl.gameObject.SetActive(true);
                pl.show(province.officer);
            } else
            {
                pl.gameObject.SetActive(false);
            }
        }
    }
}
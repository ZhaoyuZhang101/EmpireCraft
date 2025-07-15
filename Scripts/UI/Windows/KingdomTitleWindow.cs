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
    public class KingdomTitleWindow : AutoLayoutWindow<KingdomTitleWindow>
    {
        public KingdomTitle title { get; set; }
        public TextInput titleNameInput;
        public TextInput provinceNameInput;
        public AutoVertLayoutGroup layout;
        protected override void Init()
        {
            layout = this.BeginVertGroup(pSpacing: 5, pPadding: new RectOffset(0, 0, 0, 0));
            //名称输入栏
            SimpleText empireText = Instantiate(SimpleText.Prefab, null);
            string title_name = LM.Get("title_name");
            empireText.Setup($"{title_name}: ", TextAnchor.MiddleCenter, new Vector2(40, 15));
            empireText.background.enabled = false;

            titleNameInput = Instantiate(TextInput.Prefab, null);
            titleNameInput.Setup("", name_change);
            titleNameInput.SetSize(new Vector2(90, 18));
            layout.AddChild(empireText.gameObject);
            layout.AddChild(titleNameInput.gameObject);


            //省份名称输入栏
            SimpleText empireProvinceText = Instantiate(SimpleText.Prefab, null);
            string province_name = LM.Get("province_name");
            empireProvinceText.Setup($"{province_name}: ", TextAnchor.MiddleCenter, new Vector2(40, 15));
            empireProvinceText.background.enabled = false;

            provinceNameInput = Instantiate(TextInput.Prefab, null);
            provinceNameInput.Setup("", province_name_change);
            provinceNameInput.SetSize(new Vector2(90, 18));
            layout.AddChild(empireProvinceText.gameObject);
            layout.AddChild(provinceNameInput.gameObject);


            AddChild(layout.gameObject);
        }
        private void name_change(string arg0)
        {
            title.data.name = arg0;
        }

        private void province_name_change(string arg0)
        {
            title.data.province_name = arg0;
        }

        public override void OnNormalEnable()
        {
            title = ConfigData.CURRENT_SELECTED_TITLE;
            titleNameInput.input.text = title.data.name;
            provinceNameInput.input.text = title.data.province_name;
        }
    }
}
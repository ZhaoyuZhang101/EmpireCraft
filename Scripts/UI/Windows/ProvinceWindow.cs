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
using NeoModLoader.General;
using EmpireCraft.Scripts.UI.Components;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class ProvinceWindow : AutoLayoutWindow<ProvinceWindow>
    {
        public Province _province { get; set; }
        private TextInput _provinceNameInput;
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();
        protected override void Init()
        {
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 60, 3);
            _provinceNameInput = Instantiate(TextInput.Prefab, this.transform.parent.transform.parent);
            _provinceNameInput.Setup("", name_change);
        }
        public override void OnNormalEnable()
        {
            _province = ConfigData.CURRENT_SELECTED_PROVINCE;
            _provinceNameInput.input.text = _province.data.name;
            Clear();
        }
        public void Clear()
        {
            foreach (var container in _groups)
            {
                Destroy(container.Value);
            }
            _groups.Clear();
        }
        private void name_change(string arg0)
        {
            _province.data.name = arg0;
        }
    }
}
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireListWindow : AutoLayoutWindow<EmpireListWindow>
    {
        AutoVertLayoutGroup TopLayout;
        ListPool<GameObject> ListPool;
        protected override void Init()
        {
            ListPool = new ListPool<GameObject>();
            TopLayout = this.BeginVertGroup(pSpacing: 5, pPadding: new RectOffset(0, 0, 0, 0));
        }

        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            Clear();
            ShowTop();
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


        public void ShowTop() 
        {
            foreach (var empire in ModClass.EMPIRE_MANAGER)
            {
                try
                {
                    GameObject lek = PrefabHelper.FindPrefabByName("list_element_kingdom");
                    GameObject inst = GameObject.Instantiate(lek);
                    KingdomListElement kl = inst.GetComponent<KingdomListElement>();
                    kl.kingdomName.text = empire.name;
                    kl.textAge.text = empire.getAge().ToString();
                    kl.textPopulation.text = empire.countUnits().ToString();
                    kl.textArmy.text = empire.countWarriors().ToString();
                    kl.textCities.text = empire.countCities().ToString();
                    kl.textZones.text = empire.countZones().ToString();
                    kl.avatarLoader.load(empire.CoreKingdom.king);
                    kl.meta_object = empire.CoreKingdom;
                    kl.loadBanner();
                    inst.name = "list_element_kingdom";
                    inst.SetActive(true);
                    TopLayout.AddChild(inst);
                    ListPool.Add(inst);
                } catch 
                {
                    LogService.LogInfo("帝国列表生成失败");
                }
            }
        }
    }
}

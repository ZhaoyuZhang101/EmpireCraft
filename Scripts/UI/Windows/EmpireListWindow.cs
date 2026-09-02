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
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;
using UnityEngine.Events;
using EmpireCraft.Scripts.UI.Components;
using UnityEngine.UI;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameLibrary;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireListWindow : AutoLayoutWindow<EmpireListWindow>
    {
        AutoVertLayoutGroup TopLayout;
        ListPool<GameObject> ListPool;
        protected override void Init()
        {
            ListPool = new ListPool<GameObject>();
            TopLayout = this.BeginVertGroup(pSpacing: 5, pPadding: new RectOffset(3, 3, 80, 3));
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
            var toolbar = TopLayout.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.MiddleCenter);
            toolbar.AddButtonIntoVertLayout("purge_recent_100_years", LM.Get("purge_recent_100_years"), () =>
            {
                ModClass.EMPIRE_MANAGER.PurgeArchivedOlderThanYears(100);
                Clear();
                ShowTop();
            }, size: new Vector2(60, 12));
            ListPool.Add(toolbar.gameObject);
            var columnsRow = TopLayout.BeginHoriGroup(pSpacing: 8, pAlignment: TextAnchor.MiddleCenter);
            var colLeft = columnsRow.BeginVertGroup(pSpacing: 6, pAlignment: TextAnchor.UpperCenter);
            var colRight = columnsRow.BeginVertGroup(pSpacing: 6, pAlignment: TextAnchor.UpperCenter);
            int idx = 0;
            foreach (var empire in ModClass.EMPIRE_MANAGER)
            {
                try
                {
                    var card = BuildEmpireCard(empire);
                    if (idx % 2 == 0) colLeft.AddChild(card);
                    else colRight.AddChild(card);
                    idx++;
                } 
                catch 
                {
                    LogService.LogInfo("帝国列表生成失败");
                }
            }
        }
        
        private GameObject BuildEmpireCard(Empire empire)
        {
            bool alive = !empire.IsArchived();
            var card = TopLayout.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(50, 80));
            string name = empire.GetEmpireName() + (alive ? "" : "(已灭亡)");
            card.AddTextIntoVertLayout(name, hideBackground: true, TextAnchor.MiddleCenter, new Vector2(90, 12));
            Clan clan = null;
            if (alive)
            {
                clan = empire.EmpireClan ?? empire.CoreKingdom?.getKingClan();
            }
            else
            {
                var clanId = empire.data.empire_clan;
                clan = clanId != -1L ? World.world.clans.get(clanId) : null;
                if (clan == null)
                {
                    clan = empire.EmpireClan;
                }
            }
            string clanName = clan != null ? clan.GetClanName() : "无";
            card.AddTextIntoVertLayout($"最后掌控的氏族：{clanName}", hideBackground: true, TextAnchor.MiddleCenter, new Vector2(90, 10));
            Actor oldest = null;
            if (clan != null && !clan.isRekt() && clan.units != null)
            {
                int maxAge = int.MinValue;
                for (int i = 0; i < clan.units.Count; i++)
                {
                    var a = clan.units[i];
                    if (a == null) continue;
                    if (!a.isAlive()) continue;
                    int age = a.getAge();
                    if (age > maxAge)
                    {
                        maxAge = age;
                        oldest = a;
                    }
                }
            }
            long oldestId = oldest?.getID() ?? -1L;
            var avatar = UIHelper.CreateAvatarView(oldestId, () => { if (oldest != null) UIHelper.actorClick(oldest); }, pIsAlive: oldest != null);
            card.AddChild(avatar.gameObject);
            card.AddButtonIntoVertLayout("open_history", LM.Get("empire_history"), () =>
            {
                if (empire.CoreKingdom != null) SelectedMetas.selected_kingdom = empire.CoreKingdom;
                EmpireCraftMetaTypeLibrary.selected_empire = empire;
                var history = empire.data.currentHistory ?? empire.data.history?.LastOrDefault();
                if (history == null)
                {
                    Actor emActor = alive ? empire.Emperor : World.world.units.get(empire.data.emperor);
                    history = new EmpireCraftHistory
                    {
                        id = emActor?.data.id ?? -1L,
                        year_name = empire.data.year_name,
                        emperor = emActor?.getName() ?? "",
                        empire_name = empire.GetEmpireName(),
                        dynasty_name = empire.GetEmpireName(),
                        royal_surname = emActor?.GetSpecificClan()?.name ?? "",
                        descriptions = new List<HistoryDescription>(),
                    };
                }
                ConfigData.CURRENT_SELECTED_HISTORY = history;
                ScrollWindow.showWindow(nameof(EmpireHistoryWindow));
            }, size: new Vector2(40, 12));
            if (!alive)
            {
                card.AddButtonIntoVertLayout("delete_empire", LM.Get("delete_empire"), () =>
                {
                    ModClass.EMPIRE_MANAGER.RemoveArchivedEmpire(empire);
                    Clear();
                    ShowTop();
                }, size: new Vector2(40, 12));
            }
            card.transform.AddStretchBackground(alive ? "FactionFrame" : "clanFrame", size: new Vector2(50, 80));
            ListPool.Add(card.gameObject);
            return card.gameObject;
        }
    }
}

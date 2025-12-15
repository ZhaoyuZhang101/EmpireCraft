using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

public class SpecificClanListWindow : AutoLayoutWindow<SpecificClanListWindow>
{
    public List<GameObject> _groups =  new List<GameObject>();
    public AutoVertLayoutGroup _content;
    public string _lastSearchContent = "";
    [Hotfixable]
    protected override void Init()
    {
        layout.spacing = 3;
        layout.padding = new RectOffset(3, 3, 82, 3);
        _content = this.BeginVertGroup();
    }

    public void Clear()
    {
        foreach (var group in _groups)
        {
            Destroy(group.gameObject);
        }
        _groups.Clear();
    } 
    
    public override void OnNormalDisable()
    {
        base.OnNormalDisable();
        _lastSearchContent = "";
    }

    [Hotfixable]
    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        layout.spacing = 3;
        layout.padding = new RectOffset(3, 3, 82, 3);
        Clear();
        ShowTopPart();
        InitialSearchSpace();
        StartCoroutine(ShowSpecificClanList());
    }
    [Hotfixable]
    public void ShowTopPart()
    {
        var topSpace = this.BeginVertGroup();
        var gridGroup = topSpace.BeginGridGroup(10, pCellSize: new Vector2(18, 15));
        foreach (var species in ConfigData.AllCivSpecies)
        {
            var asset = AssetManager.actor_library.list.Find(a => a.id == species.id);
            gridGroup.AddButtonIntoGirdLayout(species.getLocaleID(), "", ()=>RefreshAccordingToSpecies(species.id), asset.getSpriteIcon(), size:new Vector2(18, 15), showTip:true);
        }
        topSpace.transform.AddStretchBackground("clanFrame", new Vector2(220, 80));
        topSpace.gameObject.AdjustTopPart(transform.parent.transform);
        _groups.Add(topSpace.gameObject);
    }
    [Hotfixable]
    public void InitialSearchSpace()
    {
        var topSearchSpace = this.BeginHoriGroup();
        
        //搜索框
        UIHelper.GenerateTextInput(topSearchSpace.transform, action:StartSearchSpecificClan, default_text:_lastSearchContent);
        topSearchSpace.gameObject.AdjustTopPart(transform.parent.transform, Vector2.down*67);
        _groups.Add(topSearchSpace.gameObject);
    }
    public void StartSearchSpecificClan(string content)
    {
        _lastSearchContent = content;
        var result = SpecificClanManager._specificClans.FindAll(a => a.Count>0&&(a.asset.getLocaleID()
                                                                     +a.asset.getLocalizedName()
                                                                     +a.asset.getLocalizedDescription()
                                                                     +a.name
                                                                     +a.empire_name
                                                                     +a.id
                                                                     +a.founder
                                                                     +string.Join(",", a._cache.Values.Select(v=>v.name).ToArray())
                                                                     +string.Join(",", a.AllAliveMembers.Select(u=>(u?.city?.GetCityName()??"")+(u?.kingdom?.GetKingdomName()??"")).ToArray())
                                                                     +a.empire_name
                                                                     +a.capital_city_id
                                                                     +(World.world.cities.get(a.capital_city_id)?.name??"")
                                                                     ).Contains(content));
        RefreshAccordingToSpecies("", result, true);
    }

    public void RefreshAccordingToSpecies(string species, List<SpecificClan> list = null, bool search = false)
    {
        var result = list??SpecificClanManager._specificClans.ToList().FindAll(s => s.AllAliveMembers.Any(a => a.asset.id == species));
        Clear();
        ShowTopPart();
        InitialSearchSpace();
        StartCoroutine(ShowSpecificClanList(species, result, search));
    }

    public IEnumerator ShowSpecificClanList(string speciesName = "human", List<SpecificClan> specificClans = null,
        bool search = false)
    {
        var title = _content.AddTextIntoVertLayout(search?LM.Get("title_search_result"):$"{LM.Get(speciesName)}宗族", anchor:TextAnchor.MiddleCenter, size:new Vector2(60, 20), hideBackground:true);
        _groups.Add(title.gameObject);
        yield return CoroutineHelper.wait_for_next_frame;
        AutoGridLayoutGroup specificClanGrid = _content.BeginGridGroup(4, pCellSize: new Vector2(55, 90), pSpacing:new Vector2(0, 0));
        _groups.Add(specificClanGrid.gameObject);
        yield return CoroutineHelper.wait_for_next_frame;
        var list = specificClans ?? SpecificClanManager._specificClans.ToList().FindAll(s => s.AllAliveMembers.Any(a => a?.asset?.id == "human"));
        list = list.FindAll(s => s.AllAliveMembers.Count > 0);
        var res = 4 - list.Count % 4;
        for (int i = 0; i < list.Count; i++)
        {
            var sc = list[i];
            ShowSpecificClan(sc, specificClanGrid);
            if (i+1 % 4 == 0)
            {
                yield return CoroutineHelper.wait_for_next_frame;
            }
            else
            {
                if (list.Count == i+1)
                {
                    if (res != 4)
                    {
                        for (var x = 0; x < res; x++)
                        {
                            var button = specificClanGrid.AddTextIntoGridLayout("", hideBackground:true);
                            _groups.Add(button.gameObject);
                        }
                        yield return CoroutineHelper.wait_for_next_frame;
                    }
                }
            }
            
        }
    }
    [Hotfixable]
    public void ShowSpecificClan(SpecificClan specificClan, AutoGridLayoutGroup grid)
    {
        var vertCard = grid.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        Empire empire = ModClass.EMPIRE_MANAGER.ToList().Find(e => e.EmpireSpecificClan == specificClan);
        vertCard.AddTextIntoVertLayout(specificClan.name + LM.Get("specific_clan")+$"{(empire.isRekt()?"":("("+empire.GetEmpireName()+"皇室)").ColorString(pColor:empire.CoreKingdom.getColor()._color_main))}", hideBackground:true, TextAnchor.MiddleCenter, size:new Vector2(49, 10));
        var actor = specificClan.AllAliveMembers.ToList()?.OrderByDescending(a => a?.age??0)?
            .FirstOrDefault();
        vertCard.AddActorViewIntoVertLayout(actor);
        vertCard.AddTextIntoVertLayout($"{LM.Get("i_founder")}：{SpecificClanManager.getPerson(specificClan.founder).name}", size:new Vector2(49, 8), hideBackground:true, anchor:TextAnchor.MiddleCenter);
        vertCard.AddTextIntoVertLayout($"{LM.Get("total_sc_count")}：{specificClan.AllAliveMembers.Count}/{specificClan._cache.Count}", size:new Vector2(49, 8), hideBackground:true, anchor:TextAnchor.MiddleCenter);
        var hori = vertCard.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        if (!empire.isRekt())
        {
            EmpireCraftMetaTypeLibrary.selected_empire = empire;
            hori.AddButtonIntoHoriLayout("enter_empire", "帝国", () => { ScrollWindow.showWindow(nameof(EmpireWindow));}, size:new Vector2(15, 10));
        }

        SelectedUnit._unit_main = actor;
        hori.AddButtonIntoHoriLayout("enter_empire", "详情", () => { ScrollWindow.showWindow(nameof(SpecificClanWindow));}, size:new Vector2(15, 10));
        vertCard.transform.AddStretchBackground("clanFrame", size:new Vector2(50, 90));
        _groups.Add(vertCard.gameObject);
    }
}
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;
public class ChangeUnitWindow : AutoLayoutWindow<ChangeUnitWindow>
{
    AutoGridLayoutGroup autoGridLayoutGroup;
    Empire _empire;
    OfficeObject _office;
    Kingdom _kingdom;
    SimpleText title;
    ListPool<GameObject> _listPool = new ();
    TextInput textInput;
    private OfficerPowerType _currentSortPower = OfficerPowerType.审核;
    List<Actor> _actors = new();
    protected override void Init()
    {
        layout.padding = new RectOffset(0,0,70,0);
        _listPool = new ListPool<GameObject>();
        title = Instantiate(SimpleText.Prefab);
        AddChild(title.gameObject);
        textInput = GameObject.Instantiate(TextInput.Prefab);
        textInput.Setup(LM.Get("input_name_for_unit"), StarSearchUnit);
        textInput.SetSize(new Vector2(150, 18));
        AddChild(textInput.gameObject);
        autoGridLayoutGroup = this.BeginGridGroup(5, pSpacing: new Vector2(30, 30));
        AddChild(autoGridLayoutGroup.gameObject);
    }

    public void StarSearchUnit(string content)
    {
        Clear();
        InitTopPart();
        List<Actor> actorsPool = new List<Actor>();
        if (content == "")
        {
            foreach (Kingdom kingdom in _empire.kingdoms_list)
            {
                foreach (Actor actor in kingdom.units)
                {
                    if (actor.isUnitFitToRule() && !actor.isKing() && (actor.hasTrait("jingshi") || actor.hasTrait("gongshi")))
                    {
                        actorsPool.Add(actor);
                    }
                }
            }
            textInput.text.text = LM.Get("input_name_for_unit");
            _actors = actorsPool;
            StartCoroutine(ShowUnitGroup());
            return;
        }
        foreach (Kingdom kingdom in _empire.kingdoms_list)
        {
            foreach (Actor actor in kingdom.units)
            {
                if (actor.isUnitFitToRule())
                {
                    string culture = ConfigData.speciesCulturePair.TryGetValue(actor.asset.id, out string culturePair)? culturePair:"Western";
                    string merit = "";
                    string honoraryOfficial = "";
                    string PeeragesLevel = "";
                    string officialLevel = "";
                    string kingdomName = actor.kingdom.name;
                    string cityName = actor.city.name;
                    string officer = actor.isOfficer() ? "officer" + LM.Get("actor_officer") : "";
                    string name = "";
                    int age = -1;
                    string educationLevel;
                    OfficeIdentity identity = actor.GetIdentity();
                    if (identity!=null)
                    {
                        merit = string.Join("_", culture, "meritlevel", identity.peerageType.ToString(), identity.meritLevel);
                        merit += LM.Get(merit);
                        honoraryOfficial = string.Join("_", culture, "honoraryofficial", identity.peerageType.ToString(), identity.honoraryOfficial);
                        honoraryOfficial += LM.Get(honoraryOfficial);
                        officialLevel = string.Join("_", culture, identity.officialLevel.ToString());
                        officialLevel += LM.Get(officialLevel);
                    }
                    educationLevel = (actor.hasTrait("jingshi") ? "trait_jingshi" : "") +"/" +(actor.hasTrait("gongshi") ? "trait_gongshi" : "") +"/"+ (actor.hasTrait("juren")?"trait_juren":"");
                    educationLevel += string.Join("/", educationLevel.Split('/').Select(c=>LM.Get(c)));
                    PeeragesLevel = string.Join("_", culture, actor.GetPeeragesLevel().ToString());
                    PeeragesLevel += LM.Get(PeeragesLevel);
                    name = actor.getName();
                    age = actor.getAge();
                    List<string> searchContent = new List<string>()
                    {
                        merit, honoraryOfficial, officialLevel, PeeragesLevel, name, age.ToString(), educationLevel, kingdomName, cityName, officer
                    };
                    bool isSatisfied = searchContent.ToList().Any(t =>t.Contains(content))||(int.TryParse(content, out int num) && num>=age);
                    if (isSatisfied) actorsPool.Add(actor);
                }
            }
        }
        _actors = actorsPool;
        StartCoroutine(ShowUnitGroup());
    }

    public void Clear()
    {
        float delay = 0.02f;
        foreach (GameObject go in _listPool)
        {
            go.SetActive(false);
            Destroy(go, delay);
            delay += 0.02f;
        }
        _listPool.Clear();
    }
    public override void OnNormalEnable()
    {
        Clear();
        base.OnNormalEnable();
        _office = ConfigData.CURRENT_SELECTED_OFFICE;
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        title.Setup(_office.GetName(), pAlignment:TextAnchor.MiddleCenter);
        InitTopPart();
        List<Actor> listActor = new List<Actor>();
        foreach (Kingdom kingdom in _empire.kingdoms_list)
        {
            foreach (Actor actor in kingdom.units)
            {
                if (actor.isUnitFitToRule() && !actor.isKing() && (actor.hasTrait("jingshi") || actor.hasTrait("gongshi")))
                {
                    listActor.Add(actor);
                }
            }
        }
        _actors = listActor;
        StartCoroutine(ShowUnitGroup());
    }
    public void InitTopPart()
    {
        var topSpace = this.BeginGridGroup(6, pCellSize: new Vector2(25, 15));
        foreach (OfficerPowerType power in OfficeManager.AllPower)
        {
            topSpace.AddButtonIntoGirdLayout(power.ToString(), power.ToString(), () => SortByPower(power), size: new Vector2(22, 13));
        }
        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset: Vector2.down);
        topSpace.transform.AddStretchBackground("regimeFrame", new Vector2(220, 75));
        _listPool.Add(topSpace.gameObject);
    }
    public void SortByPower(OfficerPowerType power)
    {
        _currentSortPower = power;
        Clear();
        InitTopPart();
        StartCoroutine(ShowUnitGroup());
    }

    public IEnumerator ShowUnitGroup()
    {
        _actors = _actors.OrderByDescending(a=>a.CalcPower(_currentSortPower, _empire).addition[_currentSortPower]).ToList();
        var count = 0;
        var actorSpace = this.BeginVertGroup();
        actorSpace.AddTextIntoVertLayout("候选官员列表", hideBackground: true, anchor: TextAnchor.MiddleCenter,
            size: new Vector2(45, 18));
        AutoHoriLayoutGroup currentAutoHoriLayout = null;
        foreach (var actor in _actors.Take(30))
        {
            if (actor.isRekt()) continue;
            count = (count + 1) % 2;
            if (count == 1)
            {
                currentAutoHoriLayout = actorSpace.BeginHoriGroup();
            }

            if (currentAutoHoriLayout != null)
            {
                actor.ShowActorPowerCard(_currentSortPower, currentAutoHoriLayout, _empire, () => ChangeAvatar(actor));
                yield return CoroutineHelper.wait_for_next_frame;
            }
        }
        _listPool.Add(actorSpace.gameObject);
    }


    public void ChangeAvatar(Actor actor)
    {
        if (_office!=null)
        {
            _office.SetActor(actor);
        }
        else if (_kingdom!=null)
        {
            _kingdom.setKing(actor);
            actor.joinCity(_kingdom.capital);
            actor.joinKingdom(_kingdom);
        }
        ScrollWindow.getCurrentWindow().clickBack();
    }
}

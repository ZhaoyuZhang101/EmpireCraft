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
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;
public class ChangeUnitWindow : AutoLayoutWindow<ChangeUnitWindow>
{
    AutoGridLayoutGroup autoGridLayoutGroup;
    Empire _empire;
    string _key;
    ModObject _modObject;
    SimpleText title;
    ListPool<GameObject> _listPool;
    TextInput textInput;
    protected override void Init()
    {
        layout.spacing = 30;
        layout.padding = new RectOffset(0,0,0,0);
        _listPool = new ListPool<GameObject>();
        title = Instantiate(SimpleText.Prefab);
        AddChild(title.gameObject);
        textInput = GameObject.Instantiate(TextInput.Prefab);
        textInput.Setup(LM.Get("input_name_for_unit"), StarSearchUnit);
        textInput.SetSize(new Vector2(150, 18));
        AddChild(textInput.gameObject);
        autoGridLayoutGroup = this.BeginGridGroup(5, UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount, pSpacing: new Vector2(30, 30));
        AddChild(autoGridLayoutGroup.gameObject);
    }

    public void StarSearchUnit(string content)
    {
        Clear();
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
            StartCoroutine(ShowUnitGroup(actorsPool));
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
                    string provinceName = actor.city.hasProvince() ? actor.city.GetProvince()?.name : "";
                    string officer = actor.isOfficer() ? "officer" + LM.Get("actor_officer") : "";
                    string name = "";
                    int age = -1;
                    string educationLevel;
                    OfficeIdentity identity = actor.GetIdentity(ConfigData.CURRENT_SELECTED_EMPIRE);
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
                        merit, honoraryOfficial, officialLevel, PeeragesLevel, name, age.ToString(), educationLevel, kingdomName, cityName, provinceName, officer
                    };
                    bool isSatisfied = searchContent.ToList().Any(t =>t.Contains(content))||(int.TryParse(content, out int num)?num>=age:false);
                    if (isSatisfied) actorsPool.Add(actor);
                }
            }
        }
        StartCoroutine(ShowUnitGroup(actorsPool));
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
        _empire = ConfigData.CURRENT_SELECTED_EMPIRE;
        _key = ConfigData.CURRENT_SELECTED_OFFICE;
        _modObject = ConfigData.CurrentSelectedModObject;
        string titleText = LM.Get(_key);
        if (_key == "")
        {
            titleText = _modObject.data.name;
        }
        title.Setup(titleText, pAlignment:TextAnchor.MiddleCenter);
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
        StartCoroutine(ShowUnitGroup(listActor));
    }

    public IEnumerator ShowUnitGroup(List<Actor> actors)
    {
        foreach (Actor actor in actors) 
        {
            ShowSingleUnit(actor);
            yield return CoroutineHelper.wait_for_next_frame;
        }
    }

    public void ShowSingleUnit(Actor actor)
    {
        AutoVertLayoutGroup vertLayoutGroup = this.BeginVertGroup(pSize: new Vector2(50, 80), pPadding: new RectOffset(0, 0, 25, 0));
        SimpleButton avatar = UIHelper.CreateAvatarView(actor.getID());
        SimpleText text = Instantiate(SimpleText.Prefab);
        string identityText = "";
        if (actor.hasTrait("jingshi"))
        {
            identityText += LM.Get("trait_jingshi");
        }
        else if (actor.hasTrait("gongshi"))
        {
            identityText += LM.Get("trait_gongshi");
        }
        text.Setup(identityText + "\u200A" + actor.getName(), pSize: new Vector2(25, 10));
        SimpleButton add = GameObject.Instantiate(SimpleButton.Prefab);
        add.Setup(() => ChangeAvatar(actor), SpriteTextureLoader.getSprite("ui/setOfficer"), pSize: new Vector2(15, 12));
        vertLayoutGroup.AddChild(text.gameObject);
        vertLayoutGroup.AddChild(avatar.gameObject);
        vertLayoutGroup.AddChild(add.gameObject);
        _listPool.Add(vertLayoutGroup.gameObject);
        autoGridLayoutGroup.AddChild(vertLayoutGroup.gameObject);
    }

    public void ChangeAvatar(Actor actor)
    {
        if (_key!="")
        {
            if (_empire.data.centerOffice.General.name == _key)
            {
                _empire.SetOfficer(_empire.data.centerOffice.General, actor);
            }
            if (_empire.data.centerOffice.GreaterGeneral.name == _key)
            {
                _empire.SetOfficer(_empire.data.centerOffice.GreaterGeneral, actor);
            }
            if (_empire.data.centerOffice.Minister.name == _key)
            {
                _empire.SetOfficer(_empire.data.centerOffice.Minister, actor);
            }
            foreach (var pairs in _empire.data.centerOffice.CoreOffices)
            {
                if (_key == pairs.Key)
                {
                    _empire.SetOfficer(pairs.Value, actor);
                }
            }
            foreach (var pairs in _empire.data.centerOffice.Divisions)
            {
                if (_key == pairs.Key)
                {
                    _empire.SetOfficer(pairs.Value, actor);
                }
            }
        }
        else if (_modObject!=null)
        {
            _modObject.SetOfficer(actor);
        }
        ScrollWindow.getCurrentWindow().clickBack();
    }
}

using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;

namespace EmpireCraft.Scripts.UI.Windows;
public class ChangeUnitWindow : AutoLayoutWindow<ChangeUnitWindow>
{
    AutoGridLayoutGroup autoGridLayoutGroup;
    Empire _empire;
    string _key;
    Province _province;
    SimpleText title;
    ListPool<GameObject> _listPool;
    protected override void Init()
    {
        layout.spacing = 30;
        layout.padding = new RectOffset(0,0,0,0);
        _listPool = new ListPool<GameObject>();
        title = Instantiate(SimpleText.Prefab);
        AddChild(title.gameObject);
        autoGridLayoutGroup = this.BeginGridGroup(5, UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount, pSpacing: new Vector2(30, 30));
        AddChild(autoGridLayoutGroup.gameObject);
    }

    public void Clear()
    {
        float delay = 0.02f;
        foreach (GameObject go in _listPool)
        {
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
        _province = ConfigData.CURRENT_SELECTED_PROVINCE;
        string titleText = LM.Get(_key);
        if (_key == "")
        {
            titleText = _province.data.name;
        }
        title.Setup(titleText, pAlignment:TextAnchor.MiddleCenter);
        foreach (Kingdom kingdom in _empire.kingdoms_list)
        {
            foreach(Actor actor in kingdom.units)
            {
                if (actor.isUnitFitToRule() && !actor.isKing() && (actor.hasTrait("jingshi") || actor.hasTrait("gongshi")))
                {
                    AutoVertLayoutGroup vertLayoutGroup = this.BeginVertGroup(pSize:new Vector2(50, 80), pPadding:new RectOffset(0,0,25,0));
                    SimpleButton avatar = UIHelper.CreateAvatarView(actor.getID());
                    SimpleText text = Instantiate(SimpleText.Prefab);
                    string identityText = "";
                    if (actor.hasTrait("jingshi"))
                    {
                        identityText += LM.Get("trait_jingshi");
                    } else if (actor.hasTrait("gongshi"))
                    {
                        identityText += LM.Get("trait_gongshi");
                    }
                    text.Setup(identityText+" "+actor.getName(), pSize: new Vector2(25, 10));
                    SimpleButton add = GameObject.Instantiate(SimpleButton.Prefab);
                    add.Setup(() => ChangeAvatar(actor), SpriteTextureLoader.getSprite("ui/setOfficer"), pSize:new Vector2(10, 8));
                    vertLayoutGroup.AddChild(text.gameObject);
                    vertLayoutGroup.AddChild(avatar.gameObject);
                    vertLayoutGroup.AddChild(add.gameObject);
                    _listPool.Add(vertLayoutGroup.gameObject);
                    autoGridLayoutGroup.AddChild(vertLayoutGroup.gameObject);
                }
            }
        }
    }

    public void ChangeAvatar(Actor actor)
    {
        if (_key!="")
        {
            if (_empire.data.centerOffice.General.name == _key)
            {
                _empire.data.centerOffice.General.SetActor(actor);
            }
            if (_empire.data.centerOffice.GreaterGeneral.name == _key)
            {
                _empire.data.centerOffice.GreaterGeneral.SetActor(actor);
            }
            if (_empire.data.centerOffice.Minister.name == _key)
            {
                _empire.data.centerOffice.Minister.SetActor(actor);
            }
            foreach (var pairs in _empire.data.centerOffice.CoreOffices)
            {
                if (_key == pairs.Key)
                {
                    pairs.Value.SetActor(actor);
                }
            }
            foreach (var pairs in _empire.data.centerOffice.Divisions)
            {
                if (_key == pairs.Key)
                {
                    pairs.Value.SetActor(actor);
                }
            }
        }
        else if (_province!=null)
        {
            _province.SetOfficer(actor);
        }
        ScrollWindow.getCurrentWindow().clickBack();
    }
}

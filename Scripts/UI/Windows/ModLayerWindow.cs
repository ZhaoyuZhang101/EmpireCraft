using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.api.attributes;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;
public class ModLayerWindow : AutoLayoutWindow<ModLayerWindow>
{
    ModLayer _pModLayer;
    private readonly List<GameObject> _groups = new ();
    private TextInput _input;
    protected override void Init()
    {
        this.layout.spacing = 3;
        this.layout.padding = new RectOffset(3, 3, 20, 3);
        _input = Instantiate(TextInput.Prefab, this.transform.parent.transform.parent);
        _input.Setup("", ChangeClanName);
    }
    
    public void ChangeClanName(string text)
    {
        this._pModLayer.data.name = text;
        LogService.LogInfo($"改变了 {nameof(ModLayer)} 的名字");
    }
    
    [Hotfixable]
    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        _pModLayer = ConfigData.CurrentSelectedModLayer;
        refreshAll();
    }
    //窗口顶层结构
    public void showTopPart()
    {
        InitialTextInput();
    }
    //窗口内容
    public void ShowContent()
    {
        // todo: 自定义窗口内容
        var contentSpace = this.BeginVertGroup(pSpacing:3);
        // 添加标题
        contentSpace.AddTextIntoVertLayout("大标题");
        
        var gridContent = this.BeginGridGroup(2);
        //显示角色小型信息模板
        ShowPersonalInfo(gridContent.transform, _pModLayer.CoreCity.leader);
        ShowPersonalInfo(gridContent.transform, _pModLayer.CoreCity.leader);
        ShowPersonalInfo(gridContent.transform, _pModLayer.CoreCity.leader);
        ShowPersonalInfo(gridContent.transform, _pModLayer.CoreCity.leader);
        ShowPersonalInfo(gridContent.transform, _pModLayer.CoreCity.leader);
        ShowPersonalInfo(gridContent.transform, _pModLayer.CoreCity.leader);
        ShowPersonalInfo(gridContent.transform, _pModLayer.CoreCity.leader);
        
        contentSpace.AddChild(gridContent.gameObject);
        
        _groups.Add(contentSpace.gameObject);
    }
    public void refreshAll()
    {
        Clear();
        showTopPart();
        ShowContent();
    }
    
    //初始化顶部输入框
    public void InitialTextInput()
    {
        string text = _pModLayer.name;
        UIHelper.GenerateTextInput(this.transform.parent.transform.parent, offset:new Vector2(0, 152), default_text:text, input:_input);
    }
    
    public void Clear()
    {
        foreach (var item in _groups)
        {
            Destroy(item);
        }
        _groups.Clear();
    }
    
    //角色信息栏小型通用模板
    public void ShowPersonalInfo(Transform parent, Actor actor)
    {
        AutoHoriLayoutGroup personalGroup = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);

        //右边头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(new Vector2(30, 30), pSpacing:12, pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
        
        SimpleButton clickframe = UIHelper.CreateAvatarView(actor.getID());
        avatarLayoutGroup.AddChild(clickframe.gameObject);
        avatarLayoutGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(avatarLayoutGroup.gameObject);
        
        //左边信息栏 todo: 填写角色基本信息，可自行扩展
        AutoVertLayoutGroup leftVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        //在任何文字后面加上.ColorString的方法可改版文字颜色
        leftVertGroup.AddTextIntoVertLayout("示例1".ColorString(pColor:new Color(0.4f,0.7f,0)));
        leftVertGroup.AddTextIntoVertLayout("示例2".ColorString(pColor:new Color(1,0.6f,0)));
        leftVertGroup.AddButtonIntoVertLayout("示例按钮", action:()=>SampleClickAction(actor));
        
        leftVertGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(leftVertGroup.gameObject);
        //添加背景 todo: 可修改背景
        personalGroup.transform.AddStretchBackground(SpriteTextureLoader.getSprite("ui/clanFrame"), size: new Vector2(100, 30));
        
        personalGroup.transform.SetParent(parent.transform);
        
        _groups.Add(personalGroup.gameObject);
    }

    private void SampleClickAction(Actor pActor)
    {
        //todo: 示例按钮点击事件
        LogService.LogInfo("点击了: "+pActor.name);
    }
}

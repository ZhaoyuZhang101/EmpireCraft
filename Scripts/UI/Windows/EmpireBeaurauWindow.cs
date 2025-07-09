using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.api;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using NCMS.Extensions;
using EpPathFinding.cs;
using System.Drawing.Printing;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine.Events;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.UI.Windows;
public class EmpireBeaurauWindow : AbstractWideWindow<EmpireBeaurauWindow>
{
    public Office empireOffice;
    public Transform ScrollView;

    public Empire _empire;

    [Header("UI Prefab & 根容器")]
    public GameObject _itemPrefab;
    protected override void Init()
    {
        //BackgroundTransform.AddComponent<RectMask2D>();
        //RectTransform division_panel = UIHelper.CreateUIPanel("Division_Panel", BackgroundTransform);
        //division_panel.AddComponent<DragContent>();
        //var builder = new TreeLayoutBuilder(division_panel, SimpleButton.Prefab);
        //empireOffice = BeaurauSystem.empireOffice;
        //builder.Build(empireOffice);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        _empire = ConfigData.CURRENT_SELECTED_EMPIRE;
    }
}


public class TreeLayoutBuilder
{
    private float _hSpacing = 20f;
    private float _vSpacing = 60f;
    private float _margin = 20f;
    private RectTransform _scroll;
    private SimpleButton _itemPrefab;
    private EmpireBeaurauWindow _window;

    public TreeLayoutBuilder(RectTransform scroll, SimpleButton itemPrefab)
    {
        _scroll = scroll;
        _itemPrefab = itemPrefab;
    }

    public void Build(Office root)
    {
        ComputeWidth(root);

        float nextLeafX = _margin;
        AssignPositions(root, ref nextLeafX);

        SpawnNodes(root);
    }

    private float ComputeWidth(Office node)
    {
        if (node.Children.Count == 0)
        {
            node.subtreeWidth = 1;
        }
        else
        {
            float sum = 0;
            foreach (var c in node.Children) 
            {
                if (c.Index==node.Index+1)
                {
                    sum += ComputeWidth(c);
                }
             };
            node.subtreeWidth = sum;
        }
        return node.subtreeWidth;
    }


    private void AssignPositions(Office node, ref float nextLeafX)
    {
        if (node.Children.Count == 0)
        {
            node.position.x = nextLeafX;
            nextLeafX += _hSpacing;
        }
        else
        {
            float leftBoundary = nextLeafX;
            foreach (var c in node.Children)
            {
                if (c.Index == node.Index+1)
                {
                    AssignPositions(c, ref nextLeafX);
                }else
                {
                    AssignPositions(c, ref _margin);
                }
            }
            float rightBoundary = nextLeafX - _hSpacing;
            node.position.x = (leftBoundary + rightBoundary) * 0.5f;
        }

        node.position.y = -node.Index * _vSpacing - 10f;
    }

    private void SpawnNodes(Office root)
    {
        var all = new List<Office>();
        Flatten(root, all);

        int maxDepth = 0;
        foreach (var o in all)
        {
            //var go = GameObject.Instantiate(_itemPrefab, _scroll);
            //go.Setup(SetPerson, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"), String.Join("\n", o.Name.ToArray()), new Vector2(15, 30));
            RectTransform group = Setup(SetPerson, o, new Vector2(35, 45), o.Name);
            var rt = group.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = o.position;
            maxDepth = Mathf.Max(o.Index, Mathf.RoundToInt(-o.position.y / _vSpacing));
            o.Transform = group.transform;
        }
        //float neededWidth = root.subtreeWidth * _hSpacing + _margin * 2f;
        //float contentHeight = maxDepth * _vSpacing + _margin;
        //_scroll.sizeDelta = new Vector2(neededWidth, contentHeight);
    }

    private void Flatten(Office node, List<Office> outList)
    {
        outList.Add(node);
        foreach (var c in node.Children)
            Flatten(c, outList);
    }


    private void SetPerson()
    {
        ScrollWindow._current_window.clickBack();
    }

    public RectTransform Setup(UnityAction action, Office office, Vector2 size, string name, Actor person = null)
    {
        RectTransform autoVertLayoutGroup = UIHelper.CreateVerticalLayoutContainer(name, _scroll, size);

        SimpleButton simpleButton = GameObject.Instantiate(SimpleButton.Prefab, autoVertLayoutGroup);
        simpleButton.Setup(action, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"), String.Join("\n", office.Name.ToArray()), new Vector2(15, 30));


        SimpleButton Officer = GameObject.Instantiate(SimpleButton.Prefab, autoVertLayoutGroup);
        Officer.Setup(action, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"), person==null?"无":person.getName(), new Vector2(20, 15));

        //UnitAvatarLoader avatorPrefab = (UnitAvatarLoader)Resources.Load("ui/AvatarLoaderFramed", typeof(UnitAvatarLoader));
        //GameObject avatorFramed = GameObject.Instantiate(avatorPrefab.gameObject, autoVertLayoutGroup);
        //UnitAvatarLoader unitAvatarLoader = avatorFramed.GetComponent<UnitAvatarLoader>();
        //if (person != null)
        //{
        //    unitAvatarLoader.load(person);
        //}

        return autoVertLayoutGroup;
    }
}
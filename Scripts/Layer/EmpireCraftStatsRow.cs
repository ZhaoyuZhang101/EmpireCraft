using EmpireCraft.Scripts.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace EmpireCraft.Scripts.Layer;
public class EmpireCraftStatsRow: StatsRowsContainer
{
    public new void Awake()
    {
        KeyValueField pPrefab = Resources.Load<KeyValueField>("ui/KeyValueFieldStats");
        _stats_pool = new ObjectPoolGenericMono<KeyValueField>(pPrefab, base.transform);
    }

    public new void OnEnable()
    {
    }

    public void tryToShowActor(string pTitle, long pID = -1L, string pName = null, Actor pObject = null, string pIconPath = null)
    {
        Actor actor = ((pObject != null) ? pObject : World.world.units.get(pID));
        if (!actor.isRekt())
        {
            showStatsMetaUnit(actor, pTitle, pIconPath);
        }
        else
        {
            showEmptyStatRow(pTitle, pName, pIconPath);
        }
    }

    public void showStatsMetaUnit(Actor pActor, string pTitle, string pIconPath = null)
    {
        string pValue = "???";
        string text = pActor?.kingdom.getColor().color_text;
        if (pActor != null)
        {
            pValue = pActor.getName();
            pValue += Toolbox.coloredGreyPart(pActor.getAge(), text, pUnit: true);
        }
        showStatRowMeta(pTitle, pValue, text, MetaType.Unit, pActor?.getID() ?? (-1), pColorText: false, pIconPath);
    }

    public KeyValueField IShowStatsRow(string pId, object pValue, string pColor, MetaType pMetaType = MetaType.None, long pMetaId = -1L, bool pColorText = false, string pIconPath = null, string pTooltipId = null, TooltipDataGetter pTooltipData = null, bool pLocalize = true, UnityAction action=null)
    {
        KeyValueField field = showStatRow(pId, pValue, pColor, pMetaType, pMetaId, pColorText, pIconPath, pTooltipId, pTooltipData, pLocalize);
        field.on_click_value = action;
        return field;
    }

    public void showStatRowMeta(string pId, object pValue, string pColor, MetaType pMetaType, long pMetaId, bool pColorText = false, string pIconPath = null, string pTooltipId = null, TooltipDataGetter pTooltipData = null, bool pLocalize = true)
    {
        showStatRow(pId, pValue, pColor, pMetaType, pMetaId, pColorText: true, pIconPath, pTooltipId, pTooltipData, pLocalize);
    }

    public void showEmptyStatRow(string pTitle, string pContent = null, string pIconPath = null)
    {
        if (string.IsNullOrEmpty(pContent))
        {
            pContent = "???";
        }
        if (pContent == "neutral")
        {
            pContent = "???";
        }
        showStatRow(pTitle, pContent, ColorStyleLibrary.m.color_dead_text, MetaType.None, -1L, pColorText: false, pIconPath);
    }
}

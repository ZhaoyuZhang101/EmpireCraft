using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftTooltipLibrary
{
    public static void init()
    {
        TooltipLibrary tl = AssetManager.tooltips;
        tl.add(new TooltipAsset
        {
            id = "ModLayer",
            prefab_id = "tooltips/tooltip_city",
            callback = ShowModLayerToolTip
        });
    }
    public static void ShowModLayerToolTip(Tooltip pTooltip, string pType, TooltipData pData)
    {
        pTooltip.clear();
        City city = pData.city;
        ModLayer pModLayer = city.GetModLayer();
        pTooltip.setDescription(LM.Get("ModLayer_description"), null);
        string tColorHex = pModLayer.getColor().color_text;
        pTooltip.setTitle(pModLayer.data.name, "ModLayerWindowTitle", tColorHex);
        
        pTooltip.addLineText("ModLayer", "示例1", "#CC6CE7", false, true, 21);
        pTooltip.addLineText("ModLayer", "示例2", "#CC6CE7", false, true, 21);
        pTooltip.addLineText("ModLayer", "示例3", "#CC6CE7", false, true, 21);
        pTooltip.addLineText("ModLayer", "示例4", "#CC6CE7", false, true, 21);
    }
}

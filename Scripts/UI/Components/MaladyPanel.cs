using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Components;

// 帝国主页的"朝政积弊"卡(见 MaladySystem)：六条积弊各一行——轻重(绿/黄/红)与眼下的后果；都还轻时只写一句朝政清明
public static class MaladyPanel
{
    public static void Render(AutoVertLayoutGroup parent, Empire empire, float width)
    {
        if (!MaladySystem.Applies(empire)) return;
        float average = MaladySystem.Average(empire);
        var card = parent.BeginCard(width, "malady_title", Hex(average), string.Format(LM.Get("malady_header"), average.ToString("0")));
        if (MaladySystem.All.All(key => MaladySystem.Get(empire, key) < 5f))
        {
            card.AddListRow(width, 0, null, LM.Get("malady_none"), "", titleWidth: 220f);
            return;
        }
        int index = 0;
        foreach (string key in MaladySystem.All)
        {
            float value = MaladySystem.Get(empire, key);
            string hex = Hex(value);
            card.AddListRow(width, index++, null, LM.Get("malady_" + key),
                $"<color={hex}>{value:0}</color>  {MaladySystem.EffectText(empire, key)}", titleHex: hex, titleWidth: 80f);
        }
    }

    private static string Hex(float value) => value >= 60f ? "#E8584F" : value >= 30f ? "#E8C35A" : "#9BD27A";
}

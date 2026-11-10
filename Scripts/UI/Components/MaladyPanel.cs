using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine.Events;

namespace EmpireCraft.Scripts.UI.Components;

// 帝国主页的"朝政弊病"卡(见 MaladySystem)：六条弊病各一行——轻重(绿/黄/红)与眼下的后果；都还轻时只写一句朝政清明。
// 头一行是王朝积弊(周期压力，见 DynasticCycleSystem)：两样以前都叫"积弊"，卡上写"积弊未生"、上面又是"王朝积弊 11.8"，
// 玩家看着对不上(用户 2026-10-27)——六弊明君能治、能降到零，王朝积弊随国祚只增不减、只能延缓。
// 王朝积弊下面一排按钮直接定积弊(归零、−10、+10、拉满，用户 2026-11-10，见 DynasticCycleSystem.SetPressure)，refresh 重绘本页
public static class MaladyPanel
{
    private const float StrainStep = 10f;

    public static void Render(AutoVertLayoutGroup parent, Empire empire, float width, UnityAction refresh = null)
    {
        if (!MaladySystem.Applies(empire)) return;
        float average = MaladySystem.Average(empire);
        var card = parent.BeginCard(width, "malady_title", Hex(average), string.Format(LM.Get("malady_header"), average.ToString("0")));
        float strain = DynasticCycleSystem.GetPressure(empire);
        string strainHex = Hex(strain);
        card.AddListRow(width, 0, null, LM.Get("label_dynastic_strain"),
            $"<color={strainHex}>{strain:0.0}</color>/100  {LM.Get("malady_strain_note")}", titleHex: strainHex, titleWidth: 80f);
        if (DynasticCycleSystem.Applies(empire))
        {
            void Set(float value)
            {
                DynasticCycleSystem.SetPressure(empire, value);
                refresh?.Invoke();
            }
            card.AddActionRow(width - 12f,
                (LM.Get("malady_strain_zero"), "#9BD27A", strain > 0f, () => Set(0f)),
                (LM.Get("malady_strain_down"), "#9BD27A", strain > 0f, () => Set(strain - StrainStep)),
                (LM.Get("malady_strain_up"), "#E8C35A", strain < 100f, () => Set(strain + StrainStep)),
                (LM.Get("malady_strain_full"), "#E8584F", strain < 100f, () => Set(100f)));
        }
        float imbalance = FactionBalanceSystem.Imbalance(empire);
        if (MaladySystem.All.All(key => MaladySystem.Get(empire, key) < 5f) && imbalance < 5f)
        {
            card.AddListRow(width, 1, null, LM.Get("malady_none"), "", titleWidth: 220f);
            return;
        }
        int index = 1;
        // 派系失衡：哪一派独大、走向哪种乱局(见 FactionBalanceSystem)
        if (imbalance >= 5f)
        {
            string hex = Hex(imbalance);
            FixedFaction dominant = FactionBalanceSystem.Dominant(empire);
            FactionBalanceSystem.Direction direction = FactionBalanceSystem.Current(empire);
            string detail = direction == FactionBalanceSystem.Direction.None || dominant == null
                ? LM.Get("faction_imbalance_rising")
                : string.Format(LM.Get("faction_imbalance_detail"), dominant.Name, FactionBalanceSystem.DirectionName(direction));
            card.AddListRow(width, index++, null, LM.Get("faction_imbalance_title"), $"<color={hex}>{imbalance:0}</color>  {detail}",
                titleHex: hex, titleWidth: 80f);
        }
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

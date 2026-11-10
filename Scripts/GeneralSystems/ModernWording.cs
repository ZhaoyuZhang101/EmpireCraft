using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 近代政权的措辞(用户 2026-11-10"这个现代政权了还用朝这种古代词汇很奇怪""全面清查一遍现代政权会用到的文本")：
// 现代政体、共和国看到的派系诉求流程不再满纸奏议、上奏、廷议、批红、圣眷、朝廷、皇帝——同一文本键另有 "<键>_modern" 译文的照它
// (议案、提案、审议、元首裁决、信任、中央、元首)，规则与数目不变。用在诉求天平页、朝局面板与朝局纪事(见 CourtSystem.Log)
public static class ModernWording
{
    public static bool Applies(Empire empire)
    {
        if (empire?.CoreKingdom?.data == null || empire.isRekt()) return false;
        return RepublicSystem.IsRepublic(empire) || empire.CoreKingdom.GetRegime()?.type == RegimeType.Modern;
    }

    public static string Key(bool modern, string key) => modern && !string.IsNullOrEmpty(key) && LM.Has(key + "_modern") ? key + "_modern" : key;

    public static string Key(Empire empire, string key) => Key(Applies(empire), key);

    public static string Get(Empire empire, string key) => LM.Get(Key(empire, key));
}

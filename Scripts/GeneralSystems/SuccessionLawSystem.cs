using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GeneralSystems;

// 继承法解锁判定:嫡长子/幼子/兄终弟及三法背后的逻辑本就已经存在(内部级联早就支持),不需要
// 研究即可选;分割继承法/强者继承法要通过文化科技树的 unlock_succession_law 效果解锁,解锁
// 状态记在 CultureInstitutionState(按文化,不按帝国/政权),原因跟 Institution 系统里其它持久
// 状态一样——政体这层的 Regime.options 每次读档都会被模板重新 Clone,不适合当解锁的持久存储。
public static class SuccessionLawSystem
{
    private static readonly HashSet<SuccessionLawType> DefaultUnlocked = new()
    {
        SuccessionLawType.嫡长子继承法,
        SuccessionLawType.幼子继承法,
        SuccessionLawType.兄终弟及,
    };

    // 每种继承法"天然"更受哪个派系认同,供派系诉求(TempFac_强者继承法/TempFac_转世袭等)与
    // 政治压力计算复用,避免各自维护一份重复的映射。
    public static readonly Dictionary<SuccessionLawType, FactionType> SuccessionLawFactionAffinity = new()
    {
        { SuccessionLawType.嫡长子继承法, FactionType.血脉 }, //血脉派:国王为单一血脉,天然认同嫡长子继承
        { SuccessionLawType.强者继承法, FactionType.僭主 }, //僭主派:霸者为王,天然认同强者继承
    };

    public static bool IsSuccessionLawUnlocked(string culture, SuccessionLawType law)
    {
        if (DefaultUnlocked.Contains(law)) return true;
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(culture);
        return state != null && state.unlocked_succession_laws.Contains(law);
    }

    public static bool IsSuccessionLawUnlocked(Kingdom kingdom, SuccessionLawType law) =>
        IsSuccessionLawUnlocked(CultureService.GetRealmCulture(kingdom), law);
}

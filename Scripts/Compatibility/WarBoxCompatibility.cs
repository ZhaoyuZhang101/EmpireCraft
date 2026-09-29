using System;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Compatibility;

// WarBox 的外交与战争系统跟本模组的帝国/封建体系对接。
//
// WarBox 只认识"平等的王国"：两个王国之间可以签互不侵犯条约、贸易伙伴、和约，打完仗要赔款。
// 本模组里王国之间还有上下级：同一帝国的成员(含朝贡国)、封建宗主与附庸。对接的原则：
//   · 同一帝国 / 宗主与附庸之间不签 WarBox 条约，旧条约也不拦宣战——讨伐不臣、附庸独立是帝国内政；
//   · 本模组发起的战争(伐不臣、叛乱、独立、索取法理……)由本模组决定何时结束和怎样收场，
//     WarBox 的"目标达成/投降/和谈计划"不去提前结束它们；
//   · 内战不走 WarBox 的赔款与和约(叛军战败赔款给自己的皇帝没有意义)，本模组拒绝结束的战争也不结算战利品；
//   · 夺回本就属于自己法理的城市、帝国内部城市易手，不算"占领"；
//   · 帝国成员的王权合法性以本模组的正统值为基础。
//
// 全部按类型名反射查找，没装 WarBox 就什么都不做。
public static class WarBoxCompatibility
{
    private static bool _applied;

    public static void EnsureApplied(Harmony harmony)
    {
        if (_applied || harmony == null) return;
        _applied = true;
        if (AccessTools.TypeByName("WarBox.Content.DiplomacyRelations") == null) return; // 没装 WarBox

        TryPatch(harmony, "WarBox.Content.DiplomacyRelations", "Set", nameof(BeforeSetRelation));
        TryPatch(harmony, "WarBox.Content.Patch_Diplomacy_PactBlocksWar", "Prefix", nameof(BeforePactBlock));
        TryPatch(harmony, "WarMobilization.WarSpoilsSystem", "HasTreaty", null, nameof(AfterHasTreaty));
        TryPatch(harmony, "WarMobilization.WarSpoilsSystem", "OnWarEnded", nameof(BeforeWarSpoils));
        TryPatch(harmony, "WarMobilization.PeacePlanSystem", "RequestPeace", nameof(BeforeRequestPeace));
        TryPatch(harmony, "WarBox.Content.OccupationSystem", "OnConquered", nameof(BeforeOccupation));
        TryPatch(harmony, "WarBox.Content.PoliticalSystem", "Calculate", null, nameof(AfterLegitimacy));
    }

    private static void TryPatch(Harmony harmony, string typeName, string methodName, string prefix,
        string postfix = null)
    {
        try
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo method = type == null
                ? null
                : AccessTools.GetDeclaredMethods(type).FirstOrDefault(m => m.Name == methodName);
            if (method == null)
            {
                LogService.LogWarning($"[EmpireCraft] WarBox 兼容：找不到 {typeName}.{methodName}，跳过");
                return;
            }
            harmony.Patch(method,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(WarBoxCompatibility), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(WarBoxCompatibility), postfix));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] WarBox 兼容补丁失败 {typeName}.{methodName}: {exception.Message}");
        }
    }

    #region 关系判断

    // 同一帝国(含朝贡国) / 封建宗主链上的上下级
    public static bool AreBound(Kingdom a, Kingdom b)
    {
        if (a == null || b == null || a == b || a.isRekt() || b.isRekt()) return false;
        try
        {
            return a.IsInSameEmpire(b) || FeudalVassalService.IsInChain(a, b) || FeudalVassalService.IsInChain(b, a);
        }
        catch
        {
            return false;
        }
    }

    // 帝国内政性质的战争：不结算 WarBox 的赔款与和约
    private static bool IsInternalWar(EmpireWarType type) => type is EmpireWarType.伐不臣 or EmpireWarType.地方独立
        or EmpireWarType.派系叛乱 or EmpireWarType.地方叛乱 or EmpireWarType.民族叛乱 or EmpireWarType.宗教叛乱
        or EmpireWarType.藩王索取皇位 or EmpireWarType.帝国正统 or EmpireWarType.去帝号 or EmpireWarType.清君侧
        or EmpireWarType.不奉诏 or EmpireWarType.附庸独立 or EmpireWarType.获取帝国 or EmpireWarType.统一;

    private static EmpireWarType TypeOf(War war)
    {
        try
        {
            return war == null ? EmpireWarType.None : war.GetEmpireWarType();
        }
        catch
        {
            return EmpireWarType.None;
        }
    }

    #endregion

    #region 补丁

    // 上下级之间不签 WarBox 条约(互不侵犯/贸易/敌对)
    public static bool BeforeSetRelation(Kingdom a, Kingdom b, int type) => type == 0 || !AreBound(a, b);

    // 上下级之间即使留着旧条约，也不拦宣战
    public static bool BeforePactBlock(Kingdom pAttacker, Kingdom pDefender, ref bool __result)
    {
        if (!AreBound(pAttacker, pDefender)) return true;
        __result = true;
        return false;
    }

    public static void AfterHasTreaty(Kingdom a, Kingdom b, ref bool __result)
    {
        if (__result && AreBound(a, b)) __result = false;
    }

    public static bool BeforeWarSpoils(War war)
    {
        if (war == null) return true;
        // 本模组的 endWar 补丁拒绝结束(比如索取法理还没拿全)时战争其实还在，不能先把赔款和约签了
        if (war.isAlive() && !war.hasEnded()) return false;
        if (IsInternalWar(TypeOf(war))) return false;
        return !AreBound(war.getMainAttacker(), war.getMainDefender());
    }

    // 本模组发起的战争由本模组决定何时结束
    public static bool BeforeRequestPeace(War war, ref bool __result)
    {
        if (TypeOf(war) == EmpireWarType.None) return true;
        __result = false;
        return false;
    }

    public static bool BeforeOccupation(City city, Kingdom oldKingdom, Kingdom newKingdom)
    {
        if (city == null || oldKingdom == null || newKingdom == null) return true;
        if (AreBound(oldKingdom, newKingdom)) return false;
        try
        {
            KingdomTitle title = city.GetTitle();
            if (title != null && (title.main_kingdom == newKingdom ||
                                  title.owner != null && title.owner == newKingdom.king))
                return false; // 收复法理领地
        }
        catch
        {
            // 取不到法理信息就按 WarBox 原逻辑处理
        }
        return true;
    }

    public static void AfterLegitimacy(Kingdom kingdom, object __result)
    {
        if (__result == null || kingdom == null) return;
        try
        {
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.isRekt()) return;
            // WarBox 的基础值是 25；帝国成员改用正统值：正统 50 对应 25，满正统 50，正统见底 0
            int basis = Mathf.Clamp(Mathf.RoundToInt(empire.Mandate * 0.5f), 0, 50);
            Traverse.Create(__result).Field("Base").SetValue(basis);
        }
        catch
        {
            // 字段改名了就保持 WarBox 原值
        }
    }

    #endregion
}

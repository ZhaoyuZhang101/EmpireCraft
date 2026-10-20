using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 天下之名(方案见 Docs/plan-huaxia-imperial-titles.md，用户 2026-10-20 拍板)：国号跟着"据有什么法理、得了谁的认可"走。
//   · 王国法理名：有主法理即用(现状)；
//   · 帝国法理名：华夏据有某帝国法理三分之二、称帝那一刻起用；西方称帝即用本帝国的帝国法理名；
//   · 天下之名(天下基名)：华夏现代的唯一合法政府用；西方经教宗认可(普世帝国之名)才用；
//   · 例外：华夏复国，方位 + 祖号。
// 只管华夏(制度线 Huaxia)与西方(制度线 Roma)两系，其他文化照旧。
// 本文件放开关与几处公用的查询；各章另在分部文件里。
public static partial class RealmNamingSystem
{
    // 模组设置「天下之名」(四章的改名都归它，见 ConfigActions.RealmNameRulesCallBack)；设置里没有这一项时默认开
    public static bool Enabled = true;
    // 模组设置「多京与直隶」(见 ConfigActions.MultiCapitalCallBack、MultiCapitalSystem)；默认开
    public static bool MultiCapitalEnabled = true;

    // EmpireData.name_source：国号来源
    public const int SourceLegacy = 0, SourceKingdomTitle = 1, SourceEmpireTitle = 2, SourceRestoration = 3, SourceTianxia = 4;

    public const string HuaxiaLine = "Huaxia", WesternLine = "Roma";

    public static string LineOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return "";
        try
        {
            return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) ?? "";
        }
        catch
        {
            return "";
        }
    }

    public static bool IsHuaxia(Kingdom kingdom) => LineOf(kingdom) == HuaxiaLine;
    public static bool IsHuaxia(Empire empire) => empire?.data != null && IsHuaxia(empire.CoreKingdom);

    // 西方：奉不奉教廷照风俗文化(同 PapacySystem.IsWestern)
    public static bool IsWestern(Empire empire) => empire?.data != null && PapacySystem.IsWestern(empire);
    public static bool IsWestern(Kingdom kingdom) => LineOf(kingdom) == WesternLine;

    // 帝国法理名(不带"帝国"之类的后缀)
    public static string CoreName(EmpireCore core) => core == null ? "" : EmpireCoreNamingSystem.GetCoreBaseName(core);

    // 正式的帝国法理(不含军阀临时子核心、争正统的伪核心)
    public static bool IsFormalCore(EmpireCore core) =>
        core != null && core.warlord_parent_core_id <= 0 && core.false_core_against_empire_id <= 0 &&
        core.titlesRecord is { Count: > 0 };

    // 这个帝国法理眼下归哪个现存帝国(没有返回 null)
    public static Empire HolderOf(EmpireCore core, Empire except = null) =>
        core == null ? null : EmpireCoreManager.GetEmpires(core).FirstOrDefault(empire => empire != null && empire != except && !empire.IsArchived());

    // 天下基名：玩家定的基名 → 从天下整名里按本文明的格式拆出来 → 整名不带天下称呼词的就用整名 → 主核心名
    public static string TianxiaBaseName(EmpireCore anyCore)
    {
        EmpireCore lead = TianxiaSystem.Lead(anyCore);
        if (lead == null) return "";
        if (!string.IsNullOrWhiteSpace(lead.tianxia_base_name)) return lead.tianxia_base_name.Trim();
        string full = lead.tianxia_name?.Trim() ?? "";
        if (full.Length > 0)
        {
            string split = RealmNameRules.SplitBase(full, LM.Get(TianxiaTermKey(lead)));
            if (split.Length == 0 && TianxiaTermKey(lead) != "tianxia_default_name")
                split = RealmNameRules.SplitBase(full, LM.Get("tianxia_default_name"));
            if (split.Length > 0) return split;
            string term = LM.Get(TianxiaTermKey(lead)).Replace("{0}", "").Trim();
            if (term.Length == 0 || !full.Contains(term)) return full;
        }
        return CoreName(lead);
    }

    // 天下名的格式键(同 TianxiaSystem.Term 的换词)
    private static string TianxiaTermKey(EmpireCore lead)
    {
        string suffix = TianxiaSystem.TianxiaId(lead) switch
        {
            "Roma" => "_roma",
            "Umma" => "_umma",
            "Steppe" => "_steppe",
            "Tenka" => "_tenka",
            _ => null
        };
        return suffix != null && LM.Has("tianxia_default_name" + suffix) ? "tianxia_default_name" + suffix : "tianxia_default_name";
    }

    // 宗族记的祖上帝国法理：记了编号的直接取；旧档没记的按国号找同名的正式帝国法理，再按旧都城眼下所属的法理找
    public static EmpireCore AncestralCore(SpecificClan clan)
    {
        if (clan == null || !clan.HasHistoryEmpire()) return null;
        EmpireCore recorded = EmpireCoreManager.Get(clan.history_empire_core_id);
        if (recorded != null) return recorded;
        string name = EmpireCoreManager.NormalizeCoreName(clan.empire_name);
        EmpireCore byName = EmpireCoreManager.EmpireCores.Values.FirstOrDefault(core =>
            IsFormalCore(core) && string.Equals(CoreName(core), name, StringComparison.Ordinal));
        if (byName != null) return byName;
        City capital = World.world?.cities?.get(clan.capital_city_id);
        EmpireCore byCity = capital == null || capital.isRekt() ? null : capital.GetEmpireCore();
        return IsFormalCore(byCity) ? byCity : null;
    }

    // 一片地一个帝国法理(用户 2026-10-20 定)：与现存帝国并立时另立的法理(绝嗣裂土的据地称帝、两朝分立)只算伪法理——
    // 不算正式法理(法理归属、天下照旧归原法理)，原朝亡后据有原法理过半即并回(TryLegitimizeFalseCore)。帝国法理名第一次生成时定下，此后不改
    public static void MarkProvisionalCore(Empire claimant, Empire incumbent)
    {
        if (claimant?.data == null || incumbent?.data == null || claimant == incumbent || incumbent.IsArchived()) return;
        EmpireCore own = EmpireCoreManager.Get(claimant);
        if (own == null || own == EmpireCoreManager.Get(incumbent) || own.false_core_against_empire_id > 0) return;
        own.false_core_against_empire_id = incumbent.id;
    }

    // 宗族记下祖上帝国时一并记帝国法理(称帝时帝国还没绑法理，绑完由 EmpireManager.NewEmpire 补记)
    public static void RememberAncestralCore(SpecificClan clan, Empire empire)
    {
        if (clan == null || empire?.data == null) return;
        EmpireCore core = EmpireCoreManager.Get(empire);
        if (core != null) clan.history_empire_core_id = core.id;
    }
}

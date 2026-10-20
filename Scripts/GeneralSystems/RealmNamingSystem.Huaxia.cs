using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 华夏：据有帝国法理三分之二称帝、以帝国法理为号(方案第 1 步)。
//   · 据有：按城数，本国连同附庸(EmpireCoreControl 口径，城归最上层宗主)，帝国法理至少三城，据 ⌈总城×2/3⌉；
//     同时据两个以上时主法理所在的那个优先，其次据城多的；
//   · 称帝：称帝路线「据有法理」(EmpireFormationRoute.Dominion)，走原有称帝剧情；不必国力胜过同文化各帝国；
//   · 绑法理：主法理所在帝国法理空闲先绑它(都城留在本帝国法理)，否则绑据三分之二的那个；那个还被旧朝占着就按僭越称帝
//     (临时法理、互为正统对手)；
//   · 定号：称帝那一刻改用所据帝国法理的名字；与现存帝国同号时加方位(新都相对旧朝都城)，再无同号之国时去掉。
public static partial class RealmNamingSystem
{
    // 这一国据有三分之二的帝国法理(华夏；没有返回 null)
    public static EmpireCore DominionCore(Kingdom kingdom)
    {
        if (!Enabled || kingdom?.data == null || kingdom.isRekt() || !IsHuaxia(kingdom)) return null;
        Kingdom holder = EmpireCoreControl.Sovereign(kingdom);
        if (holder != kingdom) return null;
        EmpireCore main = MainTitleCore(kingdom);
        EmpireCore best = null;
        int bestHeld = 0;
        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values.ToList())
        {
            if (!IsFormalCore(core)) continue;
            (int held, int total) = Holding(core, kingdom);
            if (!RealmNameRules.HoldsShare(held, total)) continue;
            if (core == main) return core;
            if (held <= bestHeld) continue;
            best = core;
            bestHeld = held;
        }
        return best;
    }

    // 本国(连同附庸)据有这个帝国法理多少城、它一共多少城
    public static (int held, int total) Holding(EmpireCore core, Kingdom kingdom)
    {
        if (core == null || kingdom == null) return (0, 0);
        (Dictionary<Kingdom, int> held, int total) = EmpireCoreControl.GetHolders(core);
        return (held.TryGetValue(kingdom, out int count) ? count : 0, total);
    }

    // 主法理(王国法理)所在的正式帝国法理
    public static EmpireCore MainTitleCore(Kingdom kingdom)
    {
        KingdomTitle title = kingdom?.GetMainTitle();
        if (title == null) return null;
        EmpireCore core = EmpireCoreManager.LawfulCoreOf(title) ?? title.title_capital?.GetEmpireCore();
        return IsFormalCore(core) ? core : null;
    }

    // 称帝路线「据有法理」
    public static bool DominionRoute(Kingdom kingdom) => DominionCore(kingdom) != null;

    // 诊断一行(FormationDiagnostics)
    public static string DominionNote(Kingdom kingdom)
    {
        EmpireCore core = DominionCore(kingdom);
        if (core == null) return "";
        (int held, int total) = Holding(core, kingdom);
        return $"，据有 {CoreName(core)} {held}/{total} 城";
    }

    // 称帝绑哪个帝国法理(BecomeEmpireAndStartEnfeoff 调用)；返回要按僭越与之并立的帝国(没有为 null)
    public static Empire PlanDominionBinding(Kingdom kingdom, Empire seatContested, out EmpireCore bind, out bool forceNewCore)
    {
        bind = null;
        forceNewCore = seatContested != null;
        EmpireCore dominion = DominionCore(kingdom);
        if (dominion == null) return seatContested;
        Empire holder = HolderOf(dominion);
        if (holder != null || EmpireCoreManager.IsClaimedByContest(dominion))
        {
            // 那个帝国法理还被旧朝占着：照样称帝，按僭越(临时法理、互为正统对手)
            forceNewCore = true;
            return holder ?? seatContested;
        }
        EmpireCore main = MainTitleCore(kingdom);
        bool mainFree = seatContested == null && main != null && HolderOf(main) == null &&
                        !EmpireCoreManager.IsClaimedByContest(main);
        forceNewCore = false;
        bind = mainFree ? main : dominion;
        return null;
    }

    // 定国号(Empire.SelectFoundingEmpireName 调用；返回 null 照旧规则)。只管称帝剧情来的(route 不为 None)
    public static EmpireFoundingNameChoice SelectFoundingName(Empire empire, Kingdom kingdom, EmpireCore riseCore,
        EmpireFormationRoute route)
    {
        if (!Enabled || empire?.data == null || kingdom == null || route == EmpireFormationRoute.None) return null;
        if (!IsHuaxia(kingdom)) return null;
        EmpireCore dominion = DominionCore(kingdom);
        if (dominion == null) return null;
        string name = CoreName(dominion);
        if (string.IsNullOrWhiteSpace(name)) return null;
        empire.data.directPre = "";
        empire.data.name_source = SourceEmpireTitle;
        empire.data.named_core_id = dominion.id;
        return new EmpireFoundingNameChoice(name, EmpireFoundingNameSource.DominionCore);
    }

    // 新建帝国法理时的名字：国号取自别的帝国法理(据有法理、复国)的，新法理用主法理名，免得两个帝国法理同名
    public static string FreshCoreName(Empire empire)
    {
        if (!Enabled || empire?.data == null ||
            empire.data.name_source != SourceEmpireTitle && empire.data.name_source != SourceRestoration) return null;
        string own = EmpireCoreManager.NormalizeCoreName(empire.GetEmpireName());
        bool Taken(string name) => EmpireCoreManager.EmpireCores.Values.Any(core =>
            core != null && string.Equals(CoreName(core), name, StringComparison.Ordinal));
        if (!Taken(own)) return null;
        string title = EmpireCoreManager.NormalizeCoreName(empire.CoreKingdom?.GetMainTitle()?.data?.name);
        if (!string.IsNullOrWhiteSpace(title) && !Taken(title)) return title;
        string capital = empire.CoreKingdom?.capital?.GetCityName();
        return !string.IsNullOrWhiteSpace(capital) && !Taken(capital) ? capital : null;
    }

    // 帝国绑完法理(EmpireManager.NewEmpire 末尾)：开国之君的宗族补记祖上帝国法理
    public static void OnEmpireBound(Empire empire, Kingdom founder)
    {
        SpecificClanRemember(founder?.king, empire);
    }

    private static void SpecificClanRemember(Actor king, Empire empire)
    {
        var clan = king?.GetSpecificClan();
        if (clan == null || !clan.HasHistoryEmpire()) return;
        if (!string.Equals(clan.empire_name, empire.GetEmpireName(), StringComparison.Ordinal)) return;
        RememberAncestralCore(clan, empire);
    }

    // 称帝之后(称帝剧情落定)：与现存帝国同号时加方位，以新都相对旧朝都城定
    public static void AfterProclaimed(Empire empire)
    {
        if (!Enabled || empire?.data == null || empire.CoreKingdom == null || !IsHuaxia(empire)) return;
        if (empire.data.name_source != SourceEmpireTitle && empire.data.name_source != SourceRestoration) return;
        // 已带方位(复国)、"大"之类前缀的，全名本就与旧朝有别
        if (empire.CoreKingdom.HasCustomCountryNaming() || !string.IsNullOrWhiteSpace(empire.data.directPre)) return;
        Empire namesake = Namesake(empire);
        if (namesake == null) return;
        City mine = empire.CoreKingdom.capital, theirs = namesake.CoreKingdom?.capital;
        if (mine == null || theirs == null) return;
        string prefix = RealmNameRules.Direction4(theirs.city_center.x, theirs.city_center.y,
            mine.city_center.x, mine.city_center.y) ?? RealmNameRules.Later;
        string before = empire.GetEmpireFullName();
        empire.data.directPre = prefix;
        empire.data.rival_name_prefix = prefix;
        empire.SetEmpireName(empire.data.core_name, preserveEmpireType: true);
        EventRecorder.Record(empire, string.Format(LM.Get("realm_rival_prefix_history"), before,
            namesake.GetEmpireFullName(), empire.GetEmpireFullName()));
        LogService.LogInfo($"[EmpireCraft][法理国号] {before} 与 {namesake.GetEmpireFullName()} 同号，改称 {empire.GetEmpireFullName()}");
    }

    // 与本国同号(国号本名相同)的另一个现存帝国
    private static Empire Namesake(Empire empire)
    {
        string name = empire.GetEmpireName();
        if (string.IsNullOrWhiteSpace(name) || ModClass.EMPIRE_MANAGER == null) return null;
        return ModClass.EMPIRE_MANAGER.FirstOrDefault(other => other?.data != null && other != empire && !other.isRekt() &&
                                                               !other.IsArchived() && other.CoreKingdom != null &&
                                                               string.Equals(other.GetEmpireName(), name, StringComparison.Ordinal));
    }

    // 每年(ConstitutionalEconomySystem 帝国年度结算)：再无同号之国时去掉为区别而加的方位
    private static void YearlyHuaxia(Empire empire)
    {
        EmpireData data = empire.data;
        if (string.IsNullOrWhiteSpace(data.rival_name_prefix)) return;
        if (data.directPre != data.rival_name_prefix)
        {
            // 方位已被失都改称、分立之类改过：这一笔作废
            data.rival_name_prefix = "";
            return;
        }
        if (Namesake(empire) != null) return;
        string before = empire.GetEmpireFullName();
        data.rival_name_prefix = "";
        data.directPre = data.founding_prefix ?? "";
        empire.SetEmpireName(data.core_name, preserveEmpireType: true);
        EventRecorder.Record(empire, string.Format(LM.Get("realm_rival_prefix_lifted_history"), before, empire.GetEmpireFullName()));
        LogService.LogInfo($"[EmpireCraft][法理国号] {before} 已无同号之国，复称 {empire.GetEmpireFullName()}");
    }

    // 每年(帝国年度结算，见 ConstitutionalEconomySystem)
    public static void Yearly(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null) return;
        try
        {
            if (Enabled) YearlyHuaxia(empire);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][天下之名] 年度结算失败: {exception.Message}");
        }
    }
}

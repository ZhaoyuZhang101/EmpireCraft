using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
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
// 复国(方案第 2 步)：国君的宗族或同一世系的上支曾以某帝国法理称帝、那一朝已亡、本世系眼下没在别处称帝(不限年数)，
//   称帝那一刻以祖号为号，冠方位——新法理(所据的帝国法理，没有就本帝国要绑的)首都相对祖上帝国法理首都，四方位；
//   新法理就是祖上那个法理、或两个首都重合时称"后"(用户 2026-10-20 选定)。复国方位定下后失都不再改方位(见 CapitalLossNamingSystem)。
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
        if (IsWestern(kingdom)) return SelectWesternName(empire, riseCore);
        if (!IsHuaxia(kingdom)) return null;
        EmpireCore dominion = DominionCore(kingdom);
        EmpireFoundingNameChoice restored = SelectRestorationName(empire, kingdom, dominion, riseCore);
        if (restored != null) return restored;
        if (dominion == null) return null;
        string name = CoreName(dominion);
        if (string.IsNullOrWhiteSpace(name)) return null;
        empire.data.directPre = "";
        empire.data.name_source = SourceEmpireTitle;
        empire.data.named_core_id = dominion.id;
        return new EmpireFoundingNameChoice(name, EmpireFoundingNameSource.DominionCore);
    }

    // 西方(方案第 5 步)：称帝即用本帝国的帝国法理名——要绑的现成帝国法理空闲就用它的名；要新建法理的照旧(主法理名，新法理随之得名)
    private static EmpireFoundingNameChoice SelectWesternName(Empire empire, EmpireCore riseCore)
    {
        if (!IsFormalCore(riseCore) || HolderOf(riseCore) != null || EmpireCoreManager.IsClaimedByContest(riseCore)) return null;
        string name = CoreName(riseCore);
        if (string.IsNullOrWhiteSpace(name)) return null;
        empire.data.name_source = SourceEmpireTitle;
        empire.data.named_core_id = riseCore.id;
        return new EmpireFoundingNameChoice(name, EmpireFoundingNameSource.EmpireCore);
    }

    // 复国：祖号 + 方位(见文件头)
    private static EmpireFoundingNameChoice SelectRestorationName(Empire empire, Kingdom kingdom, EmpireCore dominion,
        EmpireCore riseCore)
    {
        SpecificClan house = RestorationHouse(kingdom.king, empire);
        if (house == null) return null;
        string name = EmpireCoreManager.NormalizeCoreName(house.empire_name);
        if (string.IsNullOrWhiteSpace(name)) return null;
        EmpireCore ancestral = AncestralCore(house);
        EmpireCore newTitle = dominion ?? (IsFormalCore(riseCore) ? riseCore : null);
        City ancestralSeat = ancestral == null ? null : EmpireCoreManager.GetRepresentativeCity(ancestral);
        City newSeat = (newTitle == null ? null : EmpireCoreManager.GetRepresentativeCity(newTitle)) ?? kingdom.capital;
        float ax = ancestralSeat != null ? ancestralSeat.city_center.x : house.capital_city_pos_x;
        float ay = ancestralSeat != null ? ancestralSeat.city_center.y : house.capital_city_pos_y;
        if (newSeat == null) return null;
        bool sameTitle = newTitle != null && ancestral != null && newTitle == ancestral;
        string prefix = RealmNameRules.RestorationPrefix(sameTitle, ax, ay, newSeat.city_center.x, newSeat.city_center.y);
        empire.data.directPre = prefix;
        empire.data.founding_prefix = prefix;
        empire.data.name_source = SourceRestoration;
        empire.data.named_core_id = newTitle?.id ?? -1L;
        // 称帝定号时宗族记的还是祖上那一朝；称帝后宗族改记本朝，史书要的祖上法理名先记下
        PendingAncestral[empire.data.id] = string.IsNullOrWhiteSpace(CoreName(ancestral)) ? name : CoreName(ancestral);
        LogService.LogInfo($"[EmpireCraft][复国国号] {kingdom.GetKingdomName()} 祖号 {name}(祖法理 {CoreName(ancestral)})，" +
                           $"新法理 {CoreName(newTitle)}，方位 {prefix}");
        return new EmpireFoundingNameChoice(name, EmpireFoundingNameSource.RestoredAncestral);
    }

    // 复国之家：国君的宗族，没有帝国记录就沿分家往上找；那一朝须已亡、本世系眼下没在别处称帝
    public static SpecificClan RestorationHouse(Actor king, Empire founding = null)
    {
        SpecificClan clan = king?.GetSpecificClan();
        SpecificClan house = null;
        var visited = new HashSet<long>();
        for (SpecificClan current = clan; current != null && visited.Add(current.id);
             current = current.parent_clan_id >= 0 ? SpecificClanManager.Get(current.parent_clan_id) : null)
        {
            if (!current.HasHistoryEmpire()) continue;
            house = current;
            break;
        }
        if (house == null || ModClass.EMPIRE_MANAGER == null) return null;
        bool reigning = ModClass.EMPIRE_MANAGER.Any(other => other?.data != null && other != founding && !other.isRekt() &&
                                                              !other.IsArchived() &&
                                                              (SpecificClanManager.SameLineage(other.EmpireSpecificClan, house) ||
                                                               SpecificClanManager.SameLineage(other.EmpireSpecificClan, clan)));
        return reigning ? null : house;
    }

    // 失都改称不动复国方位(用户 2026-10-20 选定：复国方位是本朝定号，失都只显示行在)
    public static bool KeepsFoundingPrefix(Empire empire) =>
        Enabled && empire?.data != null && !string.IsNullOrWhiteSpace(empire.data.founding_prefix) &&
        empire.data.directPre == empire.data.founding_prefix;

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
        if (empire.data.name_source == SourceRestoration) RecordRestoration(empire);
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

    private static readonly Dictionary<long, string> PendingAncestral = new();

    private static void RecordRestoration(Empire empire)
    {
        if (!PendingAncestral.TryGetValue(empire.data.id, out string ancestral)) return;
        PendingAncestral.Remove(empire.data.id);
        string text = empire.data.founding_prefix == RealmNameRules.Later
            ? string.Format(LM.Get("realm_restoration_same_history"), empire.Emperor?.getName() ?? "", ancestral, empire.GetEmpireFullName())
            : string.Format(LM.Get("realm_restoration_history"), empire.Emperor?.getName() ?? "", ancestral,
                LM.Get(empire.data.founding_prefix), empire.GetEmpireFullName());
        EventRecorder.Record(empire, text);
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
            if (Enabled) YearlyUniversal(empire);
            YearlyModern(empire);
            MultiCapitalSystem.Yearly(empire);
            SyncCoercionClaim(empire);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][天下之名] 年度结算失败: {exception.Message}");
        }
    }
}

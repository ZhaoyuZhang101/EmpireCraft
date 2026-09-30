using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 军阀时期：现代政体第一次统一某个帝国核心(控制 80% 以上城市)之后，这个核心再分裂出来的政权不是正常国家。
// 配置了 strong_unification 政治特质的文化更严格：进入现代政体后、第一次统一之前就已是军阀时期，
// 统一者才得到"统一"记录和正式国号。华夏默认配置该特质，其他文化也可在 CultureRule.jsonc 中启用。
//
// "正常国家"指组建了政府(帝国层对象；现代不叫称帝，叫组建政府)的政权：
//   · 中央：核心范围内实力最强、已组建政府的政权，以核心名加正式后缀为国号(如中华民国)，是正常国家；
//     强统一文化的中央对同一理念、未组建政府的势力有吸引力，它们会易帜归附成为附庸(AttractSameIdeology)；
//   · 临时政府：与中央理念截然不同(光谱距离 ≥ 90)的势力另立中央、组建政府并向中央开战。
//     强统一文化只有这第二个是"某地临时政府"，第一个中央照旧是正常国家；
//     其他文化对峙时双方都只是临时政府。直到一方被逐出帝国核心；临时政府享有正常国家的全部机制；
//   · 地方武装：其余尚未组建政府的现代势力，不论有无法理都按理念命名(中间派军阀、社会主义人民武装等)。
//     法理只提供日后组建政府的资格，不会把所有地方势力强行显示成军阀。
//   · 再次统一(80%)结束军阀时期；分裂满模组设置里的年数(默认 200，0 = 永远不分家)后承认分治，各国恢复正常国号。
// 每年全图扫描一次，结果缓存成"帝国 → 身份"，国号查询只查缓存。
public static class WarlordEraSystem
{
    private enum Role { Central, Provisional, Warlord }

    private const float UnifyShare = 0.8f;
    private const float OppositeIdeologyDistance = 90f;
    private const float CoalitionStrengthShare = 0.6f;
    private const float SoloRivalShare = 0.5f;
    private const float CoalitionChance = 0.2f;
    // 同理念势力每年易帜归附的最高概率(中央相对实力越强越接近这个值)，调低以免中央不战而统一
    private const float AttractionChance = 0.1f;

    private static readonly Dictionary<long, (EmpireCore core, Role role)> Roles = new();
    private static object _world;
    private static double _lastScan = -1d;

    public const string StrongUnificationTrait = "strong_unification";

    public static bool HasStrongUnification(string culture) =>
        culture.HasPoliticalTrait(StrongUnificationTrait);

    #region 国号

    // 该政府眼下是否为军阀时期的临时政府(与 DecorateName 的判定一致)
    public static bool IsProvisionalGovernment(Empire empire)
    {
        if (empire?.data == null || empire.CoreKingdom == null) return false;
        if (ReferenceEquals(_world, World.world) && Roles.TryGetValue(empire.id, out var entry))
            return entry.role == Role.Provisional;
        EmpireCore ownCore = EmpireCoreManager.Get(empire);
        return ownCore?.warlord_parent_core_id > 0 && EmpireCoreManager.Get(ownCore.warlord_parent_core_id) != null;
    }

    public static string DecorateName(Empire empire, string baseName)
    {
        if (empire?.data == null || empire.CoreKingdom == null ||
            empire.CoreKingdom.HasCustomCountryNaming()) return baseName;
        if (ReferenceEquals(_world, World.world) && Roles.TryGetValue(empire.id, out var entry))
        {
            string centralName = CentralStyleName(empire, entry.core, baseName);
            return entry.role switch
            {
                Role.Central => centralName,
                Role.Provisional => ProvisionalGovernmentName(empire, entry.core),
                _ => WarlordEmpireName(empire, entry.core, baseName)
            };
        }

        // 附庸政府的城市会被控制权统计并入最上层宗主，因此它可能不出现在 Roles 缓存里；
        // 只要自己的临时核心仍挂在正规核心下，就仍应显示理念临时政府名称。
        EmpireCore ownCore = EmpireCoreManager.Get(empire);
        EmpireCore parent = ownCore?.warlord_parent_core_id > 0
            ? EmpireCoreManager.Get(ownCore.warlord_parent_core_id)
            : null;
        return parent != null ? ProvisionalGovernmentName(empire, parent) : baseName;
    }

    // 所有尚未组建政府的现代独立势力统一走理念地方武装词表；有无法理只影响建政资格。
    public static bool TryGetNonGovernmentKingdomName(Kingdom kingdom, out string name)
    {
        name = "";
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.GetRegime()?.type != RegimeType.Modern ||
            kingdom.IsEmpire() || kingdom.IsInEmpire() || kingdom.GetAdministrativeTitle() != null) return false;
        // 正在造反的(农民军、起义军、某某叛乱)用自己的叛军名号，打赢独立之后才按军阀称呼
        if (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling()) return false;
        // 名号用王国法理：自己的主法理 → 都城所在的法理(没有别的政权以它为主法理时) → 都城名。
        // 本文化废除君主制之后，王国称号不再沿用，改用该法理的省份名——偏保守的与军阀除外
        string front = UsesProvinceName(kingdom) ? ProvinceFront(kingdom) : "";
        if (string.IsNullOrWhiteSpace(front)) front = TitleName(kingdom);
        if (string.IsNullOrWhiteSpace(front)) front = CapitalTitleName(kingdom);
        if (string.IsNullOrWhiteSpace(front)) front = kingdom.GetUntitledKingdomName();
        if (string.IsNullOrWhiteSpace(front)) front = kingdom.capital?.GetCityName() ?? kingdom.data.name ?? "";
        // 称呼按文化包(Party.untitled_groups)：如华夏的中间派等为"{0}系军阀"，其余为工农红军、护法军……；没配用默认称呼
        name = ModernStateFormationSystem.FormatLabel(front, ModernStateFormationSystem.GetNonGovernmentLabel(kingdom));
        return true;
    }

    // 废除君主制之后改用省份名的势力：本文化已废除君主制，且其理念倾向统一——
    // 主张单一制(见 ConstitutionTemplates.json)，也不是保守派、军阀(保守主义、保守自由主义、中间派、资本主义)。
    // 不倾向统一的(联邦派、军阀、保守派)照旧用王国称号
    private static bool UsesProvinceName(Kingdom kingdom)
    {
        if (TechnologySystem.PremodernLocked) return false;
        string culture = CultureService.GetRealmCulture(kingdom);
        if (InstitutionSystem.GetFeature(culture, RepublicSystem.FeatureAbolishMonarchy) <= 0f) return false;
        PartyIdeology ideology = ModernStateFormationSystem.NonGovernmentIdeology(kingdom);
        if (ideology is PartyIdeology.Conservatism or PartyIdeology.ConservativeLiberalism or
            PartyIdeology.Centrism or PartyIdeology.Capitalism) return false;
        return ConstitutionSystem.IdeologyPrefersUnitary(ideology);
    }

    // 主法理(或都城所在法理)的省份名
    private static string ProvinceFront(Kingdom kingdom)
    {
        KingdomTitle title = kingdom.GetMainTitle() ?? kingdom.GetCapitalDeJureTitle();
        return title == null ? "" : title.GetTitleProvinceName()?.Trim() ?? "";
    }

    // 都城所在的王国法理名；该法理已是别的政权的主法理时不用(免得两个政权同叫"蜀系军阀")
    private static string CapitalTitleName(Kingdom kingdom)
    {
        KingdomTitle title = kingdom.GetCapitalDeJureTitle();
        if (title == null) return "";
        Kingdom holder = title.main_kingdom;
        if (holder != null && !holder.isRekt() && holder != kingdom && holder.GetMainTitle() == title) return "";
        return title.data?.name?.Trim().Trim(ModClass.NARROW_SPACE.ToCharArray()).Trim() ?? "";
    }

    // 已组建政府、但在军阀时期里只算军阀的政权(比如被超过的前中央)
    // 已组建政府、但在军阀时期里只算军阀的政权：保留自己的国号(华夏的这类政府会被撤销，见 DemoteWarlordGovernments)
    private static string WarlordEmpireName(Empire empire, EmpireCore core, string baseName) => baseName;

    // 临时政府命名："省份名 + 理念临时政府称呼"(如 某省份名护国临时政府)。
    //   · 省份名取其王国法理的省份名，没有法理才用都城名；
    //   · 称呼按立国理念(组建政府时定下，只有革命才会改)取，不随执政党轮替而变；
    //   · 华夏只有另立的第二个中央是临时政府，其他文化对峙时双方都是临时政府(见 DecorateName)。
    private static string ProvisionalGovernmentName(Empire empire, EmpireCore core)
    {
        string seat = ProvinceFront(empire.CoreKingdom);
        if (string.IsNullOrWhiteSpace(seat)) seat = empire.CoreKingdom.capital?.GetCityName();
        if (string.IsNullOrWhiteSpace(seat)) seat = empire.EnsureEmpireCoreName();
        ConstitutionalEconomyState state = empire.data?.constitutional_economy;
        PartyIdeology? founding = state != null && state.is_republic ? state.republic_ideology : null;
        string label = ModernStateFormationSystem.GetProvisionalGovernmentLabel(empire.CoreKingdom, founding);
        return string.IsNullOrWhiteSpace(label)
            ? string.Format(LM.Get("warlord_era_huaxia_provisional_name"), seat)
            : ModernStateFormationSystem.FormatLabel(seat, label);
    }

    private static string TitleName(Kingdom kingdom) =>
        kingdom?.GetMainTitle()?.data?.name?.Trim().Trim(ModClass.NARROW_SPACE.ToCharArray()).Trim() ?? "";

    // 中央以帝国核心名作国号，后缀照旧按执政理念(强统一文化另见 DecorateName)
    private static string CentralStyleName(Empire empire, EmpireCore core, string baseName)
    {
        string coreName = EmpireCoreNamingSystem.GetCoreBaseName(core);
        if (string.IsNullOrWhiteSpace(coreName)) return baseName;
        var coreData = empire.CoreKingdom.GetOrCreate();
        // 没经过改制(比如直接组建政府、旧存档)的共和国没有后缀，只剩光秃秃的核心名：按执政理念补一个并记住
        if (string.IsNullOrWhiteSpace(coreData.ideology_country_suffix) && RepublicSystem.IsRepublic(empire))
            coreData.ideology_country_suffix = PartySystem.PickCountrySuffix(empire, IdeologyFamilies.StateIdeology(empire));
        string suffix = coreData.ideology_country_suffix;
        if (!string.IsNullOrWhiteSpace(suffix)) return OverallHelperFunc.JoinNameParts(coreName, suffix);
        // 君主制的中央沿用自己的国号样式(如某帝国)，只把名号换成核心名
        string own = empire.EnsureEmpireCoreName();
        return !string.IsNullOrWhiteSpace(own) && baseName.StartsWith(own, StringComparison.Ordinal) &&
               baseName.Length > own.Length
            ? coreName + baseName.Substring(own.Length)
            : baseName;
    }

    // 军阀用自己的名号；自己的名号就是核心名时改用都城名(免得和中央同名)
    private static string WarlordBaseName(Empire empire, EmpireCore core)
    {
        string own = empire.EnsureEmpireCoreName();
        string coreName = EmpireCoreNamingSystem.GetCoreBaseName(core);
        if (string.IsNullOrWhiteSpace(own) || string.Equals(own, coreName, StringComparison.Ordinal))
            own = empire.CoreKingdom.capital?.GetCityName() ?? own;
        return own;
    }

    #endregion

    #region 组建政府的资格(强统一文化)

    // 强统一文化的现代政权能否组建政府：都城不在帝国核心里、核心已承认分治、控制核心 80% 以上(统一)，
    // 或者它是核心范围内实力最强的政权(成为中央)。临时政府由联合时另行组建(TryFormCoalition)。
    public static bool CanFormGovernment(Kingdom kingdom)
    {
        if (kingdom == null || !HasStrongUnification(CultureService.GetRealmCulture(kingdom))) return true;
        // 有法理的附庸国仍可拥有自己的现代政府；组建政府不会解除宗属关系。
        if (FeudalVassalService.GetOverlord(kingdom) != null) return true;
        EmpireCore core = kingdom.capital?.GetEmpireCore();
        if (core == null || core.split_recognized) return true;
        (Dictionary<Kingdom, int> held, int total) = EmpireCoreControl.GetHolders(core);
        if (total == 0) return true;
        if (!held.TryGetValue(kingdom, out int own) || own == 0) return false;
        if (own >= total * UnifyShare) return true;
        Empire incumbent = GetLiving(core.central_empire_id);
        if (incumbent != null && Holds(held, incumbent)) return incumbent.CoreKingdom == kingdom;
        Kingdom strongest = RankLocalHolders(core, held).Select(pair => pair.Key).FirstOrDefault();
        return strongest == kingdom;
    }


    #endregion

    #region 年度扫描

    public static void UpdateWorld(bool force = false)
    {
        if (World.world == null || EmpireCoreManager.EmpireCores == null) return;
        double now = World.world.getCurWorldTime();
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _lastScan = -1d;
            Roles.Clear();
        }
        if (!force && _lastScan >= 0d && now >= _lastScan && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        Roles.Clear();
        using var timing = new PerfTimer("军阀时期年度扫描");
        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values.ToList())
        {
            try
            {
                UpdateCore(core, now);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft] 军阀时期更新失败: {exception.Message}");
            }
        }
    }



    private static Empire Government(Kingdom holder) =>
        holder != null && holder.IsEmpire() ? holder.GetEmpire() : null;

    private static void UpdateCore(EmpireCore core, double now)
    {
        if (core == null || !EmpireCoreManager.EmpireCores.TryGetValue(core.id, out EmpireCore registered) ||
            !ReferenceEquals(core, registered)) return;
        if (core.warlord_parent_core_id > 0)
        {
            if (EmpireCoreManager.Get(core.warlord_parent_core_id) != null)
            {
                RepairMisclassifiedModernProvisional(core);
                return;
            }
            core.warlord_parent_core_id = -1L;
        }
        // 游戏高速运行时一年可能短于控制权缓存的现实五秒；年度结算必须读取即时状态。
        EmpireCoreControl.Invalidate(core);
        (Dictionary<Kingdom, int> held, int total) = EmpireCoreControl.GetHolders(core);
        if (total == 0 || held.Count == 0) return;
        List<KeyValuePair<Kingdom, int>> localHolders = RankLocalHolders(core, held).ToList();
        if (localHolders.Count == 0) return;
        Kingdom strongest = localHolders[0].Key;
        float share = held[strongest] / (float)total;
        bool strongUnification = HasStrongUnification(core.default_culture);

        // 统一：现代政体控制 80% 以上
        if (share >= UnifyShare && strongest.GetRegime()?.type == RegimeType.Modern)
        {
            Empire government = EnsureUnifierGovernment(strongest);
            if (government != null)
                CompleteUnification(core, held, strongest, government, now, strongUnification);
            return;
        }
        // 军阀时期的范围：统一过又分裂的核心(所有文化)；强统一文化在第一次统一前进入现代政体即开始争夺中央
        bool preUnification = strongUnification && core.modern_unified_at < 0d &&
                               localHolders.Any(pair => pair.Key.GetRegime()?.type == RegimeType.Modern);
        if (core.modern_unified_at < 0d && !preUnification || core.split_recognized) return;

        if (core.fragmented_since < 0d)
        {
            core.fragmented_since = now;
            EventRecorder.Record(strongest, string.Format(LM.Get(preUnification
                ? "warlord_era_huaxia_contest_history" : "warlord_era_began_history"),
                EmpireCoreManager.GetDisplayName(core)));
        }
        if (!ModClass.KINGDOM_TITLE_FREEZE && ModClass.WARLORD_ERA_YEARS > 0 &&
            Date.getYearsSince(core.fragmented_since) >= ModClass.WARLORD_ERA_YEARS)
        {
            EventRecorder.Record(strongest, string.Format(LM.Get("warlord_era_split_recognized_history"),
                EmpireCoreManager.GetDisplayName(core), ModClass.WARLORD_ERA_YEARS));
            ReleaseProvisionalCores(core);
            ResetEra(core);
            core.split_recognized = true;
            return;
        }

        Empire central = ResolveCentral(core, held, out Empire rival);
        if (rival == null && central != null) rival = TryFormCoalition(core, held, central);
        core.central_empire_id = central?.id ?? -1L;
        core.rival_central_empire_id = rival?.id ?? -1L;
        foreach (Kingdom holder in held.Keys)
        {
            Empire government = Government(holder);
            // 都城不在本核心里的是外国，只是占了几座核心城市，不算本核心的军阀
            if (government == null || government != central && government != rival &&
                !IsLocalHolder(core, holder)) continue;
            // 强统一文化：第一个中央始终是正常国家，只有另立的第二个才是临时政府；其他文化对峙时双方都是临时政府
            Role role = government == central
                ? rival != null && !strongUnification ? Role.Provisional : Role.Central
                : government == rival ? Role.Provisional : Role.Warlord;
            Roles[government.id] = (core, role);
        }
        DemoteWarlordGovernments(core, held, strongUnification);
        if (strongUnification && central != null) AttractSameIdeology(core, held, central);
    }

    private static IOrderedEnumerable<KeyValuePair<Kingdom, int>> RankLocalHolders(
        EmpireCore core, Dictionary<Kingdom, int> held) => held
        .Where(pair => IsLocalHolder(core, pair.Key))
        .OrderByDescending(pair => pair.Value)
        .ThenByDescending(pair => pair.Key.cities?.Count ?? 0)
        .ThenBy(pair => pair.Key.id);

    private static bool IsLocalHolder(EmpireCore core, Kingdom holder)
    {
        if (core == null || holder == null || holder.isRekt()) return false;
        if (holder.capital?.GetEmpireCore() == core) return true;
        KingdomTitle title = holder.GetMainTitle();
        return title != null && EmpireCoreManager.ContainsTitle(core, title);
    }

    private static Empire EnsureUnifierGovernment(Kingdom winner)
    {
        Empire government = Government(winner);
        if (government != null) return government;
        if (!winner.HasMainTitle() && winner.hasKing())
            winner.ReconcileMainTitle(winner.GetControlledTitle());
        return winner.HasMainTitle() ? ModernStateFormationSystem.FormGovernment(winner) : null;
    }

    // 修复短期错误版本留下的存档：现代临时政府曾同时被写入父核心关系和伪核心标记。
    // 真正的僭越者会带有正统对手记录，绝不能在这里被洗成普通临时政府。
    private static void RepairMisclassifiedModernProvisional(EmpireCore core)
    {
        if (core?.false_core_against_empire_id <= 0) return;
        List<Empire> governments = EmpireCoreManager.GetEmpires(core)
            .Where(empire => empire != null && !empire.IsArchived()).ToList();
        if (governments.Count == 0 || governments.Any(empire =>
                empire.data?.legitimacy_rivalry_recognized == true ||
                empire.data?.legitimacy_challenger == true ||
                empire.CoreKingdom?.GetRegime()?.type != RegimeType.Modern)) return;
        core.false_core_against_empire_id = -1L;
    }

    private static void CompleteUnification(EmpireCore core, Dictionary<Kingdom, int> held, Kingdom winner,
        Empire government, double now, bool strongUnification)
    {
        bool firstUnification = core.modern_unified_at < 0d;
        bool endingContest = firstUnification || core.fragmented_since >= 0d ||
                             core.central_empire_id > 0 || core.rival_central_empire_id > 0 ||
                             EmpireCoreManager.Get(government) != core;
        if (endingContest)
        {
            var defeated = held.Keys.Where(holder => IsLocalHolder(core, holder))
                .Select(Government).Where(empire => empire != null && empire != government)
                .Concat(new[] { GetLiving(core.central_empire_id), GetLiving(core.rival_central_empire_id) })
                .Where(empire => empire != null && empire != government).Distinct().ToList();
            foreach (Empire loser in defeated) DissolveContestingGovernment(loser, core);

            EmpireCore provisionalCore = EmpireCoreManager.Get(government);
            if (provisionalCore != null && provisionalCore != core)
                EmpireCoreManager.DestroyEmpireCore(provisionalCore);
            EmpireCoreManager.RebindEmpire(government, core);
            EmpireCoreControl.Invalidate(core);
        }

        if (firstUnification)
        {
            core.modern_unified_at = now;
            if (strongUnification)
                EventRecorder.Record(winner, string.Format(LM.Get("warlord_era_huaxia_unified_history"),
                    HolderName(winner), EmpireCoreManager.GetDisplayName(core)));
        }
        EmpireCoreNamingSystem.TryApplyCoreName(government, core);
        if (core.fragmented_since >= 0d)
            EventRecorder.Record(winner, string.Format(LM.Get("warlord_era_reunified_history"),
                HolderName(winner), EmpireCoreManager.GetDisplayName(core)));
        ReleaseProvisionalCores(core);
        ResetEra(core);
    }

    private static void DissolveContestingGovernment(Empire government, EmpireCore contestedCore)
    {
        if (government == null || government.IsArchived()) return;
        Roles.Remove(government.id);
        EmpireCore ownCore = EmpireCoreManager.Get(government);
        if (ownCore != null && ownCore != contestedCore &&
            (ownCore.warlord_parent_core_id == contestedCore.id ||
             government.id == contestedCore.rival_central_empire_id))
            EmpireCoreManager.DestroyEmpireCore(ownCore);
        ModClass.EMPIRE_MANAGER?.dissolveEmpire(government);
    }

    private static void ReleaseProvisionalCores(EmpireCore parent)
    {
        foreach (EmpireCore child in EmpireCoreManager.EmpireCores.Values
                     .Where(candidate => candidate?.warlord_parent_core_id == parent.id).ToList())
            child.warlord_parent_core_id = -1L;
    }

    // 军阀不是正常国家，只有中央、临时政府和革命建立的政府才享有正常国家地位。
    // 已组建政府却只落得军阀身份的(比如被超过的前中央、战败的临时政府)撤销政府：
    //   · 强统一文化：一律撤销，退回理念对应的地方武装；
    //   · 其他文化：有法理的保留临时政府，没有法理的撤销并退回地方武装。
    private static void DemoteWarlordGovernments(EmpireCore core, Dictionary<Kingdom, int> held,
        bool strongUnification)
    {
        foreach (Kingdom holder in held.Keys.ToList())
        {
            Empire government = Government(holder);
            if (government == null || !Roles.TryGetValue(government.id, out var entry) || entry.role != Role.Warlord ||
                 government.data?.constitutional_economy?.revolutionary_government == true ||
                 !strongUnification && !string.IsNullOrWhiteSpace(TitleName(holder))) continue;
            string name = government.GetBaseEmpireFullName();
            DissolveContestingGovernment(government, core);
            if (holder.isRekt()) continue;
            EventRecorder.Record(holder, string.Format(LM.Get("warlord_era_demoted_history"), name,
                EmpireCoreManager.GetDisplayName(core), holder.GetKingdomFullName()));
        }
    }

    // 强统一文化的中央对同一理念、尚未组建政府的地方武装有吸引力：
    // 每年按中央相对实力有一定概率易帜归附，成为中央的附庸(附庸的城市计入中央，推动统一)
    private static void AttractSameIdeology(EmpireCore core, Dictionary<Kingdom, int> held, Empire central)
    {
        Kingdom centralKingdom = central.CoreKingdom;
        if (centralKingdom == null || !held.TryGetValue(centralKingdom, out int centralHeld)) return;
        PartyIdeology ideology = IdeologyFamilies.StateIdeology(centralKingdom);
        foreach (Kingdom holder in held.Keys.ToList())
        {
            if (holder == centralKingdom || !IsLocalHolder(core, holder) || Government(holder) != null ||
                holder.IsInEmpire() || FeudalVassalService.GetOverlord(holder) != null ||
                holder.GetRegime()?.type != RegimeType.Modern || !holder.hasKing() ||
                holder.isEnemy(centralKingdom) || IdeologyFamilies.StateIdeology(holder) != ideology) continue;
            float chance = AttractionChance * centralHeld / (centralHeld + held[holder]);
            if (UnityEngine.Random.value >= chance || !FeudalVassalService.Bind(centralKingdom, holder)) continue;
            ModernStateFormationSystem.TryMergeIntoOverlordGovernment(holder);
            EmpireCoreControl.Invalidate(core);
            EventRecorder.Record(centralKingdom, string.Format(LM.Get("warlord_era_attracted_history"),
                holder.GetKingdomFullName(), PartySystem.GetIdeologyName(ideology), central.GetBaseEmpireFullName()));
        }
    }

    // 已有两个中央政府时维持对峙，直到一方被逐出帝国核心；否则已组建政府的政权里实力最强的是中央
    private static Empire ResolveCentral(EmpireCore core, Dictionary<Kingdom, int> held, out Empire rival)
    {
        rival = null;
        Empire strongest = RankLocalHolders(core, held).Where(pair => Government(pair.Key) != null)
            .OrderByDescending(pair => pair.Value).Select(pair => Government(pair.Key)).FirstOrDefault();
        Empire previous = GetLiving(core.central_empire_id);
        Empire challenger = GetLiving(core.rival_central_empire_id);
        if (previous == null && strongest != challenger) previous = strongest;
        bool challengerHolds = challenger != null && Holds(held, challenger);
        bool previousHolds = previous != null && Holds(held, previous);
        if (challengerHolds && previousHolds && previous != challenger)
        {
            rival = challenger;
            return previous;
        }
        if (challengerHolds)
        {
            // 原中央已被逐出，但临时政府未控制 80% 时仍是临时政府，不能提前取得正式国号。
            if (previous != null && !previousHolds)
            {
                EventRecorder.Record(challenger.CoreKingdom, string.Format(LM.Get("warlord_era_incumbent_expelled_history"),
                    challenger.GetBaseEmpireFullName(), previous.GetBaseEmpireFullName()));
                DissolveContestingGovernment(previous, core);
            }
            rival = challenger;
            return null;
        }
        if (previousHolds)
        {
            if (challenger != null)
            {
                EventRecorder.Record(previous.CoreKingdom, string.Format(LM.Get("warlord_era_rival_defeated_history"),
                    previous.GetBaseEmpireFullName(), challenger.GetBaseEmpireFullName()));
                DissolveContestingGovernment(challenger, core);
            }
            core.rival_alliance_id = -1L;
            return previous;
        }
        foreach (Empire defeated in new[] { previous, challenger }
                     .Where(empire => empire != null && !Holds(held, empire)).Distinct())
            DissolveContestingGovernment(defeated, core);
        core.rival_alliance_id = -1L;
        return strongest;
    }

    private static bool Holds(Dictionary<Kingdom, int> held, Empire empire) =>
        empire?.CoreKingdom != null && held.ContainsKey(empire.CoreKingdom);

    // 与中央理念截然不同的势力另立中央，组建政府(临时政府)并向中央开战：
    //   · 联合：两家以上异见军阀结盟，合计控制中央 60% 以上的核心城市(北洋时期的国民革命政府)；
    //   · 独立：一家异见势力自身就有中央一半以上的核心城市，不必结盟(国共内战中的共产党政权)。
    private static Empire TryFormCoalition(EmpireCore core, Dictionary<Kingdom, int> held, Empire central)
    {
        Kingdom centralKingdom = central.CoreKingdom;
        if (centralKingdom == null || !held.ContainsKey(centralKingdom) ||
            UnityEngine.Random.value >= CoalitionChance) return null;
        PartyIdeology centralIdeology = IdeologyFamilies.StateIdeology(centralKingdom);
        List<Kingdom> members = held.Keys.Where(holder => holder != centralKingdom &&
                IsLocalHolder(core, holder) && holder.hasKing() && !holder.hasAlliance() &&
                Government(holder) == null && !holder.IsInEmpire() &&
                FeudalVassalService.GetOverlord(holder) == null &&
                holder.GetRegime()?.type == RegimeType.Modern &&
                PartySystem.IdeologyDistance(IdeologyFamilies.StateIdeology(holder), centralIdeology) >= OppositeIdeologyDistance)
            .OrderByDescending(holder => held[holder]).ToList();
        int centralHeld = held[centralKingdom];
        bool coalition = members.Count >= 2 && members.Sum(holder => held[holder]) >= centralHeld * CoalitionStrengthShare;
        Kingdom solo = members.FirstOrDefault(holder => held[holder] >= centralHeld * SoloRivalShare &&
                                                        HasGovernmentTitle(holder));
        if (!coalition && solo == null) return null;
        if (!coalition) members = new List<Kingdom> { solo };

        Kingdom leaderKingdom = members.FirstOrDefault(HasGovernmentTitle);
        if (leaderKingdom == null) return null;
        members.Remove(leaderKingdom);
        members.Insert(0, leaderKingdom);
        Dictionary<long, string> memberNames = members.ToDictionary(member => member.id, HolderName);
        Empire leader = ModernStateFormationSystem.FormGovernment(leaderKingdom);
        if (leader == null) return null;
        EmpireCore provisionalCore = EmpireCoreManager.Get(leader);
        if (provisionalCore != null && provisionalCore != core)
            provisionalCore.warlord_parent_core_id = core.id;

        if (coalition)
        {
            var joined = new List<Kingdom> { leaderKingdom };
            foreach (Kingdom member in members.Skip(1))
            {
                leader.join(member, pRecalc: false, pForce: true, pLegitimacyTransfer: true);
                if (member.GetEmpire() == leader) joined.Add(member);
            }
            leader.recalculate();
            members = joined;
            if (members.Count < 2)
            {
                DissolveContestingGovernment(leader, core);
                return null;
            }
        }

        War war = FindActiveWar(leaderKingdom, centralKingdom) ??
                  World.world.diplomacy.startWar(leaderKingdom, centralKingdom, WarTypeLibrary.normal);
        if (war == null)
        {
            DissolveContestingGovernment(leader, core);
            return null;
        }
        core.rival_alliance_id = -1L;
        foreach (Kingdom member in members.Skip(1))
            if (!war.hasKingdom(member)) war.joinAttackers(member);
        EmpireCoreControl.Invalidate(core);
        string ideology = PartySystem.GetIdeologyName(IdeologyFamilies.StateIdeology(leaderKingdom));
        bool strongUnification = HasStrongUnification(core.default_culture);
        EventRecorder.Record(leaderKingdom, members.Count >= 2
            ? string.Format(LM.Get(strongUnification
                    ? "warlord_era_strong_coalition_history" : "warlord_era_coalition_history"),
                string.Join("、", members.Select(member => memberNames[member.id])), ideology,
                central.GetBaseEmpireFullName())
            : string.Format(LM.Get(strongUnification
                    ? "warlord_era_strong_solo_rival_history" : "warlord_era_solo_rival_history"),
                HolderName(leaderKingdom), ideology, central.GetBaseEmpireFullName()));
        return leader;
    }

    private static bool HasGovernmentTitle(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || !kingdom.hasKing()) return false;
        if (!kingdom.HasMainTitle()) kingdom.ReconcileMainTitle(kingdom.GetControlledTitle());
        return kingdom.HasMainTitle();
    }

    private static War FindActiveWar(Kingdom first, Kingdom second) => first?.getWars()
        ?.FirstOrDefault(war => war != null && war.isAlive() && !war.hasEnded() && war.hasKingdom(second));


    #endregion

    private static string HolderName(Kingdom holder) =>
        Government(holder)?.GetBaseEmpireFullName() ?? holder.GetKingdomFullName();

    private static Empire GetLiving(long id)
    {
        Empire empire = id > 0 ? ModClass.EMPIRE_MANAGER?.get(id) : null;
        return empire != null && !empire.isRekt() && !empire.IsArchived() ? empire : null;
    }

    private static void ResetEra(EmpireCore core)
    {
        core.fragmented_since = -1d;
        core.split_recognized = false;
        core.central_empire_id = -1L;
        core.rival_central_empire_id = -1L;
        core.rival_alliance_id = -1L;
    }

}

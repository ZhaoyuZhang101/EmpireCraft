using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// In the modern era an Empire object represents a normal sovereign state, not a ruler
// claiming an imperial crown. A de jure title supplies legal statehood; untitled realms
// remain armed, warlord or separatist governments until they acquire one.
public static class ModernStateFormationSystem
{
    public static bool TryUpdate(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || kingdom.GetRegime()?.type != RegimeType.Modern ||
            kingdom.GetAdministrativeTitle() != null)
            return false;
        if (TryGetSameCoreOverlord(kingdom, out Kingdom overlord) &&
            overlord.GetRegime()?.type == RegimeType.Modern)
        {
            // 同一正规核心里的附庸等宗主建政；宗主已有政府时立即合并。
            return TryMergeIntoOverlordGovernment(kingdom, overlord);
        }
        if (kingdom.IsEmpire() || kingdom.IsInEmpire()) return false;
        if (!kingdom.HasMainTitle() && kingdom.hasKing())
            kingdom.ReconcileMainTitle(kingdom.GetControlledTitle());
        if (!kingdom.HasMainTitle())
        {
            ApplyUntitledName(kindom: kingdom);
            return false;
        }
        kingdom.GetOrCreate().ideology_country_suffix = "";
        // 强统一文化组建政府的条件更苛刻：只有帝国核心内最强者(中央)或统一者才能组建，
        // 其余有法理的只是军阀；由 CultureRule.jsonc 的 strong_unification 政治特质启用。
        if (!WarlordEraSystem.CanFormGovernment(kingdom))
        {
            ApplyNonGovernmentName(kingdom);
            return false;
        }
        return FormGovernment(kingdom) != null;
    }

    // 组建政府：现代政体下取代"称帝"(所有文化)，政权由此进入帝国层、享有正常国家的全部机制
    public static Empire FormGovernment(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || !kingdom.hasKing() || kingdom.IsEmpire() || kingdom.IsInEmpire() ||
            Compatibility.AncientWarfareCompatibility.BlocksEmpireFormation(kingdom) ||
            EmpireCraftWorldLawLibrary.empirecraft_law_ban_empire.isEnabled()) return null;
        kingdom.GetOrCreate().ideology_country_suffix = "";
        EmpireCore contestedCore = EmpireCoreManager.GetRiseCandidateCore(kingdom);
        Empire empire = ModClass.EMPIRE_MANAGER?.NewEmpire(kingdom, allowCultureRival: true,
            suppressFoundingLog: true);
        if (empire == null) return null;
        EmpireCore governmentCore = EmpireCoreManager.Get(empire);
        if (contestedCore != null && governmentCore != null && governmentCore != contestedCore)
        {
            // 现代临时政府仍在原正规核心框架内争夺中央。这个子核心只是分治期间的
            // 技术记录，不能标成僭称帝号产生的伪核心；统一时它会被销毁并接管原核心。
            governmentCore.warlord_parent_core_id = contestedCore.id;
            EmpireCoreManager.RestoreParentCities(governmentCore);
        }
        PartyIdeology ideology = ResolveGovernmentIdeology(kingdom);
        RepublicSystem.SyncWithRegime(empire, foundingIdeology: ideology);
        MergeSameCoreVassals(empire);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        string text = string.Format(LM.Get("modern_state_founded_log"),
            kingdom.king?.getName() ?? LM.Get("label_none"), empire.GetEmpireFullName());
        empire.RecordHistory(directContent: text, actorId: kingdom.king?.id ?? -1L, kingdomId: kingdom.id);
        TranslateHelper.LogEventMessage(text, kingdom);
        return empire;
    }

    private static EmpireCore LawfulCore(Kingdom kingdom)
    {
        EmpireCore core = EmpireCoreManager.GetRiseCandidateCore(kingdom);
        if (core?.false_core_against_empire_id > 0) return null;
        return core?.warlord_parent_core_id > 0
            ? EmpireCoreManager.Get(core.warlord_parent_core_id)
            : core;
    }

    // 是否同属一个法统：政府按上级核心算，地方势力的主法理与都城可能落在不同核心里，任一处在政府法统内即算
    private static bool SameLawfulCore(Kingdom governmentKingdom, Kingdom subject)
    {
        EmpireCore lawful = LawfulCore(governmentKingdom);
        if (lawful == null) return false;
        if (LawfulCore(subject) == lawful) return true;
        EmpireCore capitalCore = subject?.capital?.GetEmpireCore();
        if (capitalCore?.warlord_parent_core_id > 0)
            capitalCore = EmpireCoreManager.Get(capitalCore.warlord_parent_core_id) ?? capitalCore;
        return capitalCore == lawful;
    }

    private static bool TryGetSameCoreOverlord(Kingdom subject, out Kingdom overlord)
    {
        overlord = null;
        EmpireCore core = LawfulCore(subject);
        if (core == null) return false;
        var visited = new HashSet<long> { subject.id };
        for (Kingdom parent = FeudalVassalService.GetOverlord(subject);
             parent != null && visited.Add(parent.id) && LawfulCore(parent) == core;
             parent = FeudalVassalService.GetOverlord(parent))
            overlord = parent;
        return overlord != null;
    }

    public static bool TryMergeIntoOverlordGovernment(Kingdom subject)
    {
        return TryGetSameCoreOverlord(subject, out Kingdom overlord) &&
               TryMergeIntoOverlordGovernment(subject, overlord);
    }

    private static bool TryMergeIntoOverlordGovernment(Kingdom subject, Kingdom overlord)
    {
        if (subject?.data == null || subject.isRekt() || subject.GetRegime()?.type != RegimeType.Modern ||
            Compatibility.AncientWarfareCompatibility.Owns(subject) ||
            overlord?.GetRegime()?.type != RegimeType.Modern) return false;
        Empire government = overlord.GetEmpire();
        if (government == null || government.IsArchived() || government.isRekt() ||
            government.CoreKingdom == null || !SameLawfulCore(government.CoreKingdom, subject)) return false;
        if (subject.GetEmpire() == government)
        {
            FeudalVassalService.Break(subject);
            subject.GetOrCreate().ideology_country_suffix = "";
            subject.updateColor(government.CoreKingdom.getColor());
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(subject);
            return true;
        }

        Empire former = subject.IsEmpire() ? subject.GetEmpire() : null;
        EmpireCore formerCore = EmpireCoreManager.Get(former);
        EmpireCore lawfulCore = LawfulCore(subject);
        List<Kingdom> formerMembers = former?.kingdoms_list?.Where(member => member != null &&
            !member.isRekt()).ToList() ?? new List<Kingdom>();
        if (former != null)
        {
            if (!formerMembers.Contains(subject)) formerMembers.Insert(0, subject);
            // 有其他核心成员的政府不整体拆散；其范围超出了本次同核心合并。
            if (formerMembers.Any(member => LawfulCore(member) != lawfulCore ||
                    Compatibility.AncientWarfareCompatibility.Owns(member))) return false;
            ModClass.EMPIRE_MANAGER.dissolveEmpire(former);
            if (formerCore != null && formerCore != lawfulCore &&
                formerCore.warlord_parent_core_id == lawfulCore.id &&
                !EmpireCoreManager.GetEmpires(formerCore).Any(other => !other.IsArchived()))
                EmpireCoreManager.DestroyEmpireCore(formerCore);
        }
        else if (subject.IsInEmpire())
            subject.GetEmpire()?.leave(subject);

        foreach (Kingdom member in formerMembers.Count > 0 ? formerMembers : new List<Kingdom> { subject })
        {
            foreach (Kingdom child in FeudalVassalService.GetDirectVassals(member).ToList())
                if (LawfulCore(child) != lawfulCore)
                    FeudalVassalService.Bind(government.CoreKingdom, child);
            FeudalVassalService.Break(member);
            government.join(member, pForce: true, pLegitimacyTransfer: true);
            if (member.GetEmpire() == government)
            {
                member.GetOrCreate().ideology_country_suffix = "";
                member.updateColor(government.CoreKingdom.getColor());
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
            }
        }
        if (subject.GetEmpire() != government) return false;
        // 若附庸先建政占用了正规核心，宗主随后建政时接管原核心并清掉临时核心。
        EmpireCore governmentCore = EmpireCoreManager.Get(government);
        if (formerCore == lawfulCore && governmentCore != null && governmentCore != lawfulCore &&
            governmentCore.warlord_parent_core_id == lawfulCore.id &&
            !EmpireCoreManager.GetEmpires(lawfulCore).Any(other => other != government && !other.IsArchived()))
        {
            EmpireCoreManager.DestroyEmpireCore(governmentCore);
            EmpireCoreManager.RebindEmpire(government, lawfulCore);
        }
        EmpireCoreManager.SyncCitiesFromTitles(lawfulCore, preserveOccupiedCore: true);
        EmpireCoreControl.Invalidate(lawfulCore);
        return true;
    }

    public static bool MergeSameCoreVassals(Empire government)
    {
        if (government?.CoreKingdom == null || World.world?.kingdoms == null) return false;
        bool changed = false;
        foreach (Kingdom subject in World.world.kingdoms.ToList())
        {
            if (subject == null || subject == government.CoreKingdom || subject.isRekt() ||
                !TryGetSameCoreOverlord(subject, out Kingdom overlord) ||
                overlord.GetEmpire() != government) continue;
            changed |= TryMergeIntoOverlordGovernment(subject, overlord);
        }
        return changed;
    }

    // 新国家依建国首领的理念立制；首领理念尚未被本文化解锁时，采用光谱上最接近的
    // 已解锁理念。文化刚进入现代、批量承接旧法理王国时也必须走同一套判定。
    public static PartyIdeology ResolveGovernmentIdeology(Kingdom kingdom)
    {
        PartyIdeology personal = IdeologyPopulationSystem.Get(kingdom.king);
        string culture = CultureService.GetRealmCulture(kingdom);
        if (PartySystem.IsResearched(culture, personal) &&
            TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(personal)))
            return personal;
        return Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>()
            .Where(ideology => PartySystem.IsResearched(culture, ideology) &&
                TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(ideology)))
            .OrderBy(ideology => PartySystem.IdeologyDistance(personal, ideology))
            .DefaultIfEmpty(PartyIdeology.Centrism).First();
    }

    private static void ApplyUntitledName(Kingdom kindom)
    {
        ApplyNonGovernmentName(kindom);
    }

    // 尚未组建正常政府的现代势力统一按理念称呼，是否已有法理不改变它当前的武装身份；
    // 法理只决定它日后是否有资格组建政府。中间派为军阀，社会主义为人民武装等。
    // 未组建政府的势力称呼：按本文化文化包里该理念的词库(Party.untitled_groups)抽一个，没配用 PartyNames 的默认称呼。
    // 抽中后记在王国上，理念不变就不再重抽。称呼可能含 {0}(见 FormatLabel)。
    public static string GetNonGovernmentLabel(Kingdom kingdom)
    {
        // 民族情绪高涨(都城 ≥ 60)：改用民族主义武装的称呼(国民革命军、民族解放军……)
        if (kingdom?.capital != null && NationalSentimentSystem.GetCity(kingdom.capital) >= 60f)
        {
            List<string> nationalist = Enumerable.Range(1, 6).Select(i => LM.Get($"nation_force_label_{i}"))
                .Where(label => !label.StartsWith("nation_force_label_")).ToList();
            if (nationalist.Count > 0) return nationalist[(int)(kingdom.id % nationalist.Count)];
        }
        PartyIdeology ideology = NonGovernmentIdeology(kingdom);
        List<string> pool = PartySystem.GetCultureNamePool(CultureService.GetRealmCulture(kingdom), ideology,
            party => party.untitled_groups, "Untitled");
        if (kingdom?.data == null) return pool.FirstOrDefault() ?? PartySystem.GetIdeologyName(ideology);
        var data = kingdom.GetOrCreate();
        int pick = StablePick(pool.Count, ideology, ref data.non_government_label_ideology, ref data.non_government_label_pick);
        return pick >= 0 ? pool[pick].Trim() : PartySystem.GetIdeologyName(ideology);
    }

    // 称呼含 {0} 时把名号代进去(如"{0}系军阀")，否则接在名号后面
    public static string FormatLabel(string front, string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return front ?? "";
        return label.Contains("{0}")
            ? label.Replace("{0}", front ?? "")
            : OverallHelperFunc.JoinNameParts(front, label);
    }

    // 理念变了、或记下的序号已不在词库范围里(旧存档、词库改动)就重抽
    private static int StablePick(int count, PartyIdeology ideology, ref int storedIdeology, ref int storedPick)
    {
        if (count <= 0) return -1;
        if (storedIdeology != (int)ideology || storedPick < 0 || storedPick >= count)
        {
            storedIdeology = (int)ideology;
            storedPick = UnityEngine.Random.Range(0, count);
        }
        return storedPick;
    }

    // 未组建政府的势力按都城的主流理念称呼，与悬浮提示里的"理念"一致(不按君主个人理念)
    public static PartyIdeology NonGovernmentIdeology(Kingdom kingdom) =>
        kingdom?.capital != null && !kingdom.capital.isRekt()
            ? IdeologyPopulationSystem.GetDominant(kingdom.capital)
            : IdeologyFamilies.StateIdeology(kingdom);


    // 已组建政府、但尚未取得中央地位或完成统一时，使用另一套理念临时政府称呼。
    // ideology：按哪个理念取称呼。临时政府传它的立国理念，免得执政党一换称呼就跟着变
    public static string GetProvisionalGovernmentLabel(Kingdom kingdom, PartyIdeology? ideologyOverride = null)
    {
        PartyIdeology ideology = ideologyOverride ?? IdeologyFamilies.StateIdeology(kingdom);
        List<string> pool = PartySystem.GetCultureNamePool(CultureService.GetRealmCulture(kingdom), ideology,
            party => party.provisional_groups, "Provisional");
        if (kingdom?.data == null) return pool.FirstOrDefault() ?? PartySystem.GetIdeologyName(ideology);
        var data = kingdom.GetOrCreate();
        int pick = StablePick(pool.Count, ideology, ref data.provisional_label_ideology, ref data.provisional_label_pick);
        return pick >= 0 ? pool[pick].Trim() : PartySystem.GetIdeologyName(ideology);
    }

    private static void ApplyNonGovernmentName(Kingdom kingdom)
    {
        // 含 {0} 的称呼(如"{0}系军阀")只能由 TryGetNonGovernmentKingdomName 代入名号，这里存去掉占位符的部分
        kingdom.GetOrCreate().ideology_country_suffix = GetNonGovernmentLabel(kingdom).Replace("{0}", "");
    }
}

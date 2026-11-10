using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 科技革新(用户 2026-11-11 "还有派系会支持的科技革新""也可以并行，这样就能并行三个诉求了"
// "每个派系都有自己支持的科技革新，用于巩固自己的派系利益或者通用的科技革新用于维持统治或者赚钱")：
//   · 第三条并行线：朝堂的奏议、改革线的制度、这里的革新各办各的。一个帝国同时一项，由一派倡议；
//   · 科技本身没有政治配置，这里按门类给各阶层的利弊，典章、学术与几项军事单独标；"维持统治""赚钱"两类是通用革新，
//     执政的一派要维持统治、国库吃紧时人人想赚钱；
//   · 一派对一项科技的立场 = Σ(本派对各阶层的亲疏 × 利弊) ＋ 通用项。倾向的革新：神力固定的(FixedFaction.PreferredTech)，
//     否则本文化还没研出、够得着(前置已齐或只差一步)、不超时代与年历的科技里立场最高的；
//   · 办革新：这项(或通往它的下一步)优先占一个研究位(TechnologySystem.GetResearchTargets)，国库每年另拨"费用一成 × 每点科技价"
//     直接推一成进度(倡议的一派是当权派系推一成半)；研成即告成，十五年不成或倡议的一派散了作罢；
//   · 天平：各派按中枢比例表态，赞成的占比不少于反对的、国库拨得出一年经费才发得起(神力可强行)
public static class TechDriveSystem
{
    public const float FundingFraction = 0.1f;
    public const float DominantBoost = 1.5f;
    public const int MaxYears = 15;
    public const int MaxUnfundedYears = 3;
    public const float StanceThreshold = 3f;
    private const float ClassScale = 12f;
    private const float RuleBonus = 6f;
    private const float MoneyBonus = 4f;
    private const int AiMinRatio = 10;
    private const int AiRetryYears = 2;
    private const int HistoryLimit = 12;

    #region 科技的利害

    private static (SocialClass cls, float value)[] E(params (SocialClass, float)[] effects) => effects;

    // 门类默认
    private static readonly Dictionary<string, (SocialClass cls, float value)[]> BranchEffects = new()
    {
        ["military"] = E((SocialClass.Army, 1f), (SocialClass.Noble, 0.2f), (SocialClass.Peasant, -0.3f)),
        ["naval"] = E((SocialClass.Merchant, 0.7f), (SocialClass.Army, 0.4f)),
        ["metallurgy"] = E((SocialClass.Labour, 0.6f), (SocialClass.Army, 0.4f), (SocialClass.Merchant, 0.2f)),
        ["construction"] = E((SocialClass.Labour, 0.5f), (SocialClass.Citizen, 0.5f), (SocialClass.Landlord, 0.2f)),
        ["science"] = E((SocialClass.Officer, 0.5f), (SocialClass.Citizen, 0.4f), (SocialClass.Merchant, 0.3f)),
        ["material"] = E((SocialClass.Labour, 0.4f), (SocialClass.Peasant, 0.3f)),
        ["civics"] = E((SocialClass.Officer, 0.6f))
    };

    // 单独标的(谁得利谁受损跟门类默认不一样的)
    private static readonly Dictionary<string, (SocialClass cls, float value)[]> TechEffects = new()
    {
        ["currency"] = E((SocialClass.Merchant, 1f), (SocialClass.Citizen, 0.4f), (SocialClass.Landlord, -0.3f)),
        ["charter_towns"] = E((SocialClass.Merchant, 0.9f), (SocialClass.Citizen, 0.7f), (SocialClass.Noble, -0.4f)),
        ["banking"] = E((SocialClass.Merchant, 1.1f), (SocialClass.Citizen, 0.3f), (SocialClass.Landlord, -0.3f)),
        ["political_economy"] = E((SocialClass.Merchant, 1f), (SocialClass.Labour, 0.3f), (SocialClass.Noble, -0.4f)),
        ["industrialization"] = E((SocialClass.Merchant, 0.8f), (SocialClass.Labour, 0.6f), (SocialClass.Landlord, -0.3f), (SocialClass.Peasant, -0.3f)),
        ["census"] = E((SocialClass.Officer, 0.8f), (SocialClass.Peasant, 0.2f), (SocialClass.Landlord, -0.5f), (SocialClass.Noble, -0.3f)),
        ["taxation"] = E((SocialClass.Officer, 0.8f), (SocialClass.Landlord, -0.4f), (SocialClass.Merchant, -0.2f)),
        ["fiscal_reform"] = E((SocialClass.Officer, 0.8f), (SocialClass.Peasant, 0.4f), (SocialClass.Landlord, -0.6f)),
        ["civil_examination"] = E((SocialClass.Officer, 1f), (SocialClass.Landlord, 0.2f), (SocialClass.Noble, -0.6f)),
        ["law_code"] = E((SocialClass.Officer, 0.8f), (SocialClass.Noble, -0.2f)),
        ["bureaucracy"] = E((SocialClass.Officer, 0.9f), (SocialClass.Noble, -0.3f)),
        ["postal_roads"] = E((SocialClass.Officer, 0.6f), (SocialClass.Merchant, 0.4f)),
        ["military_register"] = E((SocialClass.Army, 0.9f), (SocialClass.Peasant, -0.3f)),
        ["standing_army"] = E((SocialClass.Army, 1f), (SocialClass.Noble, -0.3f), (SocialClass.Peasant, -0.3f)),
        ["ritual_rites"] = E((SocialClass.Noble, 0.7f), (SocialClass.Officer, 0.3f)),
        ["canon_law"] = E((SocialClass.Noble, 0.4f), (SocialClass.Officer, 0.3f), (SocialClass.Merchant, -0.2f)),
        ["estates_assembly"] = E((SocialClass.Noble, 0.6f), (SocialClass.Landlord, 0.4f), (SocialClass.Merchant, 0.3f), (SocialClass.Officer, -0.3f)),
        ["secular_philosophy"] = E((SocialClass.Citizen, 0.8f), (SocialClass.Merchant, 0.4f), (SocialClass.Noble, -0.8f)),
        ["republicanism"] = E((SocialClass.Citizen, 0.9f), (SocialClass.Merchant, 0.4f), (SocialClass.Noble, -1f)),
        ["mass_politics"] = E((SocialClass.Citizen, 0.8f), (SocialClass.Labour, 0.5f), (SocialClass.Noble, -0.7f)),
        ["social_theory"] = E((SocialClass.Labour, 1f), (SocialClass.Peasant, 0.4f), (SocialClass.Merchant, -0.6f), (SocialClass.Landlord, -0.6f)),
        ["nationalism"] = E((SocialClass.Army, 0.4f), (SocialClass.Officer, 0.4f), (SocialClass.Citizen, 0.3f)),
        ["paper"] = E((SocialClass.Officer, 0.6f), (SocialClass.Merchant, 0.3f)),
        ["printing"] = E((SocialClass.Officer, 0.5f), (SocialClass.Citizen, 0.5f)),
        ["movable_type"] = E((SocialClass.Officer, 0.5f), (SocialClass.Citizen, 0.5f), (SocialClass.Merchant, 0.2f)),
        ["gunpowder"] = E((SocialClass.Army, 1f), (SocialClass.Noble, -0.4f)),
        ["matchlock"] = E((SocialClass.Army, 1f), (SocialClass.Noble, -0.4f)),
        ["hongyi_cannon"] = E((SocialClass.Army, 1.1f), (SocialClass.Noble, -0.5f)),
        ["ocean_navigation"] = E((SocialClass.Merchant, 1f), (SocialClass.Army, 0.3f), (SocialClass.Landlord, -0.2f)),
        ["coaling_stations"] = E((SocialClass.Merchant, 0.8f), (SocialClass.Army, 0.5f)),
        ["ward_housing"] = E((SocialClass.Merchant, 0.7f), (SocialClass.Citizen, 0.7f)),
        ["silversmithing"] = E((SocialClass.Merchant, 0.7f), (SocialClass.Noble, 0.4f)),
        ["deep_mining"] = E((SocialClass.Labour, 0.6f), (SocialClass.Merchant, 0.5f))
    };

    // 通用革新：维持统治(管得住、收得上、调得动) / 赚钱
    private static readonly HashSet<string> RuleTechs = new()
    {
        "law_code", "census", "bureaucracy", "taxation", "postal_roads", "civil_examination", "fiscal_reform",
        "military_register", "standing_army", "paper", "printing", "telegraph", "radio", "nationalism"
    };

    private static readonly HashSet<string> MoneyTechs = new()
    {
        "currency", "charter_towns", "banking", "political_economy", "industrialization", "ocean_navigation",
        "coaling_stations", "ward_housing", "silversmithing", "deep_mining"
    };

    public static Dictionary<SocialClass, float> ClassEffects(string techId)
    {
        var result = new Dictionary<SocialClass, float>();
        if (!TechnologySystem.TryGetTech(techId, out TechNodeConfig tech)) return result;
        if (!TechEffects.TryGetValue(tech.id, out (SocialClass cls, float value)[] effects) &&
            !BranchEffects.TryGetValue(tech.branch ?? "", out effects)) return result;
        foreach ((SocialClass cls, float value) in effects) result[cls] = value;
        return result;
    }

    // rule 维持统治 / money 赚钱 / interest 各为其利
    public static string Category(string techId) =>
        RuleTechs.Contains(techId) ? "rule" : MoneyTechs.Contains(techId) ? "money" : "interest";

    #endregion

    #region 立场与倾向

    private static List<FixedFaction> Factions(Empire empire) =>
        empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions()?.Where(faction => faction != null && !faction.Ban).ToList() ??
        new List<FixedFaction>();

    private static FixedFaction Dominant(Empire empire) => Factions(empire).OrderByDescending(faction => faction.CentralRatio).FirstOrDefault();

    // 国库吃紧：可支配的钱不够一年的平常革新经费(按十成费用的一成算)
    private static bool TreasuryTight(Empire empire) => Funds(empire) < 200;

    // 一派对一项科技立场的来由(派系窗口逐项列出)：各阶层的利弊、维持统治(执政的一派)、赚钱(国库吃紧)
    public static List<(string key, string arg, float value)> Explain(Empire empire, FixedFaction faction, string techId)
    {
        var parts = new List<(string key, string arg, float value)>();
        if (faction == null) return parts;
        FactionClassSystem.EnsureProfile(faction);
        foreach (KeyValuePair<SocialClass, float> pair in ClassEffects(techId))
        {
            float affinity = faction.ClassAffinities.TryGetValue(pair.Key, out float value) ? value : 0f;
            float part = affinity / 100f * pair.Value * ClassScale;
            if (Mathf.Abs(part) >= 0.5f)
                parts.Add((part > 0f ? "stance_part_class_gain" : "stance_part_class_loss", pair.Key.ToTranslate(), part));
        }
        string category = Category(techId);
        if (category == "rule" && Dominant(empire) == faction) parts.Add(("tech_part_rule", "", RuleBonus));
        if (category == "money" && empire != null && TreasuryTight(empire)) parts.Add(("tech_part_money", "", MoneyBonus));
        return parts.OrderByDescending(part => Mathf.Abs(part.value)).ToList();
    }

    public static float Stance(Empire empire, FixedFaction faction, string techId) =>
        Mathf.Clamp(Explain(empire, faction, techId).Sum(part => part.value), -30f, 30f);

    public static string CultureOf(Empire empire) =>
        empire?.CoreKingdom == null ? null : TechnologySystem.GetCultureOf(empire.CoreKingdom);

    // 够得着：本文化没研出，前置已齐或只差一步，不超时代、不超年历
    public static bool Reachable(string culture, TechNodeConfig tech)
    {
        if (string.IsNullOrEmpty(culture) || tech == null || TechnologySystem.HasTech(culture, tech.id)) return false;
        if (TechnologySystem.BeyondEra(culture, tech) || TechnologySystem.BeyondCalendar(culture, tech)) return false;
        if (TechnologySystem.ArePrerequisitesMet(culture, tech)) return true;
        return TechnologySystem.Requires(tech, culture).All(id => TechnologySystem.HasTech(culture, id) ||
            TechnologySystem.TryGetTech(id, out TechNodeConfig step) && TechnologySystem.ArePrerequisitesMet(culture, step));
    }

    // 本派倾向的革新，按先后：神力固定的、立场高的、便宜的
    public static List<(TechNodeConfig tech, float stance)> PreferenceOrder(Empire empire, FixedFaction faction)
    {
        string culture = CultureOf(empire);
        if (culture == null || faction == null || !TechnologySystem.IsActiveFor(culture)) return new List<(TechNodeConfig, float)>();
        return TechnologySystem.Techs.Where(tech => Reachable(culture, tech))
            .Select(tech => (tech, stance: Stance(empire, faction, tech.id)))
            .Where(item => item.tech.id == faction.PreferredTech || item.stance >= StanceThreshold)
            .OrderByDescending(item => item.tech.id == faction.PreferredTech)
            .ThenByDescending(item => item.stance)
            .ThenBy(item => TechnologySystem.GetCost(culture, item.tech))
            .ToList();
    }

    public static TechNodeConfig PreferredTech(Empire empire, FixedFaction faction) =>
        PreferenceOrder(empire, faction).Select(item => item.tech).FirstOrDefault();

    #endregion

    #region 天平

    public sealed class DriveOutlook
    {
        public List<(FixedFaction faction, float stance, int ratio)> Stances = new();
        public float Support, Opposition;
        public int YearlyCost, Funds;
        public float Progress, Cost;
    }

    public static int Funds(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null) return 0;
        return TreasurySystem.Enabled(core) ? StateSettlementSystem.DiscretionaryFunds(core) : Math.Max(0, core.GetMoney());
    }

    // 一年的经费：这一步(本项或通往它的下一步)费用的一成 × 每点科技价
    public static int YearlyCost(Empire empire, string techId)
    {
        string culture = CultureOf(empire);
        TechNodeConfig step = culture == null ? null : TechnologySystem.NextStepToward(culture, techId);
        if (step == null) return 0;
        return Mathf.CeilToInt(TechnologySystem.GetCost(culture, step) * FundingFraction * TechnologySystem.Config.research.gold_per_point);
    }

    public static DriveOutlook Outlook(Empire empire, string techId, FixedFaction sponsor)
    {
        var outlook = new DriveOutlook();
        string culture = CultureOf(empire);
        if (culture == null || !TechnologySystem.TryGetTech(techId, out TechNodeConfig tech)) return outlook;
        foreach (FixedFaction faction in Factions(empire))
        {
            float stance = Stance(empire, faction, techId);
            if (faction == sponsor) stance = Mathf.Max(stance, StanceThreshold);
            int ratio = faction.CentralRatio;
            outlook.Stances.Add((faction, stance, ratio));
            if (stance >= StanceThreshold) outlook.Support += ratio;
            else if (stance <= -StanceThreshold) outlook.Opposition += ratio;
        }
        outlook.YearlyCost = YearlyCost(empire, techId);
        outlook.Funds = Funds(empire);
        outlook.Cost = TechnologySystem.GetCost(culture, tech);
        outlook.Progress = TechnologySystem.GetProgress(culture, tech.id);
        return outlook;
    }

    // 发不起来的缘由(语言键)；发得起来返回 null
    public static string CannotStart(Empire empire, FixedFaction sponsor, string techId, bool force, DriveOutlook outlook = null)
    {
        string culture = CultureOf(empire);
        if (culture == null || sponsor == null || !TechnologySystem.TryGetTech(techId, out TechNodeConfig tech)) return "tech_drive_invalid";
        if (empire.data.tech_drive != null) return "tech_drive_busy";
        if (TechnologySystem.HasTech(culture, tech.id)) return "tech_drive_researched";
        if (!Reachable(culture, tech)) return "tech_drive_unreachable";
        outlook ??= Outlook(empire, techId, sponsor);
        if (outlook.Funds < outlook.YearlyCost) return "tech_drive_no_funds";
        if (!force && outlook.Support < outlook.Opposition) return "tech_drive_opposed";
        return null;
    }

    #endregion

    #region 办革新

    public static bool Start(Empire empire, FixedFaction sponsor, string techId, bool force)
    {
        if (CannotStart(empire, sponsor, techId, force) != null) return false;
        empire.data.tech_drive = new TechDriveState
        {
            tech_id = techId,
            sponsor_faction_id = sponsor.GetID(),
            sponsor_name = sponsor.Name ?? "",
            started = World.world.getCurWorldTime(),
            forced = force
        };
        string content = string.Format(LM.Get("tech_drive_started_history"), empire.GetEmpireName(), sponsor.Name,
            TechnologySystem.GetTechName(techId, CultureOf(empire)));
        empire.RecordHistory(directContent: content, kingdomId: empire.CoreKingdom.id);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
        return true;
    }

    public static void Stop(Empire empire, string outcome)
    {
        TechDriveState drive = empire?.data?.tech_drive;
        if (drive == null) return;
        empire.data.tech_drive = null;
        empire.data.tech_drive_history ??= new List<TechDriveRecord>();
        empire.data.tech_drive_history.Insert(0, new TechDriveRecord
        {
            tech_id = drive.tech_id,
            sponsor_name = drive.sponsor_name,
            outcome = outcome,
            started = drive.started,
            closed = World.world?.getCurWorldTime() ?? 0d,
            invested = drive.invested
        });
        if (empire.data.tech_drive_history.Count > HistoryLimit)
            empire.data.tech_drive_history.RemoveRange(HistoryLimit, empire.data.tech_drive_history.Count - HistoryLimit);
        if (outcome == "stopped") return;
        string content = string.Format(LM.Get(outcome == "done" ? "tech_drive_done_history" : "tech_drive_abandoned_history"),
            empire.GetEmpireName(), drive.sponsor_name, TechnologySystem.GetTechName(drive.tech_id, CultureOf(empire)));
        empire.RecordHistory(directContent: content, kingdomId: empire.CoreKingdom?.id ?? -1L);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }

    public static FixedFaction Sponsor(Empire empire, TechDriveState drive) =>
        drive == null ? null : Factions(empire).FirstOrDefault(faction => faction.GetID() == drive.sponsor_faction_id);

    // 每年(帝国年度更新)：拨款推进、告成或作罢；空着时按派系占比挑一项倾向的革新
    public static void Update(Empire empire)
    {
        if (empire?.data == null || empire.CoreKingdom == null || World.world == null) return;
        string culture = CultureOf(empire);
        if (culture == null || !TechnologySystem.IsActiveFor(culture)) return;
        TechDriveState drive = empire.data.tech_drive;
        if (drive != null)
        {
            if (TechnologySystem.HasTech(culture, drive.tech_id))
            {
                if (Sponsor(empire, drive) is FixedFaction winner && CourtSystem.Applies(empire))
                    CourtSystem.AdjustFaction(empire, winner, "capital", 10f);
                Stop(empire, "done");
                return;
            }
            if (Sponsor(empire, drive) == null || Date.getYearsSince(drive.started) >= MaxYears || drive.unfunded_years >= MaxUnfundedYears)
            {
                Stop(empire, "abandoned");
                return;
            }
            if (drive.last_funded >= 0d && Date.getYearsSince(drive.last_funded) < 1) return;
            drive.last_funded = World.world.getCurWorldTime();
            TechNodeConfig step = TechnologySystem.NextStepToward(culture, drive.tech_id);
            int cost = YearlyCost(empire, drive.tech_id);
            if (step == null || cost <= 0) return;
            if (!TreasurySystem.TrySpend(empire.CoreKingdom, cost, TreasuryCategory.Research))
            {
                drive.unfunded_years++;
                return;
            }
            float fraction = FundingFraction * (Dominant(empire)?.GetID() == drive.sponsor_faction_id ? DominantBoost : 1f);
            TechnologySystem.AddTechProgressFraction(culture, step.id, fraction);
            drive.invested += cost;
            drive.added += fraction;
            return;
        }
        // AI：每两年看一回，占比大的派系先挑
        if (empire.data.tech_drive_last_try >= 0d && Date.getYearsSince(empire.data.tech_drive_last_try) < AiRetryYears) return;
        empire.data.tech_drive_last_try = World.world.getCurWorldTime();
        foreach (FixedFaction faction in Factions(empire).OrderByDescending(faction => faction.CentralRatio))
        {
            if (faction.CentralRatio < AiMinRatio) break;
            TechNodeConfig wanted = PreferredTech(empire, faction);
            if (wanted != null && Start(empire, faction, wanted.id, false)) return;
        }
    }

    // 本文化各帝国在办的革新(研究位优先给它们，见 TechnologySystem.GetResearchTargets)
    public static IEnumerable<string> DriveTargets(string culture)
    {
        if (ModClass.EMPIRE_MANAGER == null || string.IsNullOrEmpty(culture)) yield break;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            string target = empire?.data?.tech_drive?.tech_id;
            if (string.IsNullOrEmpty(target) || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null) continue;
            if (string.Equals(CultureOf(empire), culture, StringComparison.Ordinal)) yield return target;
        }
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 近代化按国家各自走的判定数(见 StateModernization)；不碰游戏对象，回归测试直接调
public static class ModernizationRules
{
    // 共和革命每年的机会：底子 + 共和派人口 + 民心离散的城 + 邻国是共和国(示范)，封顶；剧本定下的君主国减半
    public const float RevolutionBase = 0.005f, RevolutionPerRepublican = 0.12f, RevolutionPerUnrest = 0.06f,
        RevolutionPerRepublicanNeighbour = 0.04f, RevolutionCap = 0.15f;
    // 共和派要到这么多人口才算有了革命的种子
    public const float RepublicanSeed = 0.15f;

    public static float RevolutionChance(float republicanShare, float unrestShare, float republicanNeighbourShare, bool scenarioMonarchy)
    {
        if (republicanShare < RepublicanSeed) return 0f;
        float chance = RevolutionBase + RevolutionPerRepublican * republicanShare + RevolutionPerUnrest * unrestShare +
                       RevolutionPerRepublicanNeighbour * republicanNeighbourShare;
        if (scenarioMonarchy) chance *= 0.5f;
        return Math.Min(RevolutionCap, Math.Max(0f, chance));
    }

    // 主张废君共和的理念(社会主义、共产、无政府、自由派)；保守、权威、法西斯、教派民主可与君主并存，中间派看出身
    public static bool Republican(PartyIdeology ideology) => ideology is PartyIdeology.SocialDemocracy or PartyIdeology.Socialism
        or PartyIdeology.Communism or PartyIdeology.Anarchism or PartyIdeology.SocialLiberalism or PartyIdeology.Libertarianism;

    // 立宪示范：本文化已有国家立宪 +10，相邻的立宪国、共和国每个 +5，合计封顶 20(从 AI 发起立宪要的议会支持里扣)
    public const float PioneerDemonstration = 10f, NeighbourDemonstration = 5f, DemonstrationCap = 20f;

    public static float ConstitutionDemonstration(bool culturePioneer, int constitutionalNeighbours) =>
        Math.Min(DemonstrationCap, (culturePioneer ? PioneerDemonstration : 0f) + NeighbourDemonstration * Math.Max(0, constitutionalNeighbours));
}

// 近代化按国家各自走(用户 2026-11-10 "这个旧机制其实并不合理, 我希望能改的更加符合历史上的近代史一点, 我是说这个全文明一同现代化的设定")。
// 以前本文化一废君(废除君主制的制度节点)：此后新立的同文化国家一律现代政体；每年把同文化的独立君主国统统改成现代政体；
// 废君以后才立的君主国强改共和；同文化一国立宪，别的君主国直接跟着立宪。英国立宪、法国共和、俄国革命、日本维新本是各走各的路。
// 现在文化里的制度只代表思潮传到了(知道共和、立宪、政党为何物)，国家要自己走过去：
//   · 立国：文化本身就是现代文化(现代中国)、本来就是现代政体、从共和国分出来或由共和派起事所立的，才一立国就是现代政体；
//     从君主国割据、自立的照旧是本文化的君主政体(见 AdjustFounding、MarkFounding)；
//   · 独立君主国：不再每年统一改制，按本国条件掷共和革命——共和派人口(不到 RepublicanSeed 不起)、民心离散的城、
//     邻国是共和国，越多越容易(见 TryRevolution)；帝国照旧走废君压力、退位、政变、革命(RepublicSystem)；
//   · 立宪：同文化先立宪的、相邻的立宪国与共和国只是示范，降低本国 AI 发起立宪要的议会支持(见 ConstitutionSupportBonus)，不再代办。
public static class StateModernization
{
    // 本文化的默认政体就是现代政体(现代中国、企业文化)：本来就以现代国家起步
    private static bool CultureIsModern(string culture) =>
        OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture ?? "", out Setting setting) &&
        setting?.regime is RegimeType.Modern or RegimeType.Republic;

    // 近代国家：现代政体、改制共和、君主立宪(有议会)，或科技进了电气时代(流亡政府、近代占领都看它)
    public static bool IsModernPower(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        Empire empire = kingdom.GetEmpire();
        bool modern = kingdom.GetRegime()?.type is RegimeType.Modern or RegimeType.Republic || RepublicSystem.IsRepublic(empire);
        bool constitutional = empire != null && (empire.data?.constitutional_economy?.constitutional_monarchy == true ||
                                                 ParliamentSystem.HasParliament(empire));
        return ExileRules.ModernEnough(modern, constitutional, TechnologySystem.GetEraTier(CultureService.GetRealmCulture(kingdom)));
    }

    private static bool IsModern(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() &&
        (kingdom.GetRegime()?.type is RegimeType.Modern or RegimeType.Republic || RepublicSystem.IsRepublic(kingdom.GetEmpire()));

    // 文化解析出现代政体(本文化已废君)时，这一国立国定政体：够格的照现代，不够格的改用本文化的君主政体
    public static RegimeType AdjustFounding(Kingdom kingdom, string culture, RegimeType resolved)
    {
        if (resolved is not (RegimeType.Modern or RegimeType.Republic) || kingdom?.data == null || CultureIsModern(culture)) return resolved;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        // 本来就是现代政体(官职丢了重建)、从共和国分出来或共和派起事所立的、剧本按文化定的
        if (data.regimeType is RegimeType.Modern or RegimeType.Republic || data.modern_origin == 1) return resolved;
        if (InstitutionSystem.TryResolveMonarchyRegime(culture, out RegimeType monarchy) && !RegimeManager.IsPrimitive(monarchy))
            return monarchy;
        return OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture ?? "", out Setting setting) && RegimeManager.IsMonarchy(setting?.regime)
            ? setting.regime
            : RegimeType.Feudalism;
    }

    // 立国时记下出身(CityPatch.makeOwnKingdom)：从共和国(现代政体)分出来，或起事的领头人是共和派
    public static void MarkFounding(Kingdom kingdom, Kingdom origin, Actor founder, bool rebellion)
    {
        if (kingdom?.data == null) return;
        bool modern = IsModern(origin) ||
                      rebellion && founder?.data != null && ModernizationRules.Republican(IdeologyPopulationSystem.Get(founder));
        kingdom.GetOrCreate().modern_origin = modern ? 1 : 2;
    }

    #region 共和革命(独立君主国)

    // 本国条件下今年起共和革命的机会
    public static float RevolutionChance(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.cities == null || kingdom.cities.Count == 0) return 0f;
        float republican = Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>().Where(ModernizationRules.Republican)
            .Sum(ideology => IdeologyPopulationSystem.GetKingdomShare(kingdom, ideology));
        int unrest = kingdom.cities.Count(city => CityStabilitySystem.Disaffected(city));
        var neighbours = new HashSet<Kingdom>();
        foreach (City city in kingdom.cities)
            if (city?.neighbours_kingdoms != null)
                foreach (Kingdom other in city.neighbours_kingdoms)
                    if (other?.data != null && other != kingdom && !other.isRekt() && !other.wild) neighbours.Add(other);
        float republicanNeighbours = neighbours.Count == 0 ? 0f : neighbours.Count(IsModern) / (float)neighbours.Count;
        return ModernizationRules.RevolutionChance(Math.Min(1f, republican), unrest / (float)kingdom.cities.Count, republicanNeighbours,
            kingdom.GetOrCreate().scenario_monarchy);
    }

    // 独立君主国掷一次共和革命；成了改现代政体，史书记一笔(组建政府交给 ModernStateFormationSystem)
    public static bool TryRevolution(Kingdom kingdom)
    {
        float chance = RevolutionChance(kingdom);
        if (chance <= 0f || UnityEngine.Random.value >= chance) return false;
        string name = kingdom.GetKingdomName();
        kingdom.SetRegimeType(RegimeType.Modern);
        kingdom.LoadRegime();
        kingdom.SystemChange();
        kingdom.GetOrCreate().scenario_monarchy = false;
        string text = string.Format(LM.Get("state_republican_revolution_history"), name, kingdom.king?.getName() ?? LM.Get("label_none"));
        EventRecorder.Record(kingdom, text, kingdom.king);
        TranslateHelper.LogEventMessage(text, kingdom);
        LogService.LogInfo($"[EmpireCraft][近代化] {name} 共和革命(机会 {chance:P1})");
        return true;
    }

    #endregion

    #region 立宪示范

    // 从 AI 发起立宪要的议会支持里扣掉的示范分：本文化已有国家立宪、相邻的立宪国与共和国
    public static float ConstitutionSupportBonus(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null) return 0f;
        bool pioneer = ConstitutionalEconomySystem.IsCultureConstitutional(empire);
        var neighbours = new HashSet<Kingdom>();
        foreach (City city in empire.cities_list ?? core.cities ?? new List<City>())
            if (city?.neighbours_kingdoms != null)
                foreach (Kingdom other in city.neighbours_kingdoms)
                    if (other?.data != null && !other.isRekt() && !other.wild && other.GetEmpire() != empire) neighbours.Add(other);
        int modern = neighbours.Select(other => other.GetEmpire()?.CoreKingdom ?? other).Distinct().Count(other =>
            IsModern(other) || other.GetEmpire() is Empire state && ConstitutionalEconomySystem.HasConstitution(state));
        return ModernizationRules.ConstitutionDemonstration(pioneer, modern);
    }

    #endregion
}

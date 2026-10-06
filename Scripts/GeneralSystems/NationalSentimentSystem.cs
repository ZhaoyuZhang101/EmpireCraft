using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 民族维度：民族主义不是光谱上的一个理念，而是叠加在所有理念之上的一层(民族资本主义、民族共产主义……)。
//
// 一、民族情绪(每座城 0~100，CityExtraData.national_sentiment)，每年结算：
//     · 受异族统治(城市主流文化 ≠ 统治者的文化)：+6；宪法"民族主义立国"的同化压力再 +3，"多民族共存"则 -3；
//     · 遭外族入侵(本国正被异族国家进攻)：+8；城里有异族军队占领：再 +4；宪法"民族主义立国"时入侵激起的情绪翻倍；
//     · 以上都没有：回落 5(退潮)。
// 二、独立运动：帝国成员国的都城民族情绪 ≥ 70、文化又不同于统治者时，每年有概率宣布独立并与中央开战。
// 三、民族统一战线：同一帝国核心里有多个理念的政府(见 WarlordEraSystem)，遇外族入侵且民族情绪高涨时，
//     各政府停止内战、共同抗敌(加入同一阵营)，期间互不宣战；外敌消失满 FrontCooldownYears 年，统一战线破裂，内战重启。
// 四、民族主义色彩(政党)：国家民族情绪越高、理念越偏民族主义越浓；色彩浓的政党在民族情绪高时多拿票(PartySystem)。
// 每年由 ConstitutionalEconomySystem 调用一次(全图)。
public static class NationalSentimentSystem
{
    private const float ForeignRuleGain = 6f;
    private const float InvasionGain = 8f;
    private const float OccupationGain = 4f;
    private const float Decay = 5f;
    private const float IndependenceThreshold = 70f;
    private const float FrontThreshold = 50f;
    private const int FrontCooldownYears = 3;

    private static object _world;
    private static double _lastScan = -1d;
    private static readonly Dictionary<long, float> EmpireSentiment = new();

    public static float GetCity(City city) => city?.GetOrCreate()?.national_sentiment ?? 0f;

    // 国家的民族情绪：各城平均(年度结算时缓存)
    public static float GetEmpire(Empire empire) =>
        empire != null && EmpireSentiment.TryGetValue(empire.id, out float value) ? value : 0f;

    public static bool InUnitedFront(EmpireCore core) => core != null && core.united_front_since >= 0d;

    // 两个政权是否同在一个民族统一战线里(统一战线期间互不宣战)
    public static bool InSameUnitedFront(Kingdom a, Kingdom b)
    {
        EmpireCore coreA = FrontCore(a?.GetEmpire());
        return coreA != null && InUnitedFront(coreA) && coreA == FrontCore(b?.GetEmpire());
    }

    // 政党的民族主义色彩 0~100：国家民族情绪 × 0.8 + 理念倾向 + 宪法民族政策
    public static float PartyColor(Empire empire, PartyIdeology ideology)
    {
        float bias = ideology switch
        {
            PartyIdeology.Fascism => 40f,
            PartyIdeology.Conservatism or PartyIdeology.Authoritarianism => 20f,
            PartyIdeology.ConservativeLiberalism or PartyIdeology.ReligiousDemocracy => 10f,
            PartyIdeology.Centrism or PartyIdeology.Socialism => 5f,
            PartyIdeology.SocialDemocracy => -5f,
            PartyIdeology.SocialLiberalism => -10f,
            PartyIdeology.Libertarianism => -15f,
            PartyIdeology.Anarchism => -25f,
            _ => 0f
        };
        bias += ConstitutionSystem.GetNation(empire) switch
        {
            ConstitutionNation.Nationalist => 15f,
            ConstitutionNation.Pluralist => -15f,
            _ => 0f
        };
        return Mathf.Clamp(GetEmpire(empire) * 0.8f + bias, 0f, 100f);
    }

    // 色彩分级的文本键：浓 / 中 / 淡
    public static string ColorKey(float color) =>
        color >= 60f ? "nation_color_strong" : color >= 30f ? "nation_color_medium" : "nation_color_weak";

    // 选票加成：国家民族情绪高时，色彩浓的政党最多多拿 40% 的票
    public static float VoteFactor(Empire empire, PartyIdeology ideology) =>
        1f + 0.4f * GetEmpire(empire) / 100f * PartyColor(empire, ideology) / 100f;

    public static void UpdateWorld()
    {
        if (World.world?.cities == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _lastScan = -1d;
            EmpireSentiment.Clear();
        }
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        try
        {
            UpdateCities();
            UpdateIndependence();
            UpdateUnitedFronts(now);
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft] 民族情绪结算失败: {exception.Message}");
        }
    }

    #region 民族情绪

    // 统治者的文化：所在帝国的统治文化；独立国家按本国文化
    public static string RulerCulture(City city)
    {
        Kingdom kingdom = city?.kingdom;
        if (kingdom == null || kingdom.isRekt()) return "";
        Empire empire = kingdom.GetEmpire();
        return empire != null && !empire.isRekt()
            ? CompositeEmpireService.GetRulingCulture(empire)
            : CultureService.GetRealmCulture(kingdom);
    }

    // 受异族统治：城市主流文化与统治者的文化不同
    public static bool IsForeignRuled(City city)
    {
        string cityCulture = CultureService.GetMainCulture(city);
        string rulerCulture = RulerCulture(city);
        return CultureService.IsValidCulture(cityCulture) && CultureService.IsValidCulture(rulerCulture) &&
               !string.Equals(cityCulture, rulerCulture, StringComparison.Ordinal);
    }

    public static bool IsInvadedCity(City city)
    {
        string cityCulture = CultureService.GetMainCulture(city);
        return city?.kingdom != null && CultureService.IsValidCulture(cityCulture) && IsInvaded(city.kingdom, cityCulture);
    }

    private static void UpdateCities()
    {
        EmpireSentiment.Clear();
        var totals = new Dictionary<long, (float sum, int count)>();
        foreach (City city in World.world.cities.ToList())
        {
            if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.isRekt()) continue;
            CityExtension.CityExtraData data = city.GetOrCreate();
            string cityCulture = CultureService.GetMainCulture(city);
            Empire empire = city.kingdom.GetEmpire();
            string rulerCulture = empire != null && !empire.isRekt()
                ? CompositeEmpireService.GetRulingCulture(empire)
                : CultureService.GetRealmCulture(city.kingdom);
            ConstitutionNation policy = ConstitutionSystem.GetNation(empire);
            bool valid = CultureService.IsValidCulture(cityCulture) && CultureService.IsValidCulture(rulerCulture);
            bool foreignRule = valid && !string.Equals(cityCulture, rulerCulture, StringComparison.Ordinal);
            bool invaded = valid && IsInvaded(city.kingdom, cityCulture);
            bool occupied = valid && city.GetOccupiedStatus().Keys.Any(occupier =>
                occupier != null && !string.Equals(CultureService.GetRealmCulture(occupier), cityCulture, StringComparison.Ordinal));
            float delta = 0f;
            if (foreignRule)
                delta += ForeignRuleGain + policy switch
                {
                    ConstitutionNation.Nationalist => 3f,
                    ConstitutionNation.Pluralist => -3f,
                    _ => 0f
                };
            if (invaded) delta += InvasionGain * (policy == ConstitutionNation.Nationalist && !foreignRule ? 2f : 1f);
            if (occupied) delta += OccupationGain;
            if (!foreignRule && !invaded && !occupied) delta = -Decay;
            data.national_sentiment = Mathf.Clamp(data.national_sentiment + delta, 0f, 100f);
            if (empire == null) continue;
            totals.TryGetValue(empire.id, out var total);
            totals[empire.id] = (total.sum + data.national_sentiment, total.count + 1);
        }
        foreach (var pair in totals) EmpireSentiment[pair.Key] = pair.Value.sum / Math.Max(1, pair.Value.count);
    }

    // 本国正被异族国家进攻(本国是守方，进攻方的文化与本城不同)
    private static bool IsInvaded(Kingdom kingdom, string cityCulture) =>
        kingdom.getWars().Any(war => war != null && !war.hasEnded() && war.isDefender(kingdom) &&
                                     war._list_attackers.Any(attacker => attacker != null && !attacker.isRekt() &&
                                         !string.Equals(CultureService.GetRealmCulture(attacker), cityCulture,
                                             StringComparison.Ordinal)));

    #endregion

    #region 独立运动

    private static void UpdateIndependence()
    {
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.CoreKingdom == null || empire.isRekt() || empire.IsArchived()) continue;
            string rulerCulture = CompositeEmpireService.GetRulingCulture(empire);
            Kingdom core = empire.CoreKingdom;
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (member == null || member.isRekt() || member == core || !member.hasKing() || member.capital == null ||
                    member.IsFactionRebelling() || member.IsLocalRebelling() || member.getWars().Any()) continue;
                float sentiment = GetCity(member.capital);
                if (sentiment < IndependenceThreshold) continue;
                string memberCulture = CultureService.GetMainCulture(member.capital);
                if (!CultureService.IsValidCulture(memberCulture) ||
                    string.Equals(memberCulture, rulerCulture, StringComparison.Ordinal)) continue;
                float chance = 0.03f + 0.15f * (sentiment - IndependenceThreshold) / (100f - IndependenceThreshold);
                if (ModernStability.IsModern(core)) chance *= ModernStability.RebellionFactor;
                if (UnityEngine.Random.value >= chance) continue;
                empire.leave(member);
                War war = DiplomacyHelpers.wars.newWar(member, core, WarTypeLibrary.normal);
                war?.SetEmpireWarType(EmpireWarType.地方独立);
                EventRecorder.Record(empire, actor: member.king, logKingdom: member, text: string.Format(
                    LM.Get("nation_independence_history"), member.GetKingdomFullName(),
                    memberCulture.GetCultureTranslate(), empire.GetEmpireFullName()));
            }
        }
    }

    #endregion

    #region 民族统一战线

    // 统一战线以帝国核心为单位：军阀时期另立的政府挂在父核心下(warlord_parent_core_id)
    private static EmpireCore FrontCore(Empire empire)
    {
        EmpireCore core = empire == null ? null : EmpireCoreManager.Get(empire);
        if (core?.warlord_parent_core_id > 0) return EmpireCoreManager.Get(core.warlord_parent_core_id) ?? core;
        return core;
    }

    private static List<Empire> CoreGovernments(EmpireCore core) =>
        ModClass.EMPIRE_MANAGER.Where(empire => empire?.CoreKingdom != null && !empire.isRekt() && !empire.IsArchived() &&
                                                FrontCore(empire) == core).ToList();

    private static void UpdateUnitedFronts(double now)
    {
        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values.ToList())
        {
            if (core == null || core.warlord_parent_core_id > 0) continue;
            List<Empire> governments = CoreGovernments(core);
            if (governments.Count < 2)
            {
                if (InUnitedFront(core)) EndFront(core, governments, restartCivilWar: false);
                continue;
            }
            HashSet<Kingdom> camp = governments.Select(government => government.CoreKingdom).ToHashSet();
            List<War> foreignWars = governments.SelectMany(government => government.CoreKingdom.getWars())
                .Where(war => war != null && !war.hasEnded() && IsForeignWar(war, camp, core.default_culture))
                .Distinct().ToList();
            float sentiment = governments.Average(GetEmpire);
            float threshold = governments.Any(government =>
                ConstitutionSystem.GetNation(government) == ConstitutionNation.Nationalist) ? FrontThreshold - 10f
                : governments.All(government => ConstitutionSystem.GetNation(government) == ConstitutionNation.Pluralist)
                    ? FrontThreshold + 10f
                    : FrontThreshold;

            if (InUnitedFront(core))
            {
                if (foreignWars.Count > 0)
                {
                    core.united_front_last_threat = now;
                    JoinForeignWars(governments, foreignWars);
                }
                else if (Date.getYearsSince(core.united_front_last_threat) >= FrontCooldownYears)
                    EndFront(core, governments, restartCivilWar: true);
                continue;
            }
            if (foreignWars.Count == 0 || sentiment < threshold) continue;
            // 组成统一战线：停止内战，共同抗敌
            foreach (Empire a in governments)
            foreach (Empire b in governments)
                if (a != b && a.CoreKingdom.isInWarWith(b.CoreKingdom)) a.CoreKingdom.EndWarWith(b.CoreKingdom);
            core.united_front_since = now;
            core.united_front_last_threat = now;
            JoinForeignWars(governments, foreignWars);
            string enemy = foreignWars[0]._list_attackers.Concat(foreignWars[0]._list_defenders)
                .FirstOrDefault(kingdom => kingdom != null && !camp.Contains(kingdom))?.GetKingdomFullName() ?? "";
            EventRecorder.Record(governments[0], string.Format(LM.Get("nation_front_formed_history"),
                string.Join("、", governments.Select(government => government.GetEmpireFullName())), enemy));
        }
    }

    // 与核心文化不同的外族交战(对方不在本核心的政府里)
    private static bool IsForeignWar(War war, HashSet<Kingdom> camp, string coreCulture) =>
        war._list_attackers.Concat(war._list_defenders).Any(kingdom => kingdom != null && !kingdom.isRekt() &&
            !camp.Contains(kingdom) && !kingdom.IsInSameEmpire(camp.First()) &&
            !string.Equals(CultureService.GetRealmCulture(kingdom), coreCulture, StringComparison.Ordinal)) &&
        war._list_attackers.Concat(war._list_defenders).Any(camp.Contains);

    // 统一战线的各政府加入同一阵营抗敌
    private static void JoinForeignWars(List<Empire> governments, List<War> wars)
    {
        foreach (War war in wars)
        {
            bool defenders = governments.Any(government => war.isDefender(government.CoreKingdom));
            foreach (Empire government in governments)
            {
                Kingdom kingdom = government.CoreKingdom;
                if (war.hasKingdom(kingdom)) continue;
                if (defenders) war.joinDefenders(kingdom);
                else war.joinAttackers(kingdom);
            }
        }
    }

    // 统一战线破裂(外敌消失满三年)：内战重启——中央与最强的另一理念政府重新开战
    private static void EndFront(EmpireCore core, List<Empire> governments, bool restartCivilWar)
    {
        core.united_front_since = -1d;
        core.united_front_last_threat = -1d;
        if (!restartCivilWar || governments.Count < 2) return;
        Empire central = governments.FirstOrDefault(government => government.id == core.central_empire_id) ??
                         governments.OrderByDescending(government => government.countCities()).First();
        Empire rival = governments.Where(government => government != central)
            .OrderByDescending(government => government.countCities()).First();
        if (!central.CoreKingdom.isInWarWith(rival.CoreKingdom))
            World.world.diplomacy.startWar(rival.CoreKingdom, central.CoreKingdom, WarTypeLibrary.normal);
        EventRecorder.Record(central, string.Format(LM.Get("nation_front_broken_history"),
            central.GetEmpireFullName(), rival.GetEmpireFullName()));
    }

    #endregion
}

using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宪政合法性：现代国家不靠天命正统，统治合法性来自宪政秩序本身——
// 议会/人民代表大会把不满吸纳进体制内，民意决定人民是否认可政府，政府是否稳固、国库是否充盈。
// 现代政体与召开议会的君主立宪国用它代替正统(Empire.Legitimacy)，决定城市忠诚、附庸态度、叛乱与革命；
// 正统仍在后台变化(战功、继承等)，只按 MandateWeight 计入一小部分。
// 军阀时期的临时政府、共和过渡期仍按正统争夺中央，不适用。
public static class ModernLegitimacy
{
    private const int Base = 55;
    private const int Representation = 15;
    private const int Constitution = 5;
    private const int Suspended = -25;
    private const int StableGovernment = 5;
    private const int MinorityGovernment = -5;
    private const int Bankrupt = -10;
    // 文官制度：职业文官中立廉洁 / 政党分肥任人唯党 / 分肥制换届后两年的官员大换血
    private const int ProfessionalService = 5;
    private const int SpoilsService = -5;
    private const int SpoilsTurnover = -10;
    private const float MandateWeight = 0.2f;
    // 军警镇压街头抗争后两年
    private const int Repression = -10;

    // 城市忠诚、好感、叛乱判定每次都要读 Empire.Legitimacy，大人口时每秒成千上万次：
    // 是否适用与数值按帝国缓存 CacheSeconds 现实秒(各项来源都是按年变化的，短暂滞后无妨)，
    // 避免反复新建来源列表产生 GC 垃圾。切换世界时清空；提示框里的来源明细(Breakdown)不走缓存
    private const float CacheSeconds = 1f;
    private static readonly Dictionary<long, (bool applies, int value, float at)> Cache = new();
    private static object _cacheWorld;

    private static (bool applies, int value) Cached(Empire empire)
    {
        if (!ReferenceEquals(_cacheWorld, World.world))
        {
            _cacheWorld = World.world;
            Cache.Clear();
        }
        float now = Time.unscaledTime;
        if (Cache.TryGetValue(empire.id, out var cached) && now - cached.at < CacheSeconds && now >= cached.at)
            return (cached.applies, cached.value);
        bool applies = ComputeApplies(empire);
        int value = 0;
        if (applies)
        {
            foreach ((string _, int amount) in Breakdown(empire)) value += amount;
            value = Mathf.Clamp(value, 0, 100);
        }
        Cache[empire.id] = (applies, value, now);
        return (applies, value);
    }

    // 立即作废某个帝国(或全部)的缓存，供需要马上看到新数值的地方调用
    public static void Invalidate(Empire empire = null)
    {
        if (empire == null) Cache.Clear();
        else Cache.Remove(empire.id);
    }

    public static bool Applies(Empire empire) =>
        empire?.CoreKingdom != null && empire.data != null && Cached(empire).applies;

    public static int Get(Empire empire) => empire?.data == null ? 0 : Cached(empire).value;

    private static bool ComputeApplies(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.data == null) return false;
        if (WarlordEraSystem.IsProvisionalGovernment(empire) || RepublicSystem.IsTransitioning(empire)) return false;
        return ModernStability.IsModern(empire.CoreKingdom) || ParliamentSystem.HasParliament(empire);
    }

    // 各项构成(界面悬停说明用)：(本地化 key, 数值)
    public static List<(string key, int value)> Breakdown(Empire empire)
    {
        var items = new List<(string key, int value)> { ("legitimacy_base", Base) };
        if (ParliamentSystem.HasParliament(empire))
        {
            items.Add((PartyBanSystem.UsesDemocraticCentralism(empire) ? "legitimacy_congress" : "legitimacy_parliament",
                Representation));
            string type = empire.data.constitutional_economy?.government_type ?? "";
            if (type == ParliamentSystem.GovernmentMinority) items.Add(("legitimacy_minority", MinorityGovernment));
            else if (!string.IsNullOrEmpty(type)) items.Add(("legitimacy_stable_government", StableGovernment));
        }
        ConstitutionData constitution = ConstitutionSystem.Get(empire);
        if (constitution != null && !constitution.provisional)
            items.Add(constitution.suspended ? ("legitimacy_suspended", Suspended) : ("legitimacy_constitution", Constitution));
        int opinion = PublicOpinionSystem.GetLevel(empire) switch
        {
            PublicOpinionSystem.Content => 10,
            PublicOpinionSystem.Dissidents => 0,
            PublicOpinionSystem.CivilResistance => -15,
            _ => -35
        };
        if (opinion != 0) items.Add(("legitimacy_public_opinion", opinion));
        if (empire.data.constitutional_economy?.bankrupt_since >= 0d) items.Add(("legitimacy_bankrupt", Bankrupt));
        if (constitution != null && !constitution.provisional && ParliamentSystem.ControlsMinistries(empire))
        {
            ConstitutionCivilService civilService = ParliamentSystem.CivilService(empire);
            if (civilService == ConstitutionCivilService.Professional)
                items.Add(("legitimacy_civil_service_professional", ProfessionalService));
            else if (civilService == ConstitutionCivilService.Spoils)
                items.Add(("legitimacy_civil_service_spoils", SpoilsService));
            if (ParliamentSystem.InSpoilsTurnover(empire)) items.Add(("legitimacy_spoils_turnover", SpoilsTurnover));
        }
        if (PartyBanSystem.HasConsultation(empire))
            items.Add(("legitimacy_consultation", PartyBanSystem.ConsultationLegitimacy));
        int street = HarshRuleSystem.LegitimacyPenalty(empire);
        if (street != 0) items.Add(("legitimacy_street_unrest", street));
        if (HarshRuleSystem.RecentlyRepressed(empire)) items.Add(("legitimacy_repression", Repression));
        // 原始事件值，与前现代治理正统分开，避免两套来源模型互相调用。
        int mandate = Mathf.RoundToInt((empire.data.Mandate - 50) * MandateWeight);
        if (mandate != 0) items.Add(("legitimacy_mandate", mandate));
        return items;
    }
}

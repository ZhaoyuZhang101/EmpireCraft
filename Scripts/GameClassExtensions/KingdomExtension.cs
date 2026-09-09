using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using HarmonyLib;
using NCMS.Extensions;
using UnityEngine;
using Random = System.Random;

namespace EmpireCraft.Scripts.GameClassExtensions;

public class TemporaryPushProgress
{
    public const int InfluenceCostPerYear = 50;
    public const int SupportBaseMoneyCost = 50;
    public bool StartedToPushTf = false;
    public TemporaryFactionType TfType = TemporaryFactionType.供养宗室;
    public int Progress = 0;
    public long pusher = -1L;
    public List<(double date, long actor, int duration)> Supporters = new  List<(double, long, int)>();
    public double StartTimestamp = -1L;
    public TemporaryFaction PendingClaim;
    public static int CalcPolicyProgressAdd(double p, int currentProgress)
    {
        int add;

        if (p < 200) add = 1;
        else if (p < 400) add = 2;
        else if (p < 700) add = 3;
        else if (p < 1000) add = 4;
        else add = 5;

        // 接近封顶时减速，防止后期涨太快
        if (currentProgress >= 90) add = Math.Min(add, 1);
        else if (currentProgress >= 75) add = Math.Min(add, 2);
        else if (currentProgress >= 60) add = Math.Min(add, 3);

        return add;
    }
    /// <summary>
    /// 获取发起人
    /// </summary>
    /// <returns></returns>
    public Actor GetPusher()
    {
        return World.world.units.get(pusher);
    }
    /// <summary>
    /// 为当前诉求添加支持者
    /// </summary>
    /// <param name="supporter">支持者</param>
    /// <param name="duration">持续时间/年</param>
    public void AddSupporter(Actor supporter, int duration)
    {
        if (supporter == null || supporter.isRekt() || duration <= 0 || supporter.id == pusher) return;
        Supporters ??= new List<(double date, long actor, int duration)>();
        if (Supporters.Any(s => s.actor == supporter.id)) return;
        this.Supporters.Add((World.world.getCurWorldTime(), supporter.id, duration));
    }
    public bool HasSupporter(Actor supporter)
    {
        return supporter != null && Supporters.Any(s => s.actor == supporter.id);
    }
    public void Stop()
    {
        StartedToPushTf = false;
        Progress = 0;
        pusher = -1L;
        StartTimestamp = -1L;
        Supporters?.Clear();
        PendingClaim = null;
    }
    /// <summary>
    /// 开始推动
    /// </summary>
    /// <param name="pActor"></param>
    public void StartToPush(Actor pActor, TemporaryFactionType tfType)
    {
        if (StartedToPushTf || !IsLocalPusher(pActor)) return;
        var faction = pActor.GetFaction();
        var template = faction?.TemporaryFactions?.Find(tf => tf != null && tf.type == tfType);
        if (faction == null || faction.Ban || template == null || !template.Active || !template.canBePushByLocal ||
            template.CountDown > 0 || template.IsStarted()) return;
        var request = template.Clone(faction);
        request.SetEmpire(pActor.kingdom.GetEmpire());
        request.SetKingdom(pActor.kingdom);
        request.pusherType = MetaType.Kingdom;
        if (!request.CheckLocalCondition(pActor.kingdom) || !request.CheckTarget()) return;
        PendingClaim = request;
        Supporters = new List<(double date, long actor, int duration)>();
        pusher = pActor.id;
        TfType = tfType;
        StartTimestamp = World.world.getCurWorldTime();
        StartedToPushTf = true;
        Progress = 0;
    }
    /// <summary>
    /// 推动诉求
    /// </summary>
    /// <param name="pActor"></param>
    public void Push(Actor pActor)
    {
        if (pActor?.id == pusher) PushOneMonths();
    }
    public int ConsumeInfluenceAndCalcAdd(Actor actor)
    {
        if (actor == null || actor.isRekt()) return 0;
        if (actor.data == null || actor.data.renown < InfluenceCostPerYear) return 0;
        actor.data.renown -= InfluenceCostPerYear;
        return CalcPolicyProgressAdd(actor.GetIdentity()?.TotalPerformance ?? 0, Progress);
    }

    public int PushOneMonths()
    {
        if (!StartedToPushTf) return 0;
        if (!ValidateRequest()) { Stop(); return 0; }
        if (Progress >= 100) { Execute(); return 0; }
        if (StartTimestamp >= 0 && Date.getMonthsSince(StartTimestamp) < 1) return 0;
        int totalAdd = 0;
        var pusherActor = GetPusher();
        totalAdd += ConsumeInfluenceAndCalcAdd(pusherActor);

        Supporters ??= new List<(double date, long actor, int duration)>();
        for (int i = Supporters.Count - 1; i >= 0; i--)
        {
            var record = Supporters[i];
            var supporter = World.world.units.get(record.actor);
            if (supporter == null || supporter.isRekt() || record.duration <= 0 ||
                supporter.kingdom?.GetEmpire() != pusherActor.kingdom.GetEmpire() ||
                supporter.GetFaction() != pusherActor.GetFaction() || supporter.id == pusher)
            {
                Supporters.RemoveAt(i);
                continue;
            }

            // duration is in years; the contribution itself is monthly.
            if (Date.getYearsSince(record.date) >= record.duration)
            {
                Supporters.RemoveAt(i);
                continue;
            }
            if (Date.getMonthsSince(record.date) < 1 || Progress + totalAdd >= 100) continue;
            totalAdd += ConsumeInfluenceAndCalcAdd(supporter);
        }

        Progress = Math.Min(100, Progress + totalAdd);
        StartTimestamp = World.world.getCurWorldTime();
        if (Progress >= 100)
        {
            Execute();
        }
        return totalAdd;
    }

    public void Execute()
    {
        if (!StartedToPushTf || Progress < 100) return;
        if (!ValidateRequest()) { Stop(); return; }
        var pPusher = GetPusher();
        Kingdom kingdom = pPusher.kingdom;
        var faction = pPusher.GetFaction();
        var claim = faction.TemporaryFactions.Find(tf => tf != null && tf.type == TfType);
        var empire = kingdom.GetEmpire();
        if (empire.RunningTemporaryFaction?.IsStarted() == true || claim.IsStarted() || claim.CountDown > 0) return;
        var factions = empire.CoreKingdom?.GetRegime()?.GetPlayerFactions();
        if (factions == null || factions.Any(f => f?.TemporaryFactions?.Any(tf => tf?.IsStarted() == true) == true)) return;
        if (claim.ShowAsPlot && empire.Emperor?.plot?.isActive() == true) return;
        claim.SetEmpire(empire);
        claim.SetKingdom(kingdom);
        claim.pusherType = MetaType.Kingdom;
        claim.TargetID = PendingClaim.TargetID;
        claim.TargetType = PendingClaim.TargetType;
        claim.Start();
        if (claim.IsStarted()) Stop();
    }

    public static bool IsLocalPusher(Actor actor)
    {
        if (actor == null || actor.isRekt() || !actor.isKing()) return false;
        var kingdom = actor.kingdom;
        var empire = kingdom?.GetEmpire();
        return empire != null && !empire.isRekt() && kingdom != empire.CoreKingdom &&
            kingdom.king == actor && !kingdom.isInWarWith(empire.CoreKingdom);
    }

    private bool ValidateRequest()
    {
        var actor = GetPusher();
        if (!IsLocalPusher(actor) || PendingClaim == null || PendingClaim.type != TfType) return false;
        var faction = actor.GetFaction();
        var template = faction?.TemporaryFactions?.Find(tf => tf != null && tf.type == TfType);
        if (faction == null || faction.Ban || template == null || !template.Active || !template.canBePushByLocal ||
            PendingClaim.factionID != faction.GetID() || PendingClaim.EmpireID != actor.kingdom.GetEmpire().id ||
            PendingClaim.KingdomID != actor.kingdom.id) return false;
        return PendingClaim.CheckLocalContinue(actor.kingdom) && PendingClaim.CheckContinue() && PendingClaim.CheckTarget();
    }

    public static void TrySubmitReadyRequests(Empire empire)
    {
        foreach (var request in empire.kingdoms_list.Where(k => k != null && !k.isRekt() && k != empire.CoreKingdom)
            .Select(k => k.GetOrCreate().PushProgress).Where(p => p != null && p.StartedToPushTf && p.Progress >= 100)
            .OrderBy(p => p.StartTimestamp).ThenBy(p => p.pusher).ToList())
        {
            request.Execute();
            if (empire.RunningTemporaryFaction?.IsStarted() == true) return;
        }
    }

    public override string ToString ()
    {
        var text = $"\n正在推进: {TfType.ToString()} | 进度: {Progress.ToString()}%";
        if (StartedToPushTf) return text;
        return "";
    }
}
public static class KingdomExtension
{
    public static readonly SemaphoreSlim _sem = new SemaphoreSlim(Environment.ProcessorCount);
    public class KingdomExtraData: ExtraDataBase
    {
        public long EmpireID = -1L;
        public double TimestampEmpire = -1L;
        public double TimestampBeFeifed = -1L;
        public long HeirID = -1L;
        public int Level = 2;
        [JsonIgnore]
        public Regime regime;
        public RegimeType regimeType;
        public KingdomType kingdomType;
        public string core_name = "";
        public string core_name_source = "";
        public SpecificClan kingdomSpecificClan;
        public int Money = 0;
        public long CenterArmID = -1L;
        [JsonIgnore]
        public Task<(Actor, string)> CalcTask;
        //拥有法理
        public long MainTitle = -1L;
        //王国已经确立的法理随王位继承，不依赖现任国王的个人继承关系。
        public List<long> RealmTitles = new List<long>();
        //国家创建时的随机名称；失去或尚未取得法理时恢复使用。
        public string initial_random_name = "";
        //律令制行政区受托管理的法理范围，不代表头衔所有权。
        public long AdministrativeTitle = -1L;
        //想要索取的法理
        public List<long> WantedTitle = new List<long>();
        public int IndependentValue = 50;
        public bool is_need_to_choose_heir = false;
        public double corruption_rate = 0.0f;
        public double last_exam_timestamp = -1L;
        public bool isFactionRebelling = false;
        public bool isLocalRebelling = false;
        // Persist every empire this polity has rebelled against; ending a war must not permit re-entry.
        public List<long> rebellion_origin_empire_ids = new List<long>();
        // 地方叛乱自动吸纳政权时可再接收的城市数。
        public int rebellion_auto_expand_remaining = -1;
        public bool isNeedToMaintainGoodOpinion = false;
        public double last_tax_timestamp = -1L;
        public double last_office_exam_timestamp = -1L;
        public double corruption_timestamp = -1L;
        public EmpireHeirLawType HeirLaw = EmpireHeirLawType.eldest_child;
        public EmpireHeirLawType DefaultHeirLaw = EmpireHeirLawType.eldest_child;
        public string faction_rebel_original_name = "";
        public LawType main_crime = default;
        public TemporaryPushProgress PushProgress = new TemporaryPushProgress()
        {
            StartedToPushTf = false,
            Progress = 0
        };
        public double last_system_change_timestamp = -1L;
        //上一次加入岁币联盟的时间
        public double last_given_alliance_timestamp = -1L;
        //上一次加入朝贡国的时间
        public double last_taken_alliance_timestamp = -1L;
        public double last_kingdom_status_ts = -1L;
        public double last_tf_check_ts = -1L;
        public double last_plots_check_ts = -1L;
        public double last_cabinet_check_ts = -1L;
        public double last_religion_check_ts = -1L;
        [JsonIgnore]
        public List<War> cached_wars = new List<War>();
        public double last_wars_ts = -1L;

        public int selfChangeRegimePlotsCountDown = 0;
        //岁币国
        public long given_empire = -1L;
        //宗主国
        public long taken_empire = -1L;
        //上一次朝贡时间
        public long last_taken_time = -1L;
        //退出朝贡国倾向
        public float leave_taken_alliance_preference = 0.0f;
        public Dictionary<long, int> local_claim_failed_opinion = new Dictionary<long, int>();
        public long office_id = -1L;
        public bool isEmpire = false;
        public int cached_warriors = 0;
        public int cached_population = 0;
        public double last_cached_timestamp = -1L;
        public int annual_power_population = 0;
        public int annual_power_military = 0;
        public int annual_power_economy = 0;
        public double annual_power_index = 0d;
        public double last_national_power_timestamp = -1L;
        public double last_faction_ratio_growth_timestamp = -1L;
        public Dictionary<string, float> faction_ratio_growth_progress = new Dictionary<string, float>();
        
        public Dictionary<FixedFaction, int> FactionRatio = new Dictionary<FixedFaction, int>();
    }
    public static LawType GetMainCrime(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().main_crime;
    }
    public static Dictionary<FixedFaction, int> GetFactionRatio(this Kingdom kingdom)
    {
        if (kingdom.isRekt())
        {
            return null;
        }

        return kingdom.GetOrCreate().FactionRatio;
    }
    public static void RemoveFactionRatio(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().FactionRatio.Clear();
    }

    public static void ReconcileFactionRatios(this Kingdom kingdom, IEnumerable<FixedFaction> factions)
    {
        if (kingdom == null || kingdom.isRekt() || factions == null) return;
        KingdomExtraData data = kingdom.GetOrCreate();
        Dictionary<FixedFaction, int> current = data.FactionRatio ?? new Dictionary<FixedFaction, int>();
        var reconciled = new Dictionary<FixedFaction, int>();
        var used = new HashSet<FixedFaction>();

        foreach (FixedFaction faction in factions.Where(faction => faction != null))
        {
            FixedFaction source = current.Keys.FirstOrDefault(candidate => candidate == faction);
            source ??= current.Keys.FirstOrDefault(candidate => candidate != null && !used.Contains(candidate) &&
                candidate.GetID() == faction.GetID());
            source ??= current.Keys.FirstOrDefault(candidate => candidate != null && !used.Contains(candidate) &&
                candidate.Type == faction.Type && candidate.Name == faction.Name);
            reconciled[faction] = source == null ? 0 : Mathf.Max(0, current[source]);
            if (source != null) used.Add(source);
        }

        data.FactionRatio = reconciled;
        kingdom.ClampFactionRatio();
    }

    public const float FactionLeaderInfluenceForMaximumGrowth = 1000f;
    public const int MaximumAnnualFactionRatioGrowth = 2;

    public static float CalculateAnnualFactionRatioGrowth(int leaderInfluence)
    {
        float normalizedInfluence = Mathf.Clamp01(Mathf.Max(0, leaderInfluence) /
                                                   FactionLeaderInfluenceForMaximumGrowth);
        return 1f + normalizedInfluence;
    }

    /// <summary>
    /// 结算直隶派系的年度自然竞争。小数增长会跨年累积，满额后沿用统一占比接口挤压其他派系。
    /// </summary>
    public static bool ApplyAnnualFactionLeaderGrowth(this Kingdom kingdom, IEnumerable<FixedFaction> factions)
    {
        if (kingdom == null || kingdom.isRekt() || factions == null) return false;
        KingdomExtraData data = kingdom.GetOrCreate();
        double now = World.world.getCurWorldTime();
        if (data.last_faction_ratio_growth_timestamp < 0)
        {
            data.last_faction_ratio_growth_timestamp = now;
            return false;
        }
        if (Date.getYearsSince(data.last_faction_ratio_growth_timestamp) < 1) return false;
        data.last_faction_ratio_growth_timestamp = now;

        List<FixedFaction> configuredFactions = factions
            .Where(faction => faction != null)
            .ToList();
        List<(FixedFaction faction, Actor leader, int influence)> activeFactions = configuredFactions
            .Where(faction => !faction.Ban)
            .Select(faction => (faction, leader: faction.GetLeader()))
            .Where(entry => entry.leader != null)
            .Select(entry => (entry.faction, entry.leader, influence: Mathf.Max(0, entry.leader.data.renown)))
            .OrderBy(entry => entry.influence)
            .ThenBy(entry => entry.faction.GetID())
            .ToList();
        if (activeFactions.Count == 0) return false;

        kingdom.ReconcileFactionRatios(configuredFactions);
        data.faction_ratio_growth_progress ??= new Dictionary<string, float>();
        var activeIds = new HashSet<string>(activeFactions.Select(entry => entry.faction.GetID()));
        foreach (string staleId in data.faction_ratio_growth_progress.Keys
                     .Where(id => !activeIds.Contains(id)).ToList())
        {
            data.faction_ratio_growth_progress.Remove(staleId);
        }

        bool changed = false;
        foreach (var entry in activeFactions)
        {
            string factionId = entry.faction.GetID();
            data.faction_ratio_growth_progress.TryGetValue(factionId, out float carriedGrowth);
            float accumulatedGrowth = carriedGrowth + CalculateAnnualFactionRatioGrowth(entry.influence);
            int increase = Mathf.Min(MaximumAnnualFactionRatioGrowth, Mathf.FloorToInt(accumulatedGrowth));
            data.faction_ratio_growth_progress[factionId] = accumulatedGrowth - increase;
            if (increase > 0 && kingdom.TryIncreaseFactionRatio(entry.faction, increase)) changed = true;
        }
        return changed;
    }
    /// <summary>
    /// 增加派系占比
    /// </summary>
    /// <param name="kingdom">王国</param>
    /// <param name="faction">派系</param>
    /// <param name="increaseRatio">增长的数值</param>
    /// <returns>是否执行成功</returns>
   public static bool TryIncreaseFactionRatio(this Kingdom kingdom, FixedFaction faction, int increaseRatio)
    {
        if (kingdom == null || faction == null)
        {
            return false;
        }

        if (increaseRatio <= 0)
        {
            return false;
        }

        KingdomExtraData data = kingdom.GetOrCreate();
        Dictionary<FixedFaction, int> ratios = data.FactionRatio;

        if (ratios == null)
        {
            return false;
        }

        if (!ratios.ContainsKey(faction))
        {
            ratios[faction] = 0;
        }

        kingdom.ClampFactionRatio();

        int currentTotal = kingdom.GetFactionRatioTotal();

        // 如果总数还没满 100，就先直接加
        int freeSpace = 100 - currentTotal;

        if (freeSpace >= increaseRatio)
        {
            ratios[faction] += increaseRatio;
            kingdom.ClampFactionRatio();
            return true;
        }

        // 需要增加的数值中，有一部分可以直接加入，剩下的是溢出值
        int overflow = increaseRatio - freeSpace;

        // 其他派系需要承担这个 overflow
        int otherTotal = 0;

        foreach (var pair in ratios)
        {
            if (!EqualityComparer<FixedFaction>.Default.Equals(pair.Key, faction))
            {
                otherTotal += pair.Value;
            }
        }

        // 其他派系不够扣，说明不能完整增加
        if (otherTotal < overflow)
        {
            return false;
        }

        // 先把目标派系增加完整 increaseRatio
        ratios[faction] += increaseRatio;

        int remainingDecrease = overflow;

        List<FixedFaction> otherFactions = ratios.Keys
            .Where(f => !EqualityComparer<FixedFaction>.Default.Equals(f, faction))
            .ToList();

        Dictionary<FixedFaction, int> decreaseMap = new Dictionary<FixedFaction, int>();

        foreach (FixedFaction otherFaction in otherFactions)
        {
            int value = ratios[otherFaction];

            if (value <= 0)
            {
                decreaseMap[otherFaction] = 0;
                continue;
            }

            int decrease = Mathf.FloorToInt((float)value / otherTotal * overflow);

            if (decrease > value)
            {
                decrease = value;
            }

            decreaseMap[otherFaction] = decrease;
            remainingDecrease -= decrease;
        }

        // 补足因为 FloorToInt 少扣的部分
        while (remainingDecrease > 0)
        {
            FixedFaction maxFaction = null;
            int maxValue = -1;

            foreach (FixedFaction otherFaction in otherFactions)
            {
                int afterDecreaseValue = ratios[otherFaction] - decreaseMap[otherFaction];

                if (afterDecreaseValue > maxValue)
                {
                    maxValue = afterDecreaseValue;
                    maxFaction = otherFaction;
                }
            }

            if (maxFaction == null || maxValue <= 0)
            {
                return false;
            }

            decreaseMap[maxFaction]++;
            remainingDecrease--;
        }

        foreach (var pair in decreaseMap)
        {
            ratios[pair.Key] -= pair.Value;

            if (ratios[pair.Key] < 0)
            {
                ratios[pair.Key] = 0;
            }
        }

        kingdom.ClampFactionRatio();

        return true;
    }
    public static void ClampFactionRatio(this Kingdom kingdom)
    {
        if (kingdom == null)
        {
            return;
        }

        Dictionary<FixedFaction, int> ratios = kingdom.GetOrCreate().FactionRatio;

        if (ratios == null || ratios.Count == 0)
        {
            return;
        }

        List<FixedFaction> keys = ratios.Keys.ToList();

        foreach (FixedFaction faction in keys)
        {
            ratios[faction] = Mathf.Clamp(ratios[faction], 0, 100);
        }

        int total = kingdom.GetFactionRatioTotal();

        if (total <= 100)
        {
            return;
        }

        int overflow = total - 100;

        // 如果因为外部代码直接设置导致超过 100，
        // 就从当前最高的派系开始扣，直到总和回到 100
        while (overflow > 0)
        {
            FixedFaction maxFaction = null;
            int maxValue = -1;

            foreach (var pair in ratios)
            {
                if (pair.Value > maxValue)
                {
                    maxValue = pair.Value;
                    maxFaction = pair.Key;
                }
            }

            if (maxFaction == null || maxValue <= 0)
            {
                break;
            }

            ratios[maxFaction]--;
            overflow--;
        }
    }
    public static int GetFactionRatioTotal(this Kingdom kingdom)
    {
        if (kingdom == null)
        {
            return 0;
        }

        Dictionary<FixedFaction, int> ratios = kingdom.GetOrCreate().FactionRatio;

        if (ratios == null || ratios.Count == 0)
        {
            return 0;
        }

        int total = 0;

        foreach (var pair in ratios)
        {
            total += Mathf.Max(0, pair.Value);
        }

        return total;
    }
    public static string FactionRatioToString(this Kingdom kingdom)
    {
        if (kingdom == null)
        {
            return "";
        }

        Dictionary<FixedFaction, int> ratios = kingdom.GetOrCreate().FactionRatio;

        if (ratios == null || ratios.Count == 0)
        {
            return "";
        }

        kingdom.ClampFactionRatio();

        string result = "";

        foreach (var pair in ratios.OrderByDescending(p => p.Value))
        {
            FixedFaction faction = pair.Key;
            int ratio = pair.Value;

            if (faction == null)
            {
                continue;
            }

            if (ratio <= 0)
            {
                continue;
            }

            if (result != "")
            {
                result += "\n";
            }

            result += $"{faction.Name}: {ratio}%";
        }

        return result;
    }
    public static int GetFactionRatioValue(this Kingdom kingdom, FixedFaction faction)
    {
        if (kingdom == null || faction == null)
        {
            return 0;
        }

        Dictionary<FixedFaction, int> ratios = kingdom.GetOrCreate().FactionRatio;

        if (ratios == null || !ratios.ContainsKey(faction))
        {
            return 0;
        }

        kingdom.ClampFactionRatio();

        return ratios[faction];
    }
    public static FixedFaction GetHighestFactionRatio(this Kingdom kingdom)
    {
        if (kingdom == null)
        {
            return null;
        }

        Dictionary<FixedFaction, int> ratios = kingdom.GetOrCreate().FactionRatio;

        if (ratios == null || ratios.Count == 0)
        {
            return null;
        }

        kingdom.ClampFactionRatio();

        FixedFaction highestFaction = null;
        int highestRatio = int.MinValue;

        foreach (var pair in ratios)
        {
            if (pair.Value > highestRatio)
            {
                highestRatio = pair.Value;
                highestFaction = pair.Key;
            }
        }

        return highestFaction;
    }
    public static void SetMainCrime(this Kingdom kingdom, LawType type)
    {
        kingdom.GetOrCreate().main_crime = type;
    }

    public static void StartMaintainGoodOpinion(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().isNeedToMaintainGoodOpinion = true;
    }

    public static void EndMaintainGoodOpinion(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().isNeedToMaintainGoodOpinion = false;
    }
    public static bool IsNeedToMaintainGoodOpinion(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().isNeedToMaintainGoodOpinion;
    }
    public static Law GetLawFromMainCrime(this Kingdom kingdom)
    {
        if (!kingdom.IsInEmpire()) return null;
        if (!kingdom.HasMainCrime()) return null;
        var type = kingdom.GetMainCrime();
        var empire = kingdom.GetEmpire();
        if (!empire?.CoreKingdom?.GetRegime()?.HasLaw(type) ?? true) return null;
        if (!EmpireLawSystem.Laws.TryGetValue(type, out var law)) return null;
        return law;
    }
    public static bool HasMainCrime(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().main_crime != default;
    }
    public static void RemoveMainCrime(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().main_crime = default;
    }
    public static void EndProgress(this Kingdom k)
    {
        if (k?.king == null) return;
        var progressObject = k.GetOrCreate().PushProgress;
        progressObject.Stop();
    }
    public static void PushProgress(this Kingdom k, TemporaryFactionType? type = null)
    {
        if (k?.king == null) return;
        var progressObject = k.GetOrCreate().PushProgress ??= new TemporaryPushProgress();
        if (!TemporaryPushProgress.IsLocalPusher(k.king)) { progressObject.Stop(); return; }
        if (!progressObject.StartedToPushTf && k.king.renown >= TemporaryPushProgress.InfluenceCostPerYear)
        {
            if (type.HasValue) progressObject.StartToPush(k.king, type.Value);
            else
            {
                var claims = k.king.GetFaction()?.TemporaryFactions?.Where(tf => tf != null && tf.Active &&
                    tf.canBePushByLocal && tf.CountDown <= 0 && !tf.IsStarted()).ToList();
                if (claims != null)
                {
                    claims.Shuffle();
                    foreach (var claim in claims)
                    {
                        progressObject.StartToPush(k.king, claim.type);
                        if (progressObject.StartedToPushTf) break;
                    }
                }
            }
        }
        progressObject.PushOneMonths();
    }
    public static void SystemChange(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().last_system_change_timestamp = World.world.getCurWorldTime();
    }

    public static int GetSystemChangeYear(this Kingdom kingdom)
    {
        return Date.getYearsSince(kingdom.GetOrCreate().last_system_change_timestamp);
    }

    public static void CacheData(this Kingdom kingdom)
    {
        var ed = kingdom.GetOrCreate();
        if (ed.last_cached_timestamp <= 0 || Date.getYearsSince(ed.last_cached_timestamp) >= 1)
        {
            ed.last_cached_timestamp = -1L;
            ed.cached_population = kingdom.getPopulationPeople();
            ed.cached_warriors = kingdom.countTotalWarriors();
            ed.last_cached_timestamp = World.world.getCurWorldTime();
        }
    }

    public static void RefreshNationalPower(this Kingdom kingdom, bool force = false)
    {
        if (kingdom == null || kingdom.isRekt()) return;
        KingdomExtraData data = kingdom.GetOrCreate();
        if (!force && data.last_national_power_timestamp > 0 &&
            Date.getYearsSince(data.last_national_power_timestamp) < 1)
        {
            return;
        }

        data.annual_power_population = Math.Max(0, kingdom.getPopulationPeople());
        data.annual_power_military = Math.Max(0, kingdom.countTotalWarriors());
        data.annual_power_economy = Math.Max(0, kingdom.GetMoney());
        data.annual_power_index = NationalPowerRules.Calculate(data.annual_power_population,
            data.annual_power_military, data.annual_power_economy);
        data.last_national_power_timestamp = World.world.getCurWorldTime();
    }

    public static double GetNationalPower(this Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return 0d;
        kingdom.RefreshNationalPower();
        return kingdom.GetOrCreate().annual_power_index;
    }

    public static string GetNationalPowerBreakdown(this Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return "0 / 0 / 0";
        kingdom.RefreshNationalPower();
        KingdomExtraData data = kingdom.GetOrCreate();
        return $"{data.annual_power_population} / {data.annual_power_military} / {data.annual_power_economy}";
    }
    public static void FinishedSelfPlot(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().selfChangeRegimePlotsCountDown = 10;
    }
    public static void CountingFinishedSelfPlot(this Kingdom kingdom)
    {
        if (kingdom.GetOrCreate().selfChangeRegimePlotsCountDown > 0)
        {
            kingdom.GetOrCreate().selfChangeRegimePlotsCountDown -= 1;  
        }
    }

    public static string GetEmpireCraftCulture(this Kingdom kingdom, bool allowTranslate = false)
    {
        if (kingdom == null) return null;
        string species = kingdom.getSpecies();
        if (species == null) return null;
        if (ConfigData.speciesCulturePair.TryGetValue(species, out string culture))
        {
            if (allowTranslate)
            {
                return culture.GetCultureTranslate();
            }
            return culture;
        }
        return null;
    }

    public static bool HasSameEmpireCraftCulture(this Kingdom kingdom, Kingdom target)
    {
        if (kingdom?.GetEmpireCraftCulture()==null||target?.GetEmpireCraftCulture()==null) return false;
        return kingdom.GetEmpireCraftCulture() == target.GetEmpireCraftCulture();
    }
    public static bool IsCountingSelfPlot(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().selfChangeRegimePlotsCountDown > 0;
    }
    public static void StartCorrupting(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().corruption_timestamp = World.world.getCurWorldTime();
    }
    public static bool IsStartCorrupting(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().corruption_timestamp > 0;
    }

    public static void EndCorrupting(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().corruption_timestamp = -1L;
    }

    public static int GetCorruptionTime(this Kingdom kingdom)
    {
        if (kingdom.GetOrCreate().corruption_timestamp < 0)
        {
            return -1;
        }
        return Date.getYearsSince(kingdom.GetOrCreate().corruption_timestamp);
    }
    public static void AddCorruptionRate(this Kingdom kingdom, double addition)
    {
        if (kingdom == null || addition == 0) return;
        double current = kingdom.GetCorruptionRate();
        if ((current >= 1.0f && addition > 0) || (current <= 0.0f && addition < 0))
        {
            return;
        }

        kingdom.SetCorruptionRate(current + addition);
    }

    public static double GetCorruptionRate(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().corruption_rate;
    }
    

    public static void SetCorruptionRate(this Kingdom kingdom, Double value)
    {
        if (kingdom == null) return;
        kingdom.GetOrCreate().corruption_rate = Math.Max(0.0, Math.Min(1.0, value));
    }
    public static void SetHeirLaw(this Kingdom k, EmpireHeirLawType type)
    {
        k.GetOrCreate().HeirLaw = type;
    }

    public static void SetDefaultHeirLaw(this Kingdom k, EmpireHeirLawType type)
    {
        k.GetOrCreate().DefaultHeirLaw = type;
    }
    public static EmpireHeirLawType GetHeirLaw(this Kingdom k)
    {
        return k.GetOrCreate().HeirLaw;
    }
    public static EmpireHeirLawType GetDefaultHeirLaw(this Kingdom k)
    {
        return k.GetOrCreate().DefaultHeirLaw;
    }

    public static void GoToNextHeirLaw(this Kingdom k)
    {
        switch (k.GetOrCreate().HeirLaw)
        {
            case EmpireHeirLawType.eldest_child:
                k.SetHeirLaw(EmpireHeirLawType.siblings);
                break;
            case EmpireHeirLawType.siblings:
                k.SetHeirLaw(EmpireHeirLawType.grand_child_generation);
                break;
            case EmpireHeirLawType.grand_child_generation:
                k.SetHeirLaw(EmpireHeirLawType.random);
                break;
            case EmpireHeirLawType.random:
                k.SetHeirLaw(!k.IsEmpire() ? EmpireHeirLawType.officer : k.GetOrCreate().DefaultHeirLaw);
                break;
            case EmpireHeirLawType.officer:
                k.SetHeirLaw(k.GetOrCreate().DefaultHeirLaw);
                break;
            case EmpireHeirLawType.smallest_child:
                k.SetHeirLaw(EmpireHeirLawType.siblings);
                break;
        }
    }

    public static void RecoverToDefaultHeir(this Kingdom k)
    {
        k.SetHeirLaw(k.GetDefaultHeirLaw());
    }
    public static bool IsNeedToTaken(this Kingdom k)
    {
        return Date.getYearsSince(k.GetOrCreate().last_taken_time)>1&&k.HasTakenAlliance();
    }

    public static void StartToTaken(this Kingdom k)
    {
        Empire empire = k.GetTakenAllianceEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived()) return;
        var core = empire.CoreKingdom;
        if (core == null || core.isRekt()) return;
        var value = k.units != null ? k.units.Count / 2 : 0;
        k.SubMoney(value);
        core.AddMoney(value);
        if (k.GetMoney()<=0)
        {
            if ((k.units?.Count ?? 0) / 3 > empire.getUnits().Count())
            {
                k.GetOrCreate().leave_taken_alliance_preference += 0.05f;
            }
        }

        if (!(k.GetOrCreate().leave_taken_alliance_preference >= 1.0)) return;
        k.RemoveTakenAlliance();
        Random random = new Random();
        var possibility = random.NextDouble();
        if (core.GetMoney() <= 0) return;
        if (!empire.CanStartPunitiveWar(k)) return;
        if (!(possibility < 0.2f)) return;
        var war = DiplomacyHelpers.wars.newWar(core, k, WarTypeLibrary.normal);
        war?.SetEmpireWarType(EmpireWarType.伐不臣);
    }

    public static bool CanStartPunitiveWar(this Empire empire, Kingdom target, float minimumAdvantage = 1.2f)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(target) ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(empire)) return false;
        Kingdom core = empire?.CoreKingdom;
        if (empire == null || empire.isRekt() || empire.IsArchived() || core?.data == null ||
            core.isRekt() || target?.data == null || target.isRekt() || core == target)
        {
            return false;
        }

        if (target.IsInEmpire() && target.GetEmpire() == empire) return false;
        if (HasAnyActiveEmpireWar(empire) || HasAnyActiveDefenderWar(target, empire))
        {
            return false;
        }

        long attackerStrength = CountCurrentEmpireWarriors(empire);
        long defenderStrength = CountEffectiveDefenderWarriors(target, empire);
        if (attackerStrength <= 0 || defenderStrength <= 0) return false;
        return attackerStrength >= Math.Ceiling(defenderStrength * Math.Max(1f, minimumAdvantage));
    }

    private static bool HasAnyActiveEmpireWar(Empire empire)
    {
        if (World.world?.wars == null || empire?.kingdoms_list == null) return true;
        for (int i = 0; i < empire.kingdoms_list.Count; i++)
        {
            Kingdom member = empire.kingdoms_list[i];
            if (member?.data != null && !member.isRekt() && World.world.wars.hasWars(member)) return true;
        }
        return false;
    }

    private static long CountCurrentEmpireWarriors(Empire empire)
    {
        long total = 0;
        if (empire?.kingdoms_list == null) return total;
        for (int i = 0; i < empire.kingdoms_list.Count; i++)
        {
            Kingdom member = empire.kingdoms_list[i];
            if (member?.data == null || member.isRekt()) continue;
            total += Math.Max(0, member.countTotalWarriors());
        }
        return total;
    }

    private static long CountEffectiveDefenderWarriors(Kingdom target, Empire attackingEmpire)
    {
        long strength = Math.Max(0, target.countTotalWarriors());
        if (target.hasAlliance())
        {
            Alliance alliance = target.getAlliance();
            if (alliance != null) strength = Math.Max(strength, alliance.countWarriors());
        }

        if (target.IsInEmpire())
        {
            Empire targetEmpire = target.GetEmpire();
            if (targetEmpire != null && targetEmpire != attackingEmpire && !targetEmpire.isRekt() && !targetEmpire.IsArchived())
            {
                strength = Math.Max(strength, CountCurrentEmpireWarriors(targetEmpire));
            }
        }

        return strength;
    }

    private static bool HasAnyActiveDefenderWar(Kingdom target, Empire attackingEmpire)
    {
        if (World.world?.wars == null || target == null) return true;
        if (World.world.wars.hasWars(target)) return true;
        if (target.hasAlliance())
        {
            Alliance alliance = target.getAlliance();
            if (alliance != null && World.world.wars.hasWars(alliance)) return true;
        }

        if (target.IsInEmpire())
        {
            Empire targetEmpire = target.GetEmpire();
            if (targetEmpire != null && targetEmpire != attackingEmpire && HasAnyActiveEmpireWar(targetEmpire)) return true;
        }
        return false;
    }
    /// <summary>
    /// 获取当前国家退出朝贡联盟的倾向
    /// </summary>
    /// <param name="k"></param>
    public static float GetLeaveTakenAlliancePreference(this Kingdom k)
    {
        return k.GetOrCreate().leave_taken_alliance_preference;
    }
    public static void AddLocalClaimFailedOpinion(this Kingdom kingdom, Empire empire, int stack = 1)
    {
        if (kingdom == null || kingdom.isRekt() || empire == null || empire.IsArchived()) return;
        var data = kingdom.GetOrCreate();
        if (data.local_claim_failed_opinion == null)
        {
            data.local_claim_failed_opinion = new Dictionary<long, int>();
        }

        long empireId = empire.getID();
        if (!data.local_claim_failed_opinion.ContainsKey(empireId))
        {
            data.local_claim_failed_opinion[empireId] = 0;
        }

        data.local_claim_failed_opinion[empireId] += Math.Max(1, stack);
    }

    public static int GetLocalClaimFailedOpinion(this Kingdom kingdom, Empire empire)
    {
        if (kingdom == null || empire == null) return 0;
        var data = kingdom.GetOrCreate();
        if (data?.local_claim_failed_opinion == null) return 0;
        return data.local_claim_failed_opinion.TryGetValue(empire.getID(), out var value) ? value : 0;
    }
    public static void JoinGivenAlliance(this Kingdom k, Empire empire)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(k) ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(empire)) return;
        k.GetOrCreate().last_given_alliance_timestamp = World.world.getCurWorldTime();
        k.GetOrCreate().given_empire = empire.id;
        empire.given_Kingdoms.Add(k);
    }
    public static void RemoveGivenAlliance(this Kingdom k)
    {
        Empire empire = k.GetGivenAllianceEmpire();
        if (empire != null)
        {
            empire.given_Kingdoms.Remove(k);
        }
        k.GetOrCreate().given_empire = -1L;
    }
    public static bool NeedToRemoveGivenAlliance(this Kingdom k)
    {
        Empire empire = k.GetGivenAllianceEmpire();
        if (empire == null||k.IsInEmpire())
        {
            return true;
        }

        if (Date.getYearsSince(k.GetOrCreate().last_given_alliance_timestamp) > 20)
        {
            return true;
        }
        return false;
    }

    public static void JoinTakenAlliance(this Kingdom k, Empire empire, bool pForce = false)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(k) ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(empire)) return;
        if (k == null || empire == null || empire.IsArchived())
        {
            return;
        }
        // Force may bypass diplomatic/de-jure eligibility after a tribute war, but a rebel
        // polity can never return to its original empire even as a tributary.
        if (k.HasRebelledAgainst(empire)) return;
        if (!pForce && (!empire.CanAcceptVoluntarySubmission() ||
            !k.CanVoluntarilyBecomeTributaryOf(empire) ||
            !empire.MeetsTributaryPowerThreshold(k))) return;

        Empire previousEmpire = k.GetTakenAllianceEmpire();
        bool alreadyJoined = previousEmpire == empire && (empire.taken_Kingdoms?.Contains(k) ?? false);
        if (alreadyJoined) return;
        if (previousEmpire != null && previousEmpire != empire)
        {
            k.RemoveTakenAlliance();
        }

        k.GetOrCreate().last_taken_alliance_timestamp = World.world.getCurWorldTime();
        k.GetOrCreate().taken_empire = empire.id;
        k.GetOrCreate().leave_taken_alliance_preference = 0.0f;
        empire.taken_Kingdoms ??= new List<Kingdom>();
        if (!empire.taken_Kingdoms.Contains(k))
        {
            empire.taken_Kingdoms.Add(k);
            k.updateColor(empire.CoreKingdom.getColor());
        }
        empire.RecordHistory(EmpireHistoryType.join_taken_alliance_history, new Dictionary<string, string>
        {
            ["kingdom"] = k.GetKingdomFullName(),
            ["empire"] = empire.GetEmpireFullName()
        }, kingdomId: k.id);
    }

    public static bool CanVoluntarilyBecomeTributaryOf(this Kingdom kingdom, Empire empire)
    {
        if (kingdom == null || kingdom.isRekt() || empire == null || empire.IsArchived() || empire.isRekt())
            return false;
        KingdomTitle mainTitle = kingdom.GetMainTitle();
        if (mainTitle == null)
            return EmpireSubmissionRules.CanVoluntarilyBecomeTributary(false, false);
        EmpireCore overlordCore = EmpireCoreManager.Get(empire);
        bool belongsToOverlordCore = overlordCore != null &&
                                     EmpireCoreManager.ContainsTitle(overlordCore, mainTitle);
        return EmpireSubmissionRules.CanVoluntarilyBecomeTributary(true, belongsToOverlordCore);
    }

    public static void RemoveTakenAlliance(this Kingdom k, bool recordHistory = true)
    {
        Empire empire = k.GetTakenAllianceEmpire();
        if (empire != null)
        {
            empire.taken_Kingdoms.Remove(k);
        }
        k.GetOrCreate().taken_empire = -1L;
        if (!k.isRekt()) k.generateColor();
        if (recordHistory && empire != null && !empire.IsArchived() && !empire.isRekt())
        {
            empire.RecordHistory(EmpireHistoryType.leave_taken_alliance_history, new Dictionary<string, string>
            {
                ["kingdom"] = k.GetKingdomFullName(),
                ["empire"] = empire.GetEmpireFullName()
            }, kingdomId: k.id);
        }
    }
    public static bool NeedToRemoveTakenAlliance(this Kingdom k)
    {
        Empire empire = k.GetTakenAllianceEmpire();
        return empire == null||k.IsInEmpire();
    }
    public static bool HasGivenAlliance(this Kingdom k)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(k)) return false;
        return k.GetOrCreate().given_empire != -1L;
    }
    public static bool HasTakenAlliance(this Kingdom k)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(k)) return false;
        return k.GetOrCreate().taken_empire != -1L;
    }
    public static Empire GetGivenAllianceEmpire(this Kingdom k)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(k)) return null;
        return ModClass.EMPIRE_MANAGER.get(k.GetOrCreate().given_empire);
    }
    public static Empire GetTakenAllianceEmpire(this Kingdom k)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(k)) return null;
        return ModClass.EMPIRE_MANAGER.get(k.GetOrCreate().taken_empire);
    }

    public static void SetCenterArmy(this Kingdom k, Army army)
    {
        army.name = $"{k?.GetEmpire()?.GetEmpireName()}-{k?.GetKingdomName()}驻军";
        k.GetOrCreate().CenterArmID = army.getID();
    }

    public static void StartFactionRebelling(this Kingdom k, FixedFaction faction)
    {
        if (k?.data == null) return;
        k.RememberRebellionOrigin(k.GetEmpire());
        if (string.IsNullOrWhiteSpace(k.GetOrCreate().faction_rebel_original_name))
        {
            k.GetOrCreate().faction_rebel_original_name = k.data.name ?? k.name ?? "";
        }
        k.data.name = faction?.Name ?? ((k.GetOrCreate().faction_rebel_original_name ?? k.name) + "叛乱");
        k.GetOrCreate().isFactionRebelling = true;
    }

    public static void EndFactionRebelling(this Kingdom k)
    {
        k.GetOrCreate().isFactionRebelling = false;
        if (!string.IsNullOrWhiteSpace(k.GetOrCreate().faction_rebel_original_name) && k.data != null)
        {
            k.data.name = k.GetOrCreate().faction_rebel_original_name;
        }
        k.GetOrCreate().faction_rebel_original_name = "";
    }

    public static void ClearRebellionStatus(this Kingdom k)
    {
        k.EndFactionRebelling();
        k.EndLocalRebelling();
    }

    public static bool IsFactionRebelling(this Kingdom k)
    {
        return k.GetOrCreate().isFactionRebelling;
    }

    public static bool StartLocalRebelling(this Kingdom kingdom, EmpireWarType warType, string pre = "")
    {
        if (kingdom?.data == null) return false;
        kingdom.RememberRebellionOrigin(kingdom.GetEmpire());
        switch (warType)
        {
            case EmpireWarType.藩王索取皇位:
                if (!kingdom.IsInEmpire()) return false;
                Empire empire = kingdom.GetEmpire();
                if (empire == null) return false;
                if (kingdom.IsEmpire()) return false;
                if (!kingdom.hasKing()) return false;
                if (kingdom.king.GetSpecificClan() != empire.EmpireSpecificClan)
                {
                    var preKing = kingdom.units.Find(a=>a.GetSpecificClan()==empire.EmpireSpecificClan);
                    if (preKing==null) return false;
                    kingdom.setKing(preKing);
                }
                if (!kingdom.HasMainTitle())
                {
                    if (kingdom.capital.hasTitle())
                    {
                        kingdom.king.AddOwnedTitle(kingdom.capital.GetTitle());
                        kingdom.king.SetMainTitle(kingdom.capital.GetTitle());
                        kingdom.SetMainTitle(kingdom.capital.GetTitle());
                        empire.SynchronizeLandedLegalTitles(kingdom);
                    }
                }

                if (!kingdom.HasMainTitle()) return false;
                    
                kingdom.ClearRebellionStatus();
                kingdom.GetRegime().SetAllowArmy(true);
                kingdom.GetRegime().SetAllowSupportCenterArmy(false);
                kingdom.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Succession);
                kingdom.getWars().ForEach(w=>DiplomacyHelpers.wars.endWar(w));
                var language = PlayerConfig.dict["language"].stringVal;
                if (language == "en")
                {
                    kingdom.data.name = $"The Regime of {kingdom.GetMainTitle().name}'s {LM.Get("default_" + kingdom.king.GetPeeragesLevel())}";
                }
                else
                {
                    kingdom.data.name = kingdom.GetMainTitle().name+""+LM.Get("King")+LM.Get("regime");
                }
                break;
            default:
                kingdom.data.name =  (string.IsNullOrEmpty(pre)?kingdom.GetKingdomName():pre)+'\u200A' + '\u200A' + LM.Get("rebelling");
                break;
        }
        var extraData = kingdom.GetOrCreate();
        extraData.isLocalRebelling = true;
        if (warType == EmpireWarType.地方叛乱)
        {
            // 叛军规模由其领袖的既有城市容量决定，避免战时无限扩张。
            extraData.rebellion_auto_expand_remaining = Mathf.CeilToInt(kingdom.getMaxCities() * 1.5f);
        }
        else
        {
            extraData.rebellion_auto_expand_remaining = -1;
        }
        return true;
    }

    public static void EndLocalRebelling(this Kingdom k)
    {
        var extraData = k.GetOrCreate();
        extraData.isLocalRebelling = false;
        extraData.rebellion_auto_expand_remaining = -1;
    }

    public static bool IsLocalRebelling(this Kingdom k)
    {
        return k.GetOrCreate().isLocalRebelling;
    }

    public static void RememberRebellionOrigin(this Kingdom kingdom, Empire empire)
    {
        if (kingdom == null || empire == null || empire.IsArchived() || empire.isRekt()) return;
        var data = kingdom.GetOrCreate();
        data.rebellion_origin_empire_ids ??= new List<long>();
        long empireId = empire.getID();
        if (empireId >= 0 && !data.rebellion_origin_empire_ids.Contains(empireId))
        {
            data.rebellion_origin_empire_ids.Add(empireId);
        }
    }

    public static bool HasRebelledAgainst(this Kingdom kingdom, Empire empire)
    {
        if (kingdom == null || empire == null) return false;
        return EmpireSubmissionRules.IsOriginalRebellionEmpire(
            kingdom.GetOrCreate().rebellion_origin_empire_ids, empire.getID());
    }

    public static void SetKingdomName(this Kingdom k, string kindomName)
    {
        if (!k.IsFactionRebelling() && k.getWars().Count() <= 0 && !k.IsLocalRebelling())
        {
            k.RememberInitialRandomKingdomName();
            k.data.name = kindomName.UseLocalizedNameSeparator();
            string coreName = ExtractKingdomFront(k, k.data.name);
            if (!string.IsNullOrWhiteSpace(coreName)) k.SetKingdomCoreName(coreName, k.data.name);
        }
    }

    public static void SetKingdomCoreName(this Kingdom kingdom, string coreName, string storedFullName = null)
    {
        if (kingdom?.data == null || string.IsNullOrWhiteSpace(coreName)) return;
        KingdomExtraData data = kingdom.GetOrCreate();
        data.core_name = coreName.Trim();
        if (storedFullName != null) kingdom.data.name = storedFullName.UseLocalizedNameSeparator();
        data.core_name_source = kingdom.data.name ?? "";
    }

    public static void RememberInitialRandomKingdomName(this Kingdom kingdom, string name = null,
        bool overwrite = false)
    {
        if (kingdom?.data == null ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return;
        KingdomExtraData data = kingdom.GetOrCreate();
        if (!overwrite && !string.IsNullOrWhiteSpace(data.initial_random_name)) return;
        string candidate = ExtractKingdomFront(kingdom, name ?? kingdom.data.name);
        if (!overwrite)
        {
            string capitalName = kingdom.hasCapital() && kingdom.capital != null && !kingdom.capital.isRekt()
                ? kingdom.capital.GetCityName()
                : null;
            string titleName = kingdom.GetMainTitle()?.data?.name;
            string capitalTitleName = kingdom.GetCapitalDeJureTitleName();
            string administrativeName = kingdom.GetAdministrativeProvinceName();
            if (string.Equals(candidate, capitalName, StringComparison.Ordinal) ||
                string.Equals(candidate, titleName, StringComparison.Ordinal) ||
                string.Equals(candidate, capitalTitleName, StringComparison.Ordinal) ||
                string.Equals(candidate, administrativeName, StringComparison.Ordinal)) return;
        }
        if (!string.IsNullOrWhiteSpace(candidate)) data.initial_random_name = candidate;
    }

    public static string GetInitialRandomKingdomName(this Kingdom kingdom)
    {
        if (kingdom?.data == null) return "";
        KingdomExtraData data = kingdom.GetOrCreate();
        if (!string.IsNullOrWhiteSpace(data.initial_random_name)) return data.initial_random_name;

        string candidate = ExtractKingdomFront(kingdom, kingdom.data.name);
        string capitalName = kingdom.hasCapital() && kingdom.capital != null && !kingdom.capital.isRekt()
            ? kingdom.capital.GetCityName()
            : null;
        string titleName = kingdom.GetMainTitle()?.data?.name;
        string capitalTitleName = kingdom.GetCapitalDeJureTitleName();
        string administrativeName = kingdom.GetAdministrativeProvinceName();
        bool currentNameIsPolitical = string.IsNullOrWhiteSpace(candidate) ||
                                      string.Equals(candidate, capitalName, StringComparison.Ordinal) ||
                                      string.Equals(candidate, titleName, StringComparison.Ordinal) ||
                                      string.Equals(candidate, capitalTitleName, StringComparison.Ordinal) ||
                                      string.Equals(candidate, administrativeName, StringComparison.Ordinal);
        if (currentNameIsPolitical)
        {
            try
            {
                candidate = kingdom.culture?.getOnomasticData(MetaType.Kingdom)?.generateName();
            }
            catch
            {
                candidate = null;
            }
        }

        if (string.IsNullOrWhiteSpace(candidate)) candidate = ExtractKingdomFront(kingdom, kingdom.data.name);
        data.initial_random_name = candidate ?? "";
        return data.initial_random_name;
    }

    public static string GetCapitalDeJureTitleName(this Kingdom kingdom)
    {
        KingdomTitle title = kingdom.GetCapitalDeJureTitle();
        return title == null || string.IsNullOrWhiteSpace(title.data?.name)
            ? null
            : title.data.name;
    }

    public static KingdomTitle GetCapitalDeJureTitle(this Kingdom kingdom)
    {
        if (kingdom == null || !kingdom.hasCapital() || kingdom.capital == null ||
            kingdom.capital.isRekt() || !kingdom.capital.hasTitle()) return null;
        KingdomTitle title = kingdom.capital.GetTitle();
        return title == null || title.isRekt() ? null : title;
    }

    public static string GetUntitledKingdomName(this Kingdom kingdom)
    {
        KingdomTitle capitalTitle = kingdom.GetCapitalDeJureTitle();
        if (capitalTitle != null)
        {
            string capitalName = kingdom.capital.GetCityName();
            if (!string.IsNullOrWhiteSpace(capitalName)) return capitalName;
        }
        return kingdom.GetInitialRandomKingdomName();
    }

    private static string ExtractKingdomFront(Kingdom kingdom, string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "";
        if (OverallHelperFunc.TryExtractEnglishPrefixedCountryName(fullName, out string prefixedName))
            return prefixedName;

        string typeName = "";
        try { typeName = LM.Get(kingdom.GetKingdomType().ToString()); }
        catch { }
        string strippedName = OverallHelperFunc.StripLocalizedTypeSuffix(fullName, typeName);
        if (!string.Equals(strippedName, fullName.Trim(), StringComparison.Ordinal))
            return strippedName.UseLocalizedNameSeparator();

        string[] parts = fullName.SplitNameParts();
        return parts.Length == 0 ? "" : parts[0].Trim();
    }

    public static Army GetCenterArmy(this Kingdom k)
    {
        var res = World.world.armies.get(k.GetOrCreate().CenterArmID);
        if (res.isRekt())
        {
            k.RemoveCenterArmy();
            return null;
        } 
        return res;
    }

    public static void RemoveCenterArmy(this Kingdom k)
    {
        k.GetOrCreate().CenterArmID = -1L;
    }
    public static int GetMoney(this Kingdom k)
    {
        if (k.isRekt()) return 0;
        return k.GetOrCreate().Money;
    }
    public static void AddMoney(this Kingdom k, int money)
    {
        k.GetOrCreate().Money += money;
    }
    public static void SubMoney(this Kingdom k, int money)
    {
        k.GetOrCreate().Money -= money; 
    }
    
    public static double GetLastTaxTime(this Kingdom k)
    {
        return k.GetOrCreate().last_tax_timestamp;
    }
    public static void RecordTaxTime(this Kingdom k)
    {
        k.GetOrCreate().last_tax_timestamp = World.world.getCurWorldTime();
    }

    public static bool IsNeedToSubmitTax(this Kingdom k)
    {
        if (!k.IsInEmpire() || k.IsEmpire()) return false;
        return Date.getYearsSince(k.GetLastTaxTime()) >= 1;
    }
    public static double GetTaxRate(this Kingdom k)
    {
        var baseTax = 0.1f;
        if (k != null && k.IsInEmpire())
        {
            Empire empire = k.GetEmpire();
            if (empire != null && !empire.isRekt() && !empire.IsArchived())
            {
                baseTax = empire.data.TaxRate;
            }
        }

        var regime = k?.GetRegime();
        if (regime != null)
        {
            switch (regime.GetTaxLevel())
            {
                case TaxLevel.None:
                    return 0.0f;
                case TaxLevel.Low:
                    return baseTax;
                case TaxLevel.Medium:
                    return baseTax + 0.2f;
                case TaxLevel.High:
                    return baseTax + 0.4f;
                default:
                    return baseTax;
            } 
        }
        return baseTax;
    }
    
    public static void SetKingdomType(this Kingdom k, KingdomType type)
    {
        k.GetOrCreate().kingdomType = type;
    }

    public static KingdomType GetKingdomType(this Kingdom k)
    {
        return k.GetOrCreate().kingdomType;
    }
    public static void SetOffice(this Kingdom k, OfficeObject office)
    {
        var res = OfficeManager.Remove(k.GetOfficeID());
        k.GetOrCreate().office_id = office.OfficeID;
    }
    public static OfficeObject GetOffice(this Kingdom k)
    {
        return OfficeManager.Offices.TryGetValue(k.GetOrCreate().office_id, out var office) ? office : null;
    }
    
    public static long GetOfficeID(this Kingdom k)
    {
        return k.GetOrCreate().office_id;
    }

    public static SpecificClan GetSpecificClan(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().kingdomSpecificClan;
    }

    public static void UpdateExamTime(this Kingdom k)
    { 
        k.GetOrCreate().last_exam_timestamp = World.world.getCurWorldTime();
    }

    public static void UpdateOfficeExamTime(this Kingdom k)
    { 
        k.GetOrCreate().last_office_exam_timestamp = World.world.getCurWorldTime();
    }
    public static bool IsNeedToExam(this Kingdom k)
    {
        var time = k.GetOrCreate().last_exam_timestamp;
        if (time <= 0) 
        {
            return true;
        }

        if (Date.getYearsSince(time)>=4)
        {
            return true;
        }
        return false;
    }
    public static bool IsNeedToOfficeExam(this Kingdom k)
    {
        var exam_time = k.GetOrCreate().last_exam_timestamp;
        var office_exam_time = k.GetOrCreate().last_office_exam_timestamp;
        if (exam_time <= 0) return true;
        if (Date.getYearsSince(office_exam_time)>=1)
        {
            return true;
        }
        return false;
    }
    public static void SetSpecificClan(this Kingdom kingdom, SpecificClan sc)
    {
        kingdom.GetOrCreate().kingdomSpecificClan = sc;
    }
    public static Regime GetRegime(this Kingdom k)
    {
        if (k?.data == null || k.isRekt()) return null;
        var ed = k.GetOrCreate();
        if (ed == null) return null;
        var reg = ed.regime;
        if (reg == null)
        {
            k.LoadRegime();
            reg = ed.regime;
        }
        return reg;
    }
    public static void SetRegimeType(this Kingdom k, RegimeType type)
    {
        if (k?.data == null || k.isRekt()) return;
        k.GetOrCreate().regimeType = type;
    }
    public static void SetRegime(this Kingdom k, Regime regime)
    {
        if (k?.data == null || k.isRekt()) return;
        k.GetOrCreate().regime = regime;
    }

    public static void LoadRegime(this Kingdom k)
    {
        if (k?.data == null || k.isRekt()) return;
        var ed = k.GetOrCreate();
        var type = ed.regimeType;
        Regime baseRegime = null;
        if (RegimeManager.regimes != null)
        {
            RegimeManager.regimes.TryGetValue(type, out baseRegime);
            if (baseRegime == null)
            {
                RegimeManager.regimes.TryGetValue(RegimeType.Feudalism, out baseRegime);
            }
        }
        Regime regime = baseRegime?.Clone(k);
        k.SetRegime(regime);
        if (k.IsEmpire())
        {
            var empire = k.GetEmpire();
            if (empire != null)
            {
                if (empire.data.centerOffice == null)
                {
                    empire.data.centerOffice = new CenterOffice();
                    var core = empire.CoreKingdom ?? k;
                    empire.data.centerOffice.Init(core);
                }
            }
        }
        var factions = regime?.GetPlayerFactions();
        if (factions != null)
        {
            factions.ForEach(f =>
            {
                f.EmpireId = k.IsEmpire() ? k.GetEmpireID() : -1L;
                f.FixMissedTemporaryFactions();
                if (f.TemporaryFactions != null)
                {
                    f.TemporaryFactions.ForEach(tf => tf.Init(f));
                }
            });
            if (k.IsEmpire()) k.ReconcileFactionRatios(factions);
        }
    }

    public static void InitialRegime(this Kingdom k)
    {
        if (k?.data == null || k.isRekt()) return;
        var culture = k.asset != null && ConfigData.speciesCulturePair.TryGetValue(k.asset.id, out string speciesCulture)
            ? speciesCulture
            : "Western";
        RegimeType regimeType = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
            ? setting.regime
            : RegimeType.Feudalism;
        k.SetRegimeType(regimeType);
        k.LoadRegime();
        var regime = k.GetRegime();
        if (regime == null) return;
        regime.SetAllowDiplomacy(true);
        var type = EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType(k);
        BureauSetting set = null;
        if (regime?.bureau_config?.kingdoms != null)
        {
            regime.bureau_config.kingdoms.TryGetValue(type, out set);
        }
        if (set == null)
        {
            set = new BureauSetting
            {
                type = 0,
                pre = "",
                description = "",
                powers = new List<OfficerPowerType>(),
                merit = 0,
                honorary = 0,
                select_from_local = false,
                leader_select_method = LeaderSelectMethod.Default,
                require_traits = new List<string>(),
                condition = new List<string>(),
                city_type = CityType.Feudalism_city
            };
        }
        OfficeObject officeObject = new OfficeObject();
        officeObject.InitialOffice(set);
        officeObject.regimeType = regime.type;
        officeObject.meta_object = k;
        officeObject.is_local = true;
        k.SetOffice(officeObject);
        foreach (var city in k.cities)
        {
            city.InitialRegime();
        }
    }
    public static int GetIndependentValue(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        return ed?.IndependentValue ?? 100;
    }

    public static List<Actor> AllGongshi(this Kingdom k)
    {
        return k.units.FindAll(a => a.hasTrait("gongshi"));
    }
    public static bool CalcHeirFinished(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        return ed.CalcTask == null;
    }

    public static void SetCalcHeirTask(this Kingdom k, Task<(Actor, string)> calcTask)
    {
        var ed = k.GetOrCreate();
        ed.CalcTask = calcTask;
    }

    public static Task<(Actor pActor, string relation)> GetCalcHeirTask(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        return ed.CalcTask;
    }
    public static void RemoveCalcHeirStatus(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        ed.CalcTask = null;
    }
    public static void SetIndependentValue(this Kingdom k, int value)
    {
        var ed = k.GetOrCreate();
        ed.IndependentValue = value;
    }

    public static void AddIndependentValue(this Kingdom k, int addition)
    {
        var ed = k.GetOrCreate();
        ed.IndependentValue += addition;
        if (ed.IndependentValue < 0)
        {
            ed.IndependentValue = 0;
        } else if (ed.IndependentValue > 100)
        {
            ed.IndependentValue = 100;
        }
    }

    public static Actor GetHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        return World.world.units.get(ed.HeirID);
    }
    public static void RemoveHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        ed.HeirID = -1L;
    }
    public static bool HasHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        if (ed.HeirID == -1L) return false;
        return !World.world.units.get(ed.HeirID).isRekt();
    }

    public static OfficeObject[] GetAllOffices(this Kingdom k)
    {
        OfficeObject[] officeObjects = Array.Empty<OfficeObject>();
        foreach (var c in k.cities)
        {
            OfficeObject o = c.GetOffice();
            officeObjects.AddItem(o);
        }

        OfficeObject kingObject = k.GetOffice();
        officeObjects.AddItem(kingObject);
        return officeObjects;
    }


    public static void SetHeir(this Kingdom k, Actor pActor)
    {
        var ed = k.GetOrCreate();
        ed.HeirID = pActor.getID();
    }    

    public static bool IsIndependent(this Kingdom kingdom)
    {
        var ed = kingdom.GetOrCreate();
        return ed.IndependentValue >= 100;
    }

    public static void StartToChooseHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        ed.is_need_to_choose_heir = true;
    }

    public static bool IsNeedToChooseHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        return ed.is_need_to_choose_heir;
    }
    
    public static void ChooseHeirFinished(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        ed.is_need_to_choose_heir = false;
    }
    public static bool CanBeTaken(this Kingdom kingdom)
    {
        var ed = kingdom.GetOrCreate();
        return ed.IndependentValue <= 0;
    }

    public static bool IsTitleWithinRealm(this Kingdom kingdom, KingdomTitle title)
    {
        if (kingdom == null || kingdom.isRekt() || title == null || title.isRekt() ||
            title.data == null || title.city_list == null || title.city_list.Count == 0 ||
            kingdom.cities == null) return false;
        if (title.main_kingdom == kingdom || kingdom.GetOrCreate().MainTitle == title.id) return true;
        int validCities = title.city_list.Count(city => city != null && !city.isRekt());
        if (validCities == 0) return false;
        int controlledCities = title.city_list.Count(city => city != null && !city.isRekt() &&
                                                        city.kingdom == kingdom);
        return controlledCities >= Math.Ceiling(validCities * title.data.title_controlled_rate);
    }

    public static List<long> GetRealmTitleIds(this Kingdom kingdom)
    {
        if (kingdom == null) return new List<long>();
        KingdomExtraData data = kingdom.GetOrCreate();
        data.RealmTitles ??= new List<long>();
        data.RealmTitles.RemoveAll(id =>
        {
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(id);
            return title == null || title.isRekt();
        });
        if (data.MainTitle > 0 && !data.RealmTitles.Contains(data.MainTitle) &&
            ModClass.KINGDOM_TITLE_MANAGER.get(data.MainTitle) != null)
        {
            data.RealmTitles.Insert(0, data.MainTitle);
        }
        return data.RealmTitles;
    }

    public static bool RegisterRealmTitle(this Kingdom kingdom, KingdomTitle title)
    {
        if (!kingdom.IsTitleWithinRealm(title)) return false;
        List<long> titles = kingdom.GetRealmTitleIds();
        if (titles.Contains(title.id)) return false;
        titles.Add(title.id);
        return true;
    }

    public static bool UnregisterRealmTitle(this Kingdom kingdom, KingdomTitle title)
    {
        return kingdom != null && title != null && kingdom.GetRealmTitleIds().Remove(title.id);
    }

    public static void SyncRealmTitlesFromRuler(this Kingdom kingdom, Actor ruler = null)
    {
        if (kingdom == null || kingdom.isRekt()) return;
        ruler ??= kingdom.hasKing() ? kingdom.king : null;
        if (ruler == null || ruler.kingdom != kingdom) return;
        foreach (long titleId in (ruler.GetOwnedTitle() ?? new List<long>()).ToList())
        {
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(titleId);
            if (kingdom.IsTitleWithinRealm(title)) kingdom.RegisterRealmTitle(title);
        }
    }

    public static void TransferRealmTitlesToRuler(this Kingdom kingdom, Actor ruler)
    {
        if (kingdom == null || kingdom.isRekt() || ruler == null || ruler.isRekt() ||
            ruler.kingdom != kingdom) return;
        List<long> realmTitles = kingdom.GetRealmTitleIds();
        foreach (long titleId in realmTitles.ToList())
        {
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(titleId);
            if (title == null || title.isRekt())
            {
                realmTitles.Remove(titleId);
                continue;
            }
            if (title.owner != null && title.owner != ruler)
            {
                title.owner.GetOwnedTitle()?.Remove(title.id);
            }
            ruler.AddOwnedTitle(title);
            title.owner = ruler;
        }

        KingdomTitle mainTitle = ModClass.KINGDOM_TITLE_MANAGER.get(kingdom.GetOrCreate().MainTitle);
        if (mainTitle != null && ruler.GetOwnedTitle()?.Contains(mainTitle.id) == true)
        {
            ruler.SetMainTitle(mainTitle);
        }
    }

    public static void SetMainTitle(this Kingdom k, KingdomTitle title)
    {
        if (k == null || title == null || title.isRekt()) return;
        if (title.title_capital == null || title.title_capital.isRekt()) return;
        if (k.capital == null || k.capital.isRekt()) return;
        if (title.title_capital != k.capital) return;
        var extraData = k.GetOrCreate();
        var current = ModClass.KINGDOM_TITLE_MANAGER.get(extraData.MainTitle);
        if (current == title)
        {
            KingdomTitle delegatedTitle = ModClass.KINGDOM_TITLE_MANAGER.get(extraData.AdministrativeTitle);
            delegatedTitle?.EndJurisdiction(k, KingdomTitle.JurisdictionAdministration);
            extraData.AdministrativeTitle = -1L;
            if (title.main_kingdom == null)
            {
                title.main_kingdom = k;
            }
            k.GetOrCreate().MainTitle = title.getID();
            k.RegisterRealmTitle(title);
            title.RecordJurisdiction(k, KingdomTitle.JurisdictionHolder);
            delegatedTitle.RefreshAdministrativeDivisionNames();
            return;
        }
        if (current != null && current != title)
        {
            current.EndJurisdiction(k, KingdomTitle.JurisdictionHolder);
            if (current.main_kingdom == k) current.main_kingdom = null;
        }
        if (title.main_kingdom != null && title.main_kingdom != k)
        {
            if (k.hasKing() && title.main_kingdom.hasKing() && title.main_kingdom.king == k.king)
            {
                return;
            }
            title.main_kingdom.RemoveMainTitle();
        }
        //正式取得法理后，行政授权关系即被实际头衔所有权取代。
        KingdomTitle delegated = ModClass.KINGDOM_TITLE_MANAGER.get(extraData.AdministrativeTitle);
        delegated?.EndJurisdiction(k, KingdomTitle.JurisdictionAdministration);
        extraData.AdministrativeTitle = -1L;
        TranslateHelper.LogKingdomChangeMainTitle(k, title);
        title.main_kingdom = k;
        k.GetOrCreate().MainTitle = title.getID();
        k.RegisterRealmTitle(title);
        title.RecordJurisdiction(k, KingdomTitle.JurisdictionHolder);
        delegated.RefreshAdministrativeDivisionNames();
    }

    public static void SetAdministrativeTitle(this Kingdom kingdom, KingdomTitle title)
    {
        if (kingdom == null || title == null || title.isRekt()) return;
        if (kingdom.GetMainTitle() != null) return;
        var extraData = kingdom.GetOrCreate();
        KingdomTitle previous = ModClass.KINGDOM_TITLE_MANAGER.get(extraData.AdministrativeTitle);
        if (previous != null && previous != title)
            previous.EndJurisdiction(kingdom, KingdomTitle.JurisdictionAdministration);
        extraData.AdministrativeTitle = title.id;
        title.RecordJurisdiction(kingdom, KingdomTitle.JurisdictionAdministration);
        // Newly created divisions have not calculated their administrative kingdom type yet.
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        if (previous != title) previous.RefreshAdministrativeDivisionNames();
        title.RefreshAdministrativeDivisionNames();
    }

    public static KingdomTitle GetAdministrativeTitle(this Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return null;
        var extraData = kingdom.GetOrCreate();
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(extraData.AdministrativeTitle);
        KingdomType kingdomType = kingdom.GetKingdomType();
        bool isAdministrative = kingdom.GetRegime()?.type == RegimeType.LvLing &&
            (kingdomType == KingdomType.LvLing_province || kingdomType == KingdomType.LvLing_jiedushi) &&
            kingdom.GetMainTitle() == null;
        if (!isAdministrative)
        {
            title?.EndJurisdiction(kingdom, KingdomTitle.JurisdictionAdministration);
            extraData.AdministrativeTitle = -1L;
            return null;
        }

        bool HasManagedTerritory(KingdomTitle candidate) => candidate != null && !candidate.isRekt() &&
            candidate.city_list != null && kingdom.cities != null && candidate.city_list.Any(kingdom.cities.Contains);
        if (HasManagedTerritory(title)) return title;
        title?.EndJurisdiction(kingdom, KingdomTitle.JurisdictionAdministration);

        //旧存档没有授权字段时，可由行政首府所属法理恢复一次。
        title = kingdom.hasCapital() && kingdom.capital != null && kingdom.capital.hasTitle()
            ? kingdom.capital.GetTitle()
            : null;
        if (!HasManagedTerritory(title))
        {
            title?.EndJurisdiction(kingdom, KingdomTitle.JurisdictionAdministration);
            extraData.AdministrativeTitle = -1L;
            return null;
        }
        extraData.AdministrativeTitle = title.id;
        title.RecordJurisdiction(kingdom, KingdomTitle.JurisdictionAdministration);
        return title;
    }

    public static string GetAdministrativeProvinceName(this Kingdom kingdom)
    {
        KingdomTitle title = kingdom.GetAdministrativeTitle();
        if (!kingdom.CanUseAdministrativeProvinceName(title)) return null;
        if (!string.IsNullOrWhiteSpace(title?.data?.province_name)) return title.data.province_name;
        return title?.title_capital != null && !title.title_capital.isRekt()
            ? title.title_capital.GetCityName()
            : null;
    }

    public static bool CanUseAdministrativeProvinceName(this Kingdom kingdom, KingdomTitle title = null)
    {
        if (kingdom == null || kingdom.isRekt()) return false;
        title ??= kingdom.GetAdministrativeTitle();
        if (title == null || title.isRekt()) return false;

        List<Kingdom> administrations = KingdomTitleRelationResolver.FindCurrentAdministrations(title);
        long capitalControllerId = title.title_capital?.kingdom?.id ?? -1L;
        long holderId = AdministrativeDivisionNameRules.SelectProvinceNameHolder(capitalControllerId,
            administrations.Select(administration => administration.id));
        return holderId == kingdom.id;
    }

    public static void RefreshAdministrativeDivisionNames(this KingdomTitle title)
    {
        if (title == null || title.isRekt()) return;
        foreach (Kingdom administration in KingdomTitleRelationResolver.FindCurrentAdministrations(title))
        {
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(administration);
        }
    }

    public static void RemoveMainTitle(this Kingdom k)
    {
        KingdomTitle kt = ModClass.KINGDOM_TITLE_MANAGER.get(k.GetOrCreate().MainTitle);
        if (kt != null)
        {
            kt.EndJurisdiction(k, KingdomTitle.JurisdictionHolder);
            kt.main_kingdom = null;
        }
        k.GetOrCreate().MainTitle = -1L;
    }
    
    public static KingdomTitle GetMainTitle(this Kingdom k)
    {
        if (k == null) return null;
        if (GetOrCreate(k) == null) return null;
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(GetOrCreate(k).MainTitle);
        if (title == null || title.isRekt() || title.title_capital == null || title.title_capital.isRekt())
        {
            title?.EndJurisdiction(k, KingdomTitle.JurisdictionHolder);
            k.GetOrCreate().MainTitle = -1L;
            return null;
        }

        Actor ruler = k.hasKing() ? k.king : null;
        bool rulerListsTitle = ruler?.GetOwnedTitle()?.Contains(title.id) ?? false;
        if (ruler == null || (title.owner != ruler && !(title.owner == null && rulerListsTitle)))
        {
            title.EndJurisdiction(k, KingdomTitle.JurisdictionHolder);
            if (title.main_kingdom == k)
            {
                title.main_kingdom = null;
            }
            k.GetOrCreate().MainTitle = -1L;
            return null;
        }
        if (title.owner == null)
        {
            title.owner = ruler;
        }

        if (k.capital == null || k.capital.isRekt() || title.title_capital != k.capital)
        {
            title.EndJurisdiction(k, KingdomTitle.JurisdictionHolder);
            if (title.main_kingdom == k)
            {
                title.main_kingdom = null;
            }
            k.GetOrCreate().MainTitle = -1L;
            return null;
        }

        if (title.main_kingdom != null && title.main_kingdom != k)
        {
            title.EndJurisdiction(k, KingdomTitle.JurisdictionHolder);
            k.GetOrCreate().MainTitle = -1L;
            return null;
        }

        if (title.main_kingdom == null)
        {
            title.main_kingdom = k;
        }

        return title;
    }

    public static bool HasMainTitle(this Kingdom k)
    {
        return k.GetMainTitle() != null;
    }

    public static bool IsDeJureTitleAcquisitionBlocked(this Kingdom kingdom, Empire titleEmpire = null)
    {
        if (kingdom == null || kingdom.isRekt()) return true;
        Empire empire = titleEmpire ?? kingdom.GetEmpire();
        if (empire == null || kingdom.GetEmpire() != empire) return false;
        bool isLvLing = empire.CoreKingdom?.GetRegime()?.type == RegimeType.LvLing;
        KingdomType kingdomType = kingdom.GetKingdomType();
        bool isProvinceOrMilitary = kingdomType == KingdomType.LvLing_province ||
                                    kingdomType == KingdomType.LvLing_jiedushi;
        return DeJureTitleBindingRules.IsAdministrativeAcquisitionBlocked(isLvLing,
            isProvinceOrMilitary, empire.Mandate);
    }

    public static bool CanPursueDeJureTitle(this Kingdom kingdom, Empire titleEmpire = null)
    {
        if (kingdom == null || kingdom.isRekt()) return false;
        // Tributaries may only receive an imperial-core title through the petition plot.
        if (kingdom.HasTakenAlliance()) return false;
        Empire empire = titleEmpire ?? kingdom.GetEmpire();
        if (kingdom.IsDeJureTitleAcquisitionBlocked(empire)) return false;
        if (kingdom.GetRegime()?.IsAllowDiplomacy() == true) return true;
        if (empire == null || kingdom.GetEmpire() != empire ||
            empire.CoreKingdom?.GetRegime()?.type != RegimeType.LvLing) return false;
        KingdomType kingdomType = kingdom.GetKingdomType();
        bool isProvinceOrMilitary = kingdomType == KingdomType.LvLing_province ||
                                    kingdomType == KingdomType.LvLing_jiedushi;
        return isProvinceOrMilitary && empire.Mandate <= DeJureTitleBindingRules.WeakMandateThreshold;
    }

    public static bool ReconcileMainTitle(this Kingdom kingdom,
        IEnumerable<KingdomTitle> newlyAcquiredTitles = null)
    {
        if (kingdom == null || kingdom.isRekt() || !kingdom.hasKing() || !kingdom.hasCapital()) return false;
        Actor king = kingdom.king;
        List<long> ownedTitleIds = king.GetOwnedTitle() ?? new List<long>();
        KingdomTitle storedTitle = ModClass.KINGDOM_TITLE_MANAGER.get(kingdom.GetOrCreate().MainTitle);

        bool IsUsable(KingdomTitle title)
        {
            return title != null && !title.isRekt() && title.title_capital != null &&
                   !title.title_capital.isRekt() && title.title_capital.kingdom == kingdom &&
                   kingdom.cities.Contains(title.title_capital) &&
                   (title.owner == king || title.owner == null && ownedTitleIds.Contains(title.id));
        }

        // The capital's de jure title always wins. A newly acquired title is the
        // fallback, followed by a previously owned title for save-game recovery.
        KingdomTitle preferred = kingdom.GetCapitalMainTitleCandidate();
        if (!IsUsable(preferred)) preferred = null;
        if (preferred == null && IsUsable(storedTitle)) preferred = storedTitle;
        if (preferred == null && newlyAcquiredTitles != null)
            preferred = newlyAcquiredTitles.FirstOrDefault(IsUsable);
        if (preferred == null)
            preferred = ownedTitleIds.Select(ModClass.KINGDOM_TITLE_MANAGER.get).FirstOrDefault(IsUsable);
        if (preferred == null)
        {
            if (storedTitle != null)
            {
                storedTitle.EndJurisdiction(kingdom, KingdomTitle.JurisdictionHolder);
                if (storedTitle.main_kingdom == kingdom) storedTitle.main_kingdom = null;
            }
            bool cleared = kingdom.GetOrCreate().MainTitle != -1L;
            kingdom.GetOrCreate().MainTitle = -1L;
            return cleared;
        }

        bool capitalChanged = kingdom.capital != preferred.title_capital;
        bool titleChanged = storedTitle != preferred || storedTitle?.main_kingdom != kingdom;
        if (!capitalChanged && !titleChanged)
        {
            kingdom.GetEmpire()?.SynchronizeLandedLegalTitles(kingdom);
            return false;
        }

        if (capitalChanged)
        {
            kingdom.setCapital(preferred.title_capital);
            if (kingdom.capital != preferred.title_capital) return false;
        }

        kingdom.SetMainTitle(preferred);
        if (kingdom.GetMainTitle() != preferred) return false;
        kingdom.GetEmpire()?.SynchronizeLandedLegalTitles(kingdom);
        if (capitalChanged) TranslateHelper.LogKingdomChangeCapitalToTitle(kingdom, preferred);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        return true;
    }

    public static bool TrySetPreferredMainTitle(this Kingdom k, KingdomTitle title)
    {
        if (k == null || title == null || title.isRekt()) return false;
        if (title.title_capital == null || title.title_capital.isRekt()) return false;
        if (k.capital == null || k.capital.isRekt() || title.title_capital != k.capital) return false;

        var current = k.GetMainTitle();
        bool shouldReplace = current == null;

        if (!shouldReplace && current != title)
        {
            bool currentInvalid = current.title_capital == null || current.title_capital.isRekt();
            bool currentCapitalMismatch = k.capital == null || current.title_capital != k.capital;

            shouldReplace = currentInvalid || currentCapitalMismatch;
        }

        if (!shouldReplace) return false;

        k.SetMainTitle(title);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(k);
        return true;
    }

    public static KingdomTitle GetCapitalMainTitleCandidate(this Kingdom kingdom)
    {
        if (kingdom == null || !kingdom.hasKing() || !kingdom.hasCapital()) return null;
        if (!kingdom.capital.hasTitle()) return null;
        var title = kingdom.capital.GetTitle();
        if (title == null || title.isRekt()) return null;
        bool listedAsOwned = kingdom.king.GetOwnedTitle()?.Contains(title.id) ?? false;
        if (title.owner != kingdom.king && !(title.owner == null && listedAsOwned)) return null;
        return title;
    }

    public static KingdomTitle GetAncestralMainTitleCandidate(this Kingdom kingdom)
    {
        if (kingdom == null || !kingdom.hasKing()) return null;
        var clan = kingdom.king.GetSpecificClan();
        var city = clan?.GetAncestralCity();
        if (city == null || city.isRekt() || !city.hasTitle()) return null;
        var title = city.GetTitle();
        if (title == null || title.isRekt()) return null;
        bool listedAsOwned = kingdom.king.GetOwnedTitle()?.Contains(title.id) ?? false;
        if (title.owner != kingdom.king && !(title.owner == null && listedAsOwned)) return null;
        return title;
    }

    public static bool CanBecomeEmpire(this Kingdom k)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.BlocksEmpireFormation(k)) return false;
        if (!k.hasKing()) return false;
        if (k.IsInEmpire()) return false;
        if (k.isRekt() || k.IsEmpire()) return false;
        if (k.countUnits() < 200) return false;

        EmpireCore riseCore = EmpireCoreManager.GetRiseCandidateCore(k);
        if (riseCore != null)
        {
            if (EmpireCoreManager.GetActiveEmpireCount(riseCore) >= 2)
            {
                return false;
            }
            int controlledCount = EmpireCoreManager.GetControlledCoreTitleCount(riseCore, k);
            int requiredCount = EmpireCoreManager.GetRequiredRiseTitleCount(riseCore);
            if (controlledCount < requiredCount)
            {
                return false;
            }

            KingdomTitle capitalTitle = k.capital?.GetTitle();
            if (capitalTitle != null && EmpireCoreManager.ContainsTitle(riseCore, capitalTitle))
            {
                List<City> coreCities = EmpireCoreManager.GetCities(riseCore);
                int totalCityCount = coreCities.Count(c => c != null && !c.isRekt());
                if (totalCityCount <= 0)
                {
                    return false;
                }

                int controlledCityCount = coreCities.Count(c => c != null && !c.isRekt() && c.kingdom == k);
                int requiredCityCount = (int)Math.Ceiling(totalCityCount / 2f);
                if (controlledCityCount < requiredCityCount)
                {
                    return false;
                }
            }

            return true;
        }

        int allEmpireNumInSameSpecies = World.world.kingdoms.ToList().FindAll(p => p.getSpecies() == k.getSpecies() && p.IsEmpire()).Count;
        return IsStrongestOfSameSpecies(k) && allEmpireNumInSameSpecies < 1;
    }

    private static bool IsStrongestOfSameSpecies(Kingdom k)
    {
        return !World.world.kingdoms.Any(other =>
            other != k &&
            other.getSpecies() == k.getSpecies() &&
            !other.isRekt() &&
            IsStronger(other, k));
    }

    private static bool IsStronger(Kingdom a, Kingdom b)
    {
        if (a.IsEmpire())
        {
            var emp = a.GetEmpire();
            var empUnits = emp?.getUnits();
            if (emp == null || emp.isRekt() || emp.IsArchived() || empUnits == null) return false;
            return empUnits.Count() > (b?.units?.Count ?? 0);
        }
        return a.countUnits() > (b?.countUnits() ?? 0);
    }
    public static KingdomExtraData GetOrCreate(this Kingdom a, bool isSave = false)
    {
        var ed = a.GetOrCreate<Kingdom, KingdomExtraData>(isSave);
        return ed;
    }

    public static double GetFiedTimestamp(this Kingdom k)
    {
        return GetOrCreate(k).TimestampBeFeifed;
    }

    public static void SetFiedTimestamp(this Kingdom k, double v)
    {
        GetOrCreate(k).TimestampBeFeifed = v;
    }

    public static string EnsureKingdomCoreName(this Kingdom kingdom)
    {
        if (kingdom?.data == null) return "";
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return kingdom.data.name ?? "";
        bool rekt = kingdom.isRekt();
        if (!rekt)
        {
            KingdomExtraData data = kingdom.GetOrCreate();
            if (!string.IsNullOrWhiteSpace(data.core_name))
            {
                if (string.IsNullOrEmpty(data.core_name_source))
                {
                    data.core_name_source = kingdom.data.name ?? "";
                    return data.core_name;
                }
                if (string.Equals(data.core_name_source, kingdom.data.name ?? "", StringComparison.Ordinal) ||
                    kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling()) return data.core_name;
            }
        }

        string coreName = "";
        try
        {
            coreName = ExtractKingdomFront(kingdom, kingdom.data.name);
            if (!rekt && !string.IsNullOrWhiteSpace(coreName))
            {
                string suffix = LM.Get(kingdom.GetKingdomType().ToString());
                if (!string.IsNullOrWhiteSpace(suffix) && string.Equals(coreName, suffix, StringComparison.Ordinal))
                {
                    coreName = "";
                }
            }

            if (string.IsNullOrWhiteSpace(coreName))
            {
                if (rekt) return kingdom.data.name?.Trim() ?? "";
                coreName = GetKingdomFrontFallback(kingdom);
            }
        }
        catch
        {
            coreName = "";
        }

        if (string.IsNullOrWhiteSpace(coreName)) coreName = kingdom.data.name?.Trim() ?? "";
        if (!rekt && !string.IsNullOrWhiteSpace(coreName))
        {
            try { kingdom.SetKingdomCoreName(coreName); } catch { }
        }
        return coreName;
    }

    public static string GetKingdomName(this Kingdom kingdom)
    {
        // CoreSystemObject.name dereferences data directly, but disposed kingdoms keep a
        // non-null object reference after their data has been cleared.
        if (kingdom?.data == null) return "";
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return kingdom.data.name ?? "";
        return kingdom.EnsureKingdomCoreName();
    }

    public static string GetKingdomFullName(this Kingdom kingdom)
    {
        if (kingdom?.data == null) return "";
        try
        {
            if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return kingdom.data.name ?? "";
            if (!kingdom.isRekt() && (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling()))
                return kingdom.data.name?.UseLocalizedNameSeparator() ?? "";
            string coreName = kingdom.EnsureKingdomCoreName();
            if (string.IsNullOrWhiteSpace(coreName)) return kingdom.data.name ?? "";
            if (kingdom.isRekt()) return coreName;
            string typeName = LM.Get(kingdom.GetKingdomType().ToString());
            return OverallHelperFunc.FormatKingdomFullName(coreName, typeName, kingdom.GetEmpireCraftCulture());
        }
        catch
        {
            return kingdom.data.name?.UseLocalizedNameSeparator() ?? "";
        }
    }

    private static string GetKingdomFrontFallback(Kingdom kingdom)
    {
        if (kingdom?.data == null) return "";
        string administrativeName = kingdom.GetAdministrativeProvinceName();
        if (!string.IsNullOrWhiteSpace(administrativeName))
        {
            return administrativeName;
        }
        var title = kingdom.GetMainTitle();
        if (!string.IsNullOrWhiteSpace(title?.data?.name))
        {
            return title.data.name;
        }

        return kingdom.GetUntitledKingdomName();
    }

    public static bool IsInSameEmpire(this Kingdom kingdom, Kingdom pKingdomTaget)
    {
        if (kingdom == null || pKingdomTaget == null) return false;
        if (!kingdom.IsInEmpire() || !pKingdomTaget.IsInEmpire()) return false;
        return kingdom.GetEmpireID() == pKingdomTaget.GetEmpireID();
    }

    public static void SetEmpireID(this Kingdom kingdom, long value)
    {
        GetOrCreate(kingdom).EmpireID = value;
    }
    public static long GetEmpireID(this Kingdom kingdom)
    {
        if (kingdom == null) return -1L;
        return GetOrCreate(kingdom).EmpireID;
    }    
    public static Empire GetEmpire(this Kingdom kingdom)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return null;
        if (ModClass.EMPIRE_MANAGER == null) return null;
        if (kingdom == null) return null;
        Empire empire = ModClass.EMPIRE_MANAGER.get(kingdom.GetEmpireID());
        return EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(empire?.CoreKingdom) ? null : empire;
    }

    public static void CheckEmpire(this Kingdom kingdom)
    {
        if (!kingdom.IsInEmpire()) return;
        var emp = kingdom.GetEmpire();
        if (emp == null || emp.isRekt() || emp.IsArchived())
        {
            kingdom.EmpireLeave();
            return;
        }
        if (kingdom.HasRebelledAgainst(emp))
        {
            emp.leave(kingdom);
            return;
        }
        if (kingdom == emp.CoreKingdom)
        {
            kingdom.GetOrCreate().isEmpire = true;
        }
    }
    public static void SetTimestampEmpire(this Kingdom kingdom, double value)
    {
        GetOrCreate(kingdom).TimestampEmpire = value;
    }
    public static double GetTimestampEmpire(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).TimestampEmpire;
    }

    public static List<Empire> GetEmpiresCanBeJoined(this Kingdom kingdom)
    {
        List<Empire> empires = new List<Empire>();
        if (kingdom == null) return empires;
        if (ModClass.EMPIRE_MANAGER == null) return empires;
        if (!ModClass.EMPIRE_MANAGER.Any()) return empires;
        foreach(City city in kingdom.cities)
        {
            foreach(Kingdom k in city.neighbours_kingdoms)
            {
                if (k != kingdom)
                {
                    if (k.IsInEmpire())
                    {
                        Empire empire = k.GetEmpire();
                        if (empire != null)
                        {
                            if (empire.canJoin(kingdom) && !empires.Contains(empire))
                                empires.Add(empire);
                        }
                    }
                }
            }
        }
        return empires;
    }
    public static void EmpireJoin(this Kingdom kingdom, Empire pEmpire)
    {
        if (kingdom == null || pEmpire == null || kingdom.HasRebelledAgainst(pEmpire)) return;
        GetOrCreate(kingdom).EmpireID = pEmpire.data.id;
        GetOrCreate(kingdom).TimestampEmpire = World.world.getCurWorldTime();
    }

    public static bool IsEmpire(this Kingdom kingdom)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return false;
        if (kingdom == null) return false;
        if (kingdom.data == null) return false;
        var ed = GetOrCreate(kingdom);
        if (ed == null) return false;
        if (!ed.isEmpire) return false;

        var empire = kingdom.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null || empire.CoreKingdom != kingdom)
        {
            ed.isEmpire = false;
            return false;
        }

        return true;
    }

    public static void EmpireLeave (this Kingdom kingdom, bool isLeave = true)
    {
        if (kingdom==null) return;
        if (GetOrCreate(kingdom) == null) return;
        kingdom.generateColor();
        GetOrCreate(kingdom).EmpireID = -1L;
        kingdom.GetOrCreate().isEmpire = false;
    }
    public static int GetLevel(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).Level;
    }

    public static List<KingdomTitle> GetControlledTitles(this Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return new List<KingdomTitle>();
        kingdom.SyncRealmTitlesFromRuler();
        return kingdom.GetRealmTitleIds()
            .Select(ModClass.KINGDOM_TITLE_MANAGER.get)
            .Where(title => title != null && !title.isRekt())
            .Distinct()
            .ToList();
    }
    public static bool HasAnyControlledTitle(this Kingdom kingdom)
    {
        return kingdom.GetControlledTitle().Any();
    }

    public static List<KingdomTitle> GetControlledTitle(this Kingdom kingdom)
    {
        List<KingdomTitle> controlledTitles = new List<KingdomTitle>();
        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER)
        {
            var titleCities = title.city_list;
            int commonCount = titleCities.Intersect(kingdom.cities).Count();
            if (commonCount >= Math.Ceiling(titleCities.Count * title.data.title_controlled_rate))
            {
                controlledTitles.Add(title);
            }
        }
        return controlledTitles;
    }

    public static Kingdom FindClosestKingdom(this Kingdom kingdom)
    {
        if (kingdom == null || kingdom.cities == null || kingdom.cities.Count == 0)
        {
            return null;
        }

        Kingdom closestKingdom = null;
        int closestDistanceSqr = int.MaxValue;

        foreach (Kingdom otherKingdom in World.world.kingdoms)
        {
            if (otherKingdom == null)
            {
                continue;
            }

            if (otherKingdom == kingdom)
            {
                continue;
            }

            if (otherKingdom.isRekt())
            {
                continue;
            }

            if (otherKingdom.cities == null || otherKingdom.cities.Count == 0)
            {
                continue;
            }

            foreach (City city in kingdom.cities)
            {
                if (city == null || city._city_tile == null)
                {
                    continue;
                }

                Vector2Int cityPos = city._city_tile.pos;

                foreach (City otherCity in otherKingdom.cities)
                {
                    if (otherCity == null || otherCity._city_tile == null)
                    {
                        continue;
                    }

                    Vector2Int otherPos = otherCity._city_tile.pos;

                    int dx = cityPos.x - otherPos.x;
                    int dy = cityPos.y - otherPos.y;

                    int distanceSqr = dx * dx + dy * dy;

                    if (distanceSqr < closestDistanceSqr)
                    {
                        closestDistanceSqr = distanceSqr;
                        closestKingdom = otherKingdom;
                    }
                }
            }
        }

        return closestKingdom;
    }

    public static bool IsNeighbourWith(this Kingdom kingdom, Kingdom target)
    {
        if (kingdom == null || target == null) return false;
        if(kingdom.IsEmpire())
        {
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.isRekt() || empire.IsArchived()) return false;
            return empire.IsNeighbourWith(target);
        }
        var cities = kingdom.cities;
        if (cities == null || cities.Count == 0) return false;
        foreach(City city in cities)
        {
            if (city == null) continue;
            var neighbours = city.neighbours_kingdoms;
            if (neighbours == null) continue;
            if (neighbours.Contains(target))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsBorder(this Kingdom kingdom)
    {
        if(kingdom.IsEmpire()) return false;
        foreach(City city in kingdom.cities)
        {
            if (city.neighbours_kingdoms.Count > 0)
            {
                foreach(Kingdom kingdom2 in city.neighbours_kingdoms)
                {
                    if (!kingdom2.IsInSameEmpire(kingdom))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public static void SetLevel(this Kingdom kingdom, int value)
    {

        GetOrCreate(kingdom).Level = value;
    }    

    public static bool IsInEmpire(this Kingdom kingdom)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return false;
        if (kingdom == null || kingdom.isRekt()) return false;
        var ed = kingdom.GetOrCreate();
        if (ed == null) return false;
        var id = ed.EmpireID;
        if (id == -1L) return false;
        if (ModClass.EMPIRE_MANAGER == null) return false;
        var emp = ModClass.EMPIRE_MANAGER.get(id);
        return emp != null && !emp.IsArchived() && !emp.isRekt() &&
            !EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(emp.CoreKingdom);
    }
    public static void EndWarWith(this Kingdom kingdom, Kingdom kingdom2)
    {
        var wars = kingdom.GetWarsCached(false);
        for (int i = 0; i < wars.Count; i++)
        {
            var w = wars[i];
            var attackers = w.getAttackers().ToArray();
            var defenders = w.getDefenders().ToArray();
            bool has = false;
            for (int a = 0; a < attackers.Length; a++) { if (attackers[a] == kingdom2) { has = true; break; } }
            if (!has)
            {
                for (int d = 0; d < defenders.Length; d++) { if (defenders[d] == kingdom2) { has = true; break; } }
            }
            if (has) w.lostWar(kingdom2);
        }
    }

    public static List<War> GetWarsCached(this Kingdom kingdom, bool pRandom = false)
    {
        var ed = GetOrCreate(kingdom);
        if (ed.last_wars_ts > 0 && Date.getMonthsSince(ed.last_wars_ts) < 1 && ed.cached_wars != null)
        {
            return ed.cached_wars;
        }
        var warsEnum = kingdom.getWars(pRandom);
        List<War> list;
        if (warsEnum is List<War> lw)
        {
            list = lw;
        }
        else
        {
            list = warsEnum.ToList();
        }
        ed.cached_wars = list;
        ed.last_wars_ts = World.world.getCurWorldTime();
        return list;
    }
}

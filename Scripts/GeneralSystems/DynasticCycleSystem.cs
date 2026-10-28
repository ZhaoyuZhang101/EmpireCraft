using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 王朝积弊(0~100)：王朝存续期间只涨不降(用户 2026-10-26："积弊无法削减，仅可延缓增长")，治世、中兴、明君只给增长打折
// (见 DynasticCycleRules)；六条朝政弊病(MaladySystem)照旧可治、可降，治下来只是不再推高——但王朝积弊越深，
// 六弊滋生越快、也治不到它的一半以下(用户 2026-10-27 要两者有关联，见 MaladySystem.Floor)。
//   · 加速：庸昏暴之君、六弊、派系失衡、连年饥荒；君主早逝一次 +2.5、近 20 年换了 3 位以上君主每年 +0.3、禅让篡位一次 +5；
//   · 满额(≥99.5)：记下满额起始时间(SaturatedYears)，每年必有民变(见 HarshRuleSystem.TryUprising)，满额后第一场民变记下时间与
//     原有城数——律令制群雄割据的入口(见 ProvincialCollapseSystem)；
//   · 改朝换代三档(用户 2026-10-26)：清空(外姓打进京城、外藩入继)、减半(兵变禅让、宗室另立或复国)、保留(权臣外戚代立)。
public static class DynasticCycleSystem
{
    public const string HarshSource = "harsh_dynastic_strain";

    // 改朝换代的三档(用户 2026-10-26)
    public enum Transition
    {
        // 清空：外姓打进京城、外藩入继这类真正的改朝换代——王朝积弊留四分之一(至多 20)、六弊各留两成、派系失衡留三成，王朝年岁从头算
        Clear,
        // 减半：军阀兵变禅让上位(赵匡胤式)、宗室另立或复国(东晋、南宋式)——王朝积弊、六弊减半，王朝年岁从头算，不另给开国气象
        Halve,
        // 保留：权臣篡位、外戚代立(司马炎式)——王朝积弊、六弊原样，王朝年岁接着算(以前归零，按新朝慢速滋生，几十年就消化掉)
        Keep
    }

    public static bool Applies(Empire empire) => MonarchyLegitimacy.UsesTraditionalModel(empire) &&
        !RepublicSystem.IsRepublic(empire);
    public static float GetPressure(Empire empire) => Applies(empire)
        ? Math.Max(0f, Math.Min(100f, empire.data.constitutional_economy?.dynastic_strain ?? 0f)) : 0f;
    public static bool InCrisis(Empire empire) => GetPressure(empire) >= DynasticCycleRules.CrisisPressure;
    public static bool IsSaturated(Empire empire) => GetPressure(empire) >= DynasticCycleRules.SaturatedPressure;

    // 满额了几年(未满额返回 0)
    public static float SaturatedYears(Empire empire)
    {
        double since = empire?.data?.constitutional_economy?.dynastic_saturated_since ?? -1d;
        if (since < 0d || World.world == null || !IsSaturated(empire) || World.world.getCurWorldTime() < since) return 0f;
        return Math.Max(0, Date.getYearsSince(since));
    }

    // 满额后的民变已经起了(群雄割据入口的前提，见 ProvincialCollapseSystem)
    public static bool SaturatedUprisingStarted(Empire empire) =>
        IsSaturated(empire) && (empire.data.constitutional_economy?.saturated_uprising_at ?? -1d) >= 0d;

    public static int GetAge(Empire empire)
    {
        if (World.world == null || empire?.data == null) return 0;
        double since = empire.data.constitutional_economy?.dynastic_since ?? -1d;
        if (since < 0d) since = empire.data.created_time;
        int offset = Math.Max(0, empire.data.constitutional_economy?.dynastic_age_offset ?? 0);
        return (since >= 0d ? Math.Max(0, Date.getYearsSince(since)) : 0) + offset;
    }

    // 补底的诊断只记一次(每个帝国、每次进游戏)
    private static readonly HashSet<long> CatchUpLogged = new();

    // 每年在苛政结算中调用；传入的负担不包含本系统自己的贡献。
    public static void Update(Empire empire, ConstitutionalEconomyState state, float governanceBurden)
    {
        if (state == null || !Applies(empire)) return;
        double now = World.world.getCurWorldTime();
        if (state.last_dynastic_update >= 0d && now >= state.last_dynastic_update &&
            Date.getYearsSince(state.last_dynastic_update) < 1) return;
        bool bankrupt = empire.CurrentMoney < 0;
        bool atWar = empire.CoreKingdom.getWars().Any();
        bool interregnum = empire.Emperor == null || empire.Emperor.isRekt() || !empire.Emperor.isAlive();
        double corruption = CorruptionSystem.GetRate(empire);
        if (state.dynastic_since < 0d)
        {
            state.dynastic_since = FindDynastyStart(empire, now);
            bool good = !bankrupt && !atWar && governanceBurden <= 20f && corruption <= 0.25d && !interregnum;
            state.dynastic_strain = DynasticCycleRules.LegacyPressure(GetAge(empire), governanceBurden, good);
        }
        state.last_dynastic_update = now;
        TrackRuler(empire, state, now);
        // 君主资质：庸弱、昏聩、荒淫、暴虐加速衰败，明君(含开国雄主、中兴之主)给增长打六折(见 RulerQualitySystem)
        float ruler = interregnum ? 0f : RulerQualitySystem.StrainDelta(empire.Emperor);
        // 六条朝政积弊合计(见 MaladySystem)、派系失衡(见 FactionBalanceSystem)、连年饥荒(见 FamineUnrestSystem)推高王朝积弊；
        // 只取正——六弊治下来只是不再推高，王朝积弊照样不降
        float extra = Math.Max(0f, ruler) + Math.Max(0f, MaladySystem.StrainDelta(empire)) +
                      Math.Max(0f, FactionBalanceSystem.StrainDelta(empire)) + Math.Max(0f, FamineUnrestSystem.StrainDelta(empire));
        float growth = DynasticCycleRules.AnnualGrowth(GetAge(empire), governanceBurden, bankrupt, atWar, corruption,
            interregnum, RulerTraitSystem.ReignedBy(empire.CoreKingdom, RulerTraitSystem.Restorer), ruler < 0f, extra,
            state.harsh_other_burden);
        // 历史周期律：积弊随王朝年岁慢慢沉积，开国气象里不涨、立国头 80 年二成、160 年内四成五(同 MaladySystem.Pace)
        float pace = MaladySystem.Pace(empire);
        float change = growth * pace;
        // 皇位更迭频繁：近 20 年换了 3 位以上君主，每年另加(不随王朝年岁打折，开国气象里照旧不涨)
        if (pace > 0f && RecentSuccessions(state, now) >= DynasticCycleRules.ChurnSuccessions)
            change += DynasticCycleRules.ChurnStrain;
        // 旧档补底：积弊低于同岁数最少该有的(以前治世会往回降、旧档读进来只补了一部分)，每年另补，补到为止
        if (pace > 0f)
        {
            float floor = DynasticCycleRules.AgeFloor(GetAge(empire));
            float gap = floor - (state.dynastic_strain + change);
            if (gap > 0f)
            {
                change += Math.Min(gap, DynasticCycleRules.CatchUpPerYear);
                if (CatchUpLogged.Add(empire.id))
                    NeoModLoader.services.LogService.LogInfo($"[EmpireCraft][王朝积弊] {empire.GetEmpireFullName()} 国祚{GetAge(empire)}年，" +
                        $"积弊{state.dynastic_strain:0.0}低于同岁数最少该有的{floor:0.0}，每年补{DynasticCycleRules.CatchUpPerYear}直到补上");
            }
        }
        state.dynastic_strain = DynasticCycleRules.Advance(state.dynastic_strain, Math.Max(0f, change));
        MonarchyLegitimacy.Invalidate(empire);
        Settle(empire, state, now);
        // 满额以来别处起的民变(连年饥荒、失地农民起义)同样算满额民变已起
        if (state.dynastic_saturated_since >= 0d && state.saturated_uprising_at < 0d &&
            PopularRebellions(empire, state.dynastic_saturated_since).count > 0)
            OnSaturatedUprising(empire);
    }

    // 危机线、满额的记载。积弊不再回落，以前回到 40 以下记"走出危机"的那一笔删了；改朝换代降到危机线下才重新记
    private static void Settle(Empire empire, ConstitutionalEconomyState state, double now)
    {
        if (state.dynastic_strain >= DynasticCycleRules.CrisisPressure && !state.dynastic_crisis_announced)
        {
            state.dynastic_crisis_announced = true;
            EventRecorder.Record(empire, string.Format(LM.Get("dynastic_crisis_history"), empire.GetEmpireFullName()));
        }
        if (state.dynastic_strain < DynasticCycleRules.SaturatedPressure)
        {
            ClearSaturation(state);
            return;
        }
        if (state.dynastic_saturated_since >= 0d) return;
        state.dynastic_saturated_since = now;
        EventRecorder.Record(empire, string.Format(LM.Get("dynastic_saturated_history"), empire.GetEmpireFullName()));
    }

    private static void ClearSaturation(ConstitutionalEconomyState state)
    {
        state.dynastic_saturated_since = -1d;
        state.saturated_uprising_at = -1d;
        state.saturated_origin_cities = 0;
    }

    #region 换君：早逝、更替频繁

    // 记下的在位君主不是当今(旧档、绕过即位登记的换君)：按当今补记，即位时间取帝国记的即位时间
    private static void TrackRuler(Empire empire, ConstitutionalEconomyState state, double now)
    {
        Actor ruler = empire.Emperor;
        if (ruler?.data == null || !ruler.isAlive() || state.reign_ruler_id == ruler.data.id) return;
        if (state.reign_ruler_id > 0)
        {
            OnSuccession(empire, ruler, false);
            return;
        }
        double since = empire.data.newEmperor_timestamp;
        if (since < 0d || since > now) since = now;
        state.reign_ruler_id = ruler.data.id;
        state.reign_ruler_since = since;
        state.reign_ruler_start_age = Math.Max(0, AgeOf(ruler) - Date.getYearsSince(since));
    }

    private static int AgeOf(Actor actor) => Math.Max(0, (int)Math.Round((double)actor.getAge()));

    // 近 20 年换君的次数(顺带清掉更早的)
    private static int RecentSuccessions(ConstitutionalEconomyState state, double now)
    {
        if (state.recent_successions == null || state.recent_successions.Count == 0) return 0;
        state.recent_successions.RemoveAll(at => at < 0d || at > now || Date.getYearsSince(at) >= DynasticCycleRules.ChurnYears);
        return state.recent_successions.Count;
    }

    // 换君(Empire.NewEmperor 即位登记的末尾)：本朝之内前任在位不满 10 年或卒年不满 30 算早逝，王朝积弊一次 +2.5；
    // 记下这次换君(近 20 年换了 3 位以上每年另加，见 Update)。新朝开国(清空、减半两档已清掉在位记录)从这一位重新记；
    // 权臣外戚代立(保留)照记一次换君，早逝不另算(篡位另有 +5)
    public static void OnSuccession(Empire empire, Actor ruler, bool newDynasty)
    {
        if (empire?.data == null || ruler?.data == null || World.world == null || !Applies(empire)) return;
        ConstitutionalEconomyState state = empire.data.constitutional_economy ??= new ConstitutionalEconomyState();
        if (state.reign_ruler_id == ruler.data.id) return;
        double now = World.world.getCurWorldTime();
        // 选帝制、推举共主(禅让)本就常换人、多立年长者，不算早逝与更替频繁
        bool elective = HolyRomanSystem.IsElective(empire) || ShanrangSystem.Elective(empire);
        if (!elective && state.reign_ruler_id > 0 && state.reign_ruler_since >= 0d && now >= state.reign_ruler_since)
        {
            state.recent_successions ??= new List<double>();
            state.recent_successions.Add(now);
            int reign = Math.Max(0, Date.getYearsSince(state.reign_ruler_since));
            int age = state.reign_ruler_start_age >= 0 ? state.reign_ruler_start_age + reign : -1;
            if (!newDynasty && (reign < DynasticCycleRules.PrematureReignYears || age >= 0 && age < DynasticCycleRules.PrematureAge))
            {
                state.dynastic_strain = DynasticCycleRules.Advance(state.dynastic_strain, DynasticCycleRules.PrematureStrain);
                LogService.LogInfo($"[EmpireCraft][王朝积弊] {empire.GetEmpireName()} 前任在位{reign}年、{(age >= 0 ? $"终年{age}" : "年纪不详")}，" +
                                   $"早逝 +{DynasticCycleRules.PrematureStrain}，积弊{state.dynastic_strain:0.0}");
                MonarchyLegitimacy.Invalidate(empire);
                Settle(empire, state, now);
            }
        }
        state.reign_ruler_id = ruler.data.id;
        state.reign_ruler_since = now;
        state.reign_ruler_start_age = AgeOf(ruler);
    }

    #endregion

    #region 改朝换代

    // 调用方在扶新君上位前声明这一次易代归哪一档(兵变禅让，见 MilitaryCoupSystem)；即位登记(Empire.NewEmperor →
    // ReignRecordSystem.OnDynastyChanged)据此处理。不存档，只在 install 执行期间有效
    private static long _expectedEmpire = -1L;
    private static Transition _expected = Transition.Clear;

    public static void Expect(Empire empire, Transition transition, Action install)
    {
        long previousEmpire = _expectedEmpire;
        Transition previous = _expected;
        _expectedEmpire = empire?.id ?? -1L;
        _expected = transition;
        try
        {
            install?.Invoke();
        }
        finally
        {
            _expectedEmpire = previousEmpire;
            _expected = previous;
        }
    }

    // 只在真正改朝换代时调用(Empire.NewEmperor 换了皇族、幕府将军家易主)。普通继位、改国号、读档不重置王朝年龄。
    // inheritCourt：朝廷里的易代(权臣篡位、外戚代立、太后另立、议立外姓、争位得胜)——保留一档；
    // 调用方用 Expect 声明了档位的按声明(兵变禅让——减半)；其余(打进京城、外藩入继、新开帝国)清空。
    // 保留、减半两档都是篡位，另加一次积弊(禅让篡位列在加速因素里，用户 2026-10-26)
    public static void OnDynastyChanged(Empire empire, bool inheritCourt = false)
    {
        Transition transition = empire != null && _expectedEmpire >= 0L && _expectedEmpire == empire.id ? _expected
            : inheritCourt ? Transition.Keep : Transition.Clear;
        OnDynastyChanged(empire, transition, usurped: transition != Transition.Clear);
    }

    public static void OnDynastyChanged(Empire empire, Transition transition, bool usurped)
    {
        if (empire?.data == null || World.world == null) return;
        ConstitutionalEconomyState state = empire.data.constitutional_economy ??= new ConstitutionalEconomyState();
        double now = World.world.getCurWorldTime();
        UnificationSystem.OnDynastyChanged(empire);
        state.last_dynastic_uprising_attempt = -1d;
        int previousBurden = DynasticCycleRules.HarshBurden(state.dynastic_strain);
        if (transition != Transition.Keep)
        {
            // 清空、减半：王朝年岁从头算，在位与换君的记录从新朝开国之君重新记(谥法一个帝国只由皇帝定一次，不随改朝换代重来，
            // 见 PosthumousNameGenerator)
            state.dynastic_since = now;
            state.dynastic_age_offset = 0;
            state.last_dynastic_update = now;
            state.dynastic_strain = DynasticCycleRules.Carry(state.dynastic_strain, transition);
            MaladySystem.OnDynastyChanged(empire, DynasticCycleRules.MaladyShare(transition));
            if (transition == Transition.Clear) FactionBalanceSystem.OnDynastyChanged(empire);
            state.reign_ruler_id = -1L;
            state.reign_ruler_since = -1d;
            state.reign_ruler_start_age = -1;
            state.recent_successions?.Clear();
        }
        if (usurped) state.dynastic_strain = DynasticCycleRules.Advance(state.dynastic_strain, DynasticCycleRules.UsurpationStrain);
        AfterCarry(empire, state, previousBurden, now);
        LogService.LogInfo($"[EmpireCraft][王朝积弊] {empire.GetEmpireName()} 改朝换代({transition}{(usurped ? "，篡位" : "")})，" +
                           $"积弊{state.dynastic_strain:0.0}，王朝{GetAge(empire)}年");
    }

    // 积弊降了：苛政里积弊那一项跟着变，危机线、满额按新的数重记
    private static void AfterCarry(Empire empire, ConstitutionalEconomyState state, int previousBurden, double now)
    {
        int burden = DynasticCycleRules.HarshBurden(state.dynastic_strain);
        state.harsh_burden = Math.Max(0f, Math.Min(100f, state.harsh_burden - previousBurden + burden));
        if (burden < previousBurden && state.harsh_main_cause == HarshSource) state.harsh_main_cause = "";
        if (state.dynastic_strain < DynasticCycleRules.CrisisPressure) state.dynastic_crisis_announced = false;
        if (state.dynastic_strain < DynasticCycleRules.SaturatedPressure) ClearSaturation(state);
        else if (state.dynastic_saturated_since < 0d) state.dynastic_saturated_since = now;
        MonarchyLegitimacy.Invalidate(empire);
    }

    // 一朝的积弊家底：另立一朝(两朝分立、宗室另立、皇族延续、绝嗣裂土)时由新朝承接
    public sealed class Legacy
    {
        public float strain;
        public double since = -1d;
        public int age_offset;
        public double saturated_since = -1d;
        public Dictionary<string, float> maladies = new();
    }

    public static Legacy Capture(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        return new Legacy
        {
            strain = Math.Max(0f, Math.Min(100f, state?.dynastic_strain ?? 0f)),
            since = state?.dynastic_since ?? -1d,
            age_offset = state?.dynastic_age_offset ?? 0,
            saturated_since = state?.dynastic_saturated_since ?? -1d,
            maladies = new Dictionary<string, float>(empire?.data?.maladies ?? new Dictionary<string, float>())
        };
    }

    // 新朝承接旧朝的王朝积弊与六弊：保留——原样，王朝年岁接着算(两朝分治、权臣分立、绝嗣裂土据地称帝)；
    // 减半——减半、王朝年岁从头算(宗室另立、皇族王国延续)。新朝自己的满额民变、换君记录从头记
    public static void Inherit(Empire to, Legacy legacy, Transition transition)
    {
        if (to?.data == null || legacy == null || World.world == null) return;
        ConstitutionalEconomyState state = to.data.constitutional_economy ??= new ConstitutionalEconomyState();
        double now = World.world.getCurWorldTime();
        int previousBurden = DynasticCycleRules.HarshBurden(state.dynastic_strain);
        float share = DynasticCycleRules.MaladyShare(transition);
        to.data.maladies = (legacy.maladies ?? new Dictionary<string, float>())
            .ToDictionary(pair => pair.Key, pair => Math.Max(0f, Math.Min(100f, pair.Value * share)));
        state.dynastic_strain = DynasticCycleRules.Carry(legacy.strain, transition);
        if (transition == Transition.Keep && legacy.since >= 0d)
        {
            state.dynastic_since = legacy.since;
            state.dynastic_age_offset = legacy.age_offset;
            state.dynastic_saturated_since = legacy.saturated_since;
        }
        else
        {
            state.dynastic_since = now;
            state.dynastic_age_offset = 0;
            state.last_dynastic_update = now;
            state.dynastic_saturated_since = -1d;
        }
        state.saturated_uprising_at = -1d;
        state.saturated_origin_cities = 0;
        state.last_dynastic_uprising_attempt = -1d;
        state.dynastic_crisis_announced = state.dynastic_strain >= DynasticCycleRules.CrisisPressure;
        state.recent_successions = new List<double>();
        Actor ruler = to.Emperor;
        state.reign_ruler_id = ruler?.data != null && ruler.isAlive() ? ruler.data.id : -1L;
        state.reign_ruler_since = state.reign_ruler_id > 0 ? now : -1d;
        state.reign_ruler_start_age = state.reign_ruler_id > 0 ? AgeOf(ruler) : -1;
        AfterCarry(to, state, previousBurden, now);
        LogService.LogInfo($"[EmpireCraft][王朝积弊] {to.GetEmpireName()} 承接旧朝积弊({transition})：{legacy.strain:0.0} → " +
                           $"{state.dynastic_strain:0.0}，王朝{GetAge(to)}年");
    }

    #endregion

    #region 满额民变

    // 本朝民变、起义的缘由(苛政民变、失地农民起义、连年饥荒)
    private static readonly HashSet<string> PopularReasons = new()
        { "rebellion_reason_harsh_rule", "rebellion_reason_land", "rebellion_reason_famine" };

    // 满额后第一场民变起了(见 HarshRuleSystem.TryUprising)：记下时间与本帝国原有的城数(连同本朝民变已占去的)
    public static void OnSaturatedUprising(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null || World.world == null || !IsSaturated(empire) || state.saturated_uprising_at >= 0d) return;
        state.saturated_uprising_at = World.world.getCurWorldTime();
        int held = empire.kingdoms_list.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
            .Sum(kingdom => kingdom.cities.Count);
        state.saturated_origin_cities = held + PopularRebellions(empire).cities;
        LogService.LogInfo($"[EmpireCraft][王朝积弊] {empire.GetEmpireName()} 积弊满额{SaturatedYears(empire):0}年，民变已起，" +
                           $"原有{state.saturated_origin_cities}城");
    }

    // 本朝(王朝起点以来，after 给了就从那时起)民变、起义立的国：还在、没回归本帝国的有几家、占了几座城，
    // 其中起事最早的一场过了几年(没有返回 0)
    public static (int count, int cities, int years) PopularRebellions(Empire empire, double after = -1d)
    {
        if (empire?.data == null || World.world?.kingdoms == null) return (0, 0, 0);
        double since = Math.Max(after, empire.data.constitutional_economy?.dynastic_since ?? -1d);
        double now = World.world.getCurWorldTime();
        int count = 0, cities = 0;
        double earliest = -1d;
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.GetEmpire() == empire) continue;
            RebellionCauseData cause = kingdom.GetOrCreate().last_rebellion_cause;
            if (cause == null || cause.origin_empire_id != empire.id || !PopularReasons.Contains(cause.reason_key ?? "") ||
                cause.timestamp < 0d || cause.timestamp > now || cause.timestamp < since) continue;
            count++;
            cities += kingdom.cities?.Count ?? 0;
            if (earliest < 0d || cause.timestamp < earliest) earliest = cause.timestamp;
        }
        return (count, cities, earliest >= 0d ? Math.Max(0, Date.getYearsSince(earliest)) : 0);
    }

    #endregion

    private static double FindDynastyStart(Empire empire, double now)
    {
        // 旧档根据本朝开国皇帝的历史时间恢复起点，只在初始化时读取历史。
        IEnumerable<EmpireCraftHistory> history = empire.data.history ?? new List<EmpireCraftHistory>();
        if (empire.data.currentHistory != null) history = history.Concat(new[] { empire.data.currentHistory });
        List<EmpireCraftHistory> reigns = history.Where(reign => reign != null && !reign.is_republic).ToList();
        EmpireCraftHistory founder = empire.data.dynasty_founder_actor_id > 0
            ? reigns.LastOrDefault(reign => reign.id == empire.data.dynasty_founder_actor_id) : null;
        founder ??= reigns.LastOrDefault(reign => reign.is_first);
        double timestamp = founder?.descriptions?.Where(item => item != null && item.timestamp >= 0d &&
                item.timestamp <= now).Select(item => item.timestamp).DefaultIfEmpty(-1d).Min() ?? -1d;
        return timestamp >= 0d ? timestamp : empire.data.created_time >= 0d && empire.data.created_time <= now
            ? empire.data.created_time : now;
    }
}

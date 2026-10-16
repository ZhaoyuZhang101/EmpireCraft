using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 军团(借鉴钢铁雄心)：无小人模式 + 帝国占领模式下，征召兵不再一团一个实体兵，而是合编成"军"——
// 兵力只是数字(从背景人口里抽，退伍还回，阵亡扣掉)，地图上最多一名统领实体(世界法则可关)；
// 交战、推进战线、攻城都由 FrontSystem 每月按数值结算。
//   · 军镇(中央/军区/军府，见 CityPopulationSystem 军制)按兵额折合：每 LegionsPerCorps 个旧制军团编一支军；
//   · 城市治安的守兵(CityStabilitySystem 出钱养的兵额)照旧是实体，留在城里不出征；
//   · 原版计兵数(City.countWarriors)把军团折算进去，战争 AI、投降、叛乱判定照常可用。
public static class CorpsSystem
{
    public const int LegionsPerCorps = 8;
    // 每月补员上限(满编的比例)、和平时每月退伍上限
    private const float ReinforceShare = 0.08f;
    private const float DemobilizeShare = 0.25f;
    // 不在交战的军每月恢复的组织度
    private const float RestOrg = 15f;
    private const float IdleOrg = 8f;
    // 统领开往军团所在地块的间隔(秒)
    private const float GeneralMoveInterval = 3f;
    // 陆上行军每个世界秒走几块地块：一个月 5 秒，步兵每月 0.5 块(各兵种再乘倍数，见 CorpsArmSystem.Speed)
    private const float MarchZonesPerSecond = 0.1f;

    public static bool Enabled => CityPopulationSystem.AbstractPopulationEnabled &&
                                  EmpireCraftWorldLawLibrary.empirecraft_law_switch_occupy_mode?.isEnabled() == true;

    public static bool GeneralUnits => EmpireCraftWorldLawLibrary.empirecraft_law_corps_general?.isEnabled() != false;

    // 关闭「军团统领实体」：地图上不留任何军人实体——没有统领、城里的治安守兵只是人数、不再组建原版城市军队
    // (羽林大将军、都督、镇将、戍主等军职空缺)；被拉去当兵的名人恢复文职。城主、刺史等地方官不受影响
    public static bool NoMilitaryUnits => Enabled && !GeneralUnits;

    private static readonly List<CorpsData> Empty = new();

    public static List<CorpsData> Of(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return Empty;
        var data = kingdom.GetOrCreate();
        return data.corps ??= new List<CorpsData>();
    }

    public static IEnumerable<(Kingdom kingdom, CorpsData corps)> All()
    {
        if (World.world?.kingdoms == null) yield break;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            List<CorpsData> list = kingdom.GetOrCreate().corps;
            if (list == null) continue;
            foreach (CorpsData corps in list) yield return (kingdom, corps);
        }
    }

    public static CorpsData Find(Kingdom kingdom, long id) => Of(kingdom).FirstOrDefault(corps => corps.id == id);

    public static City Home(CorpsData corps) => World.world?.cities?.get(corps.home_city_id) is City city &&
                                                city.data != null && !city.isRekt() ? city : null;

    public static TileZone Zone(CorpsData corps) => ZoneById(corps?.zone_id ?? -1);

    public static TileZone ZoneById(int id) =>
        id >= 0 && World.world?.zone_calculator != null &&
        World.world.zone_calculator._zones_dict_id.TryGetValue(id, out TileZone zone) ? zone : null;

    // 折合旧制军团数(按现有兵力)
    public static int Equivalent(CorpsData corps) => corps.full <= 0f || corps.strength <= 0f ? 0
        : Mathf.Max(1, Mathf.RoundToInt(corps.legions * corps.strength / corps.full));

    #region 计兵数缓存

    private static readonly Dictionary<long, int> EquivalentsByCity = new();
    private static readonly Dictionary<long, int> GeneralsByCity = new();
    private static readonly Dictionary<long, (long kingdom, long corps)> GeneralIndex = new();

    public static int EquivalentAt(City city) =>
        city != null && EquivalentsByCity.TryGetValue(city.id, out int count) ? count : 0;

    public static int GeneralsAt(City city) =>
        city != null && GeneralsByCity.TryGetValue(city.id, out int count) ? count : 0;

    public static bool IsGeneral(Actor actor) => actor?.data != null && GeneralIndex.ContainsKey(actor.id);

    public static float LeviedAt(City city)
    {
        if (city == null || !Enabled) return 0f;
        float total = 0f;
        foreach (CorpsData corps in Of(city.kingdom))
            if (corps.home_city_id == city.id) total += corps.strength;
        return total;
    }

    private static void RebuildIndex()
    {
        EquivalentsByCity.Clear();
        GeneralsByCity.Clear();
        GeneralIndex.Clear();
        foreach ((Kingdom kingdom, CorpsData corps) in All())
        {
            EquivalentsByCity.TryGetValue(corps.home_city_id, out int count);
            EquivalentsByCity[corps.home_city_id] = count + Equivalent(corps);
            if (corps.general_id < 0) continue;
            GeneralIndex[corps.general_id] = (kingdom.id, corps.id);
            GeneralsByCity.TryGetValue(corps.home_city_id, out int generals);
            GeneralsByCity[corps.home_city_id] = generals + 1;
        }
    }

    public static void ResetWorldState()
    {
        EquivalentsByCity.Clear();
        GeneralsByCity.Clear();
        GeneralIndex.Clear();
        _lastSecond = _lastMonth = -1d;
        GeneralMoves.Clear();
        Paths.Clear();
        EmbarkRetry.Clear();
        RoadNetwork.ResetWorldState();
        PureMapMode.ResetWorldState();
        CorpsArmSystem.ResetWorldState();
        VirtualIndustrySystem.ResetWorldState();
        WarScopeSystem.ResetWorldState();
        FrontSystem.ResetWorldState();
        WarReadinessSystem.ResetWorldState();
        NavySystem.ResetWorldState();
    }

    #endregion

    #region 每帧调度

    private static double _lastSecond = -1d, _lastMonth = -1d;
    private static object _world;
    private static readonly Dictionary<long, float> GeneralMoves = new();

    public static void Update()
    {
        if (World.world == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            ResetWorldState();
        }
        if (!Enabled)
        {
            if (_lastMonth >= 0d) DisbandAllOnDisable();
            return;
        }
        UI.Components.CorpsMapRenderer.EnsureHost();
        double now = World.world.getCurWorldTime();
        if (_lastMonth < 0d || now < _lastMonth || Date.getMonthsSince(_lastMonth) >= 1)
        {
            bool first = _lastMonth < 0d;
            _lastMonth = now;
            try
            {
                using (FrameProfiler.Measure("军团·月度征召")) MonthTick(now);
                // 海军先结算：制海权决定这个月的跨海登陆能不能成行(见 FrontSystem)
                if (!first) using (FrameProfiler.Measure("军团·海军月度")) NavySystem.MonthTick(now);
                if (!first) using (FrameProfiler.Measure("军团·战线月度")) FrontSystem.MonthTick(now);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][军团] 月度结算失败: {exception}");
            }
            RebuildIndex();
        }
        // 平滑国界下的逐格占领(见 FrontSystem.Smooth)
        using (FrameProfiler.Measure("战线·逐格推进")) FrontSystem.SmoothTick();
        // 路网与航线(行军、航行据此寻路；自己按真实时间限频，见 RoadNetwork)
        using (FrameProfiler.Measure("路网")) RoadNetwork.Tick();
        if (_lastSecond < 0d || now < _lastSecond || now - _lastSecond >= 1d)
        {
            _lastSecond = now;
            try { using (FrameProfiler.Measure("军团·行军")) SecondTick(); }
            catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][军团] 行军失败: {exception.Message}"); }
            try { using (FrameProfiler.Measure("军团·航行")) NavySystem.SecondTick(); }
            catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][海军] 航行失败: {exception.Message}"); }
        }
    }

    // 关闭占领模式：各军就地退伍(兵员还回人口)，统领并回人口
    private static void DisbandAllOnDisable()
    {
        foreach ((Kingdom kingdom, CorpsData corps) in All().ToList()) Disband(kingdom, corps);
        NavySystem.RemoveAllFlagships();
        RebuildIndex();
        _lastMonth = -1d;
    }

    #endregion

    #region 月度：征召、补员、退伍、休整

    private static void MonthTick(double now)
    {
        foreach (Kingdom kingdom in World.world.kingdoms.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral() ||
                Compatibility.AncientWarfareCompatibility.OwnsObject(kingdom)) continue;
            WarScopeSystem.MonthTick(kingdom);
            WarCommandSystem.MonthTick(kingdom);
            List<CorpsData> list = kingdom.GetOrCreate().corps;
            bool posts = kingdom.cities?.Any(city => city != null && !city.isRekt() &&
                CityPopulationSystem.PostOf(city, CityPopulationSystem.DoctrineOf(kingdom)) != CityPopulationSystem.ArmyPost.None) == true;
            if (!posts && (list == null || list.Count == 0)) continue;
            Mobilize(kingdom, now);
            using (FrameProfiler.Measure("军团·后勤")) LogisticsSystem.MonthTick(kingdom);
            Recover(kingdom);
            PayUpkeep(kingdom);
            // 视财力换装一支(见 CorpsArmSystem)
            CorpsArmSystem.Monthly(kingdom, kingdom.GetOrCreate().corps);
            AbsorbLegacyLegions(kingdom);
            // 禁军空饷、忠诚、不奉诏
            PalaceArmySystem.Monthly(kingdom);
        }
    }

    // 野战军饷：按人头每月发(见 CityStabilityRules.CorpsAnnual)，按兵种加权(装备越先进越贵，见 CorpsArmSystem)，本国先付，听命于中央的成员付不起由中央补；
    // 饷银发给各军的家乡城(回到民间)。发不出就军心涣散、兵丁逃散，久欠求和(见下方欠饷)。以前野战军不花一分钱。
    // 另收行军费：每人每走一块地块 MarchCostPerZone，离前线越远的军开过去越贵(所以动员只动就近的军)
    // 打仗的花销单记"战争开支"(TreasuryCategory.War，帝国收支里单列)：战时比平时多出来的军饷、行军费，
    // 以及在前线交战的军每月的粮秣军械损耗(每人 BattleCostPerSoldier，按兵种加权)；平时的军饷照旧记"野战军费"
    private const float MarchCostPerZone = 0.002f;
    private const float BattleCostPerSoldier = 0.01f;

    // 欠饷(全欠时)：组织度上限压到 100-UnpaidOrgCap(低状态，休整也回不满)；每月再扣 UnpaidOrg、逃散 DesertShare 的兵(回乡，
    // 军费随之降下来，直到发得起)，欠饷期间不补员、不新编；连着 SuePeaceMonths 个月欠饷就求和(WarGoalSystem.TrySueForPeace)。
    // 被全面侵略(要吞并、要逼成附庸、要拿都城或一半城、全面战争、叛乱独立这类谈不拢的仗)时不求和，困兽犹斗：
    // 不额外扣组织度、逃散减半，压低了的组织度照样能守。以前欠饷每月扣 20 组织度，休整只回 15，全军永远溃退休整
    private const float UnpaidShortfall = 0.15f, UnpaidOrgCap = 60f, UnpaidOrg = 10f, DesertShare = 0.05f, SuePeaceChance = 0.35f;
    private const int SuePeaceMonths = 3;

    // 军饷欠发中：不补员、不新编(起义军不算)
    public static bool Unpaid(Kingdom kingdom) =>
        Enabled && kingdom?.data != null && !kingdom.isRekt() && kingdom.GetOrCreate().corps_pay_shortfall >= UnpaidShortfall &&
        !Insurgent(kingdom);

    // 叛军、民变等起义军(在打叛乱一类的仗、是攻方)：以战养战，边打边掠——欠饷不罚、照样补员，也没有进攻门槛
    // (组织度再低也上阵、不退下去休整，见 FrontSystem.Assign)，只是战斗力打个折(InsurgentPower，见 FrontSystem.Effective)
    public const float InsurgentPower = 0.7f;
    private static readonly HashSet<EmpireWarType> UprisingTypes = new()
        { EmpireWarType.地方叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱, EmpireWarType.派系叛乱, EmpireWarType.清君侧 };
    private static readonly Dictionary<long, (double at, bool value)> InsurgentCache = new();

    public static bool Insurgent(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || World.world == null) return false;
        double now = World.world.getCurWorldTime();
        if (InsurgentCache.TryGetValue(kingdom.id, out var cached) && now >= cached.at && now - cached.at < 5d) return cached.value;
        bool value = false;
        foreach (War war in kingdom.getWars())
        {
            if (war?.data == null || war.hasEnded() || !war.isAttacker(kingdom)) continue;
            if (war.getAsset() != WarTypeLibrary.rebellion && !UprisingTypes.Contains(war.GetEmpireWarType())) continue;
            value = true;
            break;
        }
        InsurgentCache[kingdom.id] = (now, value);
        return value;
    }

    public static float InsurgentFactor(CorpsData corps) =>
        corps != null && Insurgent(World.world?.kingdoms?.get(corps.kingdom_id)) ? InsurgentPower : 1f;

    // 军饷发足了(欠饷不到一成半)
    public static bool WellPaid(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && kingdom.GetOrCreate().corps_pay_shortfall < UnpaidShortfall;

    // 打不起仗，不主动开战(含军阀联合另立政府)：军饷欠发；或没有野战军算不出欠饷的(多是小军阀)，
    // 国库不到安全储备的四分之一、经常收支还在亏
    public static bool CannotFundWar(Kingdom kingdom)
    {
        if (Unpaid(kingdom)) return true;
        if (kingdom?.data == null || kingdom.isRekt() || !TreasurySystem.Enabled(kingdom) || Insurgent(kingdom)) return false;
        if (kingdom.GetTreasuryBalance() >= TreasurySystem.SafetyReserve(kingdom) / 4) return false;
        TreasuryReport report = TreasurySystem.Report(kingdom);
        return report.months >= 3 && report.operating_balance < 0;
    }

    private static void PayUpkeep(Kingdom kingdom)
    {
        // 骑士团的军由骑士团自己付饷(见 PapacySystem.Orders)
        List<CorpsData> list = Of(kingdom).Where(corps => corps.order_id < 0).ToList();
        float soldiers = 0f, marching = 0f, engaged = 0f;
        foreach (CorpsData corps in list)
        {
            // 装备越先进军饷越贵(见 CorpsArmSystem.Upkeep)
            float weighted = Mathf.Max(0f, corps.strength) * CorpsArmSystem.Upkeep(CorpsArmSystem.Of(corps));
            soldiers += weighted;
            marching += Mathf.Max(0f, corps.strength) * corps.march_zones;
            corps.march_zones = 0;
            if (corps.task is "attack" or "defend" && FrontSystem.Battles.ContainsKey(corps.sector_city_id)) engaged += weighted;
        }
        if (soldiers < 1f)
        {
            // 没有兵就不欠饷：清掉欠饷状态。以前军团解散后欠饷标记一直留着，这个国家再也征不了兵、开不了仗
            var idle = kingdom.GetOrCreate();
            idle.corps_pay_shortfall = 0f;
            idle.corps_unpaid_months = 0;
            return;
        }
        if (!TreasurySystem.Enabled(kingdom)) return;
        var data = kingdom.GetOrCreate();
        double peace = CityStabilityRules.CorpsAnnual(soldiers, false);
        int baseBill = CityStabilityRules.Invoice(peace, ref data.corps_upkeep_carry);
        int warBill = (IsAtWar(kingdom)
                          ? CityStabilityRules.Invoice(CityStabilityRules.CorpsAnnual(soldiers, true) - peace, ref data.corps_war_carry)
                          : 0) +
                      Mathf.RoundToInt(marching * MarchCostPerZone) + Mathf.RoundToInt(engaged * BattleCostPerSoldier);
        int bill = baseBill + warBill;
        if (bill <= 0) return;
        int paidBase = Draw(kingdom, baseBill, TreasuryCategory.Military);
        int paidWar = Draw(kingdom, warBill, TreasuryCategory.War);
        Layer.Empire empire = kingdom.GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (paidBase + paidWar < bill && core != null && core != kingdom && TreasurySystem.Enabled(core) &&
            GamePatches.EmpirePeacePatch.Obeys(empire, kingdom))
        {
            paidBase += Draw(core, baseBill - paidBase, TreasuryCategory.Military);
            paidWar += Draw(core, warBill - paidWar, TreasuryCategory.War);
        }
        int paid = paidBase + paidWar;
        foreach (CorpsData corps in list)
        {
            int share = (int)(paid * (double)(Mathf.Max(0f, corps.strength) * CorpsArmSystem.Upkeep(CorpsArmSystem.Of(corps))) /
                              soldiers);
            if (share > 0 && Home(corps) is City home) EnterpriseSystem.ReturnToLocal(home, share);
        }
        float shortfall = Mathf.Max(0f, 1f - paid / (float)bill);
        data.corps_pay_shortfall = shortfall;
        // 起义军以战养战，欠饷不罚
        bool insurgent = Insurgent(kingdom);
        data.corps_unpaid_months = shortfall >= UnpaidShortfall && !insurgent ? Math.Min(120, data.corps_unpaid_months + 1) : 0;
        if (shortfall <= 0f || insurgent) return;
        bool survival = WarGoalSystem.FightingForSurvival(kingdom);
        float ceiling = 100f - UnpaidOrgCap * shortfall, deserted = 0f;
        foreach (CorpsData corps in list)
        {
            corps.org = Mathf.Min(corps.org, ceiling);
            if (!survival) corps.org = Mathf.Max(0f, corps.org - UnpaidOrg * shortfall);
            float deserters = corps.strength * DesertShare * shortfall * (survival ? 0.5f : 1f);
            if (deserters < 1f) continue;
            Demobilize(kingdom, corps, deserters);
            deserted += deserters;
        }
        if (deserted >= 1f) FrontSystem.Log(kingdom, string.Format(LM.Get("corps_log_desertion"), Mathf.RoundToInt(deserted)));
        if (data.corps_unpaid_months >= SuePeaceMonths) SueForPeace(kingdom);
    }

    // 粮饷不继，仗打不下去：能和就先和(对方条件过分的照打，见 WarGoalSystem.TrySueForPeace)
    private static void SueForPeace(Kingdom kingdom)
    {
        foreach (War war in kingdom.getWars().ToList())
        {
            if (war?.data == null || war.hasEnded() || UnityEngine.Random.value >= SuePeaceChance) continue;
            Kingdom foe = war.main_attacker == kingdom ? war.main_defender : war.main_attacker;
            if (WarGoalSystem.TrySueForPeace(war, kingdom))
                FrontSystem.Log(kingdom, string.Format(LM.Get("corps_log_unpaid_peace"), foe?.GetKingdomName() ?? ""));
        }
    }

    private static int Draw(Kingdom payer, int amount, TreasuryCategory category)
    {
        if (amount <= 0) return 0;
        int paid = Mathf.Min(amount, Mathf.Max(0, payer.GetMoney()));
        if (paid > 0) payer.SubMoney(paid, category);
        return paid;
    }

    // 量入为出养兵：兵额按养得起多少封顶——平时军饷不超过经常收入的 PeaceBudgetShare，战时(连粮秣军械)不超过
    // WarBudgetShare(被全面侵略时倾国而战，SurvivalBudgetShare)，国库存银另按 ReserveYears 年花掉；养不起就各军按比例缩编，
    // 多出来的兵逐月裁撤(战时也裁)。以前兵额只看人口、战时只增不减，全世界近四成人口在当兵，一开战军饷就是收入的
    // 一到两倍，几个月打空国库、欠饷溃散，谁也打不赢谁，大一统无从谈起。大国收入多养兵多，才压得住小国。
    // 起义军以战养战不受此限；没有几个月账目的新国家也不限
    private const float PeaceBudgetShare = 0.35f, WarBudgetShare = 0.7f, SurvivalBudgetShare = 1f, ReserveYears = 3f;
    private const float PeaceCostPerSoldier = 0.1f, WarCostPerSoldier = 0.4f;
    // 平时军镇只征兵源(背景人口 × 军制征兵比例)的这一成数(有常备军的军制用 StandingPoolShare)，战时全征
    private const float PeacePoolShare = 0.3f, StandingPoolShare = 0.5f;
    // 开战后兵源逐月征满：从平时的成数起，WarLevyMonths 个月涨到全征。以前开战当月一步全征，军饷当月就掏空国库，
    // 连欠三个月就罢兵求和(开国雄主的仗也一样)。按本国(连同所属帝国的核心王国)打得最久的那场仗算
    private const float WarLevyMonths = 9f;

    private static float WarLevyShare(Kingdom kingdom, float peace)
    {
        int months = 0;
        foreach (Kingdom side in new[] { kingdom, FrontSystem.Realm(kingdom) })
        {
            if (side?.data == null || side.isRekt()) continue;
            foreach (War war in side.getWars())
                if (war?.data != null && !war.hasEnded()) months = Mathf.Max(months, Date.getMonthsSince(war.data.created_time));
        }
        return Mathf.Lerp(peace, 1f, Mathf.Clamp01((months + 1) / WarLevyMonths));
    }
    // 起义军的正规军只征兵源的一半：仓促成军，一大半人编在义军里(见 RaiseMilitia)
    private const float InsurgentLevy = 0.5f;

    private static float BudgetFactor(Kingdom kingdom, bool war, float wanted)
    {
        if (wanted < 1f) return 1f;
        if (Insurgent(kingdom)) return InsurgentLevy;
        if (!TreasurySystem.Enabled(kingdom)) return 1f;
        if (TreasurySystem.Report(kingdom).months < 3) return 1f;
        float share = !war ? PeaceBudgetShare : WarGoalSystem.FightingForSurvival(kingdom) ? SurvivalBudgetShare : WarBudgetShare;
        double budget = StateDebtSystem.AnnualStableIncome(kingdom) * (double)share +
                        Math.Max(0L, kingdom.GetTreasuryBalance()) / (double)ReserveYears;
        float perSoldier = (war ? WarCostPerSoldier : PeaceCostPerSoldier) * AverageUpkeep(kingdom);
        return Mathf.Clamp01((float)(budget / Math.Max(0.01f, perSoldier)) / wanted);
    }

    // 本国各军按兵种的平均饷银倍数(装备越先进越贵，见 CorpsArmSystem.Upkeep)
    private static float AverageUpkeep(Kingdom kingdom)
    {
        float men = 0f, weighted = 0f;
        foreach (CorpsData corps in Of(kingdom))
        {
            if (corps.order_id >= 0 || corps.strength <= 0f) continue;
            men += corps.strength;
            weighted += corps.strength * CorpsArmSystem.Upkeep(CorpsArmSystem.Of(corps));
        }
        return men > 0f ? weighted / men : 1f;
    }

    // 各军镇应有几支军、每支多少人；缺的当场征召，多的和平时退伍
    private static void Mobilize(Kingdom kingdom, double now)
    {
        List<CorpsData> list = Of(kingdom);
        // 家乡城市已失：改驻本国首都或别的军镇
        foreach (CorpsData corps in list)
        {
            City home = Home(corps);
            if (home != null && home.kingdom == kingdom) continue;
            City fallback = kingdom.capital ?? kingdom.cities?.FirstOrDefault(city => city != null && !city.isRekt());
            if (fallback != null) corps.home_city_id = fallback.id;
        }
        foreach (CorpsData empty in list.Where(corps => corps.strength < 1f && corps.task != "attack" && corps.task != "defend").ToList())
            Disband(kingdom, empty);
        bool war = IsAtWar(kingdom);
        bool unpaid = Unpaid(kingdom);
        // 渡海先遣军(见 Detach)：仗打完、或回到驻地待命，并回原军；原军没了就自成一军
        foreach (CorpsData part in list.Where(corps => corps.parent_id >= 0).ToList())
        {
            CorpsData parent = list.FirstOrDefault(corps => corps.id == part.parent_id && corps.parent_id < 0);
            if (parent == null)
            {
                part.parent_id = -1L;
                continue;
            }
            bool back = part.task == "reserve" && !part.embarked && Zone(part) is TileZone at && at == Home(part)?.getTile()?.zone;
            if (back || !war && part.task is not ("attack" or "defend")) Rejoin(kingdom, parent, part);
        }
        CityPopulationSystem.ArmyDoctrine doctrine = CityPopulationSystem.DoctrineOf(kingdom);
        var cities = kingdom.cities.Where(city => city != null && !city.isRekt()).ToList();
        // 各军镇的兵额(旧制军团数)。平时首都、军区首都(帝国里的各藩属王国)至少留一个军团("平时只留一名将领"，
        // 见 CityPopulationSystem.LevyTarget)：城防卫兵另算，不占这一个名额——以前新开局的小国平时一个军团都没有，
        // 几十年不打仗就一直没兵；帝国里各藩属王国平时也一兵没有，全帝国只有京师一支军
        var posts = new Dictionary<City, int>();
        int totalSlots = 0;
        foreach (City city in cities)
        {
            CityPopulationSystem.ArmyPost post = CityPopulationSystem.PostOf(city, doctrine);
            if (post == CityPopulationSystem.ArmyPost.None) continue;
            int legions = Mathf.Max(post is CityPopulationSystem.ArmyPost.Central or CityPopulationSystem.ArmyPost.District ? 1 : 0,
                CityPopulationSystem.LevyTarget(city) - CityStabilitySystem.FundedSlots(city));
            posts[city] = legions;
            totalSlots += legions;
        }
        // 兵源只分给军镇的兵额：城防守兵由城市治安另出钱从本城征(见 CityStabilitySystem)，不再把军团摊薄
        // (以前按含守兵的兵额平分，平时京师那一个军团只分到兵源的几分之一，大国平时也只有一千来人)。
        // 平时只征兵源的一部分(有常备军的多些)，战时全征
        float peace = doctrine.CentralStanding || doctrine.DistrictStanding ? StandingPoolShare : PeacePoolShare;
        // 鞭长莫及：控制力低的城征不齐(见 AdministrativeControlSystem.Yield)
        float background = 0f;
        // 奴隶不征为兵(见 SlaverySystem)
        foreach (City city in cities)
            background += Mathf.Max(0f, CityPopulationSystem.GetBackgroundTotal(city) - SlaverySystem.Unfree(city)) *
                          AdministrativeControlSystem.Yield(city);
        float pool = background * doctrine.LevyShare * (war ? WarLevyShare(kingdom, peace) : peace);
        float legionSize = Mathf.Clamp(pool / Mathf.Max(1, totalSlots), 1f, CityPopulationSystem.PeoplePerLegion);
        // 量入为出：按人口该有的兵额超出养得起的，每个军团按比例缩编
        float budget = BudgetFactor(kingdom, war, totalSlots * legionSize);
        bool limited = budget < 0.999f;
        legionSize = Mathf.Max(1f, legionSize * budget);
        foreach (City city in cities)
        {
            if (!posts.TryGetValue(city, out int legions)) continue;
            int wanted = legions <= 0 ? 0 : Mathf.CeilToInt(legions / (float)LegionsPerCorps);
            var here = list.Where(corps => corps.home_city_id == city.id && !corps.militia && corps.order_id < 0 &&
                                           corps.parent_id < 0).ToList();
            // 每支军分到的旧制军团数
            for (int i = 0; i < Mathf.Max(wanted, here.Count); i++)
            {
                int share = i < wanted ? Mathf.Min(LegionsPerCorps, legions - i * LegionsPerCorps) : 0;
                if (i >= here.Count)
                {
                    if (share <= 0 || unpaid) break;
                    CorpsData raised = Raise(kingdom, city, share, share * legionSize, now);
                    if (raised == null) break;
                    here.Add(raised);
                    continue;
                }
                CorpsData corps = here[i];
                corps.legions = Mathf.Max(share, war ? corps.legions : 0);
                float full = corps.legions * legionSize;
                // 战时满编只增不减，除非养不起了
                if (war && !limited) corps.full = Mathf.Max(corps.full, full);
                else corps.full = full;
                if (corps.full <= 0f)
                {
                    if (!war && corps.task != "attack" && corps.task != "defend") Disband(kingdom, corps);
                    continue;
                }
                // 补给不到五成(海外断了航线、孤军深入、被围)补不上兵；在船上的也补不了。
                // 分出去的渡海先遣军算在本军名下(补员、裁撤按两者合计)，免得一边分兵一边补满、兵越分越多
                float away = list.Where(other => other.parent_id == corps.id).Sum(other => other.strength);
                if (corps.strength + away < corps.full && corps.supply >= 0.5f && !corps.embarked && !unpaid)
                    Reinforce(kingdom, corps, Mathf.Min(corps.full * ReinforceShare * corps.supply, corps.full - away - corps.strength));
                else if ((!war || limited) && corps.strength + away > corps.full)
                    Demobilize(kingdom, corps, Mathf.Min(corps.strength + away - corps.full, corps.full * DemobilizeShare + 1f));
            }
        }
        // 义军：仗打完就地解散
        if (!war)
            foreach (CorpsData corps in list.Where(corps => corps.militia).ToList()) Disband(kingdom, corps);
    }

    public static bool IsAtWar(Kingdom kingdom) =>
        kingdom != null && (kingdom.hasEnemies() || CityPopulationSystem.IsMobilizing(kingdom) ||
                            FrontSystem.Realm(kingdom) is Kingdom realm && realm != kingdom && realm.hasEnemies());

    private static int NextSerial(Kingdom kingdom)
    {
        var data = kingdom.GetOrCreate();
        return ++data.corps_serial;
    }

    private static CorpsData Raise(Kingdom kingdom, City home, int legions, float people, double now, bool militia = false)
    {
        var corps = new CorpsData
        {
            id = HelperFunc.OverallHelperFunc.IdGenerator.NextId(), serial = militia ? 0 : NextSerial(kingdom),
            kingdom_id = kingdom.id, home_city_id = home.id, legions = Mathf.Max(1, legions), full = people,
            post = (int)CityPopulationSystem.PostOf(home, CityPopulationSystem.DoctrineOf(kingdom)),
            zone_id = home.getTile()?.zone?.id ?? -1, raised_at = now, militia = militia
        };
        Reinforce(kingdom, corps, people, militia ? home : null);
        if (corps.strength < 1f) return null;
        Of(kingdom).Add(corps);
        return corps;
    }

    // 骑士团的军：在驻地城组建、补员、解散(见 PapacySystem.Orders)
    public static CorpsData RaiseOrderCorps(Kingdom host, City home, float people, long orderId)
    {
        if (!Enabled || host?.data == null || home?.data == null) return null;
        CorpsData corps = Raise(host, home, Mathf.Max(1, Mathf.RoundToInt(people / CityPopulationSystem.PeoplePerLegion)),
            people, World.world.getCurWorldTime());
        if (corps != null) corps.order_id = orderId;
        return corps;
    }

    public static void ReinforceOrderCorps(Kingdom host, CorpsData corps, float amount)
    {
        if (host?.data == null || corps == null || amount <= 0f || corps.embarked || corps.supply < 0.5f) return;
        Reinforce(host, corps, amount);
    }

    // 从本国各城的背景人口里征兵(义军只从本城征)：就近补员——先从离这支军最近的几座本国城征，
    // 离本国城越远补上来的越少(每离 15 块地块打一半)；近处征不够再按动员范围、全国抽(见 WarScopeSystem)
    private const int NearbySources = 4;
    private const float ReinforceDistanceHalf = 15f;

    private static void Reinforce(Kingdom kingdom, CorpsData corps, float amount, City only = null)
    {
        float need = Mathf.Min(amount, corps.full - corps.strength);
        if (need < 1f) return;
        float taken = 0f;
        TileZone at = Zone(corps);
        List<City> nearby = only != null || at == null ? new List<City>()
            : kingdom.cities.Where(city => city?.data != null && !city.isRekt() && city.getTile()?.zone != null)
                .OrderBy(city => ZoneDistance(city.getTile().zone, at)).Take(NearbySources).ToList();
        if (nearby.Count > 0)
        {
            float distance = ZoneDistance(nearby[0].getTile().zone, at);
            need *= ReinforceDistanceHalf / (ReinforceDistanceHalf + distance);
            if (need < 1f) return;
        }
        bool near = only == null && WarScopeSystem.InScope(Home(corps) ?? World.world.cities.get(corps.home_city_id));
        for (int attempt = 0; taken < need && attempt < 12; attempt++)
        {
            // 就近的几座城抽完了：只从动员范围里的城再抽；举国总动员、奉诏出征才从全国抽(不再远距离征召)
            City source = only ?? (attempt < nearby.Count ? nearby[attempt]
                : near ? CityPopulationSystem.DrawKingdomCity(kingdom, WarScopeSystem.InScope)
                : WarScopeSystem.Unbounded(kingdom) ? CityPopulationSystem.DrawKingdomCity(kingdom) : null);
            PopGroup group = source == null ? null
                : CityPopulationSystem.DrawBackground(source, candidate => candidate.Background >= 1f);
            if (group == null) break;
            float contribution = CityPopulationSystem.RemoveBackground(group, need - taken);
            if (contribution <= 0f) break;
            LegionPopulationRules.Add(corps.population, source.id, group, contribution);
            taken += contribution;
        }
        corps.strength += taken;
        if (taken > 0f) CityPopulationSystem.InvalidateHouseholdCaches();
    }

    // 退伍：按来源还回人口
    private static void Demobilize(Kingdom kingdom, CorpsData corps, float amount)
    {
        amount = Mathf.Min(amount, corps.strength);
        if (amount <= 0f) return;
        float total = LegionPopulationRules.Total(corps.population);
        if (total > 0f)
        {
            float ratio = Mathf.Clamp01(amount / total);
            foreach (LegionPopulationContribution origin in corps.population)
            {
                if (origin == null || origin.size <= 0f) continue;
                float back = origin.size * ratio;
                origin.size -= back;
                City destination = kingdom.cities?.FirstOrDefault(city => city != null && !city.isRekt() &&
                                                                          city.id == origin.city_id) ?? Home(corps);
                if (destination == null) continue;
                CityPopulationSystem.AddBackground(destination, origin.social_class == SocialClass.Army
                    ? SocialClass.Peasant : origin.social_class, origin.culture, origin.species, origin.ideology, back);
            }
            corps.population.RemoveAll(origin => origin == null || origin.size <= 0.01f);
        }
        corps.strength = Mathf.Max(0f, corps.strength - amount);
        CityPopulationSystem.InvalidateHouseholdCaches();
    }

    // 能不能分出渡海先遣军：义军、骑士团的军、先遣军自己、已在船上的不分
    public static bool CanDetach(CorpsData corps) =>
        corps != null && !corps.militia && corps.order_id < 0 && corps.parent_id < 0 && !corps.embarked && corps.strength > 1f;

    // 渡海先遣军(见 FrontSystem.Assign)：整军装不下运输船时，按剩下的运力从本军分出一支先渡海，本军留守。
    // 玩家反馈"远洋打仗还是有问题，两个隔海的国家一千五百年没打下来"：以前整军装不下就不派，战时一军一两万到几万人，
    // 蒸汽时代以前一两个港口的运力只有几千。兵员来源按比例分过去；先遣军不补员、不另占兵额，仗打完或回到驻地就并回原军
    public static CorpsData Detach(Kingdom kingdom, CorpsData parent, float amount)
    {
        if (kingdom?.data == null || !CanDetach(parent)) return null;
        amount = Mathf.Floor(Mathf.Min(amount, parent.strength - 1f));
        if (amount < 1f) return null;
        float share = amount / parent.strength;
        var part = new CorpsData
        {
            id = HelperFunc.OverallHelperFunc.IdGenerator.NextId(), serial = NextSerial(kingdom), kingdom_id = kingdom.id,
            home_city_id = parent.home_city_id, post = parent.post, legions = Mathf.Max(1, Mathf.RoundToInt(parent.legions * share)),
            full = amount, strength = amount, org = parent.org, task = parent.task, sector_city_id = parent.sector_city_id,
            zone_id = parent.zone_id, target_zone_id = parent.target_zone_id, raised_at = World.world.getCurWorldTime(),
            arm = parent.arm, supply = parent.supply, rations = parent.rations, phantom = parent.phantom, loyalty = parent.loyalty,
            parent_id = parent.id
        };
        foreach (LegionPopulationContribution origin in parent.population)
        {
            if (origin == null || origin.size <= 0f) continue;
            float moved = origin.size * share;
            origin.size -= moved;
            part.population.Add(new LegionPopulationContribution
            {
                city_id = origin.city_id, social_class = origin.social_class, culture = origin.culture,
                species = origin.species, ideology = origin.ideology, size = moved
            });
        }
        parent.population.RemoveAll(origin => origin == null || origin.size <= 0.01f);
        parent.strength -= amount;
        Of(kingdom).Add(part);
        return part;
    }

    // 先遣军并回原军：兵员来源并回去，组织度按兵力加权
    private static void Rejoin(Kingdom kingdom, CorpsData parent, CorpsData part)
    {
        float total = parent.strength + part.strength;
        if (total > 0f) parent.org = (parent.org * parent.strength + part.org * part.strength) / total;
        parent.strength = total;
        foreach (LegionPopulationContribution origin in part.population)
            if (origin != null && origin.size > 0f) parent.AddContribution(origin);
        part.population.Clear();
        part.strength = 0f;
        RemoveGeneral(part);
        Paths.Remove(part.id);
        EmbarkRetry.Remove(part.id);
        Of(kingdom).Remove(part);
    }

    public static void Disband(Kingdom kingdom, CorpsData corps)
    {
        if (corps == null) return;
        if (kingdom?.data != null && !kingdom.isRekt()) Demobilize(kingdom, corps, corps.strength);
        RemoveGeneral(corps);
        Of(kingdom).Remove(corps);
    }

    // 阵亡：兵力减少，来源按比例扣减(死去的人不回人口)
    public static void Casualties(CorpsData corps, float dead)
    {
        if (corps == null || dead <= 0f) return;
        corps.strength = Mathf.Max(0f, corps.strength - dead);
        LegionPopulationRules.ReduceTo(corps.population, corps.strength);
    }

    private static void Recover(Kingdom kingdom)
    {
        // 欠饷时组织度回不满(见 PayUpkeep)：溃退的军回到上限附近就归建，不会永远卡在溃退休整
        float regroup = Mathf.Min(50f, 100f - UnpaidOrgCap * kingdom.GetOrCreate().corps_pay_shortfall - 5f);
        foreach (CorpsData corps in Of(kingdom))
        {
            // 补给不到五成：组织度不恢复，反而下降(断绝时每月 -5)
            if (corps.supply < 0.5f)
            {
                corps.org = Mathf.Max(0f, corps.org - (0.5f - corps.supply) * 10f);
                continue;
            }
            bool fighting = corps.task is "attack" or "defend" && FrontSystem.EngagedLastMonth(corps);
            if (!fighting) corps.org = Mathf.Min(100f, corps.org + (corps.task is "reserve" or "retreat" ? RestOrg : IdleOrg));
            if (corps.task == "retreat" && corps.org >= regroup) corps.task = "reserve";
            if (corps.task == "defend" && fighting) corps.entrench = Mathf.Min(0.3f, corps.entrench + 0.05f);
            else if (corps.task != "defend") corps.entrench = 0f;
        }
    }

    // 开启军团模式前征召的实体兵(一团一个)：超出守兵额的并入本国军团，实体并回人口
    private const int MaxAbsorbPerKingdom = 12;

    private static void AbsorbLegacyLegions(Kingdom kingdom)
    {
        int absorbed = 0;
        foreach (City city in kingdom.cities.ToList())
        {
            if (city?.units == null || city.isRekt()) continue;
            int guards = NoMilitaryUnits ? 0 : CityStabilitySystem.FundedSlots(city);
            var soldiers = city.units.Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() &&
                                                     actor.city == city && CityPopulationSystem.IsSoldier(actor) &&
                                                     !CityPopulationSystem.IsVehicle(actor) && !IsGeneral(actor) &&
                                                     !actor.isKing() && !actor.isCityLeader() && !actor.isFavorite() &&
                                                     actor.GetOrCreate().legion_size > 1f).ToList();
            if (soldiers.Count <= guards) continue;
            CorpsData target = Of(kingdom).Where(corps => !corps.militia)
                .OrderBy(corps => corps.home_city_id == city.id ? 0 : 1).FirstOrDefault();
            foreach (Actor soldier in soldiers.Skip(guards))
            {
                if (absorbed >= MaxAbsorbPerKingdom) return;
                var extra = soldier.GetOrCreate();
                float alive = CityPopulationSystem.LegionAlive(soldier) - 1f;
                if (target != null && extra.legion_population != null)
                {
                    foreach (LegionPopulationContribution origin in extra.legion_population)
                        if (origin != null && origin.size > 0f)
                            target.AddContribution(origin);
                    target.strength += Mathf.Max(0f, alive);
                    target.full = Mathf.Max(target.full, target.strength);
                    extra.legion_population = null;
                    extra.legion_size = extra.legion_full = 0f;
                    extra.legion_home_city_id = -1L;
                }
                if (CityPopulationSystem.DisbandIntoPopulation(soldier, city)) absorbed++;
            }
        }
    }

    private static void AddContribution(this CorpsData corps, LegionPopulationContribution origin)
    {
        LegionPopulationContribution same = corps.population.FirstOrDefault(other => other.city_id == origin.city_id &&
            other.social_class == origin.social_class && other.culture == origin.culture &&
            other.species == origin.species && other.ideology == origin.ideology);
        if (same != null) same.size += origin.size;
        else corps.population.Add(new LegionPopulationContribution
        {
            city_id = origin.city_id, social_class = origin.social_class, culture = origin.culture,
            species = origin.species, ideology = origin.ideology, size = origin.size
        });
    }

    #endregion

    #region 不留实体时的治安守兵：只是城里的人数(从本城背景人口里抽，撤防还回)

    public static float VirtualGarrison(City city) =>
        city?.data != null && city.GetOrCreate().stability is CityStabilityData state ? state.virtual_garrison : 0f;

    public static void RecruitVirtualGarrison(City city, int needed)
    {
        if (city?.data == null || needed <= 0 || city.GetOrCreate().stability is not CityStabilityData state) return;
        state.virtual_garrison_origins ??= new List<LegionPopulationContribution>();
        float taken = 0f;
        for (int attempt = 0; taken < needed && attempt < 8; attempt++)
        {
            PopGroup group = CityPopulationSystem.DrawBackground(city, candidate => candidate.Background >= 1f);
            if (group == null) break;
            float contribution = CityPopulationSystem.RemoveBackground(group, needed - taken);
            if (contribution <= 0f) break;
            LegionPopulationRules.Add(state.virtual_garrison_origins, city.id, group, contribution);
            taken += contribution;
        }
        state.virtual_garrison += taken;
        if (taken > 0f) CityPopulationSystem.InvalidateHouseholdCaches();
    }

    // 守兵超出需要(或重新打开统领实体、改回实体守兵)时退回人口
    public static void SyncVirtualGarrison(City city, CityStabilityData state)
    {
        if (city?.data == null || state == null || state.virtual_garrison <= 0f) return;
        float keep = NoMilitaryUnits ? Mathf.Max(0, state.garrison_required) : 0f;
        if (state.virtual_garrison <= keep) return;
        float release = state.virtual_garrison - keep;
        float total = LegionPopulationRules.Total(state.virtual_garrison_origins);
        if (total > 0f)
        {
            float ratio = Mathf.Clamp01(release / total);
            foreach (LegionPopulationContribution origin in state.virtual_garrison_origins)
            {
                if (origin == null || origin.size <= 0f) continue;
                float back = origin.size * ratio;
                origin.size -= back;
                CityPopulationSystem.AddBackground(city, origin.social_class == SocialClass.Army ? SocialClass.Peasant
                    : origin.social_class, origin.culture, origin.species, origin.ideology, back);
            }
            state.virtual_garrison_origins.RemoveAll(origin => origin == null || origin.size <= 0.01f);
        }
        state.virtual_garrison = keep;
        CityPopulationSystem.InvalidateHouseholdCaches();
    }

    // 城市军队解散：军职空缺，统领(多是被拉去带兵的名人)恢复文职
    public static void DisbandCityArmy(City city)
    {
        Army army = city?.army;
        if (army == null) return;
        foreach (var office in Regimes.OfficeManager.Offices.Values.Where(office => office?.meta_object == army).ToList())
            office.RemoveActor();
        Actor captain = army.getCaptain();
        foreach (Actor soldier in army.units.ToList())
        {
            if (soldier?.data == null || soldier.isRekt()) continue;
            soldier.removeFromArmy();
            if (soldier == captain || CityPopulationSystem.IsNotable(soldier)) soldier.stopBeingWarrior();
        }
        city.disbandArmy();
    }

    #endregion

    #region 义军

    // 起义、革命：在本城武装一支义军(取代原来的 count 个实体义军兵)，返回动员的人数
    public static int RaiseMilitia(City city, int count, float share)
    {
        if (city?.kingdom == null || city.isRekt() || count <= 0) return 0;
        float people = Mathf.Min(CityPopulationSystem.GetBackgroundTotal(city) * Mathf.Clamp01(share) * CityPopulationSystem.MilitiaTurnout,
            count * CityPopulationSystem.PeoplePerLegion);
        if (people < 1f) return 0;
        // 本城已有义军(叛军又得了新城、又一次起事)：补足到这次的规模，不再另编一支——
        // 以前每次都把各城再征一遍，几座城的叛军滚上几年就有二十万义军
        CorpsData existing = Of(city.kingdom).FirstOrDefault(corps => corps.militia && corps.home_city_id == city.id);
        if (existing != null)
        {
            existing.legions = Mathf.Max(existing.legions, count);
            existing.full = Mathf.Max(existing.full, people);
            Reinforce(city.kingdom, existing, people, city);
            RebuildIndex();
            return count;
        }
        CorpsData corps = Raise(city.kingdom, city, Mathf.Max(1, count), people, World.world.getCurWorldTime(), militia: true);
        if (corps == null) return 0;
        corps.task = "reserve";
        RebuildIndex();
        return count;
    }

    #endregion

    #region 统领实体与行军

    private static int _vehicleSweep;

    private static void SecondTick()
    {
        // 每 5 秒把载具实体收进账面(不留实体时)
        if (++_vehicleSweep >= 5)
        {
            _vehicleSweep = 0;
            Compatibility.WarBoxCompatibility.SweepVehicles();
        }
        bool generals = GeneralUnits;
        foreach ((Kingdom kingdom, CorpsData corps) in All().ToList())
        {
            if (generals) EnsureGeneral(kingdom, corps);
            else RemoveGeneral(corps);
            if (corps.embarked)
            {
                // 在船上按船速走(见 Sail)
                StepTowardTarget(corps);
            }
            else
            {
                // 兵种定行军速度；山地、沼泽、丛林按兵种减速，离开道路再按兵种减速，飞机不受地形影响(见 CorpsArmSystem、RoadNetwork)
                CorpsArmSystem.Arm arm = CorpsArmSystem.Of(corps);
                float speed = MarchZonesPerSecond * CorpsArmSystem.Speed(arm);
                TileZone here = Zone(corps);
                if (TerrainSystem.SlowsMarch(here)) speed *= CorpsArmSystem.Rough(arm);
                if (!CorpsArmSystem.Flies(arm) && !RoadNetwork.IsRoad(here)) speed *= CorpsArmSystem.OffRoad(arm);
                corps.march_progress += speed;
                while (corps.march_progress >= 1f)
                {
                    corps.march_progress -= 1f;
                    int before = corps.zone_id;
                    StepTowardTarget(corps);
                    // 陆上行军记程，月底按路程收行军费(见 PayUpkeep)
                    if (!corps.embarked && corps.zone_id != before) corps.march_zones++;
                    // 到了(或走不动、上了船)就不攒进度
                    if (corps.embarked || corps.zone_id == before)
                    {
                        corps.march_progress = 0f;
                        break;
                    }
                }
            }
            MoveGeneral(corps);
        }
    }

    private static void EnsureGeneral(Kingdom kingdom, CorpsData corps)
    {
        if (corps.general_id >= 0)
        {
            Actor existing = World.world.units.get(corps.general_id);
            if (existing?.data != null && !existing.isRekt() && existing.isAlive() && existing.kingdom == kingdom) return;
            corps.general_id = -1L;
        }
        City home = Home(corps);
        if (home == null || home.kingdom != kingdom) return;
        PopGroup group = CityPopulationSystem.DrawBackground(home, candidate => candidate.Background >= 1f &&
                                                                                  !string.IsNullOrEmpty(candidate.species));
        if (group == null) return;
        Actor general = CityPopulationSystem.SpawnFromGroup(home, group, soldier: true);
        if (general == null) return;
        CityPopulationSystem.RemoveBackground(group, 1f);
        corps.general_id = general.id;
        GeneralIndex[general.id] = (kingdom.id, corps.id);
        GeneralsByCity.TryGetValue(home.id, out int count);
        GeneralsByCity[home.id] = count + 1;
    }

    private static void RemoveGeneral(CorpsData corps)
    {
        if (corps.general_id < 0) return;
        Actor general = World.world?.units?.get(corps.general_id);
        GeneralIndex.Remove(corps.general_id);
        corps.general_id = -1L;
        if (general?.data == null || general.isRekt() || !general.isAlive()) return;
        if (general.isKing() || general.isCityLeader() || general.isFavorite()) return;
        City home = Home(corps) ?? general.city;
        if (home != null) CityPopulationSystem.DisbandIntoPopulation(general, home);
    }

    // 统领阵亡：组织度大挫，下个月补任
    public static void OnGeneralDied(Actor actor)
    {
        if (actor?.data == null || !GeneralIndex.TryGetValue(actor.id, out var key)) return;
        GeneralIndex.Remove(actor.id);
        Kingdom kingdom = World.world.kingdoms.get(key.kingdom);
        CorpsData corps = kingdom == null ? null : Find(kingdom, key.corps);
        if (corps == null) return;
        corps.general_id = -1L;
        corps.org = Mathf.Max(0f, corps.org - 20f);
        FrontSystem.Log(kingdom, string.Format(LM.Get("corps_log_general_killed"), Name(corps), actor.getName()));
    }

    // 军团在地块上一格一格挪向目标(跨海登陆要有航线与运力，见 FrontSystem / NavySystem)
    private static void StepTowardTarget(CorpsData corps)
    {
        if (corps.embarked)
        {
            Sail(corps);
            return;
        }
        TileZone target = FrontSystem.TargetZone(corps);
        if (target == null) return;
        TileZone current = Zone(corps);
        if (current == null)
        {
            corps.zone_id = target.id;
            return;
        }
        if (current == target) return;
        // 飞机直线飞越高山与海面，不必去港口登船(见 CorpsArmSystem)
        if (CorpsArmSystem.Flies(corps))
        {
            Paths.Remove(corps.id);
            TileZone hop = FlyStep(current, target);
            if (hop != null) corps.zone_id = hop.id;
            return;
        }
        // 旧存档里不在船上却停在海面的军(以前陆军直接走海面)：直接上岸到目标
        if (SeaRegionSystem.IsSea(current))
        {
            corps.zone_id = target.id;
            return;
        }
        // 目标在另一片陆地上：先开到本国港口，在港口上船(见 Embark、Sail)；
        // 已在海外(不在驻地那片陆地上)、这片陆地上又没有本国港口(登陆上岸的军要回国、转战别处)：就近找一处岸边上船，
        // 不再困在海外。本土没有港口的照旧出不了海
        if (!SeaRegionSystem.IsSea(current) && !SameLand(current, target))
        {
            Kingdom kingdom = World.world.kingdoms.get(corps.kingdom_id);
            City port = PortFor(kingdom, current);
            TileZone portZone = port?.getTile()?.zone;
            if (portZone != null)
            {
                if (current == portZone || current.city == port)
                {
                    Embark(kingdom, corps, target, SeaRegionSystem.RegionsAround(port));
                    return;
                }
                target = portZone;
            }
            else
            {
                if (SameLand(current, Home(corps)?.getTile()?.zone)) return;
                TileZone shore = SeaLanding.Shores(current).FirstOrDefault();
                if (shore == null) return;
                if (current == shore)
                {
                    Embark(kingdom, corps, target, SeaLanding.Waters(shore));
                    return;
                }
                target = shore;
            }
        }
        TileZone next = NextStep(corps, current, target);
        if (next != null) corps.zone_id = next.id;
    }

    // 两块地是否在同一片陆地上(有城的取城心，野地取地块中心)；拿不准时当作同一片
    public static bool SameLand(TileZone a, TileZone b)
    {
        WorldTile ta = LandTile(a), tb = LandTile(b);
        return ta == null || tb == null || ta.isSameIsland(tb);
    }

    private static WorldTile LandTile(TileZone zone)
    {
        if (zone == null) return null;
        WorldTile tile = zone.city?.data != null && !zone.city.isRekt() ? zone.city.getTile() : zone.centerTile;
        return tile?.Type?.ground == true ? tile : null;
    }

    // 上船的港口：本国(连同宗主中央)在这片陆地上、离得最近的港口城
    private static City PortFor(Kingdom kingdom, TileZone at)
    {
        if (kingdom == null) return null;
        Kingdom realm = FrontSystem.Realm(kingdom);
        IEnumerable<City> ports = NavySystem.Ports(kingdom);
        if (realm != null && realm != kingdom) ports = ports.Concat(NavySystem.Ports(realm));
        City best = null;
        float bestDistance = float.MaxValue;
        foreach (City port in ports)
        {
            TileZone zone = port.getTile()?.zone;
            if (zone == null || !SameLand(at, zone)) continue;
            float distance = ZoneDistance(at, zone);
            if (distance >= bestDistance) continue;
            best = port;
            bestDistance = distance;
        }
        return best;
    }

    // 上船失败(没有能靠岸的海滩、航线被截断)后隔几秒(真实时间)再试
    private const float EmbarkRetrySeconds = 5f;
    private static readonly Dictionary<long, float> EmbarkRetry = new();

    // 在港口(或岸边)上船：船开往目标所在那片陆地上沿陆路离目标最近、能靠岸的海滩(见 SeaLanding.Beachhead)，
    // 上岸后走陆路开往目标；航线不经敌方制海、在舰种航程内。从这里出海走不了就按本国别的出海口算，再不行就原地等。
    // 以前船只开到目标城外的海区，内陆的城求不出航线，军在港口等一辈子(玩家反馈殖民地"要等陆地连上了才打得动")
    private static void Embark(Kingdom kingdom, CorpsData corps, TileZone target, List<SeaRegionSystem.Region> start)
    {
        if (kingdom == null || target == null) return;
        float clock = Time.unscaledTime;
        if (EmbarkRetry.TryGetValue(corps.id, out float retryAt) && clock < retryAt) return;
        TileZone shore = null;
        List<SeaRegionSystem.Region> route = null;
        if (start != null && start.Count > 0) (shore, route) = SeaLanding.Beachhead(kingdom, target, start);
        if (shore == null) (shore, route) = SeaLanding.Beachhead(kingdom, target);
        if (shore == null || route == null || route.Count == 0)
        {
            EmbarkRetry[corps.id] = clock + EmbarkRetrySeconds;
            return;
        }
        EmbarkRetry.Remove(corps.id);
        corps.embarked = true;
        corps.landing_zone_id = shore.id;
        corps.sea_route = route.Select(region => region.Id).ToList();
        corps.sea_step = 0;
        corps.sea_tick = 0;
        corps.sea_path = Voyage(corps, Zone(corps), shore);
        // 寻不到海上路线：照旧从第一个海区出发，按海区走
        if (corps.sea_path.Count == 0 && route[0].Center != null) corps.zone_id = route[0].Center.id;
        Paths.Remove(corps.id);
    }

    // 船走的路线：只走登陆航线上的海区(不进敌方制海的海区)，海面上尽量沿航线(见 RoadNetwork.SeaPath)；寻不到返回空表
    private static List<int> Voyage(CorpsData corps, TileZone from, TileZone landing)
    {
        if (from == null || landing == null || corps.sea_route == null) return new List<int>();
        var regions = new HashSet<int>(corps.sea_route);
        List<TileZone> path = RoadNetwork.SeaPath(from, landing, from.city, landing.city,
            zone => SeaRegionSystem.Of(zone) is { } region && regions.Contains(region.Id));
        return path == null || path.Count < 2 ? new List<int>() : path.Select(zone => zone.id).ToList();
    }

    // 船队每秒沿海上路线走 SailZones 块(与舰队相当)，驶入航线上的下一个海区可能遭截击，到了登陆；
    // 旧存档(没有路线)先补算一条；寻不到路线时照旧每 SailSteps 秒前进一个海区，过了最后一个海区登陆
    private const int SailSteps = 4, SailZones = 2;

    private static void Sail(CorpsData corps)
    {
        if (corps.sea_route == null || corps.sea_route.Count == 0)
        {
            Disembark(corps);
            return;
        }
        corps.sea_path ??= Voyage(corps, Zone(corps), ZoneById(corps.landing_zone_id) ??
            FrontSystem.TargetZone(corps) ?? World.world.cities.get(corps.sector_city_id)?.getTile()?.zone);
        if (corps.sea_path.Count > 0)
        {
            SailAlong(corps);
            return;
        }
        if (++corps.sea_tick < SailSteps) return;
        corps.sea_tick = 0;
        corps.sea_step++;
        if (corps.sea_step >= corps.sea_route.Count)
        {
            Disembark(corps);
            return;
        }
        SeaRegionSystem.Region region = SeaRegionSystem.Get(corps.sea_route[corps.sea_step]);
        if (region?.Center != null) corps.zone_id = region.Center.id;
        Intercept(corps, region);
    }

    private static void SailAlong(CorpsData corps)
    {
        for (int i = 0; i < SailZones; i++)
        {
            if (corps.sea_path.Count <= 1)
            {
                Disembark(corps);
                return;
            }
            corps.sea_path.RemoveAt(0);
            TileZone zone = ZoneById(corps.sea_path[0]);
            if (zone == null) continue;
            corps.zone_id = zone.id;
            SeaRegionSystem.Region region = SeaRegionSystem.Of(zone);
            int index = region == null ? -1 : corps.sea_route.IndexOf(region.Id);
            if (index <= corps.sea_step) continue;
            corps.sea_step = index;
            Intercept(corps, region);
        }
    }

    private static void Disembark(CorpsData corps)
    {
        corps.embarked = false;
        corps.sea_path = null;
        corps.sea_route = null;
        corps.sea_step = 0;
        corps.sea_tick = 0;
        // 在上船时选好的海滩靠岸，上岸后走陆路开往目标；船还在海上时目标改到了别的陆地(仗打完回驻地等)就直接到目标
        TileZone target = FrontSystem.TargetZone(corps);
        TileZone landing = ZoneById(corps.landing_zone_id);
        corps.landing_zone_id = -1;
        if (landing == null || SeaRegionSystem.IsSea(landing) ||
            target != null && !SeaRegionSystem.IsSea(target) && !SameLand(landing, target))
            landing = target;
        if (landing == null || SeaRegionSystem.IsSea(landing))
            landing = World.world.cities.get(corps.sector_city_id)?.getTile()?.zone;
        if (landing != null) corps.zone_id = landing.id;
        SeaLandingDiagnostics.Landed(corps.kingdom_id);
    }

    // 船队驶入敌方制海的海区遭截击：没有护航折损一成五，有护航减半；组织度 -15
    private static void Intercept(CorpsData corps, SeaRegionSystem.Region region)
    {
        Kingdom kingdom = World.world.kingdoms.get(corps.kingdom_id);
        if (kingdom == null || region == null || !NavySystem.IsHostile(kingdom, region)) return;
        float loss = NavySystem.Escorted(kingdom, region) ? 0.07f : 0.15f;
        Casualties(corps, corps.strength * loss);
        corps.org = Mathf.Max(0f, corps.org - 15f);
        FrontSystem.Log(kingdom, string.Format(LM.Get("corps_log_intercepted"), Name(corps), Mathf.RoundToInt(loss * 100f)));
    }

    // 行军路线：按地块寻路，绕开原版最高的地形(见 TerrainSystem.Impassable)；路线按目标缓存，
    // 走偏了(撤退、改派)或目标变了再重算；找不到路就朝目标就近挪一块可走的地
    private const int MaxPathSearch = 6000;
    private static readonly Dictionary<long, (int target, List<int> path)> Paths = new();

    private static TileZone NextStep(CorpsData corps, TileZone current, TileZone target)
    {
        if (Paths.TryGetValue(corps.id, out var cached) && cached.target == target.id && cached.path.Count >= 2 &&
            cached.path[0] == current.id)
        {
            cached.path.RemoveAt(0);
            return ZoneById(cached.path[0]);
        }
        List<int> path = FindPath(current, target, CorpsArmSystem.Of(corps));
        if (path == null || path.Count < 2)
        {
            Paths.Remove(corps.id);
            return Nearer(current, target);
        }
        path.RemoveAt(0);
        Paths[corps.id] = (target.id, path);
        return ZoneById(path[0]);
    }

    // 飞行：八方向里离目标最近的一块，不管地形与海面
    private static TileZone FlyStep(TileZone current, TileZone target)
    {
        TileZone best = null;
        int bestDistance = int.MaxValue;
        foreach (TileZone neighbour in current.neighbours_all ?? Array.Empty<TileZone>())
        {
            if (neighbour == null) continue;
            int dx = neighbour.x - target.x, dy = neighbour.y - target.y;
            int distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = neighbour;
        }
        return best;
    }

    // 按代价寻路(八方向)：路上的地块代价 1，路外按兵种加价(OffRoad 的倒数)，山地、沼泽、丛林再按兵种加价(Rough 的倒数)，
    // 斜走乘 1.41；不可通行的地块与海面不进，终点除外
    private static List<int> FindPath(TileZone from, TileZone to, CorpsArmSystem.Arm arm)
    {
        float offRoad = 1f / Mathf.Max(0.1f, CorpsArmSystem.OffRoad(arm));
        float rough = 1f / Mathf.Max(0.1f, CorpsArmSystem.Rough(arm));
        var cost = new Dictionary<int, float> { [from.id] = 0f };
        var previous = new Dictionary<int, int> { [from.id] = -1 };
        var closed = new HashSet<int>();
        var open = new List<(float key, TileZone zone)> { (0f, from) };
        while (open.Count > 0 && closed.Count < MaxPathSearch)
        {
            TileZone zone = PopMin(open);
            if (zone == to) break;
            if (!closed.Add(zone.id)) continue;
            float here = cost[zone.id];
            foreach (TileZone neighbour in zone.neighbours_all ?? Array.Empty<TileZone>())
            {
                if (neighbour == null || closed.Contains(neighbour.id)) continue;
                // 陆军不走海面(跨海见 Embark)
                if (neighbour != to && (TerrainSystem.Impassable(neighbour) || SeaRegionSystem.IsSea(neighbour))) continue;
                float step = RoadNetwork.IsRoad(neighbour) ? 1f : offRoad;
                if (TerrainSystem.SlowsMarch(neighbour)) step *= rough;
                if (neighbour.x != zone.x && neighbour.y != zone.y) step *= 1.41f;
                float total = here + step;
                if (cost.TryGetValue(neighbour.id, out float known) && known <= total) continue;
                cost[neighbour.id] = total;
                previous[neighbour.id] = zone.id;
                PushHeap(open, (total + ZoneDistance(neighbour, to), neighbour));
            }
        }
        if (!previous.ContainsKey(to.id)) return null;
        var path = new List<int>();
        for (int id = to.id; id != -1; id = previous[id]) path.Add(id);
        path.Reverse();
        return path;
    }

    public static void PushHeap(List<(float key, TileZone zone)> heap, (float key, TileZone zone) item)
    {
        heap.Add(item);
        int i = heap.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (heap[parent].key <= heap[i].key) break;
            (heap[parent], heap[i]) = (heap[i], heap[parent]);
            i = parent;
        }
    }

    public static TileZone PopMin(List<(float key, TileZone zone)> heap)
    {
        TileZone top = heap[0].zone;
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);
        for (int i = 0; ;)
        {
            int left = i * 2 + 1, right = left + 1, smallest = i;
            if (left < heap.Count && heap[left].key < heap[smallest].key) smallest = left;
            if (right < heap.Count && heap[right].key < heap[smallest].key) smallest = right;
            if (smallest == i) break;
            (heap[smallest], heap[i]) = (heap[i], heap[smallest]);
            i = smallest;
        }
        return top;
    }

    // 画兵牌用：行军途中按行军进度画在所在地块与下一块之间(行军很慢，一块地要走二十几秒，
    // 只在换地块时滑一下会一卡一卡的)；下一块按缓存的路线，飞机朝目标就近一块；不在行军返回所在地块中心
    public static Vector3 DrawPosition(CorpsData corps)
    {
        TileZone zone = Zone(corps);
        Vector3 here = zone?.centerTile?.posV ?? Vector3.zero;
        if (zone == null || corps.embarked) return here;
        TileZone next = null;
        if (CorpsArmSystem.Flies(corps))
        {
            TileZone target = FrontSystem.TargetZone(corps);
            if (target != null && target != zone) next = FlyStep(zone, target);
        }
        else if (Paths.TryGetValue(corps.id, out var cached) && cached.path.Count >= 2 && cached.path[0] == zone.id)
            next = ZoneById(cached.path[1]);
        if (next?.centerTile == null) return here;
        return Vector3.Lerp(here, next.centerTile.posV, Mathf.Clamp01(corps.march_progress));
    }

    // 别的系统借用军团的寻路(按兵种代价，优先走路，见 FindPath)，如皇帝出行(EmperorTourSystem)
    public static List<int> PathFor(TileZone from, TileZone to, CorpsArmSystem.Arm arm = CorpsArmSystem.Arm.Cavalry) =>
        from == null || to == null ? null : FindPath(from, to, arm);

    private static TileZone Nearer(TileZone current, TileZone target)
    {
        TileZone best = null;
        float bestDistance = ZoneDistance(current, target);
        foreach (TileZone neighbour in current.neighbours_all ?? Array.Empty<TileZone>())
        {
            if (neighbour == null || neighbour != target && (TerrainSystem.Impassable(neighbour) || SeaRegionSystem.IsSea(neighbour)))
                continue;
            float distance = ZoneDistance(neighbour, target);
            if (distance < bestDistance)
            {
                best = neighbour;
                bestDistance = distance;
            }
        }
        return best;
    }

    public static float ZoneDistance(TileZone a, TileZone b) =>
        a == null || b == null ? float.MaxValue : Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    private static void MoveGeneral(CorpsData corps)
    {
        if (corps.general_id < 0) return;
        Actor general = World.world.units.get(corps.general_id);
        TileZone zone = Zone(corps);
        if (general?.data == null || general.isRekt() || !general.isAlive() || zone?.centerTile == null ||
            general.current_tile == null) return;
        float now = Time.unscaledTime;
        if (GeneralMoves.TryGetValue(general.id, out float next) && now < next) return;
        GeneralMoves[general.id] = now + GeneralMoveInterval;
        if (general.current_tile.zone == zone) return;
        try { general.goTo(zone.centerTile); }
        catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][军团] 统领行军失败: {exception.Message}"); }
    }

    #endregion

    #region 名称

    private static readonly string[] Digits = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

    private static string Chinese(int number)
    {
        if (number <= 0) return "〇";
        if (number < 10) return Digits[number];
        if (number < 20) return "十" + Digits[number % 10];
        if (number < 100) return Digits[number / 10] + "十" + Digits[number % 10];
        return number.ToString();
    }

    private static string Ordinal(int number)
    {
        int tens = number % 100;
        string suffix = tens is >= 11 and <= 13 ? "th" : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return number + suffix;
    }

    public static string Name(CorpsData corps)
    {
        if (corps.militia)
            return string.Format(LM.Get("corps_name_militia"), Home(corps)?.GetCityName() ?? "");
        string format = LM.Get("corps_name");
        return string.Format(format, format.StartsWith("{0}") ? Ordinal(corps.serial) : Chinese(corps.serial));
    }

    // 兵力简写：中文按"万"，英文按 k
    public static string ShortStrength(float people)
    {
        bool chinese = !LM.Get("corps_name").StartsWith("{0}");
        if (chinese)
            return people >= 10000f ? $"{people / 10000f:0.#}万" : $"{Mathf.RoundToInt(people)}";
        return people >= 1000f ? $"{people / 1000f:0.#}k" : $"{Mathf.RoundToInt(people)}";
    }

    #endregion
}

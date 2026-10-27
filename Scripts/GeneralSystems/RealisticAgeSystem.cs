using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.GeneralSystems;

// Read the current laws at death checks: toggling never changes native stats or
// leaves an age-related death mark/risk behind after the law is disabled.
public static class RealisticAgeSystem
{
    public static bool Enabled => EmpireCraftWorldLawLibrary.empirecraft_law_realistic_age?.isEnabled() == true;
    public static bool OldAgeEnabled => WorldLawLibrary.world_law_old_age?.isEnabled() == true;

    // 开着真实年龄时老死全归年度检查(RoyalHouseSystem.OnBirthday → AgeDeathDue)，原版空闲时的老死检查不再跑：
    // 它查得频繁，超龄错开(见 Due)每查一次掷一次就错不开了
    public static bool CheckNaturalDeath(Actor actor, ref bool result)
    {
        if (!Enabled || actor?.data == null || !actor.isSapient() ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(actor)) return true;
        result = false;
        return false;
    }

    #region 天年随科技时代

    // 用户 2026-10-27(截图：真实年龄开着，可汗 129 岁、在位 94 年，储君 83 岁)："明明开了真实年龄还是死的这么慢，
    // 而且这可是古代，年龄应该随着科技增长"。
    // 那张图把原版"老死"世界法则关了，以前真实年龄跟着它不老死(整张图最老的一百七十多岁)；现在开着真实年龄就自管老死，
    // 不再看原版老死法则，想不老死关掉真实年龄或给不朽特质。
    // 天年(老死的年纪)按本人文化的科技时代(TechTree.json 的 tier，见 TechnologySystem.GetEraTier)定平均，
    // 按人散列上下浮动(近似正态，标准差 LifespanSd)：古代大多死在四十到六十几岁、极少过八十五，逐代延长到近现代；
    // 君主、储君在这之上按资质加减(见 RoyalHouseSystem.RoyalMeanDeathAge)。没开科技树、文化还没用上科技树的按 NoTechMean。
    // 实体与虚拟族人同一个散列(有族谱身份的按身份，落成、并回不变)，科技进步了天年跟着变长
    public const float LifespanSd = 10f;
    // 下标是时代 tier：0 还没研究出技术(同石器)、1 石器、2 铜器、3 青铜、4 铁器、5 中世纪、6 火药、7 工业、8 电气、9 现代、10 原子、11 未来
    private static readonly float[] EraMean = { 46f, 46f, 48f, 50f, 53f, 55f, 58f, 62f, 67f, 72f, 76f, 80f };
    private const float NoTechMean = 54f;
    private const int DeathAgeFloor = 30, DeathAgeCap = 100;

    public static float MeanDeathAge(string culture)
    {
        int tier = EraTier(culture);
        return tier < 0 ? NoTechMean : EraMean[Math.Min(tier, EraMean.Length - 1)];
    }

    public static float MeanDeathAge(Actor actor) => MeanDeathAge(CultureOf(actor));

    public static int MaxDeathAge(float mean) => Math.Min(DeathAgeCap, (int)Math.Round(mean + 3f * LifespanSd));
    private static int MinDeathAge(float mean) => Math.Max(DeathAgeFloor, (int)Math.Round(mean - 2.5f * LifespanSd));

    public static int NaturalDeathAge(long seed, float mean) =>
        Math.Max(MinDeathAge(mean), Math.Min(MaxDeathAge(mean), (int)Math.Round(mean + LifespanSd * PersonZ(seed))));

    // 有族谱身份的按身份(实体与虚拟族人是同一个人，落成后单位编号会换)，没有的按单位
    public static long Seed(Actor actor) => actor.GetPersonalIdentity()?.id ?? actor.id;

    // 按人散列出近似标准正态的数(四个均匀数相加，±3.46)：同一人每次都一样，不会每年重掷
    private static float PersonZ(long seed)
    {
        double sum = 0d;
        ulong x = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
        for (int i = 0; i < 4; i++) sum += Unit(x + (ulong)i * 0xD1B54A32D192ED03UL);
        return (float)((sum - 2d) * 1.7320508d);
    }

    // 本人文化用上了科技树按本人文化(改朝换代、被征服不跟着变)，否则按所在国、所在城
    private static string CultureOf(Actor actor)
    {
        string own = CultureService.GetActorCulture(actor);
        return TechnologySystem.IsActiveFor(own) ? own : TechnologySystem.GetCultureOf(actor);
    }

    private static string CultureOf(PersonalClanIdentity person)
    {
        if (TechnologySystem.IsActiveFor(person.culture)) return person.culture;
        return person.virtual_city_id < 0 ? "" : TechnologySystem.GetCultureOf(World.world?.cities?.get(person.virtual_city_id));
    }

    // 每个文化的时代一年算一次(GetEraTier 要过一遍已研究的技术)；-1 没开科技树或这个文化还没用上
    private static readonly Dictionary<string, (int year, int tier)> EraCache = new(StringComparer.Ordinal);
    private static object _eraWorld;

    private static int EraTier(string culture)
    {
        if (string.IsNullOrEmpty(culture) || !TechnologySystem.IsActiveFor(culture)) return -1;
        if (!ReferenceEquals(_eraWorld, World.world))
        {
            _eraWorld = World.world;
            EraCache.Clear();
        }
        int year = World.world == null ? 0 : Date.getCurrentYear();
        if (EraCache.TryGetValue(culture, out (int year, int tier) cached) && cached.year == year) return cached.tier;
        int tier = Math.Max(0, TechnologySystem.GetEraTier(culture));
        EraCache[culture] = (year, tier);
        return tier;
    }

    // 每年过生日查一次(RoyalHouseSystem.OnBirthday)：到了天年就寿终
    public static bool AgeDeathDue(Actor actor)
    {
        if (actor.hasTrait("immortal")) return false;
        if (RoyalHouseSystem.IsRoyal(actor)) return actor.getAge() >= RoyalLifespan(actor);
        return Due(actor.getAge(), NaturalDeathAge(Seed(actor), MeanDeathAge(actor)));
    }

    // 过了天年 OverdueGraceYears 年以上的(法则刚接管、读旧档、换了文化、科技失传)每年 OverdueChance，
    // 几年里错开，不在同一年成批寿终(君主、储君另有保底，见 RoyalLifespan)
    private const int OverdueGraceYears = 1;
    private const float OverdueChance = 0.35f;

    private static bool Due(int age, int deathAge) =>
        age >= deathAge && (age - deathAge <= OverdueGraceYears || UnityEngine.Random.value < OverdueChance);

    #endregion

    // 生养子女的最低年纪(用户 2026-10-26 "会出现儿子比父亲大，以及3岁就有儿子")：原版几岁就算成年，模组的生育入口
    // (原版怀孕判定的替换、妻妾添丁、君主无子强制生育、司生育的女官)以前只看成年，几岁的人也生养。
    // 智慧种族十六岁起：开着真实年龄一律按人类；关着按物种寿命折算(同 CityPopulationSystem.AssignAdultAge)
    public const int ParentAge = 16;

    public static int MinParentAge(Actor actor)
    {
        if (actor?.data == null || !actor.isSapient()) return 0;
        if (Enabled) return ParentAge;
        float lifespan = actor.stats?["lifespan"] ?? 0f;
        return lifespan > 0f && lifespan < 80f ? Math.Max(1, (int)Math.Round(ParentAge * lifespan / 80f)) : ParentAge;
    }

    // 没有这个人(单亲生育、对方空着)算够
    public static bool OldEnoughToParent(Actor actor) => actor?.data == null || actor.getAge() >= MinParentAge(actor);

    // 生养的最高年纪：母亲不过 MotherAgeMax、父亲不过 FatherAgeMax(同 RoyalHouseSystem 宗室添丁)；关着真实年龄时按物种寿命折算。
    // 君主无子强制生育、司生育的女官以前只看下限，六七十岁照样生
    public const int MotherAgeMax = 45, FatherAgeMax = 60;

    public static bool YoungEnoughToParent(Actor actor)
    {
        if (actor?.data == null || !actor.isSapient()) return true;
        int max = actor.data.sex == ActorSex.Female ? MotherAgeMax : FatherAgeMax;
        float lifespan = actor.stats?["lifespan"] ?? 0f;
        if (!Enabled && lifespan > 0f) max = Math.Max(MinParentAge(actor) + 1, (int)Math.Round(max * lifespan / 80f));
        return actor.getAge() <= max;
    }

    // 君主未成年(亲政的年纪同生养，见 MinParentAge)。原版 isAdult 按亚种的成年年龄，几岁就算成年：七岁的皇帝没人辅政、
    // 亲征当统帅、战功全记在自己头上(用户 2026-10-27 转玩家截图"幼儿即位应权臣太后主政""威权也应该涨权臣的")
    public static bool IsMinor(Actor actor) => actor?.data != null && (!actor.isAdult() || actor.getAge() < MinParentAge(actor));

    #region 宗室天年：成为君主、储君时的年纪

    // 用户 2026-10-21 转群友："加了真实年龄后，经常一称帝没有一年就死，要不就是依旧活这么久"。
    // 君主、储君的天年(平均五十二)比常人(六十到八十)短，而天年只看此刻是不是君主：六十多岁的大臣、宿将、
    // 开国补记的长子(见 VirtualGenealogySystem.EnsureFounderHouse)被立为君主、储君，天年当场从常人的七十上下
    // 掉到宗室的五十上下，已经过了，下一个年头就寿终——原版选君偏爱年长望重的人，无小人模式落成的官员二十五到五十五岁、
    // 又活到六七十，称王称帝时多半已过五十(日志里开国之君 57、67、74、80、84、101 岁)。
    // 现在记下成为君主、储君时的年纪(锚定，ActorExtraData.royal_anchor_age)：
    //   · 本来的天年(RoyalDeathAge)在保底之后的照旧；
    //   · 本来的天年已到或保底之前就到的，按"活到这个年纪的人还能活多久"另取一个(SurvivorDeathAge：同一正态分布截去
    //     锚定年纪以下的部分，按人取数)——六十岁即位余寿中位七年、七十岁五年、八十岁以上三四年，接近古人的余寿；
    //   · 即位(KingdomPatch 的 setKing 补丁，见 OnCrowned)后至少 AccessionMinYears 年不寿终：
    //     资质后来定为荒淫之君(天年减十)也不至于登基当年就死；意外、暴病照旧(见 RoyalHouseSystem.Mishap)；
    //   · 没有即位那一下的(储君、旧档里在位的君主、真实年龄刚打开)：头一回年度检查时补定，保底 1~5 年按人错开，
    //     旧档里已经超龄的君主不会一读档就成批寿终；
    //   · 不再是君主、储君一年半以上又再立的，按再立时的年纪重新锚定(年度检查每年都会看到在位的人，隔了一年半说明中间不是)。
    // 2026-10-27 起君主、储君与常人同按科技时代定天年(见上)，只差资质加减；锚定照旧管资质减寿、超龄的人被立、
    // 旧档里在位的超龄君主(原版老死关着时没查过的)。
    // 非人类物种、长寿亚种开着真实年龄时一样按这套天年(法则说明："智慧种族按科技时代定天年")；不朽特质照旧不老死
    public const int AccessionMinYears = 3;
    private const int LateMinYears = 1, LateSpreadYears = 5;
    // 一年 60 秒：一年半没看到在位就算中断过
    private const double RoyalGapSeconds = 90d;
    // 取样上限：截尾正态的极端尾巴不取(至多到本时代的天年上限 MaxDeathAge，或锚定后再 15 年)
    private const int SurvivorMaxYears = 15;

    public static int RoyalLifespan(Actor actor)
    {
        int natural = RoyalHouseSystem.RoyalDeathAge(actor);
        var data = actor.GetOrCreate();
        double now = World.world?.getCurWorldTime() ?? 0d;
        if (data.royal_anchor_age < 0 || data.royal_seen_at < 0d || now - data.royal_seen_at > RoyalGapSeconds)
            Anchor(actor, false);
        data.royal_seen_at = now;
        return Settle(actor, natural, data.royal_anchor_age, data.royal_anchor_floor);
    }

    private static int Settle(Actor actor, int natural, int anchor, int floor)
    {
        if (anchor < 0 || natural >= floor) return natural;
        return Math.Max(floor, SurvivorDeathAge(actor, anchor));
    }

    // 新君即位(KingdomPatch.new_emperor，读档不算)：按即位时的年纪锚定
    public static void OnCrowned(Actor actor)
    {
        if (!Enabled || actor?.data == null || !actor.isAlive() || !actor.isSapient() || actor.hasTrait("immortal")) return;
        Anchor(actor, true);
    }

    private static void Anchor(Actor actor, bool crowned)
    {
        var data = actor.GetOrCreate();
        int age = actor.getAge();
        double now = World.world?.getCurWorldTime() ?? 0d;
        int years = crowned
            ? AccessionMinYears
            : LateMinYears + (int)(Unit((ulong)actor.id * 0x9FB21C651E98DF25UL + 0xD1B54A32D192ED03UL) * LateSpreadYears);
        // 储君即位：做储君时许下的保底不收回
        bool continuous = data.royal_anchor_age >= 0 && data.royal_seen_at >= 0d && now - data.royal_seen_at <= RoyalGapSeconds;
        int floor = age + years;
        if (continuous) floor = Math.Max(floor, data.royal_anchor_floor);
        data.royal_anchor_age = age;
        data.royal_anchor_floor = floor;
        data.royal_seen_at = now;
        int natural = RoyalHouseSystem.RoyalDeathAge(actor);
        // 本来的天年够用的不记(每年即位的君主很多)；只记要另定的
        if (natural >= data.royal_anchor_floor) return;
        int settled = Settle(actor, natural, age, data.royal_anchor_floor);
        Log($"{(crowned ? "即位" : "补定")} {Who(actor)} {age} 岁，本来天年 {natural}，" +
            $"另定 {settled}(余寿 {settled - age} 年，至少到 {data.royal_anchor_floor})");
    }

    // 活到 anchor 岁的人的天年：正态 N(平均天年, LifespanSd) 截去 anchor 以下，按人(与锚定年纪)取一个上尾概率
    public static int SurvivorDeathAge(Actor actor, int anchor)
    {
        float mean = RoyalHouseSystem.RoyalMeanDeathAge(actor);
        double sd = LifespanSd;
        double u = Unit((ulong)Seed(actor) * 0xC2B2AE3D27D4EB4FUL + (ulong)Math.Max(0, anchor) * 0x165667B19E3779F9UL);
        double tail = UpperTail((anchor - mean) / sd);
        // 远远超龄(旧档里一两百岁的人)：上尾小到算不准，一到四年按人错开
        if (tail < 1e-9) return anchor + 1 + (int)(u * 4d);
        double z = -InverseNormal(tail * (1d - u));
        int age = (int)Math.Ceiling(mean + sd * z);
        return Math.Max(anchor, Math.Min(age, Math.Max(MaxDeathAge(mean), anchor + SurvivorMaxYears)));
    }

    // 按人散列成 [0,1) 的数(splitmix64 收尾)
    private static double Unit(ulong x)
    {
        x ^= x >> 30;
        x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27;
        x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (x >> 11) * (1d / 9007199254740992d);
    }

    // 标准正态的上尾概率 Q(z) = erfc(z/√2)/2(Numerical Recipes 的 erfc 逼近，相对误差 1.2e-7，尾部也准)
    private static double UpperTail(double z)
    {
        double x = z / Math.Sqrt(2d);
        double a = Math.Abs(x);
        double t = 1d / (1d + 0.5d * a);
        double erfc = t * Math.Exp(-a * a - 1.26551223d + t * (1.00002368d + t * (0.37409196d + t * (0.09678418d +
            t * (-0.18628806d + t * (0.27886807d + t * (-1.13520398d + t * (1.48851587d +
            t * (-0.82215223d + t * 0.17087277d)))))))));
        return 0.5d * (x >= 0d ? erfc : 2d - erfc);
    }

    // 标准正态分位数(Acklam 有理逼近，相对误差 1.2e-9)
    private static readonly double[] InvA =
        { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
    private static readonly double[] InvB =
        { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
    private static readonly double[] InvC =
        { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
    private static readonly double[] InvD =
        { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };

    private static double InverseNormal(double p)
    {
        if (p <= 0d) return -40d;
        if (p >= 1d) return 40d;
        const double low = 0.02425d;
        if (p < low || p > 1d - low)
        {
            double q = Math.Sqrt(-2d * Math.Log(p < low ? p : 1d - p));
            double x = (((((InvC[0] * q + InvC[1]) * q + InvC[2]) * q + InvC[3]) * q + InvC[4]) * q + InvC[5]) /
                       ((((InvD[0] * q + InvD[1]) * q + InvD[2]) * q + InvD[3]) * q + 1d);
            return p < low ? x : -x;
        }
        double r0 = p - 0.5d, r = r0 * r0;
        return (((((InvA[0] * r + InvA[1]) * r + InvA[2]) * r + InvA[3]) * r + InvA[4]) * r + InvA[5]) * r0 /
               (((((InvB[0] * r + InvB[1]) * r + InvB[2]) * r + InvB[3]) * r + InvB[4]) * r + 1d);
    }

    #endregion

    #region 真实年龄关着时：每年按原版寿命曲线查一次

    // 真实年龄关着时只剩原版的老死检查，原版只在人物空闲时查(Actor.checkNaturalDeath)，纯净地图、无小人模式下几乎查不到：
    // 存档里关着真实年龄的世界，皇帝在位七八十年、一百零二年的都有(实体活到一两百岁)。这两种模式下每年过生日照原版的
    // 曲线(寿命按亚种基因与特质，超过寿命后逐年升高，这里每年至多三成五)掷一次，与虚拟族人一样(见 IsVirtualAgeDeathDue)；
    // 有小人的普通模式原版自己查得到，不另掷。每年至多 NativeYearlyCap：旧档里早已活过寿命一大截的不在头一年成批老死
    private const float NativeYearlyCap = 0.35f;

    public static bool NativeAgeDeathDue(Actor actor)
    {
        if (Enabled || !OldAgeEnabled || actor?.data == null || !actor.isSapient() || actor.hasTrait("immortal")) return false;
        if (!CityPopulationSystem.AbstractPopulationEnabled && !PureMapMode.Enabled) return false;
        float lifespan = actor.stats?["lifespan"] ?? 0f;
        return Math.Min(NativeYearlyCap, NativeDeathChance(actor.getAge(), lifespan)) > UnityEngine.Random.value;
    }

    #endregion

    #region 诊断([EmpireCraft][寿数]，每年至多 LogsPerYear 条)

    private const int LogsPerYear = 16;
    private static int _logYear = int.MinValue, _logCount;

    private static string Who(Actor actor)
    {
        string realm = actor.kingdom?.data == null ? "" : actor.kingdom.GetKingdomName();
        string role = actor.isKing() ? "君主" : "储君";
        return $"{actor.getName()}({realm}{role})";
    }

    // 君主、储君老死时记一行：享年、天年怎么定的
    public static void LogRoyalDeath(Actor actor, string source)
    {
        if (actor?.data == null) return;
        int age = actor.getAge();
        if (!Enabled)
        {
            Log($"寿终 {Who(actor)} 享年 {age}，真实年龄关着，按原版寿命 {Math.Round(actor.stats?["lifespan"] ?? 0f)}({source})");
            return;
        }
        var data = actor.GetOrCreate();
        int natural = RoyalHouseSystem.RoyalDeathAge(actor);
        int settled = Settle(actor, natural, data.royal_anchor_age, data.royal_anchor_floor);
        string anchor = data.royal_anchor_age < 0 ? "未锚定"
            : $"{data.royal_anchor_age} 岁锚定，保底到 {data.royal_anchor_floor}";
        Log($"寿终 {Who(actor)} 享年 {age}，本来天年 {natural}，定下的天年 {settled}，{anchor}({source})");
    }

    private static void Log(string text)
    {
        try
        {
            int year = World.world == null ? 0 : Date.getCurrentYear();
            if (year != _logYear)
            {
                _logYear = year;
                _logCount = 0;
            }
            if (_logCount++ >= LogsPerYear) return;
            NeoModLoader.services.LogService.LogInfo("[EmpireCraft][寿数] " + text);
        }
        catch
        {
            // 诊断失败不影响生死
        }
    }

    #endregion

    public static void CaptureNativeLifetime(Actor actor, PersonalClanIdentity person)
    {
        person.virtual_native_lifespan = actor.stats?["lifespan"] ?? 0f;
        person.virtual_immortal = actor.hasTrait("immortal");
    }

    // 真实年龄开着时按科技时代的天年(与实体同一个散列，见 Seed)，不看原版老死法则；关着时照原版
    public static bool IsVirtualAgeDeathDue(PersonalClanIdentity person)
    {
        if (person.virtual_immortal) return false;
        if (Enabled) return Due(person.age, NaturalDeathAge(person.id, MeanDeathAge(CultureOf(person))));
        if (!OldAgeEnabled) return false;
        // 旧存档、虚拟出生的子女没记下寿命：按物种补上。以前读物种资产上的 lifespan，原版寿命其实写在亚种基因里、
        // 物种资产上是 0，按原版 0 即不老死，关了真实年龄时这些虚拟族人活到两三百岁还在议立新君的名单里
        if (!(person.virtual_native_lifespan > 0f)) person.virtual_native_lifespan = SpeciesLifespan(person.species);
        return NativeDeathChance(person.age, person.virtual_native_lifespan ?? 0f) > UnityEngine.Random.value;
    }

    private const float FallbackLifespan = 100f;
    private static readonly Dictionary<string, float> LifespanBySpecies = new();
    private static object _lifespanWorld;

    // 物种的天年：先看一个在世的同物种单位(含亚种基因、特质)，没有就看物种基因里的寿命，再没有按一百年
    public static float SpeciesLifespan(string species)
    {
        if (!ReferenceEquals(_lifespanWorld, World.world))
        {
            _lifespanWorld = World.world;
            LifespanBySpecies.Clear();
        }
        string key = species ?? "";
        if (LifespanBySpecies.TryGetValue(key, out float known)) return known;
        float lifespan = 0f;
        try
        {
            foreach (Actor actor in World.world?.units ?? Enumerable.Empty<Actor>())
            {
                if (actor?.data == null || actor.asset?.id != key || !actor.isAlive()) continue;
                lifespan = actor.stats?["lifespan"] ?? 0f;
                if (lifespan > 0f) break;
            }
        }
        catch
        {
            // 遍历时单位列表变了：按物种基因算
        }
        if (!(lifespan > 0f))
            foreach (GenomePart part in AssetManager.actor_library.get(key)?.genome_parts ?? new HashSet<GenomePart>())
                if (string.Equals(part.id, "lifespan", StringComparison.OrdinalIgnoreCase)) lifespan = part.value;
        if (!(lifespan > 0f) || float.IsNaN(lifespan)) lifespan = FallbackLifespan;
        LifespanBySpecies[key] = lifespan;
        return lifespan;
    }

    public static float NativeDeathChance(int age, float lifespan)
    {
        if (lifespan <= 0f || float.IsNaN(lifespan) || age <= lifespan) return 0f;
        // Same age curve as Actor.checkNaturalDeath, sampled in the existing
        // annual genealogy queue rather than adding a per-frame census.
        return Math.Min(0.9f, (float)(1d / (1d + Math.Exp(-5d * ((age - lifespan) / lifespan - 0.5d)))));
    }
}

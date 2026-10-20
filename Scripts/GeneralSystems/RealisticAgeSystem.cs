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

    public static int HumanDeathAge(long id, int recordedAge = -1)
    {
        if (recordedAge >= 60 && recordedAge <= 80) return recordedAge;
        // Stable across frames and saves; no repeated random draw can extend life.
        return 60 + (int)((ulong)id % 21UL);
    }

    public static bool CheckNaturalDeath(Actor actor, ref bool result)
    {
        if (!Enabled || actor?.data == null || !actor.isSapient() ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(actor)) return true;
        result = false;
        if (!OldAgeEnabled || !actor.isAlive() || actor.hasTrait("immortal")) return false;
        if (actor.getAge() < DeathAge(actor)) return false;
        if (RoyalHouseSystem.IsRoyal(actor)) LogRoyalDeath(actor, "原版空闲检查");
        actor.getHitFullHealth(AttackType.Age);
        result = true;
        return false;
    }

    // 君主、储君按古代的天年(见 RoyalHouseSystem.RoyalDeathAge，成为君主、储君时已近天年的另定，见 RoyalLifespan)，
    // 其余六十到八十
    public static int DeathAge(Actor actor) => RoyalHouseSystem.IsRoyal(actor)
        ? RoyalLifespan(actor)
        : HumanDeathAge(actor.id, actor.GetPersonalIdentity()?.virtual_death_age ?? -1);

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
    // 非人类物种、长寿亚种开着真实年龄时一样按这套天年(法则说明："智慧种族使用人类寿命")；不朽特质照旧不老死
    public const int AccessionMinYears = 3;
    private const int LateMinYears = 1, LateSpreadYears = 5;
    // 一年 60 秒：一年半没看到在位就算中断过
    private const double RoyalGapSeconds = 90d;
    // 取样上限：截尾正态的极端尾巴不取(至多活到 95 岁，或锚定后再 15 年)
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

    // 活到 anchor 岁的人的天年：正态 N(平均天年, 13) 截去 anchor 以下，按人(与锚定年纪)取一个上尾概率
    public static int SurvivorDeathAge(Actor actor, int anchor)
    {
        double mean = RoyalHouseSystem.RoyalMeanDeathAge(actor);
        double sd = RoyalHouseSystem.RoyalDeathSd;
        double u = Unit((ulong)actor.id * 0xC2B2AE3D27D4EB4FUL + (ulong)Math.Max(0, anchor) * 0x165667B19E3779F9UL);
        double tail = UpperTail((anchor - mean) / sd);
        // 远远超龄(旧档里一两百岁的人)：上尾小到算不准，一到四年按人错开
        if (tail < 1e-9) return anchor + 1 + (int)(u * 4d);
        double z = -InverseNormal(tail * (1d - u));
        int age = (int)Math.Ceiling(mean + sd * z);
        return Math.Max(anchor, Math.Min(age, Math.Max(RoyalHouseSystem.RoyalDeathMax, anchor + SurvivorMaxYears)));
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
        person.virtual_death_age = HumanDeathAge(actor.id, person.virtual_death_age);
    }

    public static bool IsVirtualAgeDeathDue(PersonalClanIdentity person)
    {
        if (!OldAgeEnabled || person.virtual_immortal) return false;
        if (Enabled)
        {
            person.virtual_death_age = HumanDeathAge(person.id, person.virtual_death_age);
            return person.age >= person.virtual_death_age;
        }
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

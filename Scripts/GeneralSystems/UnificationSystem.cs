using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 天下大势(历史周期律，用户 2026-10-14 定)：
// 天下 = 帝国核心(或玩家并成的联合天下，见 TianxiaSystem)的法理城市，至少五座(用户 2026-10-17："一个帝国法理一个天下，
// 玩家也可以设置多个帝国法理的联合天下")；还没有帝国核心的地方 = 同一天下标识的文化(见 LineOf)里连成一片的城
// (领地接壤，或同一块陆地上相距不远)，隔着别的文化、隔得远的是另一个天下。
// 一家 = 一个独立国家连同它的附庸；帝国连同各成员国、附庸算一家。都城在这片里的才是天下的一家
// (用户 2026-10-15："到处都是统一战争，有的甚至跨文化跨地域打")：别的文化的国家占着几座城，不算天下的人，不是统一的对象。
//   · 乱世：天下至少八座城、三家以上，没有哪家占到三成——群雄互伐，饥荒、民变照常；
//   · 统一战争：一家占到三成，或国力是第二名的两倍(且至少占 8%)——天命所归，目标二十年内打完：
//       - 统一者每月挑接壤(或隔海相望)的一家开战(目标吞并)，同时可打四场，不必休战，不受三十年强制停战；
//         天下里的别家都够不着、手上也没有统一战争，连着三年就算统一了能统一的；
//       - 兵败如山倒(用户 2026-10-19："不要整个国家一次性把土地判过去，至少做成战线快速推进")：对方正规军打到剩不到
//         满编一成半，又丢了都城或三分之一的城，仗打久了国内稳定度很差(各城平均不到 25，久战疲敝逐年压低)，
//         连着两个月没缓过来——战线崩溃、推进快一倍，城仍一座座打(见 FrontCollapse)；
//         以前是举国吞并，另有"不到统一者一成就算打光"，小国一仗没打就整国并入；
//       - 望风归附(用户 2026-10-14："兵力没有达到绝对碾压之前、主力还没打完就望风而降")：只剩一两座城的小国，正与统一者交战、
//         统一者正规军是它五倍以上、它的主力确已被打垮(正规军剩不到满编三成)，才每年有几率举国归附，统一十年后几率加倍
//         (以前不交战的也算，"不到统一者十分之一"也算打垮)；
//       - 战线推进加快一半，十年后加倍(见 FrontSystem.PushFactor)；
//       - 统一者处在开国气象里(见 InGrace)：积弊不涨、不起事、不分裂；
//   · 统一完成(占到九成，或天下再没有别的独立势力)：开国气象延续三十年；之后积弊按王朝年岁慢慢沉积(见 MaladySystem.Pace)，
//     末世崩溃，天下重回乱世，再出新的统一者。一个王朝只统一一次(改朝换代才能再来，见 OnDynastyChanged)；
//   · 天命已失、统一中辍：地盘跌到 6% 以下，或不到两成、而且已不是天下国力最强的一家；天下里有别家的地盘比统一者还大；
//     或打满三十年还没统一(玩家日志 2026-10-16：有的统一战争打了一百七十多年，一直处在开国气象里，不起事、不分裂)——
//     打满三十年时地盘过半算统一了(能统一的都统一了)，不到一半就中辍。
// 游牧难一统(用户 2026-10-20："游牧应该很难大一统"，"只要出现雄主快速统一，只要一死就会开始动荡")：游牧政权
// (游牧政体、汗国分封，帝国按核心，见 IsNomad)——
//   · 只有开国雄主(或中兴雄主，见 RulerTraitSystem.FounderReigns)在位才能发动统一战争；发动时记下这位雄主(EmpireData.nomad_hero_id)，
//     在位时照上面的规矩打，推进加成、望风归附、兵败如山倒都不变；不是帝国的游牧国没有雄主，要先称汗；
//   · 雄主不在位了(身故、被废)：统一没打完的当场中辍——打着的统一战争照中辍的规矩改名(游牧扩张/帝国扩张，见 Halt)，
//     兵败如山倒作罢；继任者不是雄主就不能再发动；
//   · 统一后的开国气象只到"雄主不在位"与"统一后满 NomadGraceYears 年"两者较早的那时(别的政体照旧 GraceYears 年)；
//   · 雄主之后第一次汗位更替必乱：不看积弊、正统(见 TakeHeroTurmoil、NomadCycleSystem.OnKhanSucceeded)，之后照常。
// 周室分封(ZhouEraSystem)的天下另有一套春秋战国，不在这里管。
public static class UnificationSystem
{
    private const float ContenderShare = 0.3f, PowerLead = 2f, PowerLeadMinShare = 0.08f, UnifiedShare = 0.9f,
        LostShare = 0.2f, CollapsedShare = 0.06f;
    // 两家就可以争(南北对峙、分治的两朝也要能统一，以前要三家，两强对峙永远不打统一战)
    private const int GraceYears = 30, MaxWars = 4, LateYears = 10, MinCities = 8, MinContenders = 2;
    // 统一战争最多打这么多年；到期地盘过半算统一了，不到就中辍
    private const int MaxUnifyYears = 30;
    private const float LateUnifiedShare = 0.5f;
    // 同一文化圈的两座城领地接壤，或同在一块陆地上、城心相距不过这么多格，就连成一片
    private const int LinkTiles = 48;
    // 望风归附：只剩一两座城、正在交战、兵力被碾压(正规军不到统一者五分之一)、主力已垮(剩不到满编三成)
    private const float CrushRatio = 0.2f, BrokenShare = 0.3f, SubmitChance = 0.1f, SubmitCap = 0.35f;
    private const int SubmitMaxCities = 2;
    private const float PushBonus = 1.5f, LatePushBonus = 2f;
    // 游牧难一统：统一后的开国气象至多这么多年(雄主先不在位就更早散)；"无雄主不能统一"的日志每家这么多年一行
    private const int NomadGraceYears = 10, NoHeroLogYears = 10;

    private static object _world;
    private static double _lastMonth = -1d, _lastYear = -1d;
    // 统一者连着几年够不着天下里的任何一家(也没在打)
    private static readonly Dictionary<long, int> Stalled = new();
    private const int StallYears = 3;
    // 上次年度统计的天下：统一者 → 它天下里的别家(按都城距离排好)
    private static readonly Dictionary<long, List<Kingdom>> Rivals = new();
    // 诊断限频：游牧无雄主不能统一(国 → 上回记的时间)；统一满十年开国气象到期(国 → 记过的那次统一时间)
    private static readonly Dictionary<long, double> NoHeroLogged = new();
    private static readonly Dictionary<long, double> GraceEndLogged = new();

    public static void ResetWorldState()
    {
        _world = null;
        _lastMonth = _lastYear = -1d;
        FrontCollapse.ResetWorldState();
        Stalled.Clear();
        Rivals.Clear();
        NoHeroLogged.Clear();
        GraceEndLogged.Clear();
    }

    #region 查询

    private static Kingdom Sovereign(Kingdom kingdom) => EmpireCoreControl.Sovereign(kingdom);

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;

    // 正在统一天下
    public static bool Unifying(Kingdom kingdom)
    {
        Kingdom sovereign = Sovereign(kingdom);
        return Alive(sovereign) && sovereign.GetOrCreate().unify_since >= 0d;
    }

    // 统一战争打了几年
    private static int UnifyYears(Kingdom sovereign)
    {
        double since = sovereign.GetOrCreate().unify_since;
        return since >= 0d ? Date.getYearsSince(since) : 0;
    }

    // 开国气象还剩几年(天下窗口显示)；统一战争期间或没有开国气象时为 -1
    public static int GraceYearsLeft(Kingdom kingdom)
    {
        if (!InGrace(kingdom)) return -1;
        Kingdom sovereign = Sovereign(kingdom);
        var data = sovereign.GetOrCreate();
        int years = IsNomad(sovereign) ? NomadGraceYears : GraceYears;
        return data.unified_at < 0d ? -1 : Math.Max(0, years - Date.getYearsSince(data.unified_at));
    }

    // 开国气象：统一战争期间、统一后三十年(见 Empire.InFoundingGrace、RebellionSystem.CanAttempt)；
    // 游牧政权只在记下的雄主在位时有，统一后至多 NomadGraceYears 年(见文件头游牧难一统)
    public static bool InGrace(Kingdom kingdom)
    {
        Kingdom sovereign = Sovereign(kingdom);
        if (!Alive(sovereign) || World.world == null) return false;
        var data = sovereign.GetOrCreate();
        if (data.unify_since < 0d && data.unified_at < 0d) return false;
        bool nomad = IsNomad(sovereign);
        if (nomad && !HeroReigns(sovereign)) return false;
        if (data.unify_since >= 0d) return true;
        if (World.world.getCurWorldTime() < data.unified_at ||
            Date.getYearsSince(data.unified_at) >= (nomad ? NomadGraceYears : GraceYears)) return false;
        // 统一后的开国气象就是受天命的治世开头(用户 2026-10-19 选"以天命为准")：天命丢了气象就散了——
        // 以前统一"完成"却没受天命，天下显示乱世，开国气象却还在
        return TianxiaSystem.LeadOf(sovereign.capital) == null || TianxiaSystem.MandateOf(sovereign) != null;
    }

    // 统一者与天下里别家的仗，不论谁先动手(不受三十年强制停战、攻方议和；把对方正规军打光就举国吞并)。
    // 统一者跟天下以外的国家(别的文化、别处)打的是寻常战争
    public static bool IsUnificationWar(War war)
    {
        if (war?.data == null || war.hasEnded()) return false;
        Kingdom attacker = war.getMainAttacker(), defender = war.getMainDefender();
        return Contends(attacker, defender) || Contends(defender, attacker);
    }

    // unifier 正在统一的天下里有 rival 这一家
    public static bool Contends(Kingdom unifier, Kingdom rival)
    {
        Kingdom sovereign = Sovereign(unifier), foe = Sovereign(rival);
        return Alive(sovereign) && Alive(foe) && sovereign != foe && sovereign.GetOrCreate().unify_since >= 0d &&
               Rivals.TryGetValue(sovereign.id, out List<Kingdom> rivals) && rivals.Contains(foe);
    }

    // 同一个天下(用户 2026-10-19 选"统一按帝国核心")：都城都在帝国核心的天下里就比天下；都不在就照旧按文化圈；
    // 外族据有这个天下四分之一以上的城也算天下一家(都城在草原也算，用户选"可受天命、本族可光复"，见 Tally)
    public static bool SameRealm(Kingdom a, Kingdom b)
    {
        if (!Alive(a) || !Alive(b)) return false;
        EmpireCore la = TianxiaSystem.LeadOf(a.capital), lb = TianxiaSystem.LeadOf(b.capital);
        if (la != null && la == lb) return true;
        if (la != null && HoldsShare(b, la) || lb != null && HoldsShare(a, lb)) return true;
        return la == null && lb == null && SameSphere(a, b);
    }

    // 外族算天下一家要据有几成
    private const float ForeignMemberShare = 0.25f;

    private static bool HoldsShare(Kingdom kingdom, EmpireCore lead)
    {
        List<City> cities = TianxiaSystem.Cities(lead);
        if (cities.Count == 0) return false;
        Kingdom sovereign = Sovereign(kingdom);
        int held = cities.Count(city => city?.kingdom != null && Sovereign(city.kingdom) == sovereign);
        return held >= cities.Count * ForeignMemberShare;
    }

    // 同一文化圈(本国文化的天下标识相同，见 LineOf)
    public static bool SameSphere(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null) return false;
        string line = LineOf(CultureService.GetRealmCulture(a));
        return line != null && line == LineOf(CultureService.GetRealmCulture(b));
    }

    // 战线推进：统一者打天下里的别家快一半，统一十年后加倍
    public static float PushFactor(Kingdom side, Kingdom opponent)
    {
        Kingdom sovereign = Sovereign(side), foe = Sovereign(opponent);
        if (!Alive(sovereign) || !Alive(foe) || sovereign == foe || sovereign.GetOrCreate().unify_since < 0d) return 1f;
        return UnifyYears(sovereign) >= LateYears ? LatePushBonus : PushBonus;
    }

    // 改朝换代：新王朝可以再统一一次；但本国仍受着天命(篡位、内禅接手的是现成的天下)时不清——
    // 以前篡了共主的人马上又算统一者、再得三十年开国气象(连保留积弊的权臣篡位也是)
    public static void OnDynastyChanged(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (!Alive(core) || TianxiaSystem.MandateOf(core) != null) return;
        var data = core.GetOrCreate();
        data.unify_since = -1d;
        data.unified_at = -1d;
    }

    #endregion

    #region 游牧难一统

    // 游牧政权：游牧政体、汗国分封(sovereign 是一家之主，帝国按核心)
    private static bool IsNomad(Kingdom sovereign)
    {
        if (!Alive(sovereign)) return false;
        var type = sovereign.GetRegime()?.type;
        return type == EmpireCraft.Scripts.Regimes.RegimeType.YouMu || type == EmpireCraft.Scripts.Regimes.RegimeType.NomadFeudalism;
    }

    // 记雄主的地方：统一者是帝国(核心)时它的帝国数据；不是帝国就没有雄主可记
    private static EmpireData HeroState(Kingdom sovereign)
    {
        Empire empire = sovereign?.GetEmpire();
        return empire?.data != null && !empire.isRekt() && empire.CoreKingdom == sovereign ? empire.data : null;
    }

    // 记下的雄主还在位：仍是这个帝国的君主，仍是开国雄主或中兴雄主
    private static bool HeroReigns(Kingdom sovereign)
    {
        EmpireData state = HeroState(sovereign);
        if (state == null || state.nomad_hero_id < 0L) return false;
        Actor emperor = sovereign.GetEmpire().Emperor;
        return emperor != null && emperor.isAlive() && emperor.id == state.nomad_hero_id && RulerTraitSystem.FounderReigns(sovereign);
    }

    // 发动统一战争时记下在位的雄主
    private static void RememberHero(Kingdom sovereign)
    {
        EmpireData state = HeroState(sovereign);
        Actor hero = sovereign.GetEmpire()?.Emperor;
        if (state == null || hero == null || !hero.isAlive()) return;
        state.nomad_hero_id = hero.id;
        state.nomad_hero_name = hero.getName() ?? "";
    }

    // 记下的雄主不在位了：统一没打完的当场中辍，统一后还在开国气象里的气象就此散了；
    // 留下"雄主之后第一次换汗必乱"(见 TakeHeroTurmoil)
    private static void HeroFell(Empire empire)
    {
        EmpireData state = empire.data;
        Kingdom core = empire.CoreKingdom;
        string hero = state.nomad_hero_name ?? "";
        state.nomad_hero_id = -1L;
        if (!Alive(core) || World.world == null) return;
        double now = World.world.getCurWorldTime();
        state.nomad_hero_fell_at = now;
        var data = core.GetOrCreate();
        string name = core.GetKingdomName();
        if (data.unify_since >= 0d)
        {
            int years = UnifyYears(core);
            Halt(core);
            Record(core, "unify_hero_fell_history", name, hero);
            LogService.LogInfo($"[EmpireCraft][天下大势] {name} 失了雄主{hero}(统一战争第{years}年)，统一中辍，打着的统一战争改名");
            return;
        }
        if (data.unified_at >= 0d && now >= data.unified_at && Date.getYearsSince(data.unified_at) < NomadGraceYears)
        {
            Record(core, "unify_hero_grace_end_history", name, hero);
            LogService.LogInfo($"[EmpireCraft][天下大势] {name} 失了雄主{hero}，统一后第{Date.getYearsSince(data.unified_at)}年开国气象结束");
            return;
        }
        LogService.LogInfo($"[EmpireCraft][天下大势] {name} 失了雄主{hero}({(data.unified_at >= 0d ? "统一后开国气象已过" : "统一未成")})，下回换汗必乱");
    }

    // 统一中辍：不再算统一者；它打着的统一战争照年度清理的规矩改名(见 RetagStaleWars)，兵败如山倒作罢。
    // 只动它自己发动的仗——年度结算中途别家的对手表还没重建，不能整个世界一起改
    private static void Halt(Kingdom unifier)
    {
        unifier.GetOrCreate().unify_since = -1d;
        Stalled.Remove(unifier.id);
        Rivals.Remove(unifier.id);
        FrontCollapse.Release(unifier);
        foreach (War war in unifier.getWars().ToList())
        {
            if (war?.data == null || war.hasEnded() || war.GetEmpireWarType() != EmpireWarType.统一 ||
                Sovereign(war.getMainAttacker()) != unifier) continue;
            Retag(war);
        }
    }

    // 游牧统一者没有雄主在位了就中辍，返回 true(见文件头游牧难一统)：记下的雄主不在位按 HeroFell 收场；
    // 旧档里打着统一战争却没记下雄主的，雄主在位就补记，不在就中辍
    private static bool HeroLost(Kingdom unifier)
    {
        if (!IsNomad(unifier)) return false;
        EmpireData state = HeroState(unifier);
        if (state != null && state.nomad_hero_id >= 0L)
        {
            if (HeroReigns(unifier)) return false;
            HeroFell(unifier.GetEmpire());
            return true;
        }
        if (state != null && RulerTraitSystem.FounderReigns(unifier))
        {
            RememberHero(unifier);
            return false;
        }
        int years = UnifyYears(unifier);
        Halt(unifier);
        Record(unifier, "unify_no_hero_history", unifier.GetKingdomName());
        LogService.LogInfo($"[EmpireCraft][天下大势] {unifier.GetKingdomName()} 游牧无雄主在位(统一战争第{years}年)，统一中辍");
        return true;
    }

    // 每月：各帝国记下的游牧雄主还在不在位，不在了按 HeroFell 收场；统一满十年雄主还在位的，开国气象到期记一行日志
    private static void WatchHeroes()
    {
        if (ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.data.nomad_hero_id < 0L) continue;
            Kingdom core = empire.CoreKingdom;
            if (!Alive(core)) continue;
            // 不再是游牧政权(改制、入主后汉化)：照别的政体的规矩，记下的雄主作罢
            if (!IsNomad(core))
            {
                empire.data.nomad_hero_id = -1L;
                continue;
            }
            if (!HeroReigns(core))
            {
                HeroFell(empire);
                continue;
            }
            var data = core.GetOrCreate();
            if (data.unify_since >= 0d || data.unified_at < 0d || World.world.getCurWorldTime() < data.unified_at ||
                Date.getYearsSince(data.unified_at) < NomadGraceYears ||
                GraceEndLogged.TryGetValue(core.id, out double logged) && logged == data.unified_at) continue;
            GraceEndLogged[core.id] = data.unified_at;
            LogService.LogInfo($"[EmpireCraft][天下大势] {core.GetKingdomName()} 统一满{NomadGraceYears}年，开国气象结束(雄主{empire.data.nomad_hero_name}仍在位)");
        }
    }

    // 新汗即位时问(见 NomadCycleSystem.OnKhanSucceeded)：是不是雄主之后第一次汗位更替。记下的雄主这才不在位的先按 HeroFell 收场
    // (统一中辍、气象散)；问过就清掉，之后的汗位更替照常。hero 是那位雄主的名字
    public static bool TakeHeroTurmoil(Empire empire, out string hero)
    {
        hero = "";
        if (empire?.data == null || empire.isRekt() || World.world == null) return false;
        EmpireData state = empire.data;
        if (state.nomad_hero_id < 0L && state.nomad_hero_fell_at < 0d) return false;
        Kingdom core = empire.CoreKingdom;
        // 不再是游牧政权：照别的政体的规矩
        if (!Alive(core) || !IsNomad(core))
        {
            state.nomad_hero_id = -1L;
            state.nomad_hero_fell_at = -1d;
            return false;
        }
        if (state.nomad_hero_id >= 0L && !HeroReigns(core)) HeroFell(empire);
        if (state.nomad_hero_fell_at < 0d) return false;
        state.nomad_hero_fell_at = -1d;
        hero = state.nomad_hero_name ?? "";
        return true;
    }

    // 游牧无雄主在位、本可发动统一战争的(诊断，每家 NoHeroLogYears 年一行)
    private static void LogNoHero(Kingdom top, Realm realm, float share)
    {
        double now = World.world.getCurWorldTime();
        if (NoHeroLogged.TryGetValue(top.id, out double at) && now >= at && Date.getYearsSince(at) < NoHeroLogYears) return;
        NoHeroLogged[top.id] = now;
        LogService.LogInfo($"[EmpireCraft][天下大势] {realm.line}：{top.GetKingdomName()} 据有{share:P0}，游牧无雄主在位，不能发动统一战争");
    }

    #endregion

    #region 天下

    // 文化圈：按城市实际的文化分，不按制度线——制度线是借用的科技树，一条线下有好几种文化
    // (游牧线有游牧、阿兹特克、奥吉布瓦，罗马线有维京、斯拉夫、法兰克……)。玩家反馈 2026-10-17：
    // 汗国把周围一城一国的别的文化当成同一个天下打统一战争——跨文化的扩张走游牧扩张、寻常战争。
    // 文化配置里标了同一天下标识的(华夏、中国、山海都标 Huaxia)合在一起
    private static string LineOf(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return null;
        try
        {
            return culture.GetTianxiaId();
        }
        catch
        {
            return culture;
        }
    }

    private static object IslandOf(WorldTile tile) => tile?.region?.island;

    // 天下(一个文化圈里连成一片的城)：各家占的城；members = 都城在这片里的各家(天下的人)
    private sealed class Realm
    {
        public string line;
        // 按天下(帝国核心)划的：主核心；按文化连片的为空
        public EmpireCore lead;
        public int total;
        public readonly Dictionary<Kingdom, int> held = new();
        public readonly HashSet<City> cities = new();
        public readonly HashSet<Kingdom> members = new();
    }

    // 天下(见 TianxiaSystem)：有帝国核心的城按核心所在的天下归拢(法理，不论文化、不论连不连片)；
    // 还没有帝国核心的城照旧按文化连片
    private const int MinCoreCities = 5;

    // 还没有帝国核心、按文化连片的天下(天下图层上显示，见 TianxiaMap)：每年结算时记下，城 → 第几片
    private static readonly Dictionary<long, int> UncoredOf = new();
    private static readonly List<List<City>> Uncored = new();
    private static object _uncoredWorld;
    // 每年重算一次就加一(图层按它清掉缓存的称呼)
    public static int UncoredVersion { get; private set; }

    public static List<City> UncoredRealmOf(City city)
    {
        if (city?.data == null || !ReferenceEquals(_uncoredWorld, World.world) ||
            !UncoredOf.TryGetValue(city.data.id, out int index)) return null;
        return Uncored[index];
    }

    public static IEnumerable<List<City>> UncoredRealms() =>
        ReferenceEquals(_uncoredWorld, World.world) ? Uncored : Enumerable.Empty<List<City>>();

    // 零星城(用户 2026-10-19 选"并入相邻天下"；玩家："这两个城市死活算不进天下里面")：法理不在任何帝国核心、
    // 同一文化圈里又连不成八城一片的城，以前两头不算——不在核心的天下里，也凑不成"未立核心"的天下。
    // 现在算进与它接壤(没有就同一块陆地上相近)的同一文化圈天下：计入天下城数、图层着色、统一战争；法理不动。每年结算时重算
    private static readonly Dictionary<long, long> OrphanOf = new();
    private static object _orphanWorld;

    public static EmpireCore OrphanLeadOf(City city)
    {
        if (city?.data == null || OrphanOf.Count == 0 || !ReferenceEquals(_orphanWorld, World.world) ||
            !OrphanOf.TryGetValue(city.data.id, out long leadId)) return null;
        EmpireCore lead = EmpireCoreManager.Get(leadId);
        return lead != null && TianxiaSystem.Lead(lead) == lead ? lead : null;
    }

    // 一片零星城该并进哪个天下：同一文化圈的天下里，与这片接壤的城最多的；都不接壤就看同一块陆地上相近的
    private static EmpireCore NeighbourLead(List<City> group, string line, Dictionary<EmpireCore, List<City>> byLead)
    {
        var score = new Dictionary<EmpireCore, int>();
        foreach (City city in group)
            foreach (City next in city.neighbours_cities ?? new HashSet<City>())
            {
                EmpireCore lead = TianxiaSystem.CoreLeadOf(next);
                if (lead == null || !byLead.ContainsKey(lead) || !SameLine(lead, line)) continue;
                score.TryGetValue(lead, out int count);
                score[lead] = count + 1;
            }
        if (score.Count == 0)
            foreach (KeyValuePair<EmpireCore, List<City>> pair in byLead)
            {
                if (!SameLine(pair.Key, line)) continue;
                int links = group.Sum(city => pair.Value.Count(other => Linked(city, other)));
                if (links > 0) score[pair.Key] = links;
            }
        return score.Count == 0 ? null : score.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.id).First().Key;
    }

    private static bool SameLine(EmpireCore lead, string line) =>
        TianxiaSystem.Members(lead).Any(core => LineOf(core.default_culture) == line);

    private static void RememberUncored(List<Realm> realms)
    {
        UncoredOf.Clear();
        Uncored.Clear();
        _uncoredWorld = World.world;
        UncoredVersion++;
        foreach (Realm realm in realms)
        {
            if (realm.lead != null) continue;
            var cities = realm.cities.ToList();
            foreach (City city in cities)
                if (city?.data != null) UncoredOf[city.data.id] = Uncored.Count;
            Uncored.Add(cities);
        }
    }

    private static List<Realm> Survey()
    {
        var byLine = new Dictionary<string, List<City>>();
        var byLead = new Dictionary<EmpireCore, List<City>>();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild) continue;
            EmpireCore lead = TianxiaSystem.CoreLeadOf(city);
            if (lead != null)
            {
                if (!byLead.TryGetValue(lead, out List<City> cored)) byLead[lead] = cored = new List<City>();
                cored.Add(city);
                continue;
            }
            if (IslandOf(city.getTile()) == null) continue;
            string line = LineOf(CultureService.GetMainCulture(city, false));
            if (line == null) continue;
            if (!byLine.TryGetValue(line, out List<City> list)) byLine[line] = list = new List<City>();
            list.Add(city);
        }
        var realms = new List<Realm>();
        var uncored = new List<Realm>();
        var queue = new Queue<City>();
        foreach (KeyValuePair<string, List<City>> pair in byLine)
        {
            // 同一条线的城按接壤、相近连成一片一片
            var remaining = new List<City>(pair.Value);
            while (remaining.Count > 0)
            {
                var realm = new Realm { line = pair.Key };
                City start = remaining[remaining.Count - 1];
                remaining.RemoveAt(remaining.Count - 1);
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    City city = queue.Dequeue();
                    realm.cities.Add(city);
                    for (int i = remaining.Count - 1; i >= 0; i--)
                    {
                        if (!Linked(city, remaining[i])) continue;
                        queue.Enqueue(remaining[i]);
                        remaining[i] = remaining[remaining.Count - 1];
                        remaining.RemoveAt(remaining.Count - 1);
                    }
                }
                uncored.Add(realm);
            }
        }
        // 连不成八城一片的零星城并进相邻的同一文化圈天下(见 OrphanLeadOf)
        OrphanOf.Clear();
        _orphanWorld = World.world;
        foreach (Realm realm in uncored)
        {
            if (realm.cities.Count >= MinCities) continue;
            List<City> group = realm.cities.ToList();
            EmpireCore target = NeighbourLead(group, realm.line, byLead);
            if (target == null) continue;
            byLead[target].AddRange(group);
            foreach (City city in group) OrphanOf[city.data.id] = target.id;
        }
        foreach (KeyValuePair<EmpireCore, List<City>> pair in byLead)
        {
            var realm = new Realm { line = TianxiaSystem.Name(pair.Key), lead = pair.Key };
            foreach (City city in pair.Value) realm.cities.Add(city);
            Tally(realm);
            // 太小的天下不打统一战争，天命照样结算
            if (pair.Value.Count >= MinCoreCities) realms.Add(realm);
            else TianxiaSystem.Settle(realm.lead, realm.held, realm.total, realm.line);
        }
        foreach (Realm realm in uncored)
        {
            if (realm.cities.Count < MinCities) continue;
            Tally(realm);
            realms.Add(realm);
        }
        if (OrphanOf.Count > 0) LogService.LogInfo($"[EmpireCraft][天下大势] {OrphanOf.Count} 座零星城并入相邻天下");
        return realms;
    }

    private static void Tally(Realm realm)
    {
        realm.total = realm.cities.Count;
        foreach (City city in realm.cities)
        {
            Kingdom holder = Sovereign(city.kingdom);
            if (!Alive(holder)) continue;
            realm.held.TryGetValue(holder, out int count);
            realm.held[holder] = count + 1;
        }
        // 天下一家：都城在这片天下里的，连同据有四分之一以上的外族(都城在草原也算——外族入主可受天命，本族可光复)
        foreach (KeyValuePair<Kingdom, int> pair in realm.held)
            if (pair.Key.capital != null && realm.cities.Contains(pair.Key.capital) ||
                realm.lead != null && pair.Value >= realm.total * ForeignMemberShare) realm.members.Add(pair.Key);
    }

    // 两座城连成一片：领地接壤，或同一块陆地上城心相距不远
    private static bool Linked(City a, City b)
    {
        // 不跨海(用户："跨大陆跨海洋就算了")：隔海接壤的也不连
        WorldTile ta = a.getTile(), tb = b.getTile();
        if (ta == null || tb == null || IslandOf(ta) != IslandOf(tb)) return false;
        if (a.neighbours_cities != null && a.neighbours_cities.Contains(b)) return true;
        return Toolbox.SquaredDistTile(ta, tb) <= LinkTiles * LinkTiles;
    }

    // 这一家是这个天下的人(都城在这片里)：只有它们能做统一者、是统一的对象
    private static bool Belongs(Kingdom sovereign, Realm realm) => Alive(sovereign) && realm.members.Contains(sovereign);

    // 两家接壤(领地相邻，或隔海相望，见 StraitReach)。各家的接壤表每帧建一次(以前每问一次就把全世界的城扫一遍，
    // 统一者对每个对手各问一次)
    private static readonly Dictionary<Kingdom, HashSet<Kingdom>> Adjacency = new();
    private static int _adjacencyFrame = -1;

    private static bool Borders(Kingdom sovereign, Kingdom rival)
    {
        if (_adjacencyFrame != UnityEngine.Time.frameCount)
        {
            _adjacencyFrame = UnityEngine.Time.frameCount;
            Adjacency.Clear();
            foreach (City city in World.world.cities.list)
            {
                if (city?.data == null || city.isRekt() || city.neighbours_cities == null || city.kingdom == null) continue;
                Kingdom own = Sovereign(city.kingdom);
                if (own == null) continue;
                foreach (City next in city.neighbours_cities)
                {
                    if (next?.kingdom == null || next.isRekt()) continue;
                    Kingdom other = Sovereign(next.kingdom);
                    if (other == null || other == own) continue;
                    if (!Adjacency.TryGetValue(own, out HashSet<Kingdom> set)) Adjacency[own] = set = new HashSet<Kingdom>();
                    set.Add(other);
                }
            }
        }
        return Adjacency.TryGetValue(sovereign, out HashSet<Kingdom> near) && near.Contains(rival) ||
               StraitReach.Across(sovereign, rival);
    }

    private static bool Exempt(Kingdom sovereign) => ZhouEraSystem.SphereOf(sovereign) != null;

    #endregion

    #region 结算

    // 每帧(EmpireCraftStrategicScheduler)；按世界时间每月、每年各做一次
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.cities == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, world))
        {
            ResetWorldState();
            _world = world;
        }
        double now = world.getCurWorldTime();
        try
        {
            if (_lastYear < 0d || now < _lastYear || Date.getYearsSince(_lastYear) >= 1)
            {
                _lastYear = now;
                // 先把城记的核心按法理对齐，天下才认得全(见 EmpireCoreManager.ReconcileCityCores)
                EmpireCoreManager.ReconcileCityCores();
                Yearly();
            }
            if (_lastMonth < 0d || now < _lastMonth || Date.getMonthsSince(_lastMonth) >= 1)
            {
                _lastMonth = now;
                Monthly();
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][天下大势] 结算失败: {exception.Message}");
        }
    }

    private static void Yearly()
    {
        // 称帝、开战卡在哪一关(诊断，只记日志)
        FormationDiagnostics.Yearly();
        Rivals.Clear();
        double now = World.world.getCurWorldTime();
        // 天下自动并分(按文化配置的天下标识，见 TianxiaSystem.AutoArrange)
        TianxiaSystem.AutoArrange();
        List<Realm> realms = Survey();
        RememberUncored(realms);
        Diagnose(realms);
        var tracked = new HashSet<Kingdom>();
        foreach (Realm realm in realms)
        {
            // 天命：据有天下七成五受天命，跌破一半失天命(天下无主，朝廷不解体)
            if (realm.lead != null) TianxiaSystem.Settle(realm.lead, realm.held, realm.total, realm.line);
            List<Kingdom> powers = realm.members.ToList();
            Kingdom unifier = powers.FirstOrDefault(holder => holder.GetOrCreate().unify_since >= 0d);
            if (unifier != null)
            {
                // 游牧统一者没了雄主：统一中辍(见文件头游牧难一统)
                if (HeroLost(unifier)) continue;
                tracked.Add(unifier);
                float share = realm.held[unifier] / (float)realm.total;
                bool alone = powers.All(holder => holder == unifier || Exempt(holder));
                var data = unifier.GetOrCreate();
                if (share >= UnifiedShare || alone)
                {
                    data.unify_since = -1d;
                    data.unified_at = now;
                    Record(unifier, "unify_done_history", unifier.GetKingdomName());
                    if (realm.lead != null) TianxiaSystem.Grant(realm.lead, unifier, realm.held[unifier], realm.total, realm.line);
                    continue;
                }
                double strongest = powers.Where(holder => holder != unifier)
                    .Select(holder => WarSituation.Power(holder)).DefaultIfEmpty(0d).Max();
                if (UnifyYears(unifier) >= MaxUnifyYears && share >= LateUnifiedShare)
                {
                    data.unify_since = -1d;
                    data.unified_at = now;
                    Record(unifier, "unify_done_history", unifier.GetKingdomName());
                    if (realm.lead != null) TianxiaSystem.Grant(realm.lead, unifier, realm.held[unifier], realm.total, realm.line);
                    continue;
                }
                bool overtaken = powers.Any(holder => holder != unifier && realm.held[holder] > realm.held[unifier]);
                if (share < CollapsedShare || share < LostShare && WarSituation.Power(unifier) < strongest || overtaken ||
                    UnifyYears(unifier) >= MaxUnifyYears)
                {
                    data.unify_since = -1d;
                    Record(unifier, "unify_lost_history", unifier.GetKingdomName());
                    continue;
                }
                Rivals[unifier.id] = Opponents(unifier, realm);
                // 够不着天下里的别家、也没在打(中间隔着别的文化的国家)：连着三年就算统一了能统一的
                bool engaged = Rivals[unifier.id].Any(rival => unifier.isInWarWith(rival) || Borders(unifier, rival));
                Stalled.TryGetValue(unifier.id, out int stalled);
                Stalled[unifier.id] = stalled = engaged ? 0 : stalled + 1;
                if (stalled >= StallYears)
                {
                    Stalled.Remove(unifier.id);
                    Rivals.Remove(unifier.id);
                    data.unify_since = -1d;
                    data.unified_at = now;
                    Record(unifier, "unify_done_history", unifier.GetKingdomName());
                    if (realm.lead != null) TianxiaSystem.Grant(realm.lead, unifier, realm.held[unifier], realm.total, realm.line);
                    continue;
                }
                Submissions(unifier);
                continue;
            }
            // 乱世(两家以上)：看有没有天命所归的
            var ranked = powers.Where(holder => !Exempt(holder)).OrderByDescending(holder => realm.held[holder]).ToList();
            if (ranked.Count < MinContenders) continue;
            Kingdom top = ranked[0];
            // 还在开国气象里的统一者不再起统一战；气象过了，旧王朝(崩了以后的残部)也能重新问鼎——以前"统一过"的标记永远不清，
            // 残部仍是最大一家时谁都发不起统一战，天下永远乱世
            if (InGrace(top)) continue;
            // 已受这个天下天命的不再起统一战(它就是天下共主)
            if (realm.lead != null && TianxiaSystem.MandateOf(top) == realm.lead) continue;
            float topShare = realm.held[top] / (float)realm.total;
            double topPower = WarSituation.Power(top);
            double second = ranked.Skip(1).Select(holder => WarSituation.Power(holder)).DefaultIfEmpty(0d).Max();
            bool leads = topShare >= ContenderShare || topShare >= PowerLeadMinShare && topPower >= second * PowerLead;
            if (!leads) continue;
            // 天下有主(治世、动荡)：别家不能问鼎，只有乱世才有统一战争(见 TianxiaSystem 天命周期)
            if (realm.lead != null && TianxiaSystem.BarsContender(realm.lead, top)) continue;
            // 游牧政权只有雄主在位才能发动统一战争，发动时记下这位雄主(见文件头游牧难一统)
            bool nomad = IsNomad(top);
            if (nomad && !RulerTraitSystem.FounderReigns(top))
            {
                LogNoHero(top, realm, topShare);
                continue;
            }
            top.GetOrCreate().unify_since = now;
            tracked.Add(top);
            if (nomad)
            {
                RememberHero(top);
                LogService.LogInfo($"[EmpireCraft][天下大势] {realm.line}：游牧 {top.GetKingdomName()} 雄主{HeroState(top)?.nomad_hero_name}在位，发动统一战争");
            }
            Record(top, "unify_start_history", top.GetKingdomName(), $"{topShare:P0}");
            Rivals[top.id] = Opponents(top, realm);
        }
        // 天下散了(都城不在任何一片天下里、天下不到八座城)的统一者：统一中辍，不再算在统一战争里
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (!Alive(kingdom) || tracked.Contains(kingdom)) continue;
            var data = kingdom.GetOrCreate();
            if (data.unify_since >= 0d) data.unify_since = -1d;
        }
        RetagStaleWars();
    }

    // 已不在同一天下的"统一战争"(旧存档跨文化开的、天下散了的)改名：游牧政权为游牧扩张，别的为帝国扩张
    private static void RetagStaleWars()
    {
        foreach (War war in World.world.wars.list.ToList())
        {
            if (war?.data == null || war.hasEnded() || war.GetEmpireWarType() != EmpireWarType.统一 || IsUnificationWar(war))
                continue;
            Retag(war);
        }
    }

    private static void Retag(War war)
    {
        Kingdom attacker = war.getMainAttacker();
        if (attacker == null) return;
        bool nomad = attacker.GetRegime()?.type == EmpireCraft.Scripts.Regimes.RegimeType.YouMu;
        war.SetEmpireWarType(nomad ? EmpireWarType.游牧扩张 : EmpireWarType.帝国扩张);
    }

    // 天下里的别家(都城在这片里的独立国家、别的帝国)，按都城离统一者都城远近排
    private static List<Kingdom> Opponents(Kingdom unifier, Realm realm)
    {
        WorldTile home = unifier.capital?.getTile();
        return realm.members.Where(holder => holder != unifier && !Exempt(holder))
            .OrderBy(holder => home == null || holder.capital?.getTile() == null ? float.MaxValue
                : Toolbox.SquaredDistTile(home, holder.capital.getTile()))
            .ToList();
    }

    // 望风归附：只剩一两座城、正与统一者交战、兵力被碾压、主力确已被打垮的，每年有几率举国归附，统一十年后几率加倍
    // (帝国不归附，要打)
    private static void Submissions(Kingdom unifier)
    {
        if (!Rivals.TryGetValue(unifier.id, out List<Kingdom> rivals)) return;
        float ours = Army(unifier).strength;
        if (ours <= 0f) return;
        bool late = UnifyYears(unifier) >= LateYears;
        foreach (Kingdom rival in rivals.ToList())
        {
            if (!Alive(rival) || rival.IsEmpire() || (rival.cities?.Count ?? 0) > SubmitMaxCities ||
                !unifier.isInWarWith(rival)) continue;
            (float strength, float full) = Army(rival);
            if (strength > ours * CrushRatio || full <= 0f || strength > full * BrokenShare) continue;
            float chance = Mathf.Min(SubmitCap, SubmitChance * (late ? 2f : 1f));
            if (UnityEngine.Random.value >= chance) continue;
            Annex(unifier, rival, null, "unify_submit_history");
        }
    }

    // 一家的正规军(帝国连同各成员国)：现有兵力与满编；没有军团制时按士兵与历来最多的士兵
    private static (float strength, float full) Army(Kingdom sovereign)
    {
        IEnumerable<Kingdom> members = sovereign.GetEmpire()?.kingdoms_list?.Where(Alive) ?? new[] { sovereign };
        if (CorpsSystem.Enabled)
        {
            var regular = members.SelectMany(member => CorpsSystem.Of(member)).Where(corps => !corps.militia && corps.order_id < 0).ToList();
            return (regular.Sum(corps => Mathf.Max(0f, corps.strength)), regular.Sum(corps => Mathf.Max(0f, corps.full)));
        }
        return (members.Sum(member => member.countTotalWarriors()), members.Sum(member => member.GetOrCreate().peak_warriors));
    }

    private static void Monthly()
    {
        FrontCollapse.Prune();
        // 游牧雄主还在不在位(见文件头游牧难一统)
        WatchHeroes();
        foreach (Kingdom unifier in World.world.kingdoms.list.Where(kingdom => Alive(kingdom) &&
                     kingdom.GetOrCreate().unify_since >= 0d && Sovereign(kingdom) == kingdom).ToList())
        {
            if (HeroLost(unifier)) continue;
            // 正规军打垮、又丢了都城或三分之一的城：兵败如山倒(见 FrontCollapse)
            foreach (War war in unifier.getWars().ToList())
            {
                if (war?.data == null || war.hasEnded() || !IsUnificationWar(war)) continue;
                Kingdom attacker = Sovereign(war.getMainAttacker());
                Kingdom foe = attacker == unifier ? Sovereign(war.getMainDefender()) : attacker;
                if (!Alive(foe) || foe == unifier) continue;
                FrontCollapse.Check(unifier, foe, war);
            }
            Declare(unifier);
        }
    }

    // 开战：手上的统一战争不到四场、养得起兵，就挑接壤(或隔海相望)的一家开战(目标吞并)，每月至多一场；不隔着别国远征
    private static void Declare(Kingdom unifier)
    {
        if (!Rivals.TryGetValue(unifier.id, out List<Kingdom> rivals) || rivals.Count == 0) return;
        int wars = unifier.getWars().Count(war => IsUnificationWar(war));
        if (wars >= MaxWars || CorpsSystem.CannotFundWar(unifier) || WarGoalSystem.FightingForSurvival(unifier)) return;
        // 共事本部落联盟的部落不打(见 TribalFissionService.Bound)
        List<Kingdom> open = rivals.Where(rival => Alive(rival) && Sovereign(rival) == rival && !unifier.isInWarWith(rival) &&
                                                   !TribalFissionService.Bound(unifier, rival)).ToList();
        Kingdom target = open.FirstOrDefault(rival => Borders(unifier, rival));
        if (target == null) return;
        War war = World.world.diplomacy.startWar(unifier, target, WarTypeLibrary.normal);
        if (war?.data == null) return;
        war.SetEmpireWarType(EmpireWarType.统一);
        var data = war.GetOrCreate();
        data.war_goal = WarGoal.吞并;
        data.war_goal_capital_id = target.capital?.data != null ? target.capital.id : -1L;
    }

    // 举国并入统一者：结束两家的战争，对方(帝国连同各成员国)所有的城逐帧移交
    private static void Annex(Kingdom unifier, Kingdom foe, War war, string historyKey)
    {
        string name = foe.GetKingdomName();
        foreach (War between in foe.getWars().ToList())
        {
            if (between?.data == null || between.hasEnded()) continue;
            bool against = between.isAttacker(unifier) && between.isDefender(foe) || between.isAttacker(foe) && between.isDefender(unifier);
            if (!against) continue;
            World.world.wars.endWar(between, between.isAttacker(unifier) ? WarWinner.Attackers : WarWinner.Defenders);
        }
        // 部落之间不吞并：赔款，施行了部落联合的纳为共事部落(见 TribalWarSystem)
        if (TribalFissionService.IsTribal(foe) && TribalFissionService.IsTribal(unifier))
        {
            TribalWarSystem.Concede(foe, unifier);
            return;
        }
        IEnumerable<Kingdom> members = foe.GetEmpire()?.kingdoms_list?.Where(Alive).ToList() ?? (IEnumerable<Kingdom>)new[] { foe };
        int cities = members.Sum(member => WarGoalSystem.QueueAnnex(member, unifier));
        if (cities > 0) Record(unifier, historyKey, name, unifier.GetKingdomName());
    }

    // 每年一行诊断(看周期律对不对)：各天下的阶段、前三家占比、统一者第几年；各王朝的年岁、积弊、弊病、正统、开国气象
    private static void Diagnose(List<Realm> realms)
    {
        foreach (Realm realm in realms.OrderByDescending(realm => realm.total).Take(4))
        {
            var top = realm.held.OrderByDescending(pair => pair.Value).Take(3)
                .Select(pair => $"{pair.Key.GetKingdomName()} {pair.Value / (float)realm.total:P0}");
            Kingdom unifier = realm.members.FirstOrDefault(holder => holder.GetOrCreate().unify_since >= 0d);
            string phase = unifier != null ? $"统一战争 {unifier.GetKingdomName()} 第{UnifyYears(unifier)}年"
                : realm.members.Any(InGrace) ? "治世(开国气象)" : "乱世/常态";
            LogService.LogInfo($"[EmpireCraft][天下大势] {realm.line} {realm.total}城 {realm.members.Count}家(另有外家{realm.held.Count - realm.members.Count})：{phase}；{string.Join("、", top)}");
        }
        var dynasties = World.world.kingdoms.list.Select(kingdom => kingdom?.GetEmpire()).Where(empire => empire != null &&
                !empire.isRekt() && !empire.IsArchived() && empire.data != null).Distinct()
            .Select(empire => $"{empire.GetEmpireName()} {DynasticCycleSystem.GetAge(empire)}年 积弊{DynasticCycleSystem.GetPressure(empire):0} " +
                              $"弊病{MaladySystem.Average(empire):0} 正统{empire.Legitimacy}{(empire.InFoundingGrace ? " 开国气象" : "")}")
            .ToList();
        if (dynasties.Count > 0) LogService.LogInfo($"[EmpireCraft][王朝周期] {string.Join("；", dynasties)}");
    }

    private static void Record(Kingdom kingdom, string key, params object[] args)
    {
        string text = string.Format(LM.Get(key), args);
        Empire empire = kingdom.GetEmpire();
        if (empire != null && !empire.isRekt()) EventRecorder.Record(empire, text, null, kingdom);
        else EventRecorder.Record(kingdom, text);
        TranslateHelper.LogEventMessage(text, kingdom);
    }

    #endregion
}

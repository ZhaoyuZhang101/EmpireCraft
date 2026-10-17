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
//       - 把对方正规军(军团；没有军团时看士兵与可征之兵)打光、连续两个月没缓过来，就举国吞并，不等和约；
//       - 望风归附(用户 2026-10-14："兵力没有达到绝对碾压之前、主力还没打完就望风而降")：统一者正规军是对方五倍以上，
//         而且对方主力已被打垮(正规军剩不到满编三成，或不到统一者十分之一)，才每年有几率举国归附，统一十年后几率加倍；
//       - 战线推进加快一半，十年后加倍(见 FrontSystem.PushFactor)；
//       - 统一者处在开国气象里(见 InGrace)：积弊不涨、不起事、不分裂；
//   · 统一完成(占到九成，或天下再没有别的独立势力)：开国气象延续三十年；之后积弊按王朝年岁慢慢沉积(见 MaladySystem.Pace)，
//     末世崩溃，天下重回乱世，再出新的统一者。一个王朝只统一一次(改朝换代才能再来，见 OnDynastyChanged)；
//   · 天命已失、统一中辍：地盘跌到 6% 以下，或不到两成、而且已不是天下国力最强的一家；天下里有别家的地盘比统一者还大；
//     或打满三十年还没统一(玩家日志 2026-10-16：有的统一战争打了一百七十多年，一直处在开国气象里，不起事、不分裂)——
//     打满三十年时地盘过半算统一了(能统一的都统一了)，不到一半就中辍。
//   · 外族入主(用户 2026-10-17 选"外族入主可受天命、本族可光复")：都城在天下以外、据有有帝国法理的天下三成以上或受着它天命的，
//     也算天下的一家(都城在草原也算)，能做统一者、受天命；统一者吞并它时只收它在天下里的城。外族受着天命而天命动摇、
//     或刚失了天命仍占着天下时，本族最强的一家起兵光复(统一战争，不按"地盘不到两成、被人超过"判中辍)。
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
    // 望风归附：兵力碾压(对方正规军不到五分之一)、对方主力已垮(剩不到满编三成，或不到十分之一)
    private const float CrushRatio = 0.2f, BrokenShare = 0.3f, RoutRatio = 0.1f, SubmitChance = 0.1f, SubmitCap = 0.35f;
    private const float PushBonus = 1.5f, LatePushBonus = 2f;
    // 正规军打光：连续几个月(月度检查)没有正规军才吞并(打光后下个月又征起来的不算)
    private const int WipeMonths = 2;
    // 没有军团制时：士兵连同可征之兵不过这么多算打光
    private const int WipedWarriors = 3;

    private static object _world;
    private static double _lastMonth = -1d, _lastYear = -1d;
    private static readonly Dictionary<long, int> Wiped = new();
    // 统一者连着几年够不着天下里的任何一家(也没在打)
    private static readonly Dictionary<long, int> Stalled = new();
    private const int StallYears = 3;
    // 上次年度统计的天下：统一者 → 它天下里的别家(按都城距离排好)
    private static readonly Dictionary<long, List<Kingdom>> Rivals = new();
    // 统一者 → 它在争的天下的主核心(按文化连片的为空)
    private static readonly Dictionary<long, EmpireCore> Arenas = new();

    public static void ResetWorldState()
    {
        _world = null;
        _lastMonth = _lastYear = -1d;
        Wiped.Clear();
        Stalled.Clear();
        Rivals.Clear();
        Arenas.Clear();
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

    // 开国气象：统一战争期间、统一后三十年(见 Empire.InFoundingGrace、RebellionSystem.CanAttempt)。
    // 开国气象是治世的开头(用户 2026-10-17 选"以天命为准")：统一了有帝国法理的天下、受了天命的，要还受着天命、天下仍是治世——
    // 天命动摇、失了天命，开国气象就此过去(以前照样满三十年，地盘丢了大半也不起事、不分裂、积弊不涨)
    public static bool InGrace(Kingdom kingdom)
    {
        Kingdom sovereign = Sovereign(kingdom);
        if (!Alive(sovereign) || World.world == null) return false;
        var data = sovereign.GetOrCreate();
        if (data.unify_since >= 0d) return true;
        if (!RecentlyUnified(sovereign)) return false;
        return !data.unified_tianxia || TianxiaSystem.HoldsInPeace(sovereign);
    }

    // 统一天下不到三十年(不论天命如何；天命动摇的判定用它，免得统一时不到七成五的第二年就动荡)
    public static bool RecentlyUnified(Kingdom kingdom)
    {
        Kingdom sovereign = Sovereign(kingdom);
        if (!Alive(sovereign) || World.world == null) return false;
        var data = sovereign.GetOrCreate();
        return data.unified_at >= 0d && World.world.getCurWorldTime() >= data.unified_at &&
               Date.getYearsSince(data.unified_at) < GraceYears;
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

    // 同一文化圈(本国文化的天下标识相同，见 LineOf)
    public static bool SameSphere(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null) return false;
        string line = LineOf(CultureService.GetRealmCulture(a));
        return line != null && line == LineOf(CultureService.GetRealmCulture(b));
    }

    // 同一个天下(用户 2026-10-17 选"天下统一按帝国核心")：两国都城都在有帝国法理的天下里的，看是不是同一个天下；
    // 有一边还没有帝国法理的，临时按文化圈(天下标识)且在同一块陆地上算
    public static bool SameTianxia(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null) return false;
        EmpireCore la = TianxiaSystem.LeadOf(a.capital), lb = TianxiaSystem.LeadOf(b.capital);
        if (la != null && lb != null) return la == lb;
        object ia = IslandOf(a.capital?.getTile()), ib = IslandOf(b.capital?.getTile());
        return ia != null && ReferenceEquals(ia, ib) && SameSphere(a, b);
    }

    // 天下有主(治世、动荡)：这一家不能问鼎(谋求统一、统一战争)，共主自己除外(见 TianxiaSystem.BarsContender)
    public static bool Barred(Kingdom kingdom)
    {
        Kingdom sovereign = Sovereign(kingdom);
        EmpireCore lead = TianxiaSystem.LeadOf(sovereign?.capital);
        return lead != null && TianxiaSystem.BarsContender(lead, sovereign);
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
        data.unify_restoring = false;
        data.unified_at = -1d;
        data.unified_tianxia = false;
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
        // 都城在这片里的(members 另含入主的外族，见 Tally)
        public readonly HashSet<Kingdom> natives = new();
    }

    // 天下(见 TianxiaSystem)：有帝国核心的城按核心所在的天下归拢(法理，不论文化、不论连不连片)；
    // 还没有帝国核心的城照旧按文化连片
    private const int MinCoreCities = 5;

    private static List<Realm> Survey()
    {
        var byLine = new Dictionary<string, List<City>>();
        var byLead = new Dictionary<EmpireCore, List<City>>();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild) continue;
            EmpireCore lead = TianxiaSystem.LeadOf(city);
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
        foreach (KeyValuePair<EmpireCore, List<City>> pair in byLead)
        {
            var realm = new Realm { line = TianxiaSystem.Name(pair.Key), lead = pair.Key };
            foreach (City city in pair.Value) realm.cities.Add(city);
            Tally(realm);
            // 太小的天下不打统一战争，天命照样结算
            if (pair.Value.Count >= MinCoreCities) realms.Add(realm);
            else TianxiaSystem.Settle(realm.lead, realm.held, realm.total, realm.line);
        }
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
                if (realm.cities.Count < MinCities) continue;
                Tally(realm);
                realms.Add(realm);
            }
        }
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
        foreach (Kingdom holder in realm.held.Keys)
        {
            if (holder.capital != null && realm.cities.Contains(holder.capital)) realm.natives.Add(holder);
            if (realm.natives.Contains(holder) || Conqueror(realm, holder)) realm.members.Add(holder);
        }
    }

    // 入主的外族(用户 2026-10-17 选"外族入主可受天命"：都城在草原也算天下一家)：都城不在天下里，却据有有帝国法理的天下三成以上、
    // 或受着这个天下的天命——算天下的一家，能争天下，也是统一、光复的对象
    private static bool Conqueror(Realm realm, Kingdom holder) => realm.lead != null &&
        (realm.held[holder] >= realm.total * ContenderShare || TianxiaSystem.Holder(realm.lead) == holder);

    // 每一家在哪个天下争天下：都城所在的天下；都城不在任何天下里的入主外族，算它据有最多的那个
    private static Dictionary<Kingdom, Realm> Homes(List<Realm> realms)
    {
        var homes = new Dictionary<Kingdom, Realm>();
        foreach (Realm realm in realms)
        foreach (Kingdom native in realm.natives)
            homes[native] = realm;
        foreach (Realm realm in realms)
        foreach (Kingdom member in realm.members)
        {
            if (realm.natives.Contains(member)) continue;
            if (homes.TryGetValue(member, out Realm home) &&
                (home.natives.Contains(member) || home.held[member] >= realm.held[member])) continue;
            homes[member] = realm;
        }
        return homes;
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

    // 两家接壤(领地相邻，或隔海相望，见 StraitReach)
    private static bool Borders(Kingdom sovereign, Kingdom rival)
    {
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.neighbours_cities == null || city.kingdom == null) continue;
            if (Sovereign(city.kingdom) != sovereign) continue;
            foreach (City next in city.neighbours_cities)
                if (next?.kingdom != null && !next.isRekt() && Sovereign(next.kingdom) == rival) return true;
        }
        return StraitReach.Across(sovereign, rival);
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
        Arenas.Clear();
        double now = World.world.getCurWorldTime();
        // 天下自动并分(按文化配置的天下标识，见 TianxiaSystem.AutoArrange)
        TianxiaSystem.AutoArrange();
        List<Realm> realms = Survey();
        Diagnose(realms);
        Dictionary<Kingdom, Realm> homes = Homes(realms);
        var tracked = new HashSet<Kingdom>();
        foreach (Realm realm in realms)
        {
            // 天命：据有天下七成五受天命，跌破一半失天命(天下无主，朝廷不解体)
            if (realm.lead != null) TianxiaSystem.Settle(realm.lead, realm.held, realm.total, realm.line);
            List<Kingdom> powers = realm.members.ToList();
            // 只在自己的天下里争天下(入主的外族也是别处天下的一家时，不在这里当统一者)
            bool Home(Kingdom holder) => homes.TryGetValue(holder, out Realm home) && home == realm;
            Kingdom unifier = powers.FirstOrDefault(holder => Home(holder) && holder.GetOrCreate().unify_since >= 0d);
            if (unifier != null)
            {
                tracked.Add(unifier);
                float share = realm.held[unifier] / (float)realm.total;
                bool alone = powers.All(holder => holder == unifier || Exempt(holder));
                var data = unifier.GetOrCreate();
                // 光复之战：外族还占着天下时，不按"地盘不到两成、被人超过"判中辍(本来就是以小搏大)
                data.unify_restoring = data.unify_restoring && Restoring(realm, unifier, homes);
                if (share >= UnifiedShare || alone)
                {
                    Complete(unifier, realm, now);
                    continue;
                }
                double strongest = powers.Where(holder => holder != unifier)
                    .Select(holder => WarSituation.Power(holder)).DefaultIfEmpty(0d).Max();
                if (UnifyYears(unifier) >= MaxUnifyYears && share >= LateUnifiedShare)
                {
                    Complete(unifier, realm, now);
                    continue;
                }
                bool restoring = data.unify_restoring;
                bool overtaken = !restoring && powers.Any(holder => holder != unifier && realm.held[holder] > realm.held[unifier]);
                if (share < CollapsedShare || !restoring && share < LostShare && WarSituation.Power(unifier) < strongest || overtaken ||
                    UnifyYears(unifier) >= MaxUnifyYears)
                {
                    data.unify_since = -1d;
                    data.unify_restoring = false;
                    data.unify_ended_at = now;
                    Record(unifier, "unify_lost_history", unifier.GetKingdomName());
                    continue;
                }
                Track(unifier, realm);
                // 够不着天下里的别家、也没在打(中间隔着别的文化的国家)：连着三年就算统一了能统一的
                bool engaged = Rivals[unifier.id].Any(rival => unifier.isInWarWith(rival) || Borders(unifier, rival));
                Stalled.TryGetValue(unifier.id, out int stalled);
                Stalled[unifier.id] = stalled = engaged ? 0 : stalled + 1;
                // 光复时外族还占着天下，够不着也不算统一了(以前停滞三年就"统一"，白得三十年开国气象)
                if (stalled >= StallYears && !restoring)
                {
                    Stalled.Remove(unifier.id);
                    Rivals.Remove(unifier.id);
                    Complete(unifier, realm, now);
                    continue;
                }
                Submissions(unifier);
                continue;
            }
            // 光复(用户 2026-10-17 选"外族入主可受天命、本族可光复")：外族受着这个天下的天命、天命已动摇，本族最强的一家起兵
            Kingdom restorer = Restorer(realm, homes, now);
            if (restorer != null)
            {
                var data = restorer.GetOrCreate();
                data.unify_since = now;
                data.unify_restoring = true;
                tracked.Add(restorer);
                Record(restorer, "unify_restore_history", restorer.GetKingdomName(), Occupier(realm, homes)?.GetKingdomName() ?? "");
                Track(restorer, realm);
                continue;
            }
            // 乱世(两家以上)：看有没有天命所归的
            var ranked = powers.Where(holder => !Exempt(holder)).OrderByDescending(holder => realm.held[holder]).ToList();
            if (ranked.Count < MinContenders) continue;
            Kingdom top = ranked[0];
            if (!Home(top)) continue;
            // 还在开国气象里的统一者不再起统一战；气象过了，旧王朝(崩了以后的残部)也能重新问鼎——以前"统一过"的标记永远不清，
            // 残部仍是最大一家时谁都发不起统一战，天下永远乱世
            if (InGrace(top)) continue;
            // 已受这个天下天命的不再起统一战(它就是天下共主)
            if (realm.lead != null && TianxiaSystem.MandateOf(top) == realm.lead) continue;
            float topShare = realm.held[top] / (float)realm.total;
            // 本朝统一过、仍据有过半的不再起统一战(一个王朝只统一一次，衰落到不足一半才能中兴再统一)——
            // 没有天命的天下(太小、文化连片)以前开国气象一过、冒出个叛军就再统一一次，几乎永远在开国气象里
            if (top.GetOrCreate().unified_at >= 0d && topShare >= LateUnifiedShare) continue;
            double topPower = WarSituation.Power(top);
            double second = ranked.Skip(1).Select(holder => WarSituation.Power(holder)).DefaultIfEmpty(0d).Max();
            bool leads = topShare >= ContenderShare || topShare >= PowerLeadMinShare && topPower >= second * PowerLead;
            if (!leads) continue;
            // 天下有主(治世、动荡)：别家不能问鼎，只有乱世才有统一战争(见 TianxiaSystem 天命周期)
            if (realm.lead != null && TianxiaSystem.BarsContender(realm.lead, top)) continue;
            top.GetOrCreate().unify_since = now;
            tracked.Add(top);
            Record(top, "unify_start_history", top.GetKingdomName(), $"{topShare:P0}");
            Track(top, realm);
        }
        // 天下散了(都城不在任何一片天下里、天下不到八座城)的统一者：统一中辍，不再算在统一战争里
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (!Alive(kingdom) || tracked.Contains(kingdom)) continue;
            var data = kingdom.GetOrCreate();
            if (data.unify_since >= 0d) data.unify_since = -1d;
            data.unify_restoring = false;
        }
        RetagStaleWars();
    }

    // 记下统一者这一年在哪个天下打、天下里的别家
    private static void Track(Kingdom unifier, Realm realm)
    {
        Rivals[unifier.id] = Opponents(unifier, realm);
        Arenas[unifier.id] = realm.lead;
    }

    // 光复：外族(与天下主核心不是同一族类)占着天下——受着天命而天命已动摇；刚失了天命(三十年内)、仍是天下的一家；
    // 或都城在别处、据有天下最多。本族(都城在天下里、同族类)最强、据有天下 8% 以上、不在开国气象里、
    // 十年内没有中辍过统一或光复的一家起兵(外族还没受过这个天下的天命、都城又在这里时不算光复，照常争天下，
    // 入主的外族也能做统一者)
    private const float RestoreMinShare = 0.08f;
    private const int RestoreCooldownYears = 10, OccupiedYears = 30;

    private static Kingdom Restorer(Realm realm, Dictionary<Kingdom, Realm> homes, double now)
    {
        Kingdom ruler = Occupier(realm, homes);
        if (ruler == null) return null;
        bool Rested(Kingdom native)
        {
            double ended = native.GetOrCreate().unify_ended_at;
            return ended < 0d || now < ended || Date.getYearsSince(ended) >= RestoreCooldownYears;
        }
        return realm.natives.Where(native => native != ruler && !Exempt(native) && !TianxiaSystem.Foreign(realm.lead, native) &&
                                             homes.TryGetValue(native, out Realm home) && home == realm && !InGrace(native) &&
                                             realm.held[native] >= realm.total * RestoreMinShare && Rested(native))
            .OrderByDescending(native => WarSituation.Power(native)).FirstOrDefault();
    }

    private static Kingdom Occupier(Realm realm, Dictionary<Kingdom, Realm> homes)
    {
        if (realm.lead == null) return null;
        int phase = TianxiaSystem.Phase(realm.lead);
        Kingdom ruler = null;
        if (phase == TianxiaSystem.Unrest) ruler = TianxiaSystem.Holder(realm.lead);
        else if (phase == TianxiaSystem.Chaos)
        {
            EmpireCore lead = realm.lead;
            if (lead.mandate_former_kingdom_id >= 0 && lead.mandate_lost_at >= 0d &&
                Date.getYearsSince(lead.mandate_lost_at) < OccupiedYears)
                ruler = realm.members.FirstOrDefault(member => member.id == lead.mandate_former_kingdom_id);
            // 都城在别处、据有天下最多的外族(以前它挡着，本族谁都起不了统一战)
            Kingdom largest = realm.members.OrderByDescending(member => realm.held[member]).FirstOrDefault();
            if (ruler == null && largest != null && !(homes.TryGetValue(largest, out Realm home) && home == realm))
                ruler = largest;
        }
        return ruler != null && TianxiaSystem.Foreign(realm.lead, ruler) ? ruler : null;
    }

    // 还在光复：天下仍被别的外族占着
    private static bool Restoring(Realm realm, Kingdom unifier, Dictionary<Kingdom, Realm> homes)
    {
        Kingdom ruler = Occupier(realm, homes);
        return ruler != null && ruler != unifier;
    }

    // 统一完成：开国气象起算；有帝国法理的天下即受天命(TianxiaSystem.Grant)，受了天命的开国气象跟着天命走(见 InGrace)
    private static void Complete(Kingdom unifier, Realm realm, double now)
    {
        var data = unifier.GetOrCreate();
        data.unify_since = -1d;
        data.unify_restoring = false;
        data.unified_at = now;
        Record(unifier, "unify_done_history", unifier.GetKingdomName());
        if (realm.lead != null) TianxiaSystem.Grant(realm.lead, unifier, realm.held[unifier], realm.total, realm.line);
        data.unified_tianxia = realm.lead != null && TianxiaSystem.Holder(realm.lead) == unifier;
    }

    // 已不在同一天下的"统一战争"(旧存档跨文化开的、天下散了的)改名：游牧政权为游牧扩张，别的为帝国扩张
    private static void RetagStaleWars()
    {
        foreach (War war in World.world.wars.list.ToList())
        {
            if (war?.data == null || war.hasEnded() || war.GetEmpireWarType() != EmpireWarType.统一 || IsUnificationWar(war))
                continue;
            Kingdom attacker = war.getMainAttacker();
            if (attacker == null) continue;
            bool nomad = attacker.GetRegime()?.type == EmpireCraft.Scripts.Regimes.RegimeType.YouMu;
            war.SetEmpireWarType(nomad ? EmpireWarType.游牧扩张 : EmpireWarType.帝国扩张);
        }
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

    // 望风归附：兵力被碾压、主力已被打垮的，每年有几率举国归附，统一十年后几率加倍(帝国不归附，要打)
    private static void Submissions(Kingdom unifier)
    {
        if (!Rivals.TryGetValue(unifier.id, out List<Kingdom> rivals)) return;
        float ours = Army(unifier).strength;
        if (ours <= 0f) return;
        bool late = UnifyYears(unifier) >= LateYears;
        foreach (Kingdom rival in rivals.ToList())
        {
            if (!Alive(rival) || rival.IsEmpire()) continue;
            (float strength, float full) = Army(rival);
            if (strength > ours * CrushRatio) continue;
            bool broken = full > 0f && strength <= full * BrokenShare || strength <= ours * RoutRatio;
            if (!broken) continue;
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
        foreach (Kingdom unifier in World.world.kingdoms.list.Where(kingdom => Alive(kingdom) &&
                     kingdom.GetOrCreate().unify_since >= 0d && Sovereign(kingdom) == kingdom).ToList())
        {
            // 正规军打光的举国吞并
            foreach (War war in unifier.getWars().ToList())
            {
                if (war?.data == null || war.hasEnded() || !IsUnificationWar(war)) continue;
                Kingdom attacker = Sovereign(war.getMainAttacker());
                Kingdom foe = attacker == unifier ? Sovereign(war.getMainDefender()) : attacker;
                if (!Alive(foe) || foe == unifier) continue;
                if (!ArmyWiped(foe, unifier))
                {
                    Wiped.Remove(foe.id);
                    continue;
                }
                Wiped.TryGetValue(foe.id, out int months);
                Wiped[foe.id] = ++months;
                if (months < WipeMonths) continue;
                Wiped.Remove(foe.id);
                Annex(unifier, foe, war, "unify_annex_history");
            }
            Declare(unifier);
        }
    }

    // 正规军打光：军团制看各成员国的正规军团(不算民兵、骑士团)；否则看士兵连同可征之兵。
    // 以前要连着两个月一兵不剩，可每月都会补征，从来没吞并成过；现在剩不到满编的一成半，或不到统一者的一成就算打光
    private const float WipedShareOfFull = 0.15f, WipedShareOfUnifier = 0.1f;

    private static bool ArmyWiped(Kingdom sovereign, Kingdom unifier)
    {
        IEnumerable<Kingdom> Members(Kingdom kingdom) => kingdom.GetEmpire()?.kingdoms_list?.Where(Alive) ?? new[] { kingdom };
        if (CorpsSystem.Enabled)
        {
            var regulars = Members(sovereign).SelectMany(member => CorpsSystem.Of(member))
                .Where(corps => !corps.militia && corps.order_id < 0).ToList();
            float strength = regulars.Sum(corps => Mathf.Max(0f, corps.strength)), full = regulars.Sum(corps => Mathf.Max(0f, corps.full));
            float theirs = Members(unifier).SelectMany(member => CorpsSystem.Of(member))
                .Where(corps => !corps.militia && corps.order_id < 0).Sum(corps => Mathf.Max(0f, corps.strength));
            return strength < 1f || strength < full * WipedShareOfFull || strength < theirs * WipedShareOfUnifier;
        }
        int warriors = Members(sovereign).Sum(member => member.countTotalWarriors() + CityPopulationSystem.MobilizableReserve(member));
        int unifierWarriors = Members(unifier).Sum(member => member.countTotalWarriors());
        return warriors <= WipedWarriors || warriors < unifierWarriors * WipedShareOfUnifier;
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
        // 都城在天下以外的一家(入主的外族)只收它在这个天下里的城，不吞它的老家
        EmpireCore arena = Arenas.TryGetValue(unifier.id, out EmpireCore found) ? found : null;
        Func<City, bool> only = arena != null && TianxiaSystem.LeadOf(foe.capital) != arena
            ? city => TianxiaSystem.LeadOf(city) == arena
            : null;
        int cities = members.Sum(member => WarGoalSystem.QueueAnnex(member, unifier, only));
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
            string cycle = realm.lead != null ? TianxiaSystem.PhaseName(TianxiaSystem.Phase(realm.lead)) : "文化连片";
            string phase = unifier != null ? $"统一战争 {unifier.GetKingdomName()} 第{UnifyYears(unifier)}年"
                : realm.members.Any(InGrace) ? $"{cycle}(开国气象)" : cycle;
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

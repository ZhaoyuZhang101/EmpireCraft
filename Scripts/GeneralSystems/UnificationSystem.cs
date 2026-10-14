using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 天下大势(历史周期律，用户 2026-10-14 定)：
// 天下 = 同一文化圈(城市主文化所属的制度线)里、与都城同一块陆地上的城。一家 = 一个独立国家连同它的附庸；帝国连同各成员国、附庸算一家。
//   · 乱世：没有哪家占到三成——群雄互伐，饥荒、民变照常；
//   · 统一战争：一家占到三成，或国力是第二名的两倍(且至少占 8%)——天命所归，目标二十年内打完：
//       - 统一者每月挑最近的一家开战(目标吞并)，同时可打四场，不必休战，不受三十年强制停战；
//       - 把对方正规军(军团；没有军团时看士兵与可征之兵)打光、连续两个月没缓过来，就举国吞并，不等和约；
//       - 实力不到统一者三分之一的每年有几率望风归附，统一十年后几率加倍；
//       - 战线推进加快一半，十年后加倍(见 FrontSystem.PushFactor)；
//       - 统一者处在开国气象里(见 InGrace)：积弊不涨、不起事、不分裂；
//   · 统一完成(占到九成，或天下再没有别的独立势力)：开国气象延续三十年；之后积弊按王朝年岁慢慢沉积(见 MaladySystem.Pace)，
//     末世崩溃，天下重回乱世，再出新的统一者。一个王朝只统一一次(改朝换代才能再来，见 OnDynastyChanged)；
//   · 天命已失、统一中辍：地盘跌到 6% 以下，或不到两成、而且已不是天下国力最强的一家。
// 周室分封(ZhouEraSystem)的天下另有一套春秋战国，不在这里管。
public static class UnificationSystem
{
    private const float ContenderShare = 0.3f, PowerLead = 2f, PowerLeadMinShare = 0.08f, UnifiedShare = 0.9f,
        LostShare = 0.2f, CollapsedShare = 0.06f;
    private const int GraceYears = 30, MaxWars = 4, LateYears = 10, MinCities = 3;
    private const float SubmitRatio = 1f / 3f, SubmitChance = 0.08f, SubmitCap = 0.35f;
    private const float PushBonus = 1.5f, LatePushBonus = 2f;
    // 正规军打光：连续几个月(月度检查)没有正规军才吞并(打光后下个月又征起来的不算)
    private const int WipeMonths = 2;
    // 没有军团制时：士兵连同可征之兵不过这么多算打光
    private const int WipedWarriors = 3;

    private static object _world;
    private static double _lastMonth = -1d, _lastYear = -1d;
    private static readonly Dictionary<long, int> Wiped = new();
    // 上次年度统计的天下：统一者 → 它天下里的各家(按都城距离排好)
    private static readonly Dictionary<long, List<Kingdom>> Rivals = new();

    public static void ResetWorldState()
    {
        _world = null;
        _lastMonth = _lastYear = -1d;
        Wiped.Clear();
        Rivals.Clear();
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

    // 开国气象：统一战争期间、统一后三十年(见 Empire.InFoundingGrace、RebellionSystem.CanAttempt)
    public static bool InGrace(Kingdom kingdom)
    {
        Kingdom sovereign = Sovereign(kingdom);
        if (!Alive(sovereign) || World.world == null) return false;
        var data = sovereign.GetOrCreate();
        if (data.unify_since >= 0d) return true;
        return data.unified_at >= 0d && World.world.getCurWorldTime() >= data.unified_at &&
               Date.getYearsSince(data.unified_at) < GraceYears;
    }

    // 统一者与别家的仗，不论谁先动手(不受三十年强制停战、攻方议和；把对方正规军打光就举国吞并)
    public static bool IsUnificationWar(War war)
    {
        if (war?.data == null || war.hasEnded()) return false;
        Kingdom attacker = Sovereign(war.getMainAttacker()), defender = Sovereign(war.getMainDefender());
        return Alive(attacker) && Alive(defender) && attacker != defender &&
               (attacker.GetOrCreate().unify_since >= 0d || defender.GetOrCreate().unify_since >= 0d);
    }

    // 战线推进：统一者打天下里的别家快一半，统一十年后加倍
    public static float PushFactor(Kingdom side, Kingdom opponent)
    {
        Kingdom sovereign = Sovereign(side), foe = Sovereign(opponent);
        if (!Alive(sovereign) || !Alive(foe) || sovereign == foe || sovereign.GetOrCreate().unify_since < 0d) return 1f;
        return UnifyYears(sovereign) >= LateYears ? LatePushBonus : PushBonus;
    }

    // 改朝换代：新王朝可以再统一一次
    public static void OnDynastyChanged(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (!Alive(core)) return;
        var data = core.GetOrCreate();
        data.unify_since = -1d;
        data.unified_at = -1d;
    }

    #endregion

    #region 天下

    private static string LineOf(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return null;
        try
        {
            return InstitutionSystem.GetCultureLine(culture);
        }
        catch
        {
            return null;
        }
    }

    private static object IslandOf(WorldTile tile) => tile?.region?.island;

    // 天下(文化圈 + 陆地)里各家占的城
    private sealed class Realm
    {
        public string line;
        public object island;
        public int total;
        public readonly Dictionary<Kingdom, int> held = new();
    }

    private static List<Realm> Survey()
    {
        var realms = new Dictionary<(string, object), Realm>();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild) continue;
            object island = IslandOf(city.getTile());
            string line = LineOf(CultureService.GetMainCulture(city, false));
            if (island == null || line == null) continue;
            if (!realms.TryGetValue((line, island), out Realm realm))
                realms[(line, island)] = realm = new Realm { line = line, island = island };
            realm.total++;
            Kingdom holder = Sovereign(city.kingdom);
            if (!Alive(holder)) continue;
            realm.held.TryGetValue(holder, out int count);
            realm.held[holder] = count + 1;
        }
        return realms.Values.Where(realm => realm.total >= MinCities).ToList();
    }

    // 都城在这块陆地上(天下里占着城的别家，不论哪种文化，都是统一的对象；隔海的不打)
    private static bool OnLand(Kingdom sovereign, Realm realm) =>
        Alive(sovereign) && IslandOf(sovereign.capital?.getTile()) == realm.island;

    // 这一家是这个天下的人(都城在这块陆地上、本国文化属这条线)：只有它们能做统一者
    private static bool Belongs(Kingdom sovereign, Realm realm) =>
        OnLand(sovereign, realm) && LineOf(CultureService.GetRealmCulture(sovereign)) == realm.line;

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
        Rivals.Clear();
        double now = World.world.getCurWorldTime();
        List<Realm> realms = Survey();
        Diagnose(realms);
        foreach (Realm realm in realms)
        {
            List<Kingdom> powers = realm.held.Keys.ToList();
            Kingdom unifier = powers.FirstOrDefault(holder => holder.GetOrCreate().unify_since >= 0d && Belongs(holder, realm));
            if (unifier != null)
            {
                float share = realm.held[unifier] / (float)realm.total;
                bool alone = powers.All(holder => holder == unifier || !OnLand(holder, realm) || Exempt(holder));
                var data = unifier.GetOrCreate();
                if (share >= UnifiedShare || alone)
                {
                    data.unify_since = -1d;
                    data.unified_at = now;
                    Record(unifier, "unify_done_history", unifier.GetKingdomName());
                    continue;
                }
                double strongest = powers.Where(holder => holder != unifier && Belongs(holder, realm))
                    .Select(holder => WarSituation.Power(holder)).DefaultIfEmpty(0d).Max();
                if (share < CollapsedShare || share < LostShare && WarSituation.Power(unifier) < strongest)
                {
                    data.unify_since = -1d;
                    Record(unifier, "unify_lost_history", unifier.GetKingdomName());
                    continue;
                }
                Rivals[unifier.id] = Opponents(unifier, realm);
                Submissions(unifier);
                continue;
            }
            // 乱世：看有没有天命所归的
            var ranked = powers.Where(holder => Belongs(holder, realm) && !Exempt(holder))
                .OrderByDescending(holder => realm.held[holder]).ToList();
            if (ranked.Count < 2) continue;
            Kingdom top = ranked[0];
            if (top.GetOrCreate().unified_at >= 0d) continue;
            float topShare = realm.held[top] / (float)realm.total;
            double topPower = WarSituation.Power(top);
            double second = ranked.Skip(1).Select(holder => WarSituation.Power(holder)).DefaultIfEmpty(0d).Max();
            bool leads = topShare >= ContenderShare || topShare >= PowerLeadMinShare && topPower >= second * PowerLead;
            if (!leads) continue;
            top.GetOrCreate().unify_since = now;
            Record(top, "unify_start_history", top.GetKingdomName(), $"{topShare:P0}");
            Rivals[top.id] = Opponents(top, realm);
        }
    }

    // 天下里的别家(独立国家、别的帝国)，按都城离统一者都城远近排
    private static List<Kingdom> Opponents(Kingdom unifier, Realm realm)
    {
        WorldTile home = unifier.capital?.getTile();
        return realm.held.Keys.Where(holder => holder != unifier && OnLand(holder, realm) && !Exempt(holder))
            .OrderBy(holder => home == null || holder.capital?.getTile() == null ? float.MaxValue
                : Toolbox.SquaredDistTile(home, holder.capital.getTile()))
            .ToList();
    }

    // 望风归附：实力不到统一者三分之一的，每年几率随实力差增加，统一十年后加倍
    private static void Submissions(Kingdom unifier)
    {
        if (!Rivals.TryGetValue(unifier.id, out List<Kingdom> rivals)) return;
        double power = WarSituation.Power(unifier);
        if (power <= 0d) return;
        bool late = UnifyYears(unifier) >= LateYears;
        foreach (Kingdom rival in rivals.ToList())
        {
            if (!Alive(rival) || rival.IsEmpire()) continue;
            float ratio = (float)(WarSituation.Power(rival) / power);
            if (ratio >= SubmitRatio) continue;
            float chance = Mathf.Min(SubmitCap, SubmitChance * (1f - ratio / SubmitRatio) * (late ? 2f : 1f) * 2f);
            if (UnityEngine.Random.value >= chance) continue;
            Annex(unifier, rival, null, "unify_submit_history");
        }
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
                if (!ArmyWiped(foe))
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

    // 正规军打光：军团制看各成员国的正规军团(不算民兵、骑士团)；否则看士兵连同可征之兵
    private static bool ArmyWiped(Kingdom sovereign)
    {
        IEnumerable<Kingdom> members = sovereign.GetEmpire()?.kingdoms_list?.Where(Alive) ?? new[] { sovereign };
        if (CorpsSystem.Enabled)
            return members.SelectMany(member => CorpsSystem.Of(member))
                .Where(corps => !corps.militia && corps.order_id < 0).Sum(corps => Mathf.Max(0f, corps.strength)) < 1f;
        int warriors = members.Sum(member => member.countTotalWarriors() + CityPopulationSystem.MobilizableReserve(member));
        return warriors <= WipedWarriors;
    }

    // 开战：手上的统一战争不到四场、养得起兵，就挑最近的一家开战(目标吞并)，每月至多一场
    private static void Declare(Kingdom unifier)
    {
        if (!Rivals.TryGetValue(unifier.id, out List<Kingdom> rivals) || rivals.Count == 0) return;
        int wars = unifier.getWars().Count(war => IsUnificationWar(war));
        if (wars >= MaxWars || CorpsSystem.CannotFundWar(unifier) || WarGoalSystem.FightingForSurvival(unifier)) return;
        Kingdom target = rivals.FirstOrDefault(rival => Alive(rival) && Sovereign(rival) == rival && !unifier.isInWarWith(rival));
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
            Kingdom unifier = realm.held.Keys.FirstOrDefault(holder => holder.GetOrCreate().unify_since >= 0d && Belongs(holder, realm));
            string phase = unifier != null ? $"统一战争 {unifier.GetKingdomName()} 第{UnifyYears(unifier)}年"
                : realm.held.Keys.Any(holder => InGrace(holder) && Belongs(holder, realm)) ? "治世(开国气象)" : "乱世/常态";
            LogService.LogInfo($"[EmpireCraft][天下大势] {realm.line} {realm.total}城 {realm.held.Count}家：{phase}；{string.Join("、", top)}");
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

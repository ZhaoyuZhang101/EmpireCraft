using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 开战目标(战后分配的一部分，参照钢铁雄心：战时占领与最终归属分开)：国战开打时按形势定一个目标，议和时按目标结算——
//   · 吞并：攻方兵力两倍以上、与对方接壤、对方是小国(西欧封建只认三城以内，周制不吞并)；
//   · 割地(局部入侵)：接壤、兵力相当；
//   · 赔款：不接壤(跨海)或攻方兵力不如对方；
//   · 劫掠：原有的劫掠战争(洗劫第一座城即收兵，见 CityPatch)；
//   · 收复：对方手里有我方法理的城(宣称口径见 ClaimsTitle)，且接壤——优先于其他目标；
//   · 迫使附庸：攻方兵力两倍以上(周制一倍半)、对方不是帝国、吞不下；
//   · 改宗：圣战且两边宗教不同——对方连同各城改奉攻方的宗教；
//   · 通商：跨海打不下城、兵力够、两边都有港口，对方锁国或攻方兵力两倍以上——对方被迫开港，与攻方通商二十年(锁国也开)；
//   · 扶立：兵力一倍半以上、对方不是帝国，攻方对那国王位有血亲宣称或对方君主年幼——从对方宗室里另立一位成年人为君，
//     二十年内两国互不开战。(用户 2026-10-19 选"改宗、开港通商、扶立君主")
// 战争窗口里点"战争目标"一行可以轮换目标(劫掠除外)。
// 议和时按这场仗里双方各丢了多少城(含被对方占了一半以上地块的城)判胜负，相差不到一城就是白和——不赔不割：
//   攻方胜：赔款 → 对方赔款；割地 → 被攻方占了一半以上的城割给占领者；吞并 → 同割地，对方丢了一半城或都城时余下的城一并吞并；
//           收复 → 我方法理的城中占了一半以上的归还占领者，占了法理城的一半以上时全部归还；迫使附庸 → 对方成为攻方的附庸；
//   守方胜：攻方赔款(劫掠的不赔，打退就是)。
// 以前不论怎么收场都按"有胜负"处理，议和(原版最常见的结局)也让攻方赔款，几乎每场仗打完都赔款。
// 叛乱、独立、索取法理、去帝号这类有自己结局的战争不在此列，照旧各按各的规则。
public static class WarGoalSystem
{
    // 吞并：攻方兵力至少是对方几倍、对方城数不超过多少(或不超过攻方一半)
    private const float AnnexStrength = 2f;
    private const int AnnexSmallCities = 3;
    // 割地：攻方兵力至少是对方几成
    private const float ConquestStrength = 0.8f;
    // 迫使附庸：攻方兵力至少是对方几倍。一倍半 → 两倍(用户 2026-10-21 选"封建能吞小国")：迫使附庸是死战目标(见 Excessive)，
    // 以前行分封的攻方兵力一倍半就定迫使附庸，本该只求割地的仗反把对方逼得困兽犹斗；一倍半到两倍之间改求割地
    private const float VassalStrength = 2f;
    // 扶立君主：攻方兵力至少是对方几倍(仍是一倍半，不随迫使附庸提到两倍——用户只定了迫使附庸的门槛)
    private const float InstallStrength = 1.5f;
    // 周制(华夏分封)的迫使附庸照旧一倍半：周室与诸侯有自己的兼并规则(见 ZhouEraSystem)，用户这次只放开西欧封建
    private const float ZhouVassalStrength = 1.5f;
    // 战争窗口里轮换的顺序(劫掠不参与)
    private static readonly WarGoal[] Cycle =
        { WarGoal.吞并, WarGoal.割地, WarGoal.收复, WarGoal.迫使附庸, WarGoal.扶立, WarGoal.改宗, WarGoal.通商, WarGoal.赔款 };
    // 扶立君主之后两国互不开战的年数
    private const int InstalledYears = 20;
    // 占了一座城一半以上的地块才算到手
    private const float HeldShare = 0.5f;
    // 每帧最多易主几座城(易主有原版开销)
    private const int CessionsPerFrame = 3;

    private static readonly HashSet<EmpireWarType> OwnEnding = new()
    {
        EmpireWarType.游牧扩张, EmpireWarType.迫使朝贡, EmpireWarType.藩王索取皇位, EmpireWarType.派系叛乱,
        EmpireWarType.地方叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱, EmpireWarType.地方独立,
        EmpireWarType.附庸独立, EmpireWarType.清君侧, EmpireWarType.索取法理, EmpireWarType.去帝号,
        EmpireWarType.帝国正统, EmpireWarType.不奉诏, EmpireWarType.获取帝国, EmpireWarType.征伐不服,
        EmpireWarType.私战, EmpireWarType.对立国王, EmpireWarType.草场之争, EmpireWarType.逼冕
    };

    private static readonly List<(long city, long to)> PendingCessions = new();
    private static readonly List<(long lord, long subject)> PendingVassals = new();

    public static void ResetWorldState()
    {
        PendingCessions.Clear();
        PendingVassals.Clear();
    }

    private static bool Applies(War war) =>
        war?.data != null && war.getAsset() != WarTypeLibrary.rebellion && !OwnEnding.Contains(war.GetEmpireWarType());

    public static WarGoal Get(War war)
    {
        if (war?.data == null) return WarGoal.None;
        if (war.GetEmpireWarType() == EmpireWarType.劫掠) return WarGoal.劫掠;
        return Applies(war) ? war.GetOrCreate().war_goal : WarGoal.None;
    }

    // 开打时定目标(WarPatch.update 每场仗第一次结算时调用)；读档后已定的不变
    public static void Ensure(War war)
    {
        if (war?.data == null || war.hasEnded()) return;
        var data = war.GetOrCreate();
        if (data.war_goal != WarGoal.None || !Applies(war)) return;
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        if (attacker?.data == null || defender?.data == null || attacker.isRekt() || defender.isRekt()) return;
        if (war.GetEmpireWarType() == EmpireWarType.劫掠)
        {
            data.war_goal = WarGoal.劫掠;
            return;
        }
        float strength = WarSituation.StrengthRatio(defender, attacker);
        int theirs = WarSituation.SideCities(defender), ours = WarSituation.SideCities(attacker);
        bool borders = attacker.IsNeighbourWith(defender);
        RegimeType? regime = attacker.GetRegime()?.type;
        bool feudal = regime == RegimeType.Feudalism || regime == RegimeType.ZhouFeudalism;
        bool small = theirs <= AnnexSmallCities || theirs * 2 <= ours;
        // 西欧封建以前永不选吞并，打赢了只得附庸，核心永远只有几座城(用户 2026-10-21 选"封建能吞小国")：
        // 兵力两倍以上、对方三城以内的也吞并。别的政体的"小国"还含不到攻方一半大的；封建不含，城多的照旧收作附庸。
        // 周制照旧不吞并、一倍半就迫使附庸(只放开西欧)
        bool zhou = regime == RegimeType.ZhouFeudalism;
        bool annexable = zhou ? false : feudal ? theirs <= AnnexSmallCities : small;
        float vassalAt = zhou ? ZhouVassalStrength : VassalStrength;
        // 战国诸侯互伐：兵力够就以兼并(对方小)或割地为务，不再只求赔款
        if (borders && ZhouEraSystem.WarringRivals(attacker, defender) && Claimed(attacker, defender.cities).Count == 0)
            data.war_goal = strength >= ConquestStrength ? small ? WarGoal.吞并 : WarGoal.割地 : WarGoal.赔款;
        // 迫使附庸与吞并的兵力门槛现在都是两倍(以前行分封的一倍半就迫使附庸；周制照旧一倍半)：吞不下的、不是帝国的，迫其为附庸
        else data.war_goal = !borders ? OverseasGoal(attacker, defender, strength, small)
            : Claimed(attacker, defender.cities).Count > 0 ? WarGoal.收复
            : strength >= AnnexStrength && annexable ? WarGoal.吞并
            : strength >= vassalAt && !defender.IsEmpire() ? WarGoal.迫使附庸
            : strength >= ConquestStrength ? WarGoal.割地
            : WarGoal.赔款;
        // 扩张上限：已经过度扩张，或对方都城远在本国控制范围之外，不谋土地，改为迫其称臣(帝国则求赔款)
        if ((data.war_goal == WarGoal.吞并 || data.war_goal == WarGoal.割地) &&
            AdministrativeControlSystem.BeyondReach(attacker, defender.capital))
            data.war_goal = defender.IsEmpire() ? WarGoal.赔款 : WarGoal.迫使附庸;
        // 圣战且两边宗教不同：要对方改宗
        if (war.GetEmpireWarType() == EmpireWarType.神圣 && Convertible(attacker, defender)) data.war_goal = WarGoal.改宗;
        // 扶立君主：对那国王位有血亲宣称、或对方君主年幼，又找得到人扶立
        else if (data.war_goal is WarGoal.迫使附庸 or WarGoal.割地 && strength >= InstallStrength && !defender.IsEmpire() &&
                 (RoyalMarriageSystem.HasClaim(attacker, defender) || defender.king?.isAdult() == false) &&
                 InstallCandidate(defender) != null)
            data.war_goal = WarGoal.扶立;
        data.war_goal_capital_id = defender.capital?.data != null ? defender.capital.id : -1L;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_goal_declared"), attacker.GetKingdomName(),
            defender.GetKingdomName(), Name(data.war_goal, attacker)), attacker);
        // 开打即拜统帅(谋划开战时已拜的不变，见 WarCommandSystem)
        WarCommandSystem.MonthTick(FrontSystem.Realm(attacker));
        WarCommandSystem.MonthTick(FrontSystem.Realm(defender));
    }

    // 跨海：兵力两倍以上、战舰是对方一倍半以上、对方都城在舰种航程内，才谋割地(小国则吞并)，否则只求赔款
    private static WarGoal OverseasGoal(Kingdom attacker, Kingdom defender, float strength, bool small)
    {
        if (strength < AnnexStrength || defender.capital?.data == null) return OverseasFallback(attacker, defender, strength);
        int ours = NavySystem.Warships(attacker), theirs = NavySystem.Warships(defender);
        if (ours <= 0 || ours < theirs * 1.5f) return OverseasFallback(attacker, defender, strength);
        List<SeaRegionSystem.Region> route = NavySystem.LandingRoute(attacker, defender.capital);
        if (route == null || route.Count > NavySystem.MaxRange(attacker)) return OverseasFallback(attacker, defender, strength);
        return small ? WarGoal.吞并 : WarGoal.割地;
    }

    // 跨海打不下城：兵力够、两边都有港口，对方锁国或攻方兵力两倍以上的，逼对方开港通商；否则赔款
    private static WarGoal OverseasFallback(Kingdom attacker, Kingdom defender, float strength) =>
        strength >= ConquestStrength && TradeRouteSystem.HasPort(attacker) && TradeRouteSystem.HasPort(defender) &&
        (TradeRouteSystem.Closed(defender) || strength >= AnnexStrength)
            ? WarGoal.通商
            : WarGoal.赔款;

    private static bool Convertible(Kingdom attacker, Kingdom defender) =>
        attacker?.religion != null && defender?.data != null && !defender.isRekt() && defender.religion != attacker.religion;

    // 改宗：对方连同各城、城里的人改奉攻方的宗教
    private static bool Convert(Kingdom defender, Kingdom attacker)
    {
        if (!Convertible(attacker, defender)) return false;
        Religion faith = attacker.religion;
        defender.setReligion(faith);
        foreach (City city in defender.cities.ToList())
        {
            if (city?.data == null || city.isRekt()) continue;
            city.setReligion(faith);
            city.units?.ForEach(actor => actor?.setReligion(faith));
        }
        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_converted"), defender.GetKingdomName(),
            attacker.GetKingdomName(), faith.name), attacker);
        return true;
    }

    // 扶立：对方宗室里另一位成年人(不是现任君主)，威望最高的
    private static Actor InstallCandidate(Kingdom defender)
    {
        SpecificClan clan = defender?.king?.GetSpecificClan();
        return clan?.AllAliveMembers?
            .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                            actor != defender.king && !actor.isKing() && actor.kingdom == defender && actor.isUnitFitToRule())
            .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
    }

    private static bool Install(Kingdom attacker, Kingdom defender)
    {
        Actor candidate = InstallCandidate(defender);
        if (candidate == null) return false;
        Actor former = defender.king;
        GraceEdictService.Crown(defender, candidate);
        if (defender.king != candidate) return false;
        var extra = defender.GetOrCreate();
        extra.installed_by_kingdom_id = attacker.id;
        extra.installed_at = World.world.getCurWorldTime();
        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_installed"), attacker.GetKingdomName(),
            candidate.getName(), defender.GetKingdomName(), former?.getName() ?? ""), attacker);
        return true;
    }

    // 扶立之君：二十年内两国互不开战(见 WarGate、WarTargetPatch)
    public static bool Installed(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null || a == b || World.world == null) return false;
        return InstalledBy(a, b) || InstalledBy(b, a);
    }

    private static bool InstalledBy(Kingdom puppet, Kingdom patron)
    {
        var extra = puppet.GetOrCreate();
        return extra.installed_by_kingdom_id == patron.id && extra.installed_at >= 0d &&
               Date.getYearsSince(extra.installed_at) < InstalledYears;
    }

    // 窗口里能不能轮换目标：定了目标、还没打完、不是劫掠
    public static bool Switchable(War war)
    {
        WarGoal goal = Get(war);
        return goal != WarGoal.None && goal != WarGoal.劫掠 && war?.data != null && !war.hasEnded();
    }

    public static void Next(War war)
    {
        if (!Switchable(war)) return;
        var data = war.GetOrCreate();
        int index = Array.IndexOf(Cycle, data.war_goal);
        data.war_goal = Cycle[(index + 1) % Cycle.Length];
        if (data.war_goal_capital_id < 0 && war.main_defender?.capital?.data != null)
            data.war_goal_capital_id = war.main_defender.capital.id;
    }

    // cities 里攻方一方(帝国按整个帝国算)有宣称的、不在攻方一方手里的城；挑宣战目标也按它先收法理(见 DeJureAgenda)
    internal static List<City> Claimed(Kingdom claimant, IEnumerable<City> cities)
    {
        var result = new List<City>();
        Kingdom realm = FrontSystem.Realm(claimant);
        if (cities == null || realm == null) return result;
        foreach (City city in cities)
        {
            if (city?.data == null || city.isRekt() || city.kingdom != null && FrontSystem.Realm(city.kingdom) == realm) continue;
            // 兼属：这座城兼属的王国法理、帝国法理在攻方一方，也算收复(见 DeJureShareSystem)
            if (DeJureShareSystem.ClaimedBy(city, realm))
            {
                result.Add(city);
                continue;
            }
            KingdomTitle title = city.GetTitle();
            if (title != null && !title.isRekt() && ClaimsTitle(realm, title)) result.Add(city);
        }
        return result;
    }

    // 这一方(帝国按整个帝国算)对这个王国法理有宣称(用户 2026-10-26 "感觉所有国家都没有按照法理扩张"，
    // 问答选"握都城或过半就宣称整个法理"；以前只认法理主国，全图多半法理没有主国，宣称几乎没有)：
    // 法理主国或持有人在这一方；以它为行政法理的省份在这一方；法理都城在这一方手里；这一方占着它过半的城
    public static bool ClaimsTitle(Kingdom realm, KingdomTitle title)
    {
        if (realm == null || title == null || title.isRekt()) return false;
        Kingdom holder = title.main_kingdom ?? title.owner?.kingdom;
        if (holder?.data != null && !holder.isRekt() && FrontSystem.Realm(holder) == realm) return true;
        if (AdminHolder(title) is Kingdom admin && FrontSystem.Realm(admin) == realm) return true;
        City capital = title.title_capital;
        if (capital?.kingdom != null && !capital.isRekt() && FrontSystem.Realm(capital.kingdom) == realm) return true;
        int total = 0, ours = 0;
        foreach (City city in title.getCities())
        {
            if (city?.data == null || city.isRekt()) continue;
            total++;
            if (city.kingdom != null && FrontSystem.Realm(city.kingdom) == realm) ours++;
        }
        return ours * 2 > total;
    }

    // 以这个王国法理为行政法理的省份(一帧查一次表)
    private static int _adminFrame = -1;
    private static readonly Dictionary<long, Kingdom> AdminTitles = new();

    private static Kingdom AdminHolder(KingdomTitle title)
    {
        if (_adminFrame != UnityEngine.Time.frameCount)
        {
            _adminFrame = UnityEngine.Time.frameCount;
            AdminTitles.Clear();
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                long id = kingdom.GetOrCreate().AdministrativeTitle;
                if (id >= 0) AdminTitles[id] = kingdom;
            }
        }
        return AdminTitles.TryGetValue(title.id, out Kingdom found) ? found : null;
    }

    public static string Name(WarGoal goal) => LM.Get("war_goal_" + goal);

    // 不行封建附庸的攻方：迫使附庸称"迫使称臣"(战后对方称臣纳贡，见 Tick)
    public static string Name(WarGoal goal, Kingdom attacker) =>
        goal == WarGoal.迫使附庸 && attacker?.data != null && !FeudalVassalService.UsesFeudalVassals(attacker)
            ? LM.Get("war_goal_迫使称臣")
            : Name(goal);

    // 战争窗口：目标与战况(双方各已得几城)
    public static string Describe(War war)
    {
        WarGoal goal = Get(war);
        if (goal == WarGoal.None) return null;
        if (war.hasEnded()) return Name(goal, war.main_attacker);
        (int attackerGain, int defenderGain) = Gains(war);
        return string.Format(LM.Get("war_goal_standing"), Name(goal, war.main_attacker), attackerGain, defenderGain);
    }

    // 攻守双方在这场仗里各得几城：对方参战以来少了的城 + 己方占了一半以上地块的对方城市
    public static (int attackerGain, int defenderGain) Gains(War war)
    {
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        int defenderLost = Math.Max(0, WarSituation.CitiesLost(war, defender));
        int attackerLost = Math.Max(0, WarSituation.CitiesLost(war, attacker));
        return (defenderLost + HeldCities(war, war._list_defenders, war._list_attackers).Count,
            attackerLost + HeldCities(war, war._list_attackers, war._list_defenders).Count);
    }

    // owners 的城里被 occupiers 一方占了一半以上地块的：城 → 占得最多的那国
    private static List<(City city, Kingdom holder)> HeldCities(War war, IEnumerable<Kingdom> owners, IEnumerable<Kingdom> occupiers)
    {
        var result = new List<(City, Kingdom)>();
        List<Kingdom> side = occupiers.Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).ToList();
        foreach (Kingdom owner in owners)
        {
            if (owner?.cities == null || owner.isRekt()) continue;
            foreach (City city in owner.cities)
            {
                if (city?.data == null || city.isRekt() || city.zones == null || city.zones.Count == 0) continue;
                int total = 0, best = 0;
                Kingdom holder = null;
                foreach (Kingdom kingdom in side)
                {
                    int count = city.GetOccupiedTileZoneCount(kingdom);
                    if (count <= 0) continue;
                    total += count;
                    if (count > best)
                    {
                        best = count;
                        holder = kingdom;
                    }
                }
                if (holder != null && total >= city.zones.Count * HeldShare) result.Add((city, holder));
            }
        }
        return result;
    }

    // 战争收尾时(WarPatch.end_war，清掉占领之前)：判胜负、按目标结算
    public static void Settle(War war, WarWinner winner)
    {
        // 部落之间破城求和已经了结(见 TribalWarSystem)
        if (TribalWarSystem.Settling(war)) return;
        try
        {
            if (war?.data == null || winner == WarWinner.Nobody || winner == WarWinner.Merged || !Applies(war) &&
                war.GetEmpireWarType() != EmpireWarType.劫掠) return;
            Kingdom attacker = war.main_attacker, defender = war.main_defender;
            if (attacker?.data == null || defender?.data == null || attacker == defender) return;
            WarGoal goal = Get(war);
            if (goal == WarGoal.None) goal = WarGoal.赔款;
            (int attackerGain, int defenderGain) = Gains(war);
            bool attackersWon = winner == WarWinner.Attackers || winner == WarWinner.Peace && attackerGain >= defenderGain + 1;
            bool defendersWon = winner == WarWinner.Defenders || winner == WarWinner.Peace && defenderGain >= attackerGain + 1;
            if (attackersWon)
            {
                if (defender.isRekt()) return;
                switch (goal)
                {
                    case WarGoal.赔款:
                        WarReparationsSystem.Impose(defender, attacker);
                        break;
                    case WarGoal.割地:
                        Cede(war, attacker, defender, annex: false);
                        break;
                    case WarGoal.吞并:
                        Cede(war, attacker, defender, annex: true);
                        break;
                    case WarGoal.收复:
                        Reclaim(war, attacker, defender);
                        break;
                    case WarGoal.迫使附庸:
                        // 帝国不会整个沦为附庸，退回赔款
                        if (defender.IsEmpire()) WarReparationsSystem.Impose(defender, attacker);
                        else PendingVassals.Add((attacker.id, defender.id));
                        break;
                    case WarGoal.改宗:
                        if (!Convert(defender, attacker)) WarReparationsSystem.Impose(defender, attacker);
                        break;
                    case WarGoal.通商:
                        if (TradeRouteSystem.ImposeTreaty(defender, attacker))
                            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_trade"), defender.GetKingdomName(),
                                attacker.GetKingdomName()), attacker);
                        else WarReparationsSystem.Impose(defender, attacker);
                        break;
                    case WarGoal.扶立:
                        if (defender.IsEmpire() || !Install(attacker, defender)) WarReparationsSystem.Impose(defender, attacker);
                        break;
                }
            }
            else if (defendersWon)
            {
                if (goal != WarGoal.劫掠 && !attacker.isRekt()) WarReparationsSystem.Impose(attacker, defender);
            }
            if (attackersWon) Modernize(war, defender, attacker, war._list_defenders, war._list_attackers);
            else if (defendersWon) Modernize(war, attacker, defender, war._list_attackers, war._list_defenders);
            else if (!attacker.isRekt() && !defender.isRekt())
            {
                TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_white"), attacker.GetKingdomName(),
                    defender.GetKingdomName()), attacker);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][开战目标] 议和结算失败: {exception.Message}");
        }
    }

    // 发不出军饷的一方求和(见 CorpsSystem.SueForPeace)：攻方罢兵(判和，按双方所得结算，占不到便宜的反赔)；
    // 守方只要条件不过分就认输(按攻方的开战目标结算)。叛乱、独立、争位这类有自己收场的仗、全面战争不在此列
    public static bool TrySueForPeace(War war, Kingdom broke)
    {
        if (war?.data == null || war.hasEnded() || broke == null || war.isTotalWar()) return false;
        if (!Applies(war) && war.GetEmpireWarType() != EmpireWarType.劫掠) return false;
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        if (attacker?.data == null || defender?.data == null || attacker == defender) return false;
        // 开国雄主、天下统一的仗：欠饷只逃散减员，不罢兵(以前开战当月军饷掏空国库，三个月后攻方就"各守旧土")
        if (broke == attacker && RulerTraitSystem.IsFounderUnificationWar(war)) return false;
        if (broke == attacker)
        {
            World.world.wars.endWar(war, WarWinner.Peace);
            return true;
        }
        if (broke != defender || Excessive(war, defender)) return false;
        World.world.wars.endWar(war, WarWinner.Attackers);
        return true;
    }

    // 守方认输的代价太大：要吞并、要逼成附庸(帝国不会整个沦为附庸，只赔款)、割地要拿走都城或一半以上的城。
    // 2026-10-21 核对(封建能吞小国)：按兵力定的吞并、迫使附庸现在都要攻方两倍以上，死战落在被两倍兵力压着的国家身上，照旧合理；
    // 行分封的攻方兵力一倍半到两倍之间改求割地，对方只在都城或半数城被占时才死战，不再一开打就困兽犹斗。
    // 没动的：扶立仍是一倍半；过度扩张改迫使附庸、战国诸侯兼并、跨海吞并照旧
    private static bool Excessive(War war, Kingdom defender)
    {
        WarGoal goal = Get(war);
        if (goal == WarGoal.吞并 || (goal == WarGoal.迫使附庸 || goal == WarGoal.扶立) && !defender.IsEmpire()) return true;
        if (goal != WarGoal.割地) return false;
        List<(City city, Kingdom holder)> held = HeldCities(war, new[] { defender }, war._list_attackers);
        int cities = defender.cities?.Count ?? 0;
        return held.Exists(entry => entry.city == defender.capital) || held.Count * 2 >= Math.Max(1, cities);
    }

    // 正被全面侵略：作为主守方的仗求不了和(对方条件过分，或是叛乱、独立、争位这类)，或在打全面战争。欠饷也困兽犹斗
    public static bool FightingForSurvival(Kingdom kingdom)
    {
        if (kingdom == null) return false;
        foreach (War war in kingdom.getWars())
        {
            if (war?.data == null || war.hasEnded() || !war.isDefender(kingdom)) continue;
            if (war.isTotalWar()) return true;
            if (war.main_defender != kingdom) continue;
            if (!Applies(war) && war.GetEmpireWarType() != EmpireWarType.劫掠 || Excessive(war, kingdom)) return true;
        }
        return false;
    }

    // 被迫现代化：败方是帝国中央或独立君主国，战胜方是立宪、共和或现代国家时，按败方丢城多少决定是否被强行改造
    private static void Modernize(War war, Kingdom loser, Kingdom victor, IEnumerable<Kingdom> losers, IEnumerable<Kingdom> victors)
    {
        if (loser?.data == null || loser.isRekt()) return;
        Empire empire = loser.GetEmpire();
        if (empire?.CoreKingdom != loser) return;
        List<(City city, Kingdom holder)> held = HeldCities(war, new[] { loser }, victors);
        City capital = loser.capital;
        bool lostCapital = capital != null && held.Exists(entry => entry.city == capital);
        ConstitutionalEconomySystem.ImposeModernization(empire, victor, held.Count, lostCapital);
    }

    // 割让攻方占了一半以上的城；吞并且对方丢了一半城或都城时，主守方余下的城一并归主攻方(帝国核心不整体吞并，只割城)
    private static void Cede(War war, Kingdom attacker, Kingdom defender, bool annex)
    {
        List<(City city, Kingdom holder)> held = HeldCities(war, war._list_defenders, war._list_attackers);
        foreach ((City city, Kingdom holder) in held) PendingCessions.Add((city.id, holder.id));
        int ceded = held.Count;
        if (annex && !defender.IsEmpire())
        {
            Dictionary<long, int> initial = war.GetOrCreate().initial_cities;
            int before = initial != null && initial.TryGetValue(WarSituation.SideKey(defender), out int count) ? count : 0;
            int lost = Math.Max(0, WarSituation.CitiesLost(war, defender)) + held.Count;
            City capital = World.world.cities.get(war.GetOrCreate().war_goal_capital_id);
            bool capitalTaken = capital?.data != null && (capital.kingdom != defender || held.Any(entry => entry.city == capital));
            if (capitalTaken || before > 0 && lost * 2 >= before)
            {
                var queued = new HashSet<long>(held.Select(entry => entry.city.id));
                foreach (City city in defender.cities.ToList())
                    if (city?.data != null && !city.isRekt() && queued.Add(city.id)) PendingCessions.Add((city.id, attacker.id));
                TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_annexed"), attacker.GetKingdomName(),
                    defender.GetKingdomName()), attacker);
                return;
            }
        }
        ceded += CompleteTitles(war, attacker, held);
        if (ceded > 0)
            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_ceded"), attacker.GetKingdomName(),
                defender.GetKingdomName(), ceded), attacker);
    }

    // 割地把法理补完整(用户 2026-10-26 问答选"占了的照割，法理城优先")：割完以后攻方一方握着哪个王国法理的都城、或占了它过半的城，
    // 守方一方手里这个法理余下的城一并割给主攻方。返回多割了几座
    private static int CompleteTitles(War war, Kingdom attacker, List<(City city, Kingdom holder)> held)
    {
        Kingdom realm = FrontSystem.Realm(attacker);
        if (realm == null || held.Count == 0) return 0;
        var ceded = new HashSet<long>(held.Select(entry => entry.city.id));
        var defenders = new HashSet<Kingdom>(war._list_defenders.Where(kingdom => kingdom?.data != null));
        bool Ours(City city) => ceded.Contains(city.id) || city.kingdom != null && FrontSystem.Realm(city.kingdom) == realm;
        int added = 0;
        foreach (KingdomTitle title in held.Select(entry => entry.city.GetTitle()).Where(title => title != null && !title.isRekt())
                     .Distinct().ToList())
        {
            List<City> cities = title.getCities().Where(city => city?.data != null && !city.isRekt()).ToList();
            City capital = title.title_capital;
            bool claimed = capital?.data != null && !capital.isRekt() && Ours(capital) || cities.Count(Ours) * 2 > cities.Count;
            if (!claimed) continue;
            foreach (City city in cities)
            {
                if (Ours(city) || city.kingdom == null || !defenders.Contains(city.kingdom)) continue;
                ceded.Add(city.id);
                PendingCessions.Add((city.id, attacker.id));
                added++;
            }
        }
        return added;
    }

    // 收复：我方法理的城里占了一半以上的归还占领者；占了法理城的一半以上时全部归还主攻方。对方手里没有我方法理的城时按割地
    private static void Reclaim(War war, Kingdom attacker, Kingdom defender)
    {
        List<City> claimed = Claimed(attacker, war._list_defenders.Where(kingdom => kingdom?.cities != null)
            .SelectMany(kingdom => kingdom.cities).ToList());
        if (claimed.Count == 0)
        {
            Cede(war, attacker, defender, annex: false);
            return;
        }
        var claimedSet = new HashSet<City>(claimed);
        List<(City city, Kingdom holder)> held = HeldCities(war, war._list_defenders, war._list_attackers)
            .Where(entry => claimedSet.Contains(entry.city)).ToList();
        var queued = new HashSet<long>();
        foreach ((City city, Kingdom holder) in held)
            if (queued.Add(city.id)) PendingCessions.Add((city.id, holder.id));
        if (held.Count * 2 >= claimed.Count)
            foreach (City city in claimed)
                if (queued.Add(city.id)) PendingCessions.Add((city.id, attacker.id));
        if (queued.Count > 0)
            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_reclaimed"), attacker.GetKingdomName(),
                defender.GetKingdomName(), queued.Count), attacker);
    }

    // 一座城排进移交队列(诸侯私战收场割城、退城，见 HolyRomanSystem.Feuds)，逐帧易主
    public static void QueueCession(City city, Kingdom to)
    {
        if (city?.data == null || city.isRekt() || to?.data == null || to.isRekt() || city.kingdom == to) return;
        PendingCessions.Add((city.id, to.id));
    }

    // 整国并入(见 UnificationSystem)：把一国所有的城排进移交队列，逐帧易主；返回排了几座
    public static int QueueAnnex(Kingdom from, Kingdom to)
    {
        if (from?.cities == null || to?.data == null || to.isRekt() || from == to) return 0;
        int queued = 0;
        foreach (City city in from.cities.ToList())
        {
            if (city?.data == null || city.isRekt()) continue;
            PendingCessions.Add((city.id, to.id));
            queued++;
        }
        return queued;
    }

    // 战争结束后几帧内逐城易主、立附庸(不在原版结束战争的同一帧里改归属)
    public static void Tick()
    {
        if (World.world == null) return;
        while (PendingVassals.Count > 0)
        {
            (long lordId, long subjectId) = PendingVassals[0];
            PendingVassals.RemoveAt(0);
            Kingdom lord = World.world.kingdoms.get(lordId), subject = World.world.kingdoms.get(subjectId);
            if (lord?.data == null || subject?.data == null || lord.isRekt() || subject.isRekt()) continue;
            try
            {
                // 称臣纳贡(用户 2026-10-19 选"朝贡双向与称臣和约")：不行封建附庸的胜者(华夏、游牧、阿拉伯……)不再得
                // 西方式附庸，改为对方向本帝国称臣纳贡；收不了朝贡的照旧赔款
                if (!FeudalVassalService.UsesFeudalVassals(lord))
                {
                    // 降部戍边：游牧政权败给施行了羁縻的帝国，整部内附为羁縻州(见 FrontierRelations)
                    if (FrontierRelations.SettleSurrenderedTribe(lord, subject)) { }
                    else if (TributaryRelations.Submit(lord, subject))
                        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_tributary"), subject.GetKingdomName(),
                            lord.GetEmpire()?.GetEmpireName() ?? lord.GetKingdomName()), lord);
                    else WarReparationsSystem.Impose(subject, lord);
                }
                else if (FeudalVassalService.ForceBind(lord, subject))
                    TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_vassalized"), subject.GetKingdomName(),
                        lord.GetKingdomName()), lord);
                else WarReparationsSystem.Impose(subject, lord);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][开战目标] 立附庸失败: {exception.Message}");
            }
        }
        if (PendingCessions.Count == 0) return;
        int done = 0;
        while (PendingCessions.Count > 0 && done < CessionsPerFrame)
        {
            (long cityId, long toId) = PendingCessions[0];
            PendingCessions.RemoveAt(0);
            City city = World.world.cities.get(cityId);
            Kingdom to = World.world.kingdoms.get(toId);
            if (city?.data == null || city.isRekt() || to?.data == null || to.isRekt() || city.kingdom == to) continue;
            try
            {
                Kingdom from = city.kingdom;
                bool capital = from?.data != null && from.capital == city;
                AdministrativeControlSystem.MarkConquered(city, to);
                city.joinAnotherKingdom(to);
                done++;
                // 周室诸侯的都城在和约中割走，与攻破都城一样灭其国；割走王畿则攻者取而代之
                if (capital && city.kingdom == to && ZhouEraSystem.SphereOf(from) is Empire sphere)
                    ZhouEraSystem.QueueCapitalTaken(city, from, to, sphere.CoreKingdom == from);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][开战目标] {city.data.name} 割让失败: {exception.Message}");
            }
        }
    }
}

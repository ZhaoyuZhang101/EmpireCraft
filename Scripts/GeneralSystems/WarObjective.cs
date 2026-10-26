using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 具体战争目标(用户 2026-10-27："应该多添加更多战争类型，比如某个法理，吞并某个城市，战争目标要清楚"，看过示意图定稿
// Tasks/ui-mockups/war-wide.png)：开战定目标时(见 WarGoalSystem.Ensure)把目标落到具体的城、法理上，写进战争名——
//   · 争法理：攻方宣称的王国法理里有对方接壤的城——要这个法理里归对方一边的城，主城池是法理都城(不在对方手里就挑其中最大的)；
//   · 夺城：接壤城里有攻方宣称的——要其中一两座；侵略：没有宣称——挑一两座最大的接壤城；主城池是头一座；
//   · 争夺资源：接壤城里有攻方境内没有的矿藏(挑最值钱、开采得了的)，或游牧缺草场时的大片牧场——要这座城；
//   · 吞并：对方全境，主城池是都城；收复：对方一边手里本方法理的城，主城池是法理都城或最大的一座；
//   · 臣服(原迫使附庸)、扶立、改宗：主城池是对方都城；
//   · 继承：攻方君主对那国王位有血亲宣称(四步内)、政体能兼领(封建、古典共和)、兵力不弱——攻下都城即兼领王位，两国共主；
//   · 羁縻：攻方帝国施行了羁縻、对方是别的天下的小国(三城以内)、兵力一倍半以上——攻下都城即内附为羁縻州；
// 攻下主城池(城归攻方一边，或攻方一边占了它过半地块)就算拿到目标，战争当即以攻方胜收场(用户选"攻下主城池就算拿到")：
// 夺城、侵略、争夺资源割目标城；争法理割该法理里对方一边的城；收复归还目标城；吞并整国并入(见 WarGoalSystem.Cede)。
// 没拿到就议和的照旧按双方得失城数判胜负；争夺资源没打下产地而攻方占优的，败方岁供该物十年(见 ResourceTributeSystem)。
// 主城池落到第三家手里、或城没了：换目标里还在对方手里的另一座；都没了就改为赔款。
// 叛乱、革命(派系叛乱、地方叛乱，含其中的共和革命、现代革命、变法之乱、阶层起事、农民起义)：起事一方攻下京城即胜(示意图 ⑧)。
public static class WarObjective
{
    private const float HeldShare = 0.5f;
    private const int MaxTargets = 2;
    private const int JimiMaxCities = 3;
    private const float JimiStrength = 1.5f, SuccessionStrength = 0.8f;
    // 每场仗隔多久查一次主城池(世界时间秒，约一个月)
    private const double CheckSeconds = 5d;
    // 牧场：一座城至少这么多处牧村才算"马场"
    private const int PastureSettlements = 2;
    public const string PastureResource = "pasture";
    private static readonly Dictionary<long, double> LastCheck = new();
    // 和约里改归属、入羁縻、兼领王位不在原版结束战争的同一帧做(见 Tick)
    private static readonly List<Action> Pending = new();

    public static void ResetWorldState()
    {
        LastCheck.Clear();
        Pending.Clear();
    }

    // 有主城池、攻下即胜的目标
    public static bool Achievable(WarGoal goal) => goal is WarGoal.夺城 or WarGoal.侵略 or WarGoal.争法理 or WarGoal.争夺资源 or
        WarGoal.吞并 or WarGoal.收复 or WarGoal.迫使附庸 or WarGoal.羁縻 or WarGoal.继承 or WarGoal.扶立 or WarGoal.改宗;

    #region 定目标

    // 开战定下大方向(WarGoalSystem.Ensure)之后落到具体目标上。substitute：按形势改成继承、羁縻
    // (别的系统直接定的目标、窗口里轮换的目标不改，免得圣战、禁令变成继承之战，轮换卡在继承上)
    internal static void Refine(War war, Kingdom attacker, Kingdom defender, float strength, bool substitute = true)
    {
        WarExtension.WarExtraData data = war.GetOrCreate();
        data.goal_city_ids ??= new List<long>();
        data.goal_city_ids.Clear();
        data.goal_main_city_id = data.goal_title_id = data.goal_claimant_id = -1L;
        data.goal_resource = "";
        data.goal_achieved = false;
        WarGoal goal = data.war_goal;
        // 圣战收复圣地：主城池就是圣地
        if (goal == WarGoal.割地 && war.GetEmpireWarType() == EmpireWarType.神圣 && data.crusade_city_id >= 0 &&
            World.world.cities.get(data.crusade_city_id) is City holy && holy.data != null && !holy.isRekt() &&
            holy.kingdom != null && war._list_defenders.Contains(holy.kingdom))
        {
            data.goal_city_ids.Add(holy.id);
            SetMain(data, holy);
            data.war_goal = WarGoal.夺城;
            return;
        }
        if (substitute && goal is WarGoal.割地 or WarGoal.迫使附庸 or WarGoal.扶立 && CanInherit(attacker, defender, strength))
            goal = WarGoal.继承;
        else if (substitute && goal is WarGoal.吞并 or WarGoal.迫使附庸 or WarGoal.割地 && CanJimi(attacker, defender, strength))
            goal = WarGoal.羁縻;
        if (goal == WarGoal.收复 && !SetReclaim(war, attacker, data)) goal = WarGoal.割地;
        if (goal == WarGoal.割地) goal = Conquest(war, attacker, defender, data);
        data.war_goal = goal;
        switch (goal)
        {
            case WarGoal.吞并:
                foreach (City city in defender.cities ?? new List<City>())
                    if (city?.data != null && !city.isRekt()) data.goal_city_ids.Add(city.id);
                SetMain(data, defender.capital);
                break;
            case WarGoal.迫使附庸:
            case WarGoal.羁縻:
            case WarGoal.扶立:
            case WarGoal.改宗:
                SetMain(data, defender.capital, add: true);
                break;
            case WarGoal.继承:
                SetMain(data, defender.capital, add: true);
                data.goal_claimant_id = attacker.king?.id ?? -1L;
                break;
        }
        // 主城池开战时就已被攻方一边占了过半(别的仗留下的占领)：换目标里还没占的一座，免得开打当月就收场
        if (Main(data) is City main && Held(war, main))
        {
            City other = Cities(data).Where(city => city != main && !Held(war, city)).OrderByDescending(Size).FirstOrDefault();
            if (other != null) data.goal_main_city_id = other.id;
        }
    }

    private static List<City> Cities(WarExtension.WarExtraData data) =>
        (data.goal_city_ids ?? new List<long>()).Select(id => World.world.cities.get(id))
        .Where(city => city?.data != null && !city.isRekt()).ToList();

    private static void SetMain(WarExtension.WarExtraData data, City city, bool add = false)
    {
        if (city?.data == null || city.isRekt()) return;
        data.goal_main_city_id = city.id;
        if (add && !data.goal_city_ids.Contains(city.id)) data.goal_city_ids.Add(city.id);
    }

    private static bool CanInherit(Kingdom attacker, Kingdom defender, float strength) =>
        strength >= SuccessionStrength && !defender.IsEmpire() && attacker.king != null &&
        PersonalUnionService.IsUnionRegime(attacker) && !PersonalUnionService.IsAtFeudalRealmLimit(attacker.king) &&
        RoyalMarriageSystem.HasClaim(attacker, defender);

    private static bool CanJimi(Kingdom attacker, Kingdom defender, float strength)
    {
        Empire empire = attacker.GetEmpire();
        if (empire?.CoreKingdom == null || !JimiSystem.Enacted(empire) || strength < JimiStrength) return false;
        if (defender.IsEmpire() || defender.IsInEmpire() || (defender.cities?.Count ?? 0) > JimiMaxCities) return false;
        string ours = ReligionSphere.SphereOfCulture(CultureService.GetRealmCulture(empire.CoreKingdom));
        string theirs = ReligionSphere.SphereOfCulture(CultureService.GetRealmCulture(defender));
        return !string.IsNullOrEmpty(theirs) && !string.Equals(ours, theirs, StringComparison.Ordinal);
    }

    private static List<City> DefenderCities(War war) => war._list_defenders
        .Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
        .SelectMany(kingdom => kingdom.cities).Where(city => city?.data != null && !city.isRekt()).Distinct().ToList();

    private static int Size(City city) => (city.zones?.Count ?? 0) + (city.isCapitalCity() ? 1000 : 0);

    // 收复：对方一边手里本方法理的城；主城池是其中的法理都城，否则最大的一座
    private static bool SetReclaim(War war, Kingdom attacker, WarExtension.WarExtraData data)
    {
        List<City> claimed = WarGoalSystem.Claimed(attacker, DefenderCities(war));
        if (claimed.Count == 0) return false;
        foreach (City city in claimed) data.goal_city_ids.Add(city.id);
        City main = claimed.FirstOrDefault(city => city.GetTitle()?.title_capital == city) ??
                    claimed.OrderByDescending(Size).First();
        SetMain(data, main);
        data.goal_title_id = main.GetTitle()?.id ?? -1L;
        return true;
    }

    // 割地落到具体：争法理 → 夺城 → 争夺资源 → 侵略；一座接壤城都挑不出就求赔款
    private static WarGoal Conquest(War war, Kingdom attacker, Kingdom defender, WarExtension.WarExtraData data)
    {
        Kingdom realm = FrontSystem.Realm(attacker);
        List<City> border = BorderCities(war, attacker, defender);
        // 割地不要对方都城(要都城的是吞并、臣服)；只挨着都城的改求赔款
        border.RemoveAll(city => city == defender.capital);
        if (border.Count == 0) return WarGoal.赔款;
        var defenders = new HashSet<Kingdom>(war._list_defenders.Where(kingdom => kingdom?.data != null));
        // 争法理(同属一个帝国的两家，法理都算在帝国名下，不争)
        bool sameRealm = FrontSystem.Realm(defender) == realm;
        KingdomTitle bestTitle = null;
        List<City> bestCities = null;
        foreach (KingdomTitle title in border.Select(city => city.GetTitle()).Where(title => !sameRealm && title != null && !title.isRekt())
                     .Distinct())
        {
            if (!WarGoalSystem.ClaimsTitle(realm, title)) continue;
            List<City> theirs = title.getCities().Where(city => city?.data != null && !city.isRekt() && city.kingdom != null &&
                                                                 defenders.Contains(city.kingdom)).ToList();
            if (theirs.Count > (bestCities?.Count ?? 0))
            {
                bestTitle = title;
                bestCities = theirs;
            }
        }
        if (bestTitle != null)
        {
            data.goal_title_id = bestTitle.id;
            foreach (City city in bestCities) data.goal_city_ids.Add(city.id);
            City capital = bestTitle.title_capital;
            SetMain(data, capital != null && bestCities.Contains(capital) ? capital : bestCities.OrderByDescending(Size).First());
            return WarGoal.争法理;
        }
        // 夺城
        List<City> claimed = WarGoalSystem.Claimed(attacker, border);
        if (claimed.Count > 0)
        {
            Pick(data, claimed, border);
            return WarGoal.夺城;
        }
        // 争夺资源
        (City site, string resource) = ResourceTarget(attacker, realm, border);
        if (site != null)
        {
            data.goal_city_ids.Add(site.id);
            SetMain(data, site);
            data.goal_resource = resource;
            return WarGoal.争夺资源;
        }
        Pick(data, border, border);
        return WarGoal.侵略;
    }

    // 头一座挑最大的，第二座挑挨着它的(没有就挑次大的)
    private static void Pick(WarExtension.WarExtraData data, List<City> preferred, List<City> border)
    {
        City first = preferred.OrderByDescending(Size).First();
        data.goal_city_ids.Add(first.id);
        SetMain(data, first);
        City second = border.Where(city => city != first && first.neighbours_cities.Contains(city)).OrderByDescending(Size).FirstOrDefault() ??
                      preferred.Where(city => city != first).OrderByDescending(Size).FirstOrDefault();
        if (second != null && data.goal_city_ids.Count < MaxTargets) data.goal_city_ids.Add(second.id);
    }

    // 对方挨着攻方一边的城(攻方是帝国中央、两边不同属一个帝国时，帝国成员的城也算挨着)；跨海够不着的挑离攻方都城最近的两座
    private static List<City> BorderCities(War war, Kingdom attacker, Kingdom defender)
    {
        var result = new List<City>();
        Kingdom realm = FrontSystem.Realm(attacker);
        bool sameRealm = realm != null && FrontSystem.Realm(defender) == realm;
        bool Ours(Kingdom kingdom) => kingdom != null && (war._list_attackers.Contains(kingdom) ||
                                                          !sameRealm && FrontSystem.Realm(kingdom) == realm);
        foreach (City city in defender.cities ?? new List<City>())
        {
            if (city?.data == null || city.isRekt()) continue;
            foreach (City next in city.neighbours_cities)
                if (Ours(next?.kingdom))
                {
                    result.Add(city);
                    break;
                }
        }
        if (result.Count > 0 || attacker.capital?.data == null) return result;
        return (defender.cities ?? new List<City>()).Where(city => city?.data != null && !city.isRekt())
            .OrderBy(city => Toolbox.DistVec2Float(city.city_center, attacker.capital.city_center)).Take(MaxTargets).ToList();
    }

    // 接壤城里攻方一边没有、开采得了的矿藏(挑最值钱的)；游牧缺草场时也争大片牧场
    private static (City city, string resource) ResourceTarget(Kingdom attacker, Kingdom realm, List<City> border)
    {
        var ours = new HashSet<string>();
        bool ourPasture = false;
        foreach (City city in RealmCities(realm))
        {
            foreach ((string id, _, _, float chance) in MineralResourceSystem.Minerals)
                if (!ours.Contains(id) && MineralResourceSystem.HasDeposit(city, id, chance)) ours.Add(id);
            if (!ourPasture && Pastures(city) >= PastureSettlements) ourPasture = true;
        }
        int level = attacker.capital?.data != null ? MineralResourceSystem.MiningLevel(attacker.capital) : 1;
        City best = null;
        string bestId = null;
        int bestValue = 0;
        foreach (City city in border)
        {
            foreach ((string id, int tier, _, float chance) in MineralResourceSystem.Minerals)
            {
                if (ours.Contains(id) || tier > level + 1 || !MineralResourceSystem.HasDeposit(city, id, chance)) continue;
                int value = Value(id);
                if (value <= bestValue) continue;
                best = city;
                bestId = id;
                bestValue = value;
            }
            if (!ourPasture && FrontierRelations.IsNomadRealm(attacker) && bestValue < 4 && Pastures(city) >= PastureSettlements)
            {
                best = city;
                bestId = PastureResource;
                bestValue = 4;
            }
        }
        return (best, bestId);
    }

    private static int Value(string id) => id switch
    {
        "uranium" => 10, "oil" => 9, "rare_earth" => 8, "gold" => 7, "silver" => 6, "saltpeter" => 5, "aluminium" => 5,
        "coal" => 4, "sulfur" => 4, "copper" => 3, _ => 2
    };

    private static int Pastures(City city) =>
        RuralSettlementSystem.InCity(city).Count(settlement => settlement.trade == LivelihoodSystem.Pasture);

    private static IEnumerable<City> RealmCities(Kingdom realm)
    {
        Empire empire = realm?.GetEmpire();
        IEnumerable<Kingdom> kingdoms = empire?.kingdoms_list != null ? empire.kingdoms_list : new List<Kingdom> { realm };
        return kingdoms.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
            .SelectMany(kingdom => kingdom.cities).Where(city => city?.data != null && !city.isRekt());
    }

    #endregion

    #region 攻下主城池

    // 城归攻方一边，或攻方一边占了它过半地块。只认这场仗里的两边：帝国里的叛乱、伐不臣两边同属一个帝国，按帝国比会把京城当成已到手
    public static bool Held(War war, City city) => HeldShareOf(war, city) >= HeldShare;

    // 攻方一边占了这座城几成(城已归攻方一边为 1)
    public static float HeldShareOf(War war, City city)
    {
        if (war?.data == null || city?.data == null || city.isRekt()) return 0f;
        if (city.kingdom != null && war._list_attackers.Contains(city.kingdom)) return 1f;
        int total = city.zones?.Count ?? 0;
        if (total == 0) return 0f;
        int held = 0;
        foreach (Kingdom kingdom in war._list_attackers)
            if (kingdom?.data != null && !kingdom.isRekt()) held += city.GetOccupiedTileZoneCount(kingdom);
        return Math.Min(1f, held / (float)total);
    }

    // 每场仗约每月一次(WarPatch.update)
    public static void Check(War war)
    {
        if (war?.data == null || war.hasEnded() || World.world == null) return;
        double now = World.world.getCurWorldTime();
        if (LastCheck.TryGetValue(war.data.id, out double at) && now >= at && now - at < CheckSeconds) return;
        LastCheck[war.data.id] = now;
        try
        {
            if (TakesCapital(war)) return;
            WarExtension.WarExtraData data = war.GetOrCreate();
            WarGoal goal = WarGoalSystem.Get(war);
            if (!Achievable(goal) || data.goal_achieved || data.goal_main_city_id < 0) return;
            City main = World.world.cities.get(data.goal_main_city_id);
            if (main?.data != null && !main.isRekt() && Held(war, main))
            {
                data.goal_achieved = true;
                WarChronicle.Record(war, WarChronicle.Goal, string.Format(LM.Get("war_log_goal_achieved"),
                    war.main_attacker.GetKingdomName(), main.GetCityName()));
                World.world.wars.endWar(war, WarWinner.Attackers);
                return;
            }
            if (main?.data == null || main.isRekt() || !OnDefenderSide(war, main)) Retarget(war, data);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][战争目标] 查主城池失败: {exception.Message}");
        }
    }

    private static bool OnDefenderSide(War war, City city) => city.kingdom != null && war._list_defenders.Contains(city.kingdom);

    // 主城池落到第三家手里或没了：换目标里还在对方一边的另一座；都没了就改为赔款
    private static void Retarget(War war, WarExtension.WarExtraData data)
    {
        City next = (data.goal_city_ids ?? new List<long>()).Select(id => World.world.cities.get(id))
            .Where(city => city?.data != null && !city.isRekt() && OnDefenderSide(war, city)).OrderByDescending(Size).FirstOrDefault();
        if (next == null && data.war_goal is WarGoal.吞并 or WarGoal.迫使附庸 or WarGoal.羁縻 or WarGoal.继承 or WarGoal.扶立 or WarGoal.改宗)
            next = war.main_defender?.capital;
        if (next?.data != null && !next.isRekt())
        {
            data.goal_main_city_id = next.id;
            return;
        }
        data.war_goal = WarGoal.赔款;
        data.goal_main_city_id = -1L;
        WarChronicle.Record(war, WarChronicle.Goal, string.Format(LM.Get("war_log_goal_lost"), war.main_attacker.GetKingdomName()));
    }

    // 叛乱、革命攻下京城即胜(清君侧另有规矩，见 FactionPurgeSystem；藩王争位见 CityPatch)：认开战时的京城(朝廷迁都不算数)，
    // 只占了过半地块的，京城随和约归起事一方(议和时会清掉占领)
    private static bool TakesCapital(War war)
    {
        if (!RebelWar(war)) return false;
        City capital = RebelTarget(war);
        if (capital?.data == null || capital.isRekt() || !Held(war, capital)) return false;
        WarChronicle.Record(war, WarChronicle.Goal, string.Format(LM.Get("war_log_capital_taken"),
            war.main_attacker.GetKingdomName(), capital.GetCityName()));
        if (capital.kingdom != null && !war._list_attackers.Contains(capital.kingdom))
            WarGoalSystem.QueueCession(capital, war.main_attacker);
        World.world.wars.endWar(war, WarWinner.Attackers);
        return true;
    }

    // 叛乱要攻的京城：开战时记下的(见 WarPatch.update)，没记的按眼下的
    public static City RebelTarget(War war)
    {
        long id = war.GetOrCreate().war_goal_capital_id;
        City city = id >= 0 ? World.world?.cities?.get(id) : null;
        return city?.data != null && !city.isRekt() ? city : war.main_defender?.capital;
    }

    public static bool RebelWar(War war) => war?.data != null &&
                                            war.GetEmpireWarType() is EmpireWarType.派系叛乱 or EmpireWarType.地方叛乱;

    #endregion

    #region 议和结算

    // 攻方胜时按具体目标结算(WarGoalSystem.Settle)；返回是否已处理
    internal static bool SettleWin(War war, Kingdom attacker, Kingdom defender, WarGoal goal)
    {
        WarExtension.WarExtraData data = war.GetOrCreate();
        switch (goal)
        {
            case WarGoal.夺城:
            case WarGoal.侵略:
            case WarGoal.争夺资源:
            case WarGoal.争法理:
            {
                List<City> targets = Targets(war, data);
                bool got = data.goal_achieved || Main(data) is City main && Held(war, main);
                List<City> ceded = got ? targets : targets.Where(city => Held(war, city)).ToList();
                foreach (City city in ceded) WarGoalSystem.QueueCession(city, attacker);
                if (ceded.Count > 0)
                    TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_ceded_named"), defender.GetKingdomName(),
                        attacker.GetKingdomName(), string.Join(LM.Get("list_separator"), ceded.Select(city => city.GetCityName()))), attacker);
                else if (goal == WarGoal.争夺资源 && !string.IsNullOrEmpty(data.goal_resource))
                    ResourceTributeSystem.Impose(defender, attacker, data.goal_resource);
                else WarReparationsSystem.Impose(defender, attacker);
                return true;
            }
            case WarGoal.羁縻:
                Pending.Add(() =>
                {
                    if (!Jimi(attacker, defender)) WarGoalSystem.QueueVassal(attacker, defender);
                });
                return true;
            case WarGoal.继承:
                Pending.Add(() =>
                {
                    if (!Inherit(war, data, attacker, defender) && !WarGoalSystem.TryInstall(attacker, defender))
                        WarGoalSystem.QueueVassal(attacker, defender);
                });
                return true;
        }
        return false;
    }

    public static City Main(WarExtension.WarExtraData data) =>
        data == null || data.goal_main_city_id < 0 ? null : World.world?.cities?.get(data.goal_main_city_id);

    // 目标城里还在对方一边手里的(落到第三家、已单独议和的盟国手里的不算)
    public static List<City> Targets(War war, WarExtension.WarExtraData data) =>
        (data?.goal_city_ids ?? new List<long>()).Select(id => World.world.cities.get(id))
        .Where(city => city?.data != null && !city.isRekt() && city.kingdom != null && war._list_defenders.Contains(city.kingdom))
        .ToList();

    // 内附为羁縻州(同降部戍边，见 FrontierRelations.SettleSurrenderedTribe，不限游牧)
    private static bool Jimi(Kingdom lord, Kingdom subject)
    {
        Empire empire = lord?.GetEmpire();
        if (empire?.CoreKingdom == null || subject?.data == null || subject.isRekt() || subject.IsEmpire() || subject.IsInEmpire() ||
            !JimiSystem.Enacted(empire)) return false;
        if (subject.HasTakenAlliance()) subject.RemoveTakenAlliance();
        empire.join(subject, true, true);
        if (subject.GetEmpire() != empire) return false;
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(subject);
        string text = string.Format(LM.Get("war_peace_jimi"), subject.GetKingdomName(), empire.GetEmpireName());
        empire.RecordHistory(directContent: text, kingdomId: subject.id);
        TranslateHelper.LogEventMessage(text, lord);
        return true;
    }

    // 兼领王位：宣称者(开战时的攻方君主)还在、还能兼领
    private static bool Inherit(War war, WarExtension.WarExtraData data, Kingdom attacker, Kingdom defender)
    {
        Actor claimant = World.world.units.get(data.goal_claimant_id) ?? attacker?.king;
        if (claimant?.data == null || claimant.isRekt() || !claimant.isAlive() || defender?.data == null || defender.isRekt() ||
            defender.IsEmpire() || !PersonalUnionService.IsUnionRegime(attacker) || PersonalUnionService.IsAtFeudalRealmLimit(claimant))
            return false;
        Actor former = defender.king;
        PersonalUnionService.CrownInUnion(defender, claimant);
        if (defender.king != claimant) return false;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_inherited"), claimant.getName(), defender.GetKingdomName(),
            former?.getName() ?? ""), attacker);
        return true;
    }

    // 和约里的改归属在结束战争之后几帧做(WarGoalSystem.Tick 开头调)
    internal static void Tick()
    {
        while (Pending.Count > 0)
        {
            Action action = Pending[0];
            Pending.RemoveAt(0);
            try
            {
                action();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][战争目标] 和约执行失败: {exception.Message}");
            }
        }
    }

    #endregion
}

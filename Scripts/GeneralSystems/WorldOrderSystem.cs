using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 世界格局(用户 2026-11-24 "现代可以再更新什么玩法，有人说现代之后就很无聊了")。以前进了现代，国与国之间除了核威慑
// 再没有新东西：理念不影响好感与结盟，"世界大战"只改个名字。现在近代国家(见 StateModernization.IsModernPower)之间：
//   · 理念定亲疏：立国理念相近的彼此亲善，相去甚远的互相提防，截然对立的敌视；刚闹完革命、还在激进期的政权，君主国都防着它；
//   · 阵营：两个理念截然对立的大国够得着对方时，各以本国理念为旗号纠合同道——理念相近的国家陆续入盟(受对面威胁的更急)，
//     同阵营互不开战、一国挨打盟友来援；理念变了的退盟，盟主倒了由同道的大国接手，没人接就散；
//   · 冷战：理念相左的两个阵营之间紧张逐年积累，时有危机(代理人战争、军备、核、边境)；军备竞赛让双方平时多养兵多拨军费，
//     紧张到六成以上两边断绝通商。紧张满了：两边盟主都有核、或够不着，就缓和几年(核恐怖平衡下盟主之间不直接开战)；
//     不然就打起来——盟友依约参战，就是世界大战。一方盟主国力衰到对方四成以下、或改旗易帜，冷战不战而终；
//   · 万国公会：近代国家够多时由最强的大国发起(世界大战打完由战胜国发起)，各国陆续入会。会员挨打、或会员打人，
//     国力最强的五席议处：兴兵者自己在席、或它的盟友在席就否决；通过则制裁——各会员与之断绝通商。屡屡否决，公会威信扫地而散；
//   · 世界大战：参战各国倾国而战(军费照生死存亡之战拨)，打到第三年起工农怨气逐年加码；打完战败国正统大跌、革命形势陡增，
//     战胜国发起万国公会。
// 世界法则「禁止AI结盟」关着阵营与公会的组建，「禁止AI宣战」照旧拦着开战(见 WarGate)。
public static class WorldOrderSystem
{
    private const float CloseIdeology = 30f, FarIdeology = 60f, OpposedIdeology = 90f;
    private const int MaxGreatPowers = 6, GreatPowerMinCities = 5;
    private const float GreatPowerShare = 0.3f;
    // 盟主保位的门槛(最强国国力的这一成)：比立阵营的门槛低。以前一跌出大国就散，1937 剧本里苏联开局国力 561、次年 430，
    // 跌破最强国(1686)的三成，社会主义阵营立了当年就"群龙无首，土崩瓦解"(用户 2026-11-25 "修这个")
    private const float LeaderKeepShare = 0.2f;
    private const float JoinChance = 0.25f, ThreatenedJoinChance = 0.6f, JoinMinOpinion = -20f;
    private const int UnopposedYears = 15;
    private const int KinOpinion = 25, WaryOpinion = -20, FoeOpinion = -40, RevolutionaryOpinion = -30;
    private const int BlocOpinion = 40, RivalOpinion = -40, SanctionOpinion = -20;
    private const float RadicalRegime = 40f;
    private const float StartTension = 30f, TensionDrift = 3f, ProxyTension = 5f, DetenteCooling = 4f;
    private const float CrisisBase = 0.08f, DetenteTension = 55f, CollapseShare = 0.4f;
    // 衰落：国力对比(本方 ÷ 对方)掉到开始对峙时的这一成以下才算(见 UpdateColdWars)
    private const float DeclineShare = 0.6f;
    public const float IronCurtainAt = 60f;
    private const int DetenteYears = 8, CrisisGapYears = 2;
    private const float AidChance = 0.2f;
    private const float LeagueFoundChance = 0.05f, LeagueJoinChance = 0.4f;
    private const int LeagueMinStates = 6, LeagueMinGreats = 3, CouncilSeats = 5, SanctionYears = 10;
    private const int WearyFromYear = 2;
    private const float ArmsPoolPerLevel = 0.08f, ArmsBudgetPerLevel = 0.08f;

    private static WorldOrderData _data = new WorldOrderData();
    private static double _lastScan = -1d;
    private static readonly Dictionary<long, BlocData> BlocIndex = new();
    private static bool _indexDirty = true;

    #region 存读档

    public static void Load(WorldOrderData data)
    {
        _data = data ?? new WorldOrderData();
        _data.blocs ??= new List<BlocData>();
        _data.cold_wars ??= new List<ColdWarData>();
        _data.sanctions ??= new List<SanctionData>();
        _data.trade_wars ??= new List<TradeWarData>();
        _data.space_levels ??= new Dictionary<long, int>();
        _data.space_progress ??= new Dictionary<long, float>();
        _data.space_firsts ??= new List<SpaceFirstData>();
        _lastScan = -1d;
        _indexDirty = true;
        Profiles.Clear();
    }

    public static WorldOrderData Save() => _data;

    // 关税战、世界性大萧条也记在这份数据里(见 TradeWarSystem、EconomicCycleSystem)
    internal static WorldOrderData Data => _data;
    public static bool WorldDepression => _data.depression_since >= 0d;
    public static double DepressionSince => _data.depression_since;

    public static void ResetWorldState() => Load(null);

    public static IReadOnlyList<BlocData> Blocs => _data.blocs;
    public static IReadOnlyList<ColdWarData> ColdWars => _data.cold_wars;
    public static LeagueData League => _data.league;
    public static IReadOnlyList<SanctionData> Sanctions => _data.sanctions;

    #endregion

    #region 查询

    private static Empire Living(long id)
    {
        Empire empire = id > 0 ? ModClass.EMPIRE_MANAGER?.get(id) : null;
        return empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom?.data != null &&
               !empire.CoreKingdom.isRekt() ? empire : null;
    }

    public static Empire LeaderOf(BlocData bloc) => bloc == null ? null : Living(bloc.leader_id);

    public static BlocData Bloc(int id) => _data.blocs.FirstOrDefault(bloc => bloc.id == id);

    public static string BlocName(BlocData bloc) => bloc == null ? "" : string.Format(LM.Get("world_bloc_name"),
        PartySystem.GetIdeologyName(Enum.IsDefined(typeof(PartyIdeology), bloc.ideology) ? (PartyIdeology)bloc.ideology : PartyIdeology.Centrism));

    private static void Reindex()
    {
        if (!_indexDirty) return;
        _indexDirty = false;
        BlocIndex.Clear();
        foreach (BlocData bloc in _data.blocs)
            foreach (long id in bloc.members) BlocIndex[id] = bloc;
    }

    public static BlocData BlocOf(Empire empire)
    {
        if (empire?.data == null || _data.blocs.Count == 0) return null;
        Reindex();
        return BlocIndex.TryGetValue(empire.id, out BlocData bloc) ? bloc : null;
    }

    // 一国所在的阵营：自己的政府在哪个阵营；封臣跟着宗主
    public static BlocData BlocOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || _data.blocs.Count == 0) return null;
        BlocData bloc = BlocOf(kingdom.GetEmpire());
        if (bloc != null) return bloc;
        Kingdom overlord = FeudalVassalService.GetOverlord(kingdom);
        return overlord != null && overlord != kingdom ? BlocOf(overlord.GetEmpire()) : null;
    }

    public static bool SameBloc(Kingdom a, Kingdom b)
    {
        if (_data.blocs.Count == 0 || a == null || b == null) return false;
        BlocData bloc = BlocOf(a);
        return bloc != null && bloc == BlocOf(b);
    }

    public static ColdWarData ColdWarBetween(BlocData x, BlocData y) => x == null || y == null || x == y ? null
        : _data.cold_wars.FirstOrDefault(war => war.a == x.id && war.b == y.id || war.a == y.id && war.b == x.id);

    public static ColdWarData ColdWarBetween(Kingdom a, Kingdom b) =>
        _data.cold_wars.Count == 0 ? null : ColdWarBetween(BlocOf(a), BlocOf(b));

    // 核恐怖平衡：冷战双方的盟主都有核，盟主之间不直接开战(各自的盟友、第三国的仗不拦)
    public static bool MutuallyDeterred(Kingdom attacker, Kingdom target)
    {
        if (_data.cold_wars.Count == 0) return false;
        BlocData x = BlocOf(attacker), y = BlocOf(target);
        if (ColdWarBetween(x, y) == null) return false;
        Kingdom lx = LeaderOf(x)?.CoreKingdom, ly = LeaderOf(y)?.CoreKingdom;
        if (lx == null || ly == null || Realm(attacker) != lx || Realm(target) != ly) return false;
        return Compatibility.NuclearDoctrineSystem.IsNuclearPower(lx) && Compatibility.NuclearDoctrineSystem.IsNuclearPower(ly);
    }

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    public static bool IsSanctioned(Empire empire) =>
        empire?.data != null && _data.sanctions.Any(sanction => sanction.target_id == empire.id);

    public static bool IsLeagueMember(Empire empire) =>
        empire?.data != null && _data.league?.members.Contains(empire.id) == true;

    // 断绝通商(见 MarketSystem.Embargoed)：万国公会制裁的国与各会员之间；冷战紧张过了 IronCurtainAt 的两个阵营之间
    public static bool TradeCut(Kingdom a, Kingdom b)
    {
        if (_data.sanctions.Count == 0 && _data.cold_wars.Count == 0) return false;
        Empire ea = a?.GetEmpire(), eb = b?.GetEmpire();
        if (ea == null || eb == null || ea == eb) return false;
        foreach (SanctionData sanction in _data.sanctions)
        {
            if (sanction.by_bloc >= 0) continue;
            if (sanction.target_id == ea.id && IsLeagueMember(eb) || sanction.target_id == eb.id && IsLeagueMember(ea)) return true;
        }
        return ColdWarBetween(BlocOf(ea), BlocOf(eb)) is { } war && war.tension >= IronCurtainAt;
    }

    // 军备竞赛(见 CorpsSystem)：平时兵源多征的成数、军费多拨的份额
    public static float ArmsPool(Kingdom kingdom) => (BlocOf(kingdom)?.arms_level ?? 0) * ArmsPoolPerLevel;
    public static float ArmsBudget(Kingdom kingdom) => (BlocOf(kingdom)?.arms_level ?? 0) * ArmsBudgetPerLevel;

    // 正在打世界大战(见 WorldWarSystem)：倾国而战
    public static bool InWorldWar(Kingdom kingdom)
    {
        foreach (Kingdom side in new[] { kingdom, Realm(kingdom) })
        {
            if (side?.data == null || side.isRekt()) continue;
            foreach (War war in side.getWars())
                if (war?.data != null && !war.hasEnded() && war.GetOrCreate().world_war) return true;
        }
        return false;
    }

    #endregion

    #region 好感

    private struct Profile
    {
        public bool modern, monarchy, radical;
        public PartyIdeology ideology;
        public float at;
    }

    private static readonly Dictionary<long, Profile> Profiles = new();
    private const float ProfileSeconds = 5f;

    private static Profile ProfileOf(Kingdom kingdom)
    {
        Kingdom top = Realm(kingdom);
        float now = Time.unscaledTime;
        if (Profiles.TryGetValue(top.id, out Profile hit) && now >= hit.at && now - hit.at < ProfileSeconds) return hit;
        Empire empire = top.GetEmpire();
        var state = empire?.data?.constitutional_economy;
        var profile = new Profile
        {
            modern = StateModernization.IsModernPower(top),
            monarchy = RegimeManager.IsMonarchy(top.GetRegime()?.type) && !RepublicSystem.IsRepublic(empire),
            ideology = empire != null ? Ideology(empire) : IdeologyFamilies.StateIdeology(top),
            radical = state != null && state.post_revolution_since >= 0d && state.post_revolution_phase == 1 &&
                      state.post_revolution_radical >= RadicalRegime,
            at = now
        };
        if (Profiles.Count > 512) Profiles.Clear();
        Profiles[top.id] = profile;
        return profile;
    }

    // 理念定亲疏(见 EmpireCraftOpinionAddition)
    public static int IdeologyOpinion(Kingdom main, Kingdom target)
    {
        if (main?.data == null || target?.data == null || main == target || main.IsInSameEmpire(target)) return 0;
        Profile a = ProfileOf(main), b = ProfileOf(target);
        if (!a.modern || !b.modern) return 0;
        float distance = PartySystem.IdeologyDistance(a.ideology, b.ideology);
        int value = distance <= CloseIdeology ? KinOpinion : distance >= OpposedIdeology ? FoeOpinion
            : distance >= FarIdeology ? WaryOpinion : 0;
        if (a.monarchy && b.radical || b.monarchy && a.radical) value += RevolutionaryOpinion;
        return value;
    }

    // 阵营与制裁
    public static int BlocOpinionOf(Kingdom main, Kingdom target)
    {
        if (_data.blocs.Count == 0 && _data.sanctions.Count == 0) return 0;
        if (main?.data == null || target?.data == null || main == target || main.IsInSameEmpire(target)) return 0;
        int value = 0;
        BlocData x = BlocOf(main), y = BlocOf(target);
        if (x != null && x == y) value += BlocOpinion;
        else if (ColdWarBetween(x, y) != null) value += RivalOpinion;
        if (_data.sanctions.Count > 0 && _data.league != null && IsLeagueMember(main.GetEmpire()) &&
            _data.sanctions.Any(sanction => sanction.by_bloc < 0 && sanction.target_id == target.GetEmpire()?.id))
            value += SanctionOpinion;
        return value;
    }

    #endregion

    #region 年度

    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.EMPIRE_MANAGER == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (now < _lastScan) _lastScan = -1d;
        if (_lastScan >= 0d && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        try
        {
            Scan(now);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界格局] 年度结算失败: {exception}");
        }
        _indexDirty = true;
    }

    private static int Cities(Empire empire) =>
        empire.kingdoms_list?.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt()).Sum(kingdom => kingdom.cities.Count) ?? 0;

    // 近代的主权国家：组建了政府、够得上近代国家，不是别国的附从与封臣
    private static List<Empire> States() => ModClass.EMPIRE_MANAGER.Where(empire =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom?.data != null &&
        !empire.CoreKingdom.isRekt() && empire.CoreKingdom.hasKing() && StateModernization.IsModernPower(empire.CoreKingdom) &&
        !WarlordEraSystem.Dependent(empire.CoreKingdom) && FeudalVassalService.GetOverlord(empire.CoreKingdom) == null &&
        Cities(empire) > 0).ToList();

    // 一国的旗号看立国理念(宪法定的、共和国建国时的，没有才看执政理念，见 WarlordEraSystem.GovernmentIdeology)：
    // 以前看执政理念，一换届阵营就跟着变色、盟主"改旗易帜"阵营瓦解
    private static PartyIdeology Ideology(Empire empire) => WarlordEraSystem.GovernmentIdeology(empire);

    private static float Distance(Empire a, Empire b) => PartySystem.IdeologyDistance(Ideology(a), Ideology(b));

    private static float Distance(Empire empire, BlocData bloc) =>
        PartySystem.IdeologyDistance(Ideology(empire), (PartyIdeology)bloc.ideology);

    private static bool Contact(Empire a, Empire b)
    {
        Kingdom ca = a.CoreKingdom, cb = b.CoreKingdom;
        return ConnectivitySystem.CanContactEither(ca, cb) ?? WarReach.Reachable(ca, cb);
    }

    private static void Scan(double now)
    {
        List<Empire> states = States();
        if (states.Count < 2 && _data.blocs.Count == 0 && _data.league == null && _data.sanctions.Count == 0 &&
            _data.trade_wars.Count == 0 && _data.depression_since < 0d) return;
        Dictionary<long, double> power = states.ToDictionary(empire => empire.id, empire => empire.GetNationalPower());
        double strongest = power.Count == 0 ? 0d : power.Values.Max();
        List<Empire> greats = states.Where(empire => power[empire.id] >= strongest * GreatPowerShare &&
                                                     Cities(empire) >= GreatPowerMinCities)
            .OrderByDescending(empire => power[empire.id]).Take(MaxGreatPowers).ToList();
        var alive = new HashSet<long>(states.Select(empire => empire.id));

        PruneBlocs(alive, greats, power, now);
        if (!DiplomacyLaws.AllianceBlocked)
        {
            FormBlocs(greats, now);
            JoinWartime(states);
            JoinBlocs(states);
        }
        _indexDirty = true;
        UpdateColdWars(power, now);
        UpdateLeague(states, greats, power, alive, now);
        ExpireSanctions(now);
        WarWeariness();
        // 关税战的升级与收场、世界性大萧条(见 TradeWarSystem、EconomicCycleSystem)
        TradeWarSystem.Yearly(states, now);
        EconomicCycleSystem.WorldScan(_data, greats, now);
        // 原子时代以后：制空权与战略轰炸、航天竞赛、去殖民浪潮(见各系统)
        AirPowerSystem.Yearly(now);
        SpaceRaceSystem.Yearly(states, greats, now);
        DecolonizationSystem.Yearly(states, greats, now);
        LogService.LogInfo($"[EmpireCraft][世界格局] 近代国家 {states.Count}，大国 {greats.Count}，阵营 {_data.blocs.Count}，" +
                           $"冷战 {_data.cold_wars.Count}，公会 {(_data.league == null ? "无" : _data.league.members.Count + " 国")}，制裁 {_data.sanctions.Count}");
    }

    // 记一笔：主角的史书与屏幕；其余只进各自的史书
    internal static void Record(string text, Empire main, IEnumerable<Empire> others = null)
    {
        if (main != null) EventRecorder.Record(main, text);
        if (others == null) return;
        foreach (Empire other in others.Distinct())
            if (other != null && other != main && !other.isRekt() && !other.IsArchived())
                other.RecordHistory(directContent: text, kingdomId: other.CoreKingdom?.id ?? -1L);
    }

    #endregion

    #region 阵营

    private static void PruneBlocs(HashSet<long> alive, List<Empire> greats, Dictionary<long, double> power, double now)
    {
        double strongest = power.Count == 0 ? 0d : power.Values.Max();
        bool Keeps(Empire empire) => empire != null && alive.Contains(empire.id) && power.TryGetValue(empire.id, out double value) &&
                                     value >= strongest * LeaderKeepShare && Cities(empire) >= GreatPowerMinCities;
        foreach (BlocData bloc in _data.blocs.ToList())
        {
            bloc.members.RemoveAll(id => !alive.Contains(id));
            bloc.wartime ??= new List<long>();
            bloc.wartime.RemoveAll(id => !bloc.members.Contains(id));
            Empire leader = Living(bloc.leader_id);
            bool leaderFit = Keeps(leader) &&
                             PartySystem.IdeologyDistance(Ideology(leader), (PartyIdeology)bloc.ideology) < FarIdeology;
            if (!leaderFit)
            {
                // 盟主倒了、衰了、改旗易帜了：同道的大国接手，没人接就散
                Empire successor = bloc.members.Select(Living).Where(empire => empire != null && empire != leader && Keeps(empire) &&
                                                                              Distance(empire, bloc) <= CloseIdeology)
                    .OrderByDescending(empire => power[empire.id]).FirstOrDefault();
                if (successor == null)
                {
                    Dissolve(bloc, leader, "world_bloc_collapsed_history");
                    continue;
                }
                string name = BlocName(bloc);
                if (leader != null && Distance(leader, bloc) >= FarIdeology) bloc.members.Remove(leader.id);
                bloc.leader_id = successor.id;
                Record(string.Format(LM.Get("world_bloc_succession_history"), successor.GetEmpireFullName(), name), successor,
                    bloc.members.Select(Living));
                leader = successor;
            }
            // 盟主的理念就是阵营的旗号(小幅漂移跟着走)
            bloc.ideology = (int)Ideology(leader);
            foreach (long id in bloc.members.ToList())
            {
                Empire member = Living(id);
                if (member == null || member == leader) continue;
                if (Distance(member, bloc) < FarIdeology)
                {
                    // 理念本就合得来的战时盟友：仗打完留下，算正经的盟友
                    if (bloc.wartime.Contains(id) && CommonEnemy(member, bloc) == null) bloc.wartime.Remove(id);
                    continue;
                }
                // 战时盟友：还在共同对敌就留着
                if (bloc.wartime.Contains(id))
                {
                    if (CommonEnemy(member, bloc) != null) continue;
                    bloc.wartime.Remove(id);
                    bloc.members.Remove(id);
                    Record(string.Format(LM.Get("world_bloc_left_wartime_history"), member.GetEmpireFullName(), BlocName(bloc)), member,
                        new[] { leader });
                    continue;
                }
                bloc.members.Remove(id);
                Record(string.Format(LM.Get("world_bloc_left_history"), member.GetEmpireFullName(), BlocName(bloc)), member,
                    new[] { leader });
            }
            if (!bloc.members.Contains(leader.id)) bloc.members.Add(leader.id);
        }
        _indexDirty = true;
    }

    private static void Dissolve(BlocData bloc, Empire leader, string key)
    {
        string name = BlocName(bloc);
        List<Empire> members = bloc.members.Select(Living).Where(member => member != null).ToList();
        _data.blocs.Remove(bloc);
        _indexDirty = true;
        Empire speaker = leader ?? members.FirstOrDefault();
        if (speaker != null) Record(string.Format(LM.Get(key), name), speaker, members);
        foreach (ColdWarData war in _data.cold_wars.Where(war => war.a == bloc.id || war.b == bloc.id).ToList())
            EndColdWar(war, Bloc(war.a == bloc.id ? war.b : war.a), members);
    }

    private static void FormBlocs(List<Empire> greats, double now)
    {
        foreach (Empire great in greats)
        {
            if (BlocOf(great) != null) continue;
            // 正在交战的也算(以前要求没在交战：两个对立的大国开局就在打，阵营永远立不起来，仗也就只是两国的仗)
            Empire rival = greats.FirstOrDefault(other => other != great && Distance(great, other) >= OpposedIdeology &&
                                                          (Contact(great, other) || great.CoreKingdom.isInWarWith(other.CoreKingdom)));
            if (rival == null) continue;
            // 已有同道的阵营就入盟，不另起炉灶；够不着它的盟主就先等着(免得同一面旗号下立两个阵营)
            BlocData kin = _data.blocs.Where(bloc => Distance(great, bloc) <= CloseIdeology && LeaderOf(bloc) != null)
                .OrderBy(bloc => Distance(great, bloc)).FirstOrDefault();
            if (kin != null)
            {
                if (Contact(great, LeaderOf(kin))) Join(kin, great);
                continue;
            }
            var bloc = new BlocData
            {
                id = _data.next_bloc_id++, leader_id = great.id, ideology = (int)Ideology(great), since = now,
                members = new List<long> { great.id }
            };
            _data.blocs.Add(bloc);
            _indexDirty = true;
            Record(string.Format(LM.Get("world_bloc_formed_history"), great.GetEmpireFullName(),
                PartySystem.GetIdeologyName(Ideology(great)), BlocName(bloc), rival.GetEmpireFullName()), great, new[] { rival });
        }
    }

    private static void Join(BlocData bloc, Empire state)
    {
        bloc.members.Add(state.id);
        _indexDirty = true;
        EventRecorder.RecordRoutine(state, string.Format(LM.Get("world_bloc_joined_history"), state.GetEmpireFullName(), BlocName(bloc)));
    }

    // 共同的敌人(用户 2026-11-25 "先修世界阵营"；同盟国里理念各异，只因都在打轴心国)：这个阵营的盟主正在打的另一个阵营的盟主，
    // 也正是这一国在打的——返回那个敌对阵营
    private static BlocData CommonEnemy(Empire state, BlocData bloc)
    {
        Kingdom core = state?.CoreKingdom, lead = LeaderOf(bloc)?.CoreKingdom;
        if (core == null || lead == null) return null;
        foreach (BlocData enemy in _data.blocs)
        {
            Kingdom foe = enemy == bloc ? null : LeaderOf(enemy)?.CoreKingdom;
            if (foe != null && foe != core && lead.isInWarWith(foe) && core.isInWarWith(foe)) return enemy;
        }
        return null;
    }

    // 战时入盟：不看理念远近，只要不是敌对阵营的同道、没在打这个阵营的盟主、够得着它(或正并肩打着同一个敌人)
    private static void JoinWartime(List<Empire> states)
    {
        foreach (Empire state in states)
        {
            if (BlocOf(state) != null) continue;
            Kingdom core = state.CoreKingdom;
            foreach (BlocData bloc in _data.blocs)
            {
                Empire leader = LeaderOf(bloc);
                BlocData enemy = leader == null || leader == state || core.isInWarWith(leader.CoreKingdom) ? null : CommonEnemy(state, bloc);
                if (enemy == null || Distance(state, enemy) <= CloseIdeology) continue;
                bloc.members.Add(state.id);
                (bloc.wartime ??= new List<long>()).Add(state.id);
                _indexDirty = true;
                Record(string.Format(LM.Get("world_bloc_joined_wartime_history"), state.GetEmpireFullName(), BlocName(bloc),
                    LeaderOf(enemy).GetEmpireFullName()), state, new[] { leader });
                break;
            }
        }
    }

    private static void JoinBlocs(List<Empire> states)
    {
        if (_data.blocs.Count == 0) return;
        foreach (Empire state in states)
        {
            if (BlocOf(state) != null) continue;
            Kingdom core = state.CoreKingdom;
            BlocData best = _data.blocs.Where(bloc => Distance(state, bloc) <= CloseIdeology && LeaderOf(bloc) is Empire leader &&
                                                     !core.isInWarWith(leader.CoreKingdom) &&
                                                     (World.world.diplomacy?.getOpinion(core, leader.CoreKingdom)?.total ?? 0f) >= JoinMinOpinion &&
                                                     Contact(state, leader))
                .OrderBy(bloc => Distance(state, bloc)).FirstOrDefault();
            if (best == null) continue;
            if (UnityEngine.Random.value >= (Threatened(state, best) ? ThreatenedJoinChance : JoinChance)) continue;
            Join(best, state);
        }
    }

    // 对面阵营的国与它接壤或正在打它：入盟更急
    private static bool Threatened(Empire state, BlocData candidate)
    {
        foreach (ColdWarData war in _data.cold_wars)
        {
            if (war.a != candidate.id && war.b != candidate.id) continue;
            BlocData rival = Bloc(war.a == candidate.id ? war.b : war.a);
            if (rival == null) continue;
            foreach (long id in rival.members)
            {
                Kingdom other = Living(id)?.CoreKingdom;
                if (other != null && (state.CoreKingdom.isInWarWith(other) || state.IsNeighbourWith(other))) return true;
            }
        }
        return false;
    }

    // 盟友来援(王国 AI 每月，帝国核心自己查；同跨文明通好的依约相助)：同阵营的国挨了阵营外的打
    public static void Aid(Kingdom core)
    {
        if (_data.blocs.Count == 0) return;
        Empire empire = core?.GetEmpire();
        if (empire == null || empire.CoreKingdom != core || core.hasEnemies()) return;
        BlocData bloc = BlocOf(empire);
        if (bloc == null || UnityEngine.Random.value >= AidChance * (bloc.leader_id == empire.id ? 1f : 0.5f)) return;
        foreach (long id in bloc.members)
        {
            if (id == empire.id) continue;
            Kingdom ally = Living(id)?.CoreKingdom;
            if (ally == null) continue;
            War war = ally.getWars().FirstOrDefault(candidate =>
                candidate?.data != null && candidate.isAlive() && !candidate.hasEnded() && candidate.isDefender(ally) &&
                !candidate.hasKingdom(core) && candidate.main_attacker != null && !SameBloc(candidate.main_attacker, core));
            Kingdom attacker = war?.main_attacker;
            if (attacker?.data == null || attacker.isRekt() || MutuallyDeterred(core, attacker) ||
                EmpirePeacePatch.HasLeft(core, war, joiningAttackers: false) || WarSituation.TooBusyForWar(core) ||
                CorpsSystem.CannotFundWar(core) || !WarGate.CanDeclare(core, attacker) || !WarReach.Reachable(core, attacker)) continue;
            war.joinDefenders(core);
            if (!war._list_defenders.Contains(core)) continue;
            TranslateHelper.LogEventMessage(string.Format(LM.Get("world_bloc_aid"), empire.GetEmpireFullName(),
                ally.GetEmpire()?.GetEmpireFullName() ?? ally.GetKingdomName(), BlocName(bloc), attacker.GetKingdomName()), core);
            return;
        }
    }

    #endregion

    #region 冷战

    private static double Years(int years) => years * 12d * Date.getMonthTime();

    private static void UpdateColdWars(Dictionary<long, double> power, double now)
    {
        // 理念相左、盟主够得着对方的两个阵营：冷战
        for (int i = 0; i < _data.blocs.Count; i++)
        for (int j = i + 1; j < _data.blocs.Count; j++)
        {
            BlocData x = _data.blocs[i], y = _data.blocs[j];
            Empire lx = LeaderOf(x), ly = LeaderOf(y);
            if (lx == null || ly == null) continue;
            bool hostile = PartySystem.IdeologyDistance((PartyIdeology)x.ideology, (PartyIdeology)y.ideology) >= FarIdeology;
            ColdWarData war = ColdWarBetween(x, y);
            bool fighting = lx.CoreKingdom.isInWarWith(ly.CoreKingdom);
            if (hostile && war == null && (fighting || Contact(lx, ly)))
            {
                _data.cold_wars.Add(new ColdWarData
                {
                    a = x.id, b = y.id, since = now, tension = fighting ? 100f : StartTension, hot = fighting,
                    power_a = power.TryGetValue(lx.id, out double startX) ? startX : -1d, power_b = power.TryGetValue(ly.id, out double startY) ? startY : -1d
                });
                Record(string.Format(LM.Get(fighting ? "bloc_war_started_history" : "cold_war_started_history"), BlocName(x), BlocName(y), lx.GetEmpireFullName(),
                    ly.GetEmpireFullName()), lx, Members(x).Concat(Members(y)));
            }
            else if (!hostile && war != null) EndColdWar(war, null, null);
        }
        foreach (ColdWarData war in _data.cold_wars.ToList())
        {
            BlocData x = Bloc(war.a), y = Bloc(war.b);
            Empire lx = LeaderOf(x), ly = LeaderOf(y);
            if (x == null || y == null || lx == null || ly == null)
            {
                _data.cold_wars.Remove(war);
                continue;
            }
            x.unopposed_since = y.unopposed_since = -1d;
            Kingdom cx = lx.CoreKingdom, cy = ly.CoreKingdom;
            // 已经打起来了：等这一仗打完
            if (cx.isInWarWith(cy))
            {
                war.hot = true;
                war.tension = 100f;
                continue;
            }
            // 一方盟主国力衰到对方四成以下、而且比开始对峙时的对比掉了一大截：不战而终。
            // 以前只看四成：1937 剧本里苏联开局国力就不到日本的四成，社会主义阵营立了五年照样"不战而终"(用户 2026-11-25 "修这个")
            double px = power.TryGetValue(lx.id, out double valueX) ? valueX : 0d;
            double py = power.TryGetValue(ly.id, out double valueY) ? valueY : 0d;
            if (war.power_a < 0d || war.power_b < 0d)
            {
                war.power_a = px;
                war.power_b = py;
            }
            bool Declined(double self, double other, double selfThen, double otherThen) =>
                self < other * CollapseShare && (otherThen <= 0d || selfThen <= 0d || self / Math.Max(1d, other) < selfThen / otherThen * DeclineShare);
            if (Date.getYearsSince(war.since) >= 5 && (Declined(px, py, war.power_a, war.power_b) || Declined(py, px, war.power_b, war.power_a)))
            {
                BlocData loser = px < py ? x : y;
                Dissolve(loser, LeaderOf(loser), "world_bloc_collapsed_history");
                continue;
            }
            bool detente = war.detente_until >= 0d && now < war.detente_until;
            bool proxy = ProxyWar(x, y);
            war.tension = Mathf.Clamp(war.tension + (detente ? -DetenteCooling
                : TensionDrift + (proxy ? ProxyTension : 0f) + (x.arms_level + y.arms_level) * 0.5f), 10f, 100f);
            int arms = detente ? 1 : war.tension >= 75f ? 3 : war.tension >= 50f ? 2 : 1;
            x.arms_level += Math.Sign(arms - x.arms_level);
            y.arms_level += Math.Sign(arms - y.arms_level);
            string nameX = lx.GetEmpireFullName(), nameY = ly.GetEmpireFullName();
            bool nuclearX = Compatibility.NuclearDoctrineSystem.IsNuclearPower(cx);
            bool nuclearY = Compatibility.NuclearDoctrineSystem.IsNuclearPower(cy);
            if (!detente && (war.last_crisis < 0d || Date.getYearsSince(war.last_crisis) >= CrisisGapYears) &&
                UnityEngine.Random.value < CrisisBase + war.tension / 400f)
            {
                string kind = proxy ? "proxy" : nuclearX != nuclearY ? "nuclear" : lx.IsNeighbourWith(cy) ? "border"
                    : x.arms_level >= 2 && y.arms_level >= 2 ? "arms" : "incident";
                war.tension = Mathf.Min(100f, war.tension + UnityEngine.Random.Range(12f, 20f));
                war.last_crisis = now;
                war.crises++;
                Record(string.Format(LM.Get("cold_war_crisis_" + kind + "_history"), nameX, nameY), lx, new[] { ly });
            }
            if (war.tension < 100f) continue;
            // 紧张到头：都有核、或打不成，就缓和；不然国力强的一方动手
            Kingdom attacker = px >= py ? cx : cy, defender = px >= py ? cy : cx;
            bool mad = nuclearX && nuclearY;
            bool fight = !mad && WarGate.CanDeclare(attacker, defender) && WarReach.MayDeclare(attacker, defender, WarTypeLibrary.normal) &&
                         !WarSituation.TooBusyForWar(attacker) && !CorpsSystem.CannotFundWar(attacker) &&
                         WarMuster.Declare(attacker, defender, EmpireWarType.None);
            if (fight)
            {
                Record(string.Format(LM.Get("cold_war_hot_history"), BlocName(x), BlocName(y)), attacker.GetEmpire(),
                    Members(x).Concat(Members(y)));
                continue;
            }
            war.tension = DetenteTension;
            war.detente_until = now + Years(DetenteYears);
            Record(string.Format(LM.Get(mad ? "cold_war_detente_nuclear_history" : "cold_war_detente_history"), nameX, nameY), lx,
                new[] { ly });
        }
        // 没有对手的阵营：军备松下来，太久没有对手就散
        foreach (BlocData bloc in _data.blocs.ToList())
        {
            if (_data.cold_wars.Any(war => war.a == bloc.id || war.b == bloc.id)) continue;
            bloc.arms_level = Math.Max(0, bloc.arms_level - 1);
            if (bloc.unopposed_since < 0d || now < bloc.unopposed_since) bloc.unopposed_since = now;
            else if (Date.getYearsSince(bloc.unopposed_since) >= UnopposedYears)
                Dissolve(bloc, LeaderOf(bloc), "world_bloc_disbanded_history");
        }
    }

    private static IEnumerable<Empire> Members(BlocData bloc) =>
        bloc == null ? Enumerable.Empty<Empire>() : bloc.members.Select(Living).Where(member => member != null);

    // 两个阵营的国正在别处交手(盟主之间没打)：代理人战争
    private static bool ProxyWar(BlocData x, BlocData y)
    {
        foreach (Empire member in Members(x))
            foreach (Empire other in Members(y))
                if (member.CoreKingdom.isInWarWith(other.CoreKingdom)) return true;
        return false;
    }

    // winner 为空：握手言和(理念不再相左)。losers：输的一方的成员(阵营已散时传进来)
    private static void EndColdWar(ColdWarData war, BlocData winner, List<Empire> losers)
    {
        if (!_data.cold_wars.Remove(war)) return;
        BlocData x = Bloc(war.a), y = Bloc(war.b);
        int years = Math.Max(1, Date.getYearsSince(war.since));
        if (winner == null)
        {
            Empire speaker = LeaderOf(x) ?? LeaderOf(y);
            if (speaker != null)
                Record(string.Format(LM.Get("cold_war_reconciled_history"), BlocName(x), BlocName(y), years), speaker,
                    Members(x).Concat(Members(y)));
            return;
        }
        Empire leader = LeaderOf(winner);
        if (leader == null) return;
        leader.AddMandate(10);
        Record(string.Format(LM.Get(war.hot ? "bloc_war_won_history" : "cold_war_won_history"), years, BlocName(winner), leader.GetEmpireFullName()), leader,
            Members(winner).Concat(losers ?? new List<Empire>()));
        // 输的一方人心动摇
        foreach (Empire loser in losers ?? new List<Empire>()) RevolutionSystem.Shock(loser, 20f);
    }

    #endregion

    #region 万国公会

    private static void UpdateLeague(List<Empire> states, List<Empire> greats, Dictionary<long, double> power,
        HashSet<long> alive, double now)
    {
        LeagueData league = _data.league;
        if (league == null)
        {
            if (DiplomacyLaws.AllianceBlocked || states.Count < LeagueMinStates || greats.Count < LeagueMinGreats ||
                UnityEngine.Random.value >= LeagueFoundChance) return;
            FoundLeague(greats[0], new[] { greats[0] }, now, "world_league_founded_history");
            league = _data.league;
        }
        league.members.RemoveAll(id => !alive.Contains(id));
        if (Living(league.founder_id) == null || !league.members.Contains(league.founder_id))
            league.founder_id = league.members.Select(Living).Where(member => member != null)
                .OrderByDescending(member => power.TryGetValue(member.id, out double value) ? value : 0d).FirstOrDefault()?.id ?? -1L;
        Empire founder = Living(league.founder_id);
        if (founder == null || league.members.Count == 0)
        {
            _data.league = null;
            return;
        }
        if (!DiplomacyLaws.AllianceBlocked)
            foreach (Empire state in states)
            {
                if (league.members.Contains(state.id) || IsSanctioned(state) ||
                    state.CoreKingdom.isInWarWith(founder.CoreKingdom) || UnityEngine.Random.value >= LeagueJoinChance) continue;
                league.members.Add(state.id);
                EventRecorder.RecordRoutine(state, string.Format(LM.Get("world_league_joined_history"), state.GetEmpireFullName()));
            }
        // 议处新起的战事
        List<Empire> council = league.members.Select(Living).Where(member => member != null)
            .OrderByDescending(member => power.TryGetValue(member.id, out double value) ? value : 0d).Take(CouncilSeats).ToList();
        foreach (War war in World.world.wars.list.ToList())
        {
            if (war?.data == null || war.hasEnded() || !war.isAlive()) continue;
            WarExtension.WarExtraData extra = war.GetOrCreate();
            if (extra.league_case || war.getAge() > 2f) continue;
            extra.league_case = true;
            Kingdom attacker = war.getMainAttacker(), defender = war.getMainDefender();
            Empire aggressor = attacker?.GetEmpire(), victim = defender?.GetEmpire();
            // 只管国与国的仗：叛乱、内战、同一帝国里的不管
            if (aggressor == null || victim == null || aggressor == victim || aggressor.CoreKingdom != attacker ||
                victim.CoreKingdom != defender || attacker.IsFactionRebelling() || attacker.IsLocalRebelling() ||
                RepublicSystem.IsRevolutionWar(war) || WarlordEraSystem.IsCivilWar(war)) continue;
            if (!league.members.Contains(victim.id) && !league.members.Contains(aggressor.id)) continue;
            Decide(league, council, aggressor, victim, founder);
            if (_data.league == null) return;
        }
    }

    private static void FoundLeague(Empire founder, IEnumerable<Empire> members, double now, string key)
    {
        _data.league = new LeagueData
        {
            since = now, founder_id = founder.id,
            members = members.Where(member => member != null).Select(member => member.id).Distinct().ToList()
        };
        if (!_data.league.members.Contains(founder.id)) _data.league.members.Add(founder.id);
        Record(string.Format(LM.Get(key), founder.GetEmpireFullName()), founder, _data.league.members.Select(Living));
    }

    private static void Decide(LeagueData league, List<Empire> council, Empire aggressor, Empire victim, Empire founder)
    {
        string a = aggressor.GetEmpireFullName(), v = victim.GetEmpireFullName();
        Kingdom ca = aggressor.CoreKingdom, cv = victim.CoreKingdom;
        Empire veto = council.FirstOrDefault(seat => seat == aggressor || SameBloc(seat.CoreKingdom, ca));
        if (veto != null)
        {
            league.failures++;
            Record(string.Format(LM.Get("world_league_veto_history"), a, v, veto.GetEmpireFullName()), founder, new[] { aggressor, victim });
        }
        else
        {
            int yes = council.Count(seat => seat == victim || SameBloc(seat.CoreKingdom, cv) ||
                                            ColdWarBetween(seat.CoreKingdom, ca) != null ||
                                            (World.world.diplomacy?.getOpinion(seat.CoreKingdom, ca)?.total ?? 0f) <
                                            (World.world.diplomacy?.getOpinion(seat.CoreKingdom, cv)?.total ?? 0f));
            if (yes * 2 > council.Count)
            {
                league.resolutions++;
                Sanction(aggressor, SanctionYears, -1, "aggression");
                league.members.Remove(aggressor.id);
                Record(string.Format(LM.Get("world_league_sanction_history"), a, v), founder, new[] { aggressor, victim });
                return;
            }
            league.failures++;
            Record(string.Format(LM.Get("world_league_failed_history"), a, v), founder, new[] { aggressor, victim });
        }
        // 屡屡束手，威信扫地
        if (league.failures < 3 || league.failures <= league.resolutions * 2) return;
        List<Empire> members = league.members.Select(Living).Where(member => member != null).ToList();
        _data.league = null;
        _data.sanctions.RemoveAll(sanction => sanction.by_bloc < 0);
        Record(LM.Get("world_league_dissolved_history"), founder, members);
    }

    private static void Sanction(Empire target, int years, int byBloc, string reason)
    {
        SanctionData existing = _data.sanctions.FirstOrDefault(sanction => sanction.target_id == target.id && sanction.by_bloc == byBloc);
        if (existing != null) _data.sanctions.Remove(existing);
        _data.sanctions.Add(new SanctionData
        {
            target_id = target.id, since = World.world.getCurWorldTime(), years = years, by_bloc = byBloc, reason = reason
        });
    }

    private static void ExpireSanctions(double now)
    {
        foreach (SanctionData sanction in _data.sanctions.ToList())
        {
            Empire target = Living(sanction.target_id);
            bool over = target == null || now < sanction.since || Date.getYearsSince(sanction.since) >= sanction.years;
            if (!over) continue;
            _data.sanctions.Remove(sanction);
            if (target != null)
                EventRecorder.RecordRoutine(target, string.Format(LM.Get("world_sanction_lifted_history"), target.GetEmpireFullName()));
        }
    }

    #endregion

    #region 世界大战

    // 打到第三年起，参战各国工农的怨气逐年加码(见 WorldWarSystem)
    private static void WarWeariness()
    {
        foreach (War war in World.world.wars.list.ToList())
        {
            if (war?.data == null || war.hasEnded() || !war.GetOrCreate().world_war) continue;
            int years = Mathf.FloorToInt(war.getAge());
            if (years < WearyFromYear) continue;
            var seen = new HashSet<long>();
            foreach (Kingdom kingdom in war._list_attackers.Concat(war._list_defenders).ToList())
            {
                Empire empire = kingdom?.GetEmpire();
                if (empire?.data == null || empire.isRekt() || empire.IsArchived() || !seen.Add(empire.id)) continue;
                float amount = 2f * (years - WearyFromYear + 1);
                ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, amount);
                ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, amount);
                if (years == WearyFromYear + 1)
                    EventRecorder.RecordRoutine(empire, string.Format(LM.Get("world_war_weary_history"), empire.GetEmpireFullName()));
            }
        }
    }

    // 世界大战打完(WorldWarSystem.Resolve)：战败国正统大跌、革命形势陡增，战胜国发起万国公会
    public static void OnWorldWarEnded(War war, WarWinner winner)
    {
        if (war == null || winner != WarWinner.Attackers && winner != WarWinner.Defenders) return;
        _data.world_war_ended_at = World.world.getCurWorldTime();
        List<Empire> Side(IEnumerable<Kingdom> kingdoms) => kingdoms.Select(kingdom => kingdom?.GetEmpire())
            .Where(empire => empire?.data != null && !empire.isRekt() && !empire.IsArchived()).Distinct().ToList();
        List<Empire> winners = Side(winner == WarWinner.Attackers ? war._list_attackers : war._list_defenders);
        List<Empire> losers = Side(winner == WarWinner.Attackers ? war._list_defenders : war._list_attackers);
        foreach (Empire loser in losers.Except(winners))
        {
            loser.AddMandate(-15);
            RevolutionSystem.Shock(loser, 25f);
            // 战败之后百业凋敝(见 EconomicCycleSystem)
            EconomicCycleSystem.Shock(loser, "war");
        }
        foreach (Empire victor in winners) victor.AddMandate(8);
        Empire chief = (winner == WarWinner.Attackers ? war.getMainAttacker() : war.getMainDefender())?.GetEmpire();
        if (chief == null || !winners.Contains(chief)) chief = winners.FirstOrDefault();
        if (chief == null || _data.league != null || DiplomacyLaws.AllianceBlocked ||
            !StateModernization.IsModernPower(chief.CoreKingdom)) return;
        FoundLeague(chief, winners.Where(victor => StateModernization.IsModernPower(victor.CoreKingdom)),
            World.world.getCurWorldTime(), "world_league_postwar_history");
    }

    #endregion
}

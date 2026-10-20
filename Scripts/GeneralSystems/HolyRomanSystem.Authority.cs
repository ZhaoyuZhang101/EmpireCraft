using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 帝国权威(封建制帝国、有帝国议会的，见 HasDiet)：一根贯穿的皇权刻度 0~100，算式见 ImperialAuthorityRules。
//   · 起算：新帝国 50；旧档第一次年结按现状折算(加冕、改革、和约、家族领地、让步)，不记事件；
//   · 涨：教宗加冕 +15(自行加冕 +8，每位皇帝一次)；护佑诸侯——中央应求援加入成员守方，守方胜 +6、议和而成员没丢城 +2；
//         帝国战争胜 +8；禁令执行成功 +6；宗教战争皇帝胜 +10；同族连任或储君即位 +3；
//   · 跌：选举让步 −4/项；诸侯抗命 −2/次(每年至多 −8)；帝国战争负 −8；禁令失败 −10；订立和约 −30；
//         成员被帝国外的人吞并 −8/国、城被外人拿走 −1/城(每年至多 −6)、成员离开帝国 −5/国(年结比对上年快照)；
//   · 每年：太平(本年没有禁令战、宗教战、成员叛乱，也没失城)+1；选帝制家族领地四成以上 +1、一成以下 −1；
//         成员超过 12 国 −1；已订和约 −1；到 80 以上所有涨幅减半；
//   · 花：颁行帝国改革(门槛 60、花 30，改为议会议案，见 Diet)；颁布帝国治安令(50/25)；确认诸侯特权(花 5)；
//         提请帝国禁令(花 10，AI 权威不到 20 不提)；恢复世袭皇权(80/50)。神力按钮照扣，扣到 0 为止，不因权威不够拦下；
//   · 作用：议会每票 +(权威−50)/200；抗命几率 ×(1.5 − 权威/100)；正统 80 以上 +5、20 以下 −5。
// 权威每笔变动只走 ChangeAuthority(记账、诊断)。
public static partial class HolyRomanSystem
{
    public const int AuthCoronation = 15, AuthSelfCoronation = 8, AuthProtected = 6, AuthProtectedPeace = 2;
    public const int AuthImperialWarWin = 8, AuthImperialWarLoss = -8, AuthBanExecuted = 6, AuthBanFailed = -10;
    public const int AuthLeagueCrushed = 10, AuthReligiousPeace = -30, AuthContinuity = 3, AuthConcession = -4;
    public const int AuthDefiance = -2, AuthDefianceCap = 8, AuthAnnexed = -8, AuthCityLost = -1, AuthCityLossCap = 6;
    public const int AuthMemberLeft = -5;
    // 门槛(不到就不能做)与花费(做成时扣)
    public const int ReformThreshold = 60, ReformCost = 30, PeaceThreshold = 50, PeaceCost = 25, PrivilegeCost = 5;
    public const int BanThreshold = 10, BanCost = 10, BanAiThreshold = 20, HereditaryThreshold = 80, HereditaryCost = 50;
    // 正统：皇权隆盛 / 式微
    public const int AuthorityLegitimacyHigh = 5, AuthorityLegitimacyLow = -5;
    // 每年漂移：家族领地厚薄、小邦林立
    private const float DriftHouseStrong = 0.4f, DriftHouseWeak = 0.1f;
    private const int CrowdedMembers = 12;
    // 权威低于此起算"式微年数"，回到 LowRecover 以上重计
    public const int LowAuthority = 20, LowRecover = 25;
    private const int AuthorityLogSize = 16, LegacyAuthority = -2;

    // 当前权威(还没起算的按 50，读值的地方不顺手起算——读档中途调用时世界还没就绪)
    public static int Authority(Empire empire)
    {
        HolyRomanState state = State(empire);
        return state == null || state.authority < 0 ? ImperialAuthorityRules.NewEmpire : state.authority;
    }

    public static bool AuthorityTracked(Empire empire) => HasDiet(empire) && State(empire)?.authority >= 0;

    public static string AuthorityTierText(int authority) =>
        LM.Get("hre_auth_tier_" + ImperialAuthorityRules.Tier(authority));

    // 起算：新帝国 50；旧档按现状折算，文化节点已施行的直接记帝国改革已颁行(不扣权威，行为不变)
    private static void EnsureAuthority(Empire empire, HolyRomanState state)
    {
        if (state == null || state.authority >= 0) return;
        bool legacy = state.authority == LegacyAuthority;
        if (legacy && state.reform_at < 0d && ReformNodeEnacted(empire)) state.reform_at = World.world.getCurWorldTime();
        state.authority = legacy
            ? ImperialAuthorityRules.Initial(PapacySystem.IsCrowned(empire), state.reform_at >= 0d, state.sovereign_princes,
                IsElective(empire), HouseShare(empire), state.concessions?.Count ?? 0)
            : ImperialAuthorityRules.NewEmpire;
        state.authority_log ??= new List<AuthorityEntry>();
        if (empire.Emperor != null && PapacySystem.IsCrowned(empire)) state.authority_crowned_id = empire.Emperor.id;
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 帝国权威起算 {state.authority}" +
                           (legacy ? "(旧档折算)" : "(新帝国)"));
    }

    // 唯一的加减入口：软顶、夹值、记账、诊断；返回实际变动
    public static int ChangeAuthority(Empire empire, int delta, string textKey, params object[] args)
    {
        if (delta == 0 || !HasDiet(empire)) return 0;
        HolyRomanState state = State(empire);
        EnsureAuthority(empire, state);
        int before = state.authority;
        int after = ImperialAuthorityRules.Apply(before, delta);
        int real = after - before;
        if (real == 0) return 0;
        state.authority = after;
        string text = args == null || args.Length == 0 ? LM.Get(textKey) : string.Format(LM.Get(textKey), args);
        state.authority_log ??= new List<AuthorityEntry>();
        state.authority_log.Add(new AuthorityEntry { at = World.world.getCurWorldTime(), delta = real, text = text });
        while (state.authority_log.Count > AuthorityLogSize) state.authority_log.RemoveAt(0);
        if (real > 0) state.year_gain += real;
        else state.year_loss -= real;
        FiscalDiagnostics.Count(real > 0 ? "神罗·权威+" : "神罗·权威-", Math.Abs(real));
        if (Math.Abs(real) >= 10)
            LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 权威 {before}→{after}：{text}");
        if (before >= ImperialAuthorityRules.Glorious != after >= ImperialAuthorityRules.Glorious ||
            before < LowAuthority != after < LowAuthority)
            MonarchyLegitimacy.Invalidate(empire);
        return real;
    }

    // 够不够门槛(没有帝国议会的帝国不讲权威，一律够)
    public static bool CanSpend(Empire empire, int threshold) => !HasDiet(empire) || Authority(empire) >= threshold;

    // 花权威：照扣，扣到 0 为止
    public static void Spend(Empire empire, int cost, string textKey, params object[] args) =>
        ChangeAuthority(empire, -Math.Abs(cost), textKey, args);

    // 诸侯抗命：每次 −2，每年至多 −8
    private static void AuthorityDefiance(Empire empire, HolyRomanState state, Kingdom kingdom)
    {
        int room = AuthDefianceCap - state.year_defiance_loss;
        if (room <= 0) return;
        int loss = Math.Min(-AuthDefiance, room);
        state.year_defiance_loss += loss;
        ChangeAuthority(empire, -loss, "hre_auth_defiance", kingdom.GetKingdomName());
    }

    // 加冕(PapacySystem.Crown 调用)：教宗加冕 +15、自行加冕 +8，每位皇帝只计一次
    public static void OnCrowned(Empire empire, Actor emperor, bool self)
    {
        try
        {
            if (emperor?.data == null || !HasDiet(empire)) return;
            HolyRomanState state = State(empire);
            if (state.authority_crowned_id == emperor.id) return;
            state.authority_crowned_id = emperor.id;
            ChangeAuthority(empire, self ? AuthSelfCoronation : AuthCoronation,
                self ? "hre_auth_self_coronation" : "hre_auth_coronation", emperor.getName());
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 加冕计权威失败: {exception.Message}");
        }
    }

    // 护佑诸侯(MemberWarAid.AnswerRequest 调用)：中央应求援加入成员守方，战后结算(见 OnWarEnded)
    public static void NoteProtection(Empire empire, War war, Kingdom member)
    {
        if (war?.data == null || member?.data == null || !HasDiet(empire)) return;
        HolyRomanState state = State(empire);
        state.protect_wars ??= new Dictionary<long, long>();
        state.protect_wars[war.data.id] = member.id;
    }

    // 护佑之战收场：守方胜 +6；议和而成员一方没丢城 +2
    private static void SettleProtection(Empire empire, HolyRomanState state, War war, bool defendersWon, int attackerGain,
        WarWinner winner)
    {
        if (state.protect_wars == null || !state.protect_wars.TryGetValue(war.data.id, out long memberId)) return;
        state.protect_wars.Remove(war.data.id);
        string name = World.world.kingdoms.get(memberId)?.GetKingdomName() ?? "";
        if (defendersWon) ChangeAuthority(empire, AuthProtected, "hre_auth_protected", name);
        else if (winner == WarWinner.Peace && attackerGain <= 0) ChangeAuthority(empire, AuthProtectedPeace, "hre_auth_protected_peace", name);
    }

    #region 年结

    // 上年快照：成员国(含中央)、帝国各城所属；只在内存，读档后第一年只建底
    private sealed class MemberSnapshot
    {
        public HashSet<long> realms = new();
        public Dictionary<long, string> names = new();
        public Dictionary<long, long> cities = new();
    }

    private static readonly Dictionary<long, MemberSnapshot> Snapshots = new();

    // 换世界、读档时清掉(SaveManagerPatch)
    public static void ResetWorldState() => Snapshots.Clear();

    private static MemberSnapshot TakeSnapshot(Empire empire)
    {
        var snapshot = new MemberSnapshot();
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.GetEmpire() != empire) continue;
            snapshot.realms.Add(kingdom.id);
            snapshot.names[kingdom.id] = kingdom.GetKingdomName();
            foreach (City city in kingdom.cities)
                if (city?.data != null && !city.isRekt()) snapshot.cities[city.id] = kingdom.id;
        }
        return snapshot;
    }

    // 年结(Yearly 最先调)：起算、比对快照、每年漂移、式微年数
    private static void AuthorityYearly(Empire empire, HolyRomanState state)
    {
        EnsureAuthority(empire, state);
        state.protect_wars ??= new Dictionary<long, long>();
        foreach (long id in state.protect_wars.Keys.ToList())
            if (LiveWar(id) == null) state.protect_wars.Remove(id);
        int citiesLost = CompareSnapshot(empire, state);
        AuthorityDrift(empire, state, citiesLost);
        double now = World.world.getCurWorldTime();
        if (state.authority < LowAuthority && state.authority_low_since < 0d) state.authority_low_since = now;
        else if (state.authority >= LowRecover) state.authority_low_since = -1d;
    }

    // 比对上年快照：成员被外人吞并、离开帝国、城被外人拿走；返回本年失城数(太平判定用)
    private static int CompareSnapshot(Empire empire, HolyRomanState state)
    {
        MemberSnapshot now = TakeSnapshot(empire);
        bool had = Snapshots.TryGetValue(empire.data.id, out MemberSnapshot previous);
        Snapshots[empire.data.id] = now;
        if (!had) return 0;
        var annexed = new HashSet<long>();
        foreach (long id in previous.realms)
        {
            if (now.realms.Contains(id)) continue;
            Kingdom kingdom = World.world.kingdoms.get(id);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                // 国亡：它的城多半落在帝国外的人手里才算被吞并(并入别的成员、被法院判走的不算)
                List<long> owned = previous.cities.Where(pair => pair.Value == id).Select(pair => pair.Key).ToList();
                int outside = owned.Count(cityId => World.world.cities.get(cityId) is City city && city.data != null &&
                                                    !city.isRekt() && city.kingdom?.GetEmpire() != empire);
                if (owned.Count > 0 && outside * 2 > owned.Count)
                {
                    annexed.Add(id);
                    string name = owned.Select(cityId => World.world.cities.get(cityId)?.kingdom)
                        .FirstOrDefault(other => other?.data != null && other.GetEmpire() != empire)?.GetKingdomName() ?? "";
                    ChangeAuthority(empire, AuthAnnexed, "hre_auth_member_annexed",
                        previous.names.TryGetValue(id, out string fallen) ? fallen : "", name);
                }
            }
            // 随保护同盟离开的已在当时扣过(见 Decline)
            else if (kingdom.GetEmpire() != empire && !LeftUnderProtection(kingdom))
                ChangeAuthority(empire, AuthMemberLeft, "hre_auth_member_left", kingdom.GetKingdomName());
        }
        int lost = 0;
        foreach (var pair in previous.cities)
        {
            if (annexed.Contains(pair.Value) || now.cities.ContainsKey(pair.Key)) continue;
            City city = World.world.cities.get(pair.Key);
            Kingdom holder = city?.data == null || city.isRekt() ? null : city.kingdom;
            // 原主带着城离开帝国的不算失城(上面已按离开扣过)
            if (holder?.data == null || holder.isRekt() || holder.GetEmpire() == empire || holder.id == pair.Value) continue;
            lost++;
        }
        int room = AuthCityLossCap - state.year_city_loss;
        int loss = Math.Min(lost, Math.Max(0, room));
        if (loss > 0)
        {
            state.year_city_loss += loss;
            ChangeAuthority(empire, AuthCityLost * loss, "hre_auth_cities_lost", lost);
        }
        return lost + annexed.Count;
    }

    // 每年漂移：合成一笔记账
    private static void AuthorityDrift(Empire empire, HolyRomanState state, int citiesLost)
    {
        var items = new List<string>();
        int delta = 0;
        if (Tranquil(empire, state, citiesLost))
        {
            delta++;
            items.Add(LM.Get("hre_auth_drift_tranquil"));
        }
        if (IsElective(empire))
        {
            float share = HouseShare(empire);
            if (share >= DriftHouseStrong)
            {
                delta++;
                items.Add(LM.Get("hre_auth_drift_house_strong"));
            }
            else if (share < DriftHouseWeak)
            {
                delta--;
                items.Add(LM.Get("hre_auth_drift_house_weak"));
            }
        }
        if (Members(empire).Count > CrowdedMembers)
        {
            delta--;
            items.Add(LM.Get("hre_auth_drift_crowded"));
        }
        if (state.sovereign_princes)
        {
            delta--;
            items.Add(LM.Get("hre_auth_drift_sovereign"));
        }
        state.year_turmoil = false;
        if (delta != 0) ChangeAuthority(empire, delta, "hre_auth_drift", string.Join("、", items));
    }

    // 太平：本年没有禁令战、宗教战，没有成员叛乱，也没失城
    private static bool Tranquil(Empire empire, HolyRomanState state, int citiesLost)
    {
        if (citiesLost > 0 || state.year_turmoil || BanWar(empire) != null || LeagueWar(empire) != null) return false;
        return !empire.kingdoms_list.Any(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.GetEmpire() == empire &&
                                                     (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling()));
    }

    // 年度诊断的权威一段；说完清掉本年计数
    private static string AuthorityStatus(Empire empire, HolyRomanState state)
    {
        string text = state.authority < 0 ? "未起算"
            : $"{state.authority}({AuthorityTierText(state.authority)})，本年+{state.year_gain}/-{state.year_loss}" +
              (state.authority_low_since >= 0d ? $"，低于{LowAuthority}已{Date.getYearsSince(state.authority_low_since)}年" : "");
        state.year_gain = state.year_loss = 0;
        state.year_defiance_loss = state.year_city_loss = 0;
        return text;
    }

    #endregion
}

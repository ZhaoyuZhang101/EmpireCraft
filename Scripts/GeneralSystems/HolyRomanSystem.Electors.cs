using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 选帝侯席位(封建制帝国，见 HasDiet)：世俗四席绑定领国(state.elector_realm_ids，-1 = 出缺)，不再每次按兵力重排；
// 教会三席照旧随主教领与宗教等级动态取(见 EmpireCraftEmpireBehCheckCabinet.SetCabinetForFeudalism)。
//   · 起算：第一次按当时在座的世俗选帝侯所在国填入(不足四家的按兵力补齐)，保持现状；
//   · 出缺：席位所在国亡了、离开帝国、成了自由城市或主教领；席位国当选成了中央照样留着(皇帝自家领国握一票)；
//   · 停权：席位国被帝国禁令放逐时不得列席、不得投票(Voters、ElectorRealms 已排除被禁者)；
//   · 褫夺：禁令执行成功后褫夺被禁者的席位，花权威 10；权威不到 10 就只停权、禁令解除后照旧；
//   · 授予：出缺满一年、权威 15 以上时花 15 授给亲近皇帝的诸侯——得分 = 对皇帝好感/100 + 与皇帝同族 0.5 + 皇帝党 0.2
//     + 城数名次(最大的 0.1、依次递减)；排除中央、自由城市、主教领、被禁者、已在座的；每年至多授一席；
//   · 兜底：权威不到 15、出缺满五年，按旧例补给兵力最强的普通诸侯(不花权威)，免得选帝侯越来越少；
//   · 世俗席至多四席；神力按钮"授予"(有出缺时，人选同 AI，花 15)、"褫夺"(只对被禁者，花 10)，权威不够照扣到 0。
public static partial class HolyRomanSystem
{
    public const int SecularSeats = 4, ChurchSeats = 3;
    public const int ElectorGrantThreshold = 15, ElectorGrantCost = 15, ElectorStripCost = 10;
    private const int ElectorGrantWaitYears = 1, ElectorFallbackYears = 5, ElectorLogSize = 10;

    // 能占世俗选帝侯席的诸侯：普通成员国(非自由城市、主教领、教会领)
    private static bool SecularSeatEligible(Empire empire, Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && kingdom.GetEmpire() == empire && Estate(kingdom) == EstateNone &&
        kingdom.GetRegime()?.GetReligionLevel() != ReligionLevel.High;

    private static List<long> Seats(HolyRomanState state) => state.elector_realm_ids ??= new List<long>();

    // 内阁维护用：世俗四席的在座者(按席位顺序；出缺、停权、无君的填 -1)
    public static List<long> SecularElectorKings(Empire empire)
    {
        HolyRomanState state = State(empire);
        EnsureSeats(empire, state);
        var result = new List<long>();
        foreach (long id in Seats(state))
        {
            Kingdom realm = id >= 0 ? World.world.kingdoms.get(id) : null;
            bool sitting = realm?.data != null && !realm.isRekt() && realm.GetEmpire() == empire && !IsBanned(realm) &&
                           realm.hasKing() && realm.king?.data != null;
            result.Add(sitting ? realm.king.id : -1L);
        }
        while (result.Count < SecularSeats) result.Add(-1L);
        return result;
    }

    // 第一次：按当时在座的世俗选帝侯所在国填入，不足的按兵力补齐(同旧例)
    private static void EnsureSeats(Empire empire, HolyRomanState state)
    {
        List<long> seats = Seats(state);
        if (seats.Count >= SecularSeats) return;
        if (seats.Count == 0)
            foreach (Actor actor in empire.GetCabinetMembers())
            {
                Kingdom realm = actor?.kingdom;
                if (seats.Count >= SecularSeats || realm == null || realm.king != actor || realm == empire.CoreKingdom ||
                    !SecularSeatEligible(empire, realm) || seats.Contains(realm.id)) continue;
                seats.Add(realm.id);
            }
        foreach (Kingdom realm in StrongestPrinces(empire, seats))
        {
            if (seats.Count >= SecularSeats) break;
            seats.Add(realm.id);
        }
        while (seats.Count < SecularSeats) seats.Add(-1L);
        if (seats.Contains(-1L) && state.elector_vacant_at < 0d) state.elector_vacant_at = World.world.getCurWorldTime();
    }

    // 旧例：兵力最强的普通诸侯(非中央、非被禁、未在座)
    private static IEnumerable<Kingdom> StrongestPrinces(Empire empire, List<long> seated) =>
        Members(empire).Where(realm => SecularSeatEligible(empire, realm) && realm.hasKing() && !IsBanned(realm) &&
                                       !seated.Contains(realm.id))
            .OrderByDescending(realm => realm.countTotalWarriors());

    private static void ElectorLog(Empire empire, HolyRomanState state, string text, bool history)
    {
        state.elector_log ??= new List<string>();
        state.elector_log.Add(text);
        while (state.elector_log.Count > ElectorLogSize) state.elector_log.RemoveAt(0);
        if (history) Record(empire, text);
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 选帝侯：{text}");
    }

    // 每年：出缺、授予、兜底
    private static void ElectorsYearly(Empire empire, HolyRomanState state)
    {
        EnsureSeats(empire, state);
        List<long> seats = Seats(state);
        double now = World.world.getCurWorldTime();
        for (int i = 0; i < seats.Count; i++)
        {
            if (seats[i] < 0) continue;
            Kingdom realm = World.world.kingdoms.get(seats[i]);
            if (realm?.data != null && !realm.isRekt() && realm.GetEmpire() == empire && Estate(realm) == EstateNone) continue;
            string name = realm?.data != null ? realm.GetKingdomName() : "";
            seats[i] = -1L;
            ElectorLog(empire, state, string.Format(LM.Get("hre_elector_vacant_log"), name), history: false);
        }
        if (!seats.Contains(-1L))
        {
            state.elector_vacant_at = -1d;
            return;
        }
        if (state.elector_vacant_at < 0d || now < state.elector_vacant_at) state.elector_vacant_at = now;
        double years = Date.getYearsSince(state.elector_vacant_at);
        // 帝位虚悬时不授席位
        if (years < ElectorGrantWaitYears || Interregnum(empire)) return;
        if (Authority(empire) >= ElectorGrantThreshold) GrantElector(empire, state, spend: true);
        else if (years >= ElectorFallbackYears) FallbackElector(empire, state);
    }

    // 亲近的诸侯：对皇帝好感、同族、皇帝党、城数名次
    public static Kingdom ElectorCandidate(Empire empire)
    {
        HolyRomanState state = State(empire);
        EnsureSeats(empire, state);
        Kingdom core = empire.CoreKingdom;
        List<Kingdom> pool = Members(empire).Where(realm => SecularSeatEligible(empire, realm) && realm.hasKing() &&
                                                            !IsBanned(realm) && !Seats(state).Contains(realm.id)).ToList();
        if (pool.Count == 0) return null;
        List<Kingdom> bySize = pool.OrderByDescending(realm => realm.cities.Count).ToList();
        float Score(Kingdom realm)
        {
            float score = Opinion(realm, core) / 100f;
            if (SameHouse(realm.king, empire.Emperor)) score += 0.5f;
            if (PapacySystem.Party(realm) == PapacySystem.PartyEmperor) score += 0.2f;
            score += 0.1f * (1f - bySize.IndexOf(realm) / (float)bySize.Count);
            return score;
        }
        return pool.OrderByDescending(Score).First();
    }

    // 授予一席(AI 与神力按钮)：花 15
    private static Kingdom GrantElector(Empire empire, HolyRomanState state, bool spend)
    {
        List<long> seats = Seats(state);
        int index = seats.IndexOf(-1L);
        Kingdom pick = index < 0 ? null : ElectorCandidate(empire);
        if (pick == null) return null;
        seats[index] = pick.id;
        state.elector_vacant_at = seats.Contains(-1L) ? World.world.getCurWorldTime() : -1d;
        if (spend) Spend(empire, ElectorGrantCost, "hre_auth_elector_grant", pick.GetKingdomName());
        pick.king?.RecordPersonalHistory(LM.Get("personal_history_became_elector"));
        FiscalDiagnostics.Count("神罗·授选帝侯");
        ElectorLog(empire, state, string.Format(LM.Get("hre_elector_granted_history"),
            empire.Emperor?.getName() ?? empire.CoreKingdom.GetKingdomName(), pick.GetKingdomName()), history: true);
        return pick;
    }

    // 兜底：权威不足、久缺不补时按旧例给兵力最强的普通诸侯
    private static void FallbackElector(Empire empire, HolyRomanState state)
    {
        List<long> seats = Seats(state);
        int index = seats.IndexOf(-1L);
        Kingdom pick = index < 0 ? null : StrongestPrinces(empire, seats).FirstOrDefault();
        if (pick == null) return;
        seats[index] = pick.id;
        state.elector_vacant_at = seats.Contains(-1L) ? World.world.getCurWorldTime() : -1d;
        FiscalDiagnostics.Count("神罗·授选帝侯");
        ElectorLog(empire, state, string.Format(LM.Get("hre_elector_fallback_history"), pick.GetKingdomName()), history: true);
    }

    // 褫夺被禁者的席位(禁令执行成功时自动；神力按钮)：花 10；auto 时权威不到 10 就只停权
    private static bool StripElector(Empire empire, HolyRomanState state, long realmId, bool auto)
    {
        List<long> seats = Seats(state);
        int index = realmId >= 0 ? seats.IndexOf(realmId) : -1;
        if (index < 0 || auto && Authority(empire) < ElectorStripCost) return false;
        string name = World.world.kingdoms.get(realmId)?.GetKingdomName() ?? state.banned_name;
        seats[index] = -1L;
        if (state.elector_vacant_at < 0d) state.elector_vacant_at = World.world.getCurWorldTime();
        Spend(empire, ElectorStripCost, "hre_auth_elector_strip", name);
        FiscalDiagnostics.Count("神罗·褫夺选帝侯");
        ElectorLog(empire, state, string.Format(LM.Get("hre_elector_stripped_history"),
            empire.Emperor?.getName() ?? empire.CoreKingdom.GetKingdomName(), name), history: true);
        return true;
    }

    public static Kingdom ActGrantElector(Empire empire)
    {
        if (!HasDiet(empire)) return null;
        HolyRomanState state = State(empire);
        EnsureSeats(empire, state);
        return Seats(state).Contains(-1L) ? GrantElector(empire, state, spend: true) : null;
    }

    public static bool ActStripElector(Empire empire)
    {
        if (!HasDiet(empire)) return false;
        HolyRomanState state = State(empire);
        return state.banned_kingdom_id >= 0 && StripElector(empire, state, state.banned_kingdom_id, auto: false);
    }

    // 界面用：世俗四席(席位国、在座者、状态 0 在座 / 1 停权 / 2 出缺)
    public static List<(Kingdom realm, Actor holder, int status)> SecularSeatsView(Empire empire)
    {
        HolyRomanState state = State(empire);
        EnsureSeats(empire, state);
        var result = new List<(Kingdom, Actor, int)>();
        foreach (long id in Seats(state))
        {
            Kingdom realm = id >= 0 ? World.world.kingdoms.get(id) : null;
            if (realm?.data == null || realm.isRekt() || realm.GetEmpire() != empire)
                result.Add((null, null, 2));
            else result.Add((realm, realm.king, IsBanned(realm) || !realm.hasKing() ? 1 : 0));
        }
        return result;
    }

    // 界面用：教会席(内阁里不是世俗席在座者的人)
    public static List<Actor> ChurchElectorsView(Empire empire)
    {
        var secular = new HashSet<long>(SecularElectorKings(empire).Where(id => id >= 0));
        return empire.GetCabinetMembers().Where(actor => actor?.data != null && !secular.Contains(actor.id))
            .Take(ChurchSeats).ToList();
    }

    public static bool HasVacantSeat(Empire empire)
    {
        HolyRomanState state = State(empire);
        EnsureSeats(empire, state);
        return Seats(state).Contains(-1L);
    }

    public static bool BannedHoldsSeat(Empire empire)
    {
        HolyRomanState state = State(empire);
        return state.banned_kingdom_id >= 0 && Seats(state).Contains(state.banned_kingdom_id);
    }

    // 年度诊断的选帝侯一段
    private static string ElectorStatus(Empire empire, HolyRomanState state)
    {
        List<(Kingdom realm, Actor holder, int status)> seats = SecularSeatsView(empire);
        int church = ChurchElectorsView(empire).Count;
        string vacancy = state.elector_vacant_at >= 0d ? $"，出缺已{Date.getYearsSince(state.elector_vacant_at)}年" : "";
        return $"在座{seats.Count(seat => seat.status == 0) + church}/7(教会{church}，世俗：" +
               string.Join("、", seats.Select(seat => seat.realm == null ? "出缺" : seat.realm.GetKingdomName() +
                                                                              (seat.status == 1 ? "(停权)" : ""))) + $"){vacancy}";
    }
}

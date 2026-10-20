using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 同名相争(用户 2026-10-20 定)：帝国法理只给"可用这个帝国名命名"的资格，不代表正统。
//   · 同名 = 前缀与名都一样：东某、西某、南某、北某、后某各是一个名，可以并存(两朝分立、复国的南某与原朝)，
//     只有前缀也一样(两个"中华"、两个"东中华")才相争；
//   · 只管同名相争：好几家都在用(或让出过)同一个帝国法理名、同一个天下之名时，只有据有其一半以上城的那家能用——
//     争帝国法理名看那个帝国法理自己的城，争天下之名看整个天下的城(按城数，本国连附庸，EmpireCoreControl 口径)；
//     恰好两家各半时看谁受着天命；没人过半就谁都不用。不同的名各归各家、互不影响；
//   · 用不了的改用自家王国法理名(都城所在的，其次主法理)，没有就用都城名，与别国重名再加方位(相对重名那国的都城)；
//   · 只有一家要用这个名字时照旧，按资格直接用(让出过的、对手没了也复称)；
//   · 新冒出同名(绝嗣裂土、两朝分立、据有法理称帝、复国……)每月一查当即改正，不算换名；正统易手每年一查，
//     一家十年内至多换一次，免得来回改；改名都记史书。旧档里已经同名的，读档后头一回检查就改正。
// 只管华夏、西方两系(其他文化照旧)；未组建政府的现代势力、军阀时期的临时政府另有命名(见 WarlordEraSystem)，不在其列。
public static partial class RealmNamingSystem
{
    private const float SwitchCooldownYears = 10f;
    private static readonly Dictionary<long, double> LastConflictCheck = new();
    private static object _contestWorld;

    private static bool InContest(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null ||
            empire.CoreKingdom.isRekt() || empire.CoreKingdom.HasCustomCountryNaming()) return false;
        if (!IsHuaxia(empire) && !IsWestern(empire)) return false;
        if (PapacySystem.IsHolySee(empire)) return false;
        return !IsModernGovernment(empire) || NamedAfterTianxia(empire);
    }

    // 受约束的名：某个正式帝国法理的名(城最多的那个)，或某个天下的基名
    private static bool RestrictedName(string name, out EmpireCore core, out EmpireCore lead)
    {
        core = null;
        lead = null;
        if (string.IsNullOrWhiteSpace(name)) return false;
        int best = -1;
        foreach (EmpireCore candidate in EmpireCoreManager.EmpireCores.Values)
        {
            if (!IsFormalCore(candidate) || !string.Equals(CoreName(candidate), name, StringComparison.Ordinal)) continue;
            int cities = EmpireCoreControl.GetHolders(candidate).total;
            if (cities <= best) continue;
            best = cities;
            core = candidate;
        }
        foreach (EmpireCore candidate in TianxiaSystem.Leads())
        {
            if (!string.Equals(TianxiaBaseName(candidate), name, StringComparison.Ordinal)) continue;
            lead = candidate;
            break;
        }
        return core != null || lead != null;
    }

    private static bool CanSwitch(Empire empire)
    {
        double at = empire.data.title_claim_switched_at;
        return at < 0d || World.world.getCurWorldTime() < at || Date.getYearsSince(at) >= SwitchCooldownYears;
    }

    // 国号的前缀(手选的以手选为准)；同名相争比前缀 + 名
    private static string PrefixOf(EmpireData data) =>
        data.prefix_manual ? "manual:" + (data.prefix_choice ?? "").Trim() : (data.directPre ?? "").Trim();

    // 让出前用的前缀
    private static string ClaimPrefixOf(EmpireData data) =>
        data.prefix_manual ? "manual:" + (data.prefix_choice ?? "").Trim() : (data.title_claim_prev_prefix ?? "").Trim();

    private static void DropClaim(Empire empire)
    {
        empire.data.title_claim_name = "";
        empire.data.title_claim_yielded = false;
        empire.data.title_claim_own_name = "";
        empire.data.title_claim_prev_prefix = "";
    }

    // 每月(ConstitutionalEconomySystem.Update)：新冒出同名的当即改正；让出过的、已不再有同名的当即复称
    public static void Monthly(Empire empire)
    {
        if (!Enabled || World.world == null || !InContest(empire)) return;
        if (!ReferenceEquals(_contestWorld, World.world))
        {
            _contestWorld = World.world;
            LastConflictCheck.Clear();
        }
        double now = World.world.getCurWorldTime();
        if (LastConflictCheck.TryGetValue(empire.data.id, out double at) && now >= at && Date.getMonthsSince(at) < 1) return;
        LastConflictCheck[empire.data.id] = now;
        try
        {
            if (empire.data.title_claim_yielded)
            {
                ResolveName(ClaimPrefixOf(empire.data), empire.data.title_claim_name, conflictOnly: true, releaseAlone: true);
                return;
            }
            string name = empire.GetEmpireName(), prefix = PrefixOf(empire.data);
            if (ModClass.EMPIRE_MANAGER.Any(other => other != empire && InContest(other) && !other.data.title_claim_yielded &&
                                                     string.Equals(other.GetEmpireName(), name, StringComparison.Ordinal) &&
                                                     string.Equals(PrefixOf(other.data), prefix, StringComparison.Ordinal)))
                ResolveName(prefix, name, conflictOnly: true);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][同名相争] 月查失败: {exception.Message}");
        }
    }

    // 每年(RealmNamingSystem.Yearly)：本国所用或让出的名
    private static void YearlyContest(Empire empire)
    {
        if (!InContest(empire)) return;
        bool yielded = empire.data.title_claim_yielded;
        ResolveName(yielded ? ClaimPrefixOf(empire.data) : PrefixOf(empire.data),
            yielded ? empire.data.title_claim_name : empire.GetEmpireName(), conflictOnly: false);
    }

    // 一个名(前缀 + 名)的相争；conflictOnly = 只改正眼下同时有几家在用的(不按正统易手换名)；
    // releaseAlone = 让出过的已无同名就当即复称(月查)
    public static void ResolveName(string prefix, string name, bool conflictOnly, bool releaseAlone = false)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(name) || ModClass.EMPIRE_MANAGER == null) return;
        if (!RestrictedName(name, out EmpireCore core, out EmpireCore lead)) return;
        var claimants = new List<Empire>();
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (!InContest(empire)) continue;
            EmpireData data = empire.data;
            if (data.title_claim_yielded)
            {
                if (!string.Equals(data.title_claim_name, name, StringComparison.Ordinal) ||
                    !string.Equals(ClaimPrefixOf(data), prefix, StringComparison.Ordinal)) continue;
                // 让出后又被别处改了名(篡位改号、分立……)：这笔作废
                if (!string.Equals(data.core_name, data.title_claim_own_name, StringComparison.Ordinal))
                {
                    DropClaim(empire);
                    continue;
                }
                claimants.Add(empire);
            }
            else if (string.Equals(empire.GetEmpireName(), name, StringComparison.Ordinal) &&
                     string.Equals(PrefixOf(data), prefix, StringComparison.Ordinal))
            {
                data.title_claim_name = name;
                claimants.Add(empire);
            }
        }
        if (claimants.Count == 0) return;
        if (claimants.Count == 1)
        {
            Empire only = claimants[0];
            if (only.data.title_claim_yielded && (releaseAlone || !conflictOnly && CanSwitch(only)))
                Take(only, name, stamp: !releaseAlone, alone: true);
            return;
        }
        Empire legit = Legitimate(claimants, core, lead);
        List<Empire> users = claimants.Where(empire => !empire.data.title_claim_yielded).ToList();
        if (users.Count >= 2)
        {
            foreach (Empire user in users)
                if (user != legit) Yield(user, name, stamp: false);
            if (legit != null && legit.data.title_claim_yielded && CanSwitch(legit)) Take(legit, name, stamp: true, alone: false);
            return;
        }
        if (conflictOnly) return;
        Empire holder = users.FirstOrDefault();
        if (holder != null && holder != legit && CanSwitch(holder))
        {
            Yield(holder, name, stamp: true);
            holder = null;
        }
        if (holder == null && legit != null && legit.data.title_claim_yielded && CanSwitch(legit))
            Take(legit, name, stamp: true, alone: false);
    }

    // 据有其一半以上城的那家：争天下之名看整个天下，争帝国法理名看那个帝国法理；恰好各半看天命；没人过半为 null
    private static Empire Legitimate(List<Empire> claimants, EmpireCore core, EmpireCore lead)
    {
        bool tianxia = core == null || claimants.Any(empire => empire.data.name_source == SourceTianxia);
        Dictionary<Kingdom, int> held;
        int total;
        if (tianxia)
        {
            EmpireCore realm = lead ?? TianxiaSystem.Lead(core);
            if (realm == null) return null;
            (held, total) = TianxiaSystem.Holdings(realm);
            lead = realm;
        }
        else
        {
            (held, total) = EmpireCoreControl.GetHolders(core);
            lead = TianxiaSystem.Lead(core);
        }
        if (total <= 0) return null;
        List<Empire> halves = claimants.Where(empire => held.TryGetValue(empire.CoreKingdom, out int count) && count * 2 >= total).ToList();
        if (halves.Count == 1) return halves[0];
        List<Empire> mandated = halves.Where(empire => lead != null && TianxiaSystem.MandateOf(empire.CoreKingdom) == lead).ToList();
        return mandated.Count == 1 ? mandated[0] : null;
    }

    // 天下的正统：据有整个天下一半以上城(多京那条线用)
    public static bool HoldsHalfOfRealm(Empire empire, EmpireCore lead)
    {
        if (empire?.CoreKingdom == null || lead == null) return false;
        (Dictionary<Kingdom, int> held, int total) = TianxiaSystem.Holdings(lead);
        return total > 0 && held.TryGetValue(empire.CoreKingdom, out int count) && count * 2 >= total;
    }

    // 让出：改用自家王国法理名(都城所在的，其次主法理)，没有就用都城名；与别国重名加方位
    private static void Yield(Empire empire, string name, bool stamp)
    {
        string own = OwnName(empire, name);
        if (string.IsNullOrWhiteSpace(own)) return;
        EmpireData data = empire.data;
        string before = empire.GetEmpireFullName();
        data.title_claim_name = name;
        data.title_claim_prev_prefix = data.directPre ?? "";
        bool holy = OverallHelperFunc.IsHolyPrefix(data);
        if (!holy) data.directPre = "";
        // 与别国完全同名(前缀也一样，让出后不带前缀)才加方位
        Empire namesake = ModClass.EMPIRE_MANAGER.FirstOrDefault(other => other?.data != null && other != empire && !other.isRekt() &&
                                                                         !other.IsArchived() && other.CoreKingdom?.capital != null &&
                                                                         string.Equals(other.GetEmpireName(), own, StringComparison.Ordinal) &&
                                                                         PrefixOf(other.data).Length == 0);
        City mine = empire.CoreKingdom.capital, theirs = namesake?.CoreKingdom?.capital;
        if (!holy && mine != null && theirs != null)
            data.directPre = RealmNameRules.Direction4(theirs.city_center.x, theirs.city_center.y,
                mine.city_center.x, mine.city_center.y) ?? RealmNameRules.Later;
        empire.SetEmpireName(own, preserveEmpireType: true);
        data.title_claim_own_name = data.core_name ?? "";
        data.title_claim_yielded = true;
        if (stamp) data.title_claim_switched_at = World.world.getCurWorldTime();
        EventRecorder.Record(empire, string.Format(LM.Get("realm_title_yield_history"), before, name, empire.GetEmpireFullName()));
        LogService.LogInfo($"[EmpireCraft][同名相争] {before} 未据其半，不得用 {name}，改称 {empire.GetEmpireFullName()}" +
                           (stamp ? "(正统易手)" : "(同名改正)"));
    }

    // 复称：据有其一半以上，或别家都不用了
    private static void Take(Empire empire, string name, bool stamp, bool alone)
    {
        EmpireData data = empire.data;
        string before = empire.GetEmpireFullName();
        data.directPre = data.title_claim_prev_prefix ?? "";
        data.title_claim_yielded = false;
        data.title_claim_own_name = "";
        data.title_claim_prev_prefix = "";
        empire.SetEmpireName(name, preserveEmpireType: true);
        if (stamp) data.title_claim_switched_at = World.world.getCurWorldTime();
        EventRecorder.Record(empire, string.Format(LM.Get(alone ? "realm_title_take_alone_history" : "realm_title_take_history"),
            before, empire.GetEmpireFullName()));
        LogService.LogInfo($"[EmpireCraft][同名相争] {before} {(alone ? "别家已不用" : "据有其半")}，复称 {empire.GetEmpireFullName()}");
    }

    private static string OwnName(Empire empire, string contested)
    {
        Kingdom core = empire.CoreKingdom;
        foreach (string candidate in new[]
                 {
                     core.capital?.GetTitle()?.data?.name, core.GetMainTitle()?.data?.name,
                     core.capital?.EnsureCityCoreName(), core.capital?.GetCityName()
                 })
        {
            string name = EmpireCoreManager.NormalizeCoreName(candidate);
            if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, contested, StringComparison.Ordinal)) return name;
        }
        return "";
    }
}

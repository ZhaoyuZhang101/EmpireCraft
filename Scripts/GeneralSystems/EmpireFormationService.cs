using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 称帝的正统路线。数值越小优先级越高，同时满足多条时只取最好的一条。
public enum EmpireFormationRoute
{
    None = 0,
    Legitimacy = 1,  // 承续法统：控制前朝帝国核心的法理
    Restoration = 2, // 宗室复国：前朝皇族血脉，前朝灭亡未满百年
    Acclamation = 3, // 诸侯推戴：同文化过半独立国家对其好感为正
    Hegemony = 4,    // 武力霸权：国力达到同文化第二强的两倍
    Fallback = 5     // 兜底：本文化五十年无帝国，由最强者称帝
}

// 称帝逻辑：
//   · 资格 = 基础条件(独立、有君、有主法理、国库不负、人口≥200、至少三城、不在冷却期；同文化已有帝国也可称帝)
//     主法理在别国帝国核心里的称帝为"僭越"：天命 20，与该帝国并立并互为正统对手，之后任一方都可发起正统之争
//            + 至少满足一条正统路线；
//   · 筹备期(称帝剧情进行中)打了败仗、国库转负、被同文化别国超过、或不再满足路线，称帝即失败，
//     十年内不能再称帝，国王威望下降；
//   · 称帝后按路线定初始天命与国号(国号见 Empire.SelectFoundingEmpireName)；原同盟成员按好感
//     归附/朝贡/退出，同文化不服者记仇，彼此接壤的不服者结成反帝同盟。
public static class EmpireFormationService
{
    public const int MinimumPopulation = 200;
    public const int MinimumCities = 3;
    private const int FallbackYears = 50;
    private const int RestorationYears = 100;
    private const float HegemonyRatio = 2f;
    private const int FailureCooldownYears = 10;
    private const int FailureRenownPenalty = 30;
    private const int AllyJoinOpinion = 50;
    private const int GrudgeYears = 20;
    private const int GrudgeStrength = 150;
    private const int HegemonyGrudgeStrength = 250;

    public static int GetInitialMandate(EmpireFormationRoute route) => route switch
    {
        EmpireFormationRoute.Legitimacy => 80,
        EmpireFormationRoute.Restoration => 70,
        EmpireFormationRoute.Acclamation => 60,
        EmpireFormationRoute.Hegemony => 40,
        EmpireFormationRoute.Fallback => 30,
        _ => 100
    };

    public static string GetRouteName(EmpireFormationRoute route) => LM.Get($"empire_formation_route_{route}");

    #region 资格

    public static bool IsInFailureCooldown(Kingdom kingdom)
    {
        double failed = kingdom.GetOrCreate().empire_formation_failed_timestamp;
        return failed >= 0 && Date.getYearsSince(failed) < FailureCooldownYears;
    }

    public static EmpireFormationRoute GetBestRoute(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || !kingdom.hasKing()) return EmpireFormationRoute.None;
        string culture = CultureService.GetRealmCulture(kingdom);
        if (!CultureService.IsValidCulture(culture)) return EmpireFormationRoute.None;
        if (MeetsLegitimacy(kingdom)) return EmpireFormationRoute.Legitimacy;
        if (MeetsRestoration(kingdom)) return EmpireFormationRoute.Restoration;
        List<Kingdom> rivals = GetSameCultureIndependents(kingdom, culture);
        if (MeetsAcclamation(kingdom, rivals)) return EmpireFormationRoute.Acclamation;
        if (MeetsHegemony(kingdom, rivals)) return EmpireFormationRoute.Hegemony;
        if (GetYearsWithoutEmpire(culture) >= FallbackYears && kingdom.IsStrongestEmpireCandidateOfCulture())
            return EmpireFormationRoute.Fallback;
        return EmpireFormationRoute.None;
    }

    // 承续法统：沿用帝国核心已有的"崛起所需头衔数"，并且都城要在核心法理之内
    private static bool MeetsLegitimacy(Kingdom kingdom)
    {
        EmpireCore core = EmpireCoreManager.GetRiseCandidateCore(kingdom);
        // 该核心仍属于某个现存帝国时谈不上承续法统
        if (core == null || EmpireCoreManager.GetEmpires(core).Any(empire => !empire.IsArchived())) return false;
        if (EmpireCoreManager.GetControlledCoreTitleCount(core, kingdom) <
            EmpireCoreManager.GetRequiredRiseTitleCount(core)) return false;
        KingdomTitle capitalTitle = kingdom.capital?.GetTitle();
        if (capitalTitle == null || !EmpireCoreManager.ContainsTitle(core, capitalTitle)) return false;
        List<City> coreCities = EmpireCoreManager.GetCities(core).Where(city => city != null && !city.isRekt()).ToList();
        if (coreCities.Count == 0) return false;
        int controlled = coreCities.Count(city => city.kingdom == kingdom);
        return controlled >= (int)Math.Ceiling(coreCities.Count / 2f);
    }

    // 宗室复国：国王所在宗族曾是某个帝国的皇族，该帝国灭亡不满百年，且宗族眼下没有在统治任何帝国
    private static bool MeetsRestoration(Kingdom kingdom)
    {
        SpecificClan clan = kingdom.king?.GetSpecificClan();
        if (clan == null || !clan.HasHistoryEmpire()) return false;
        if (clan.empire_fall_timestamp < 0 || Date.getYearsSince(clan.empire_fall_timestamp) > RestorationYears)
            return false;
        return ModClass.EMPIRE_MANAGER == null ||
               !ModClass.EMPIRE_MANAGER.Any(empire => empire != null && !empire.isRekt() &&
                                                      empire.EmpireSpecificClan == clan);
    }

    // 诸侯推戴：同文化其他独立国家中超过半数(至少两国)对它好感为正
    private static bool MeetsAcclamation(Kingdom kingdom, List<Kingdom> rivals)
    {
        if (rivals.Count < 2) return false;
        int supporters = rivals.Count(other => GetOpinion(other, kingdom) > 0);
        return supporters >= 2 && supporters * 2 > rivals.Count;
    }

    // 武力霸权：综合国力达到同文化第二强(其他独立国家中最强者)的两倍
    private static bool MeetsHegemony(Kingdom kingdom, List<Kingdom> rivals)
    {
        double second = rivals.Select(GetBlocPower).DefaultIfEmpty(0d).Max();
        return GetBlocPower(kingdom) >= second * HegemonyRatio;
    }

    // 城邦一城一国，实力看整个城邦同盟：同盟成员不算竞争者，国力按全同盟合计
    private static bool IsCityStateBloc(Kingdom kingdom) =>
        CityStateService.IsCityState(kingdom) && kingdom.hasAlliance();

    private static IEnumerable<Kingdom> GetBlocMembers(Kingdom kingdom) =>
        IsCityStateBloc(kingdom)
            ? kingdom.getAlliance().kingdoms_hashset.Where(member => member != null && !member.isRekt())
            : new[] { kingdom };

    public static double GetBlocPower(Kingdom kingdom) =>
        kingdom == null ? 0d : GetBlocMembers(kingdom).Sum(member => member.GetNationalPower());

    private static List<Kingdom> GetSameCultureIndependents(Kingdom kingdom, string culture)
    {
        HashSet<Kingdom> bloc = GetBlocMembers(kingdom).ToHashSet();
        return World.world.kingdoms.Where(other =>
                other != null && other != kingdom && !bloc.Contains(other) && !other.isRekt() && other.hasKing() &&
                !other.IsEmpire() && !other.IsInEmpire() &&
                string.Equals(CultureService.GetRealmCulture(other), culture, StringComparison.Ordinal))
            .ToList();
    }

    private static int GetOpinion(Kingdom from, Kingdom to) =>
        World.world?.diplomacy?.getOpinion(from, to)?.total ?? 0;

    // 本文化连续多少年没有帝国。第一次发现某文化无帝国时开始计时(旧存档从读档时开始计)。
    public static float GetYearsWithoutEmpire(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return 0f;
        if (!ModClass.CULTURE_NO_EMPIRE_SINCE.TryGetValue(culture, out double since))
        {
            ModClass.CULTURE_NO_EMPIRE_SINCE[culture] = World.world.getCurWorldTime();
            return 0f;
        }
        return Date.getYearsSince(since);
    }

    #endregion

    #region 帝国兴亡记录

    public static void OnEmpireCreated(Empire empire)
    {
        string culture = empire == null ? "" : CultureService.GetEmpireDefaultCulture(empire);
        if (CultureService.IsValidCulture(culture)) ModClass.CULTURE_NO_EMPIRE_SINCE.Remove(culture);
    }

    // 帝国解散时调用：记下皇族的亡国时间；如果该文化已没有别的帝国，开始"无帝国"计时。
    public static void OnEmpireDissolving(Empire empire)
    {
        if (empire?.data == null || World.world == null) return;
        double now = World.world.getCurWorldTime();
        SpecificClan clan = empire.EmpireSpecificClan;
        if (clan != null) clan.empire_fall_timestamp = now;
        string culture = CultureService.GetEmpireDefaultCulture(empire);
        if (CultureService.IsValidCulture(culture) && !CultureService.HasActiveEmpireForCulture(culture, empire))
            ModClass.CULTURE_NO_EMPIRE_SINCE[culture] = now;
    }

    #endregion

    #region 筹备期

    // 称帝剧情开始前调用：记下路线、开始时间，以及此时同文化比自己强的国家数
    public static void BeginPreparation(Kingdom kingdom, EmpireFormationRoute route)
    {
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        data.empire_formation_route = route;
        data.empire_formation_started_timestamp = World.world.getCurWorldTime();
        data.empire_formation_stronger_rivals = CountStrongerRivals(kingdom);
    }

    private static int CountStrongerRivals(Kingdom kingdom)
    {
        string culture = CultureService.GetRealmCulture(kingdom);
        double power = GetBlocPower(kingdom);
        return GetSameCultureIndependents(kingdom, culture).Count(other => GetBlocPower(other) > power);
    }

    // 称帝剧情的 check_should_continue。返回 false 即剧情中止；只有"筹备失败"才会进入冷却。
    public static bool ShouldContinuePreparation(Actor actor)
    {
        Kingdom kingdom = actor?.kingdom;
        if (kingdom == null || kingdom.isRekt() || !actor.isKing() || kingdom.king != actor) return false;
        // 同文化已经有人先一步称帝，或者基础条件(禁止称帝法则、已入帝国等)不再满足：直接取消，不算失败
        if (!MeetsBaseRequirements(kingdom)) return false;

        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        string reason = null;
        if (data.empire_formation_lost_war_timestamp >= 0 &&
            data.empire_formation_lost_war_timestamp >= data.empire_formation_started_timestamp)
            reason = "empire_formation_fail_war";
        else if (kingdom.GetMoney() < 0)
            reason = "empire_formation_fail_money";
        else if (CountStrongerRivals(kingdom) > data.empire_formation_stronger_rivals)
            reason = "empire_formation_fail_surpassed";
        else if (GetBestRoute(kingdom) == EmpireFormationRoute.None)
            reason = "empire_formation_fail_route";
        if (reason == null) return true;

        FailPreparation(kingdom, actor, reason);
        return false;
    }

    private static void FailPreparation(Kingdom kingdom, Actor king, string reasonKey)
    {
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        data.empire_formation_failed_timestamp = World.world.getCurWorldTime();
        data.empire_formation_route = EmpireFormationRoute.None;
        data.empire_formation_started_timestamp = -1d;
        king.editRenown(-FailureRenownPenalty);
        string content = string.Format(LM.Get("empire_formation_failed_history"), king.getName(),
            kingdom.GetKingdomName(), LM.Get(reasonKey));
        king.RecordPersonalHistory(content);
        ActionLibrary.showWhisperTip(content);
        LogService.LogInfo(content);
    }

    // 战争结束时对战败方调用：正在筹备称帝的国家会因此失败
    public static void OnWarLost(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (data.empire_formation_started_timestamp >= 0)
            data.empire_formation_lost_war_timestamp = World.world.getCurWorldTime();
    }

    public const int UsurpationMandate = 20;

    // 主法理(城邦为都城)所在核心目前属于哪个现存帝国；没有则返回 null
    public static Empire GetSeatCoreEmpire(Kingdom kingdom)
    {
        City seat = kingdom?.GetMainTitle()?.title_capital;
        if (seat == null && CityStateService.IsCityState(kingdom)) seat = kingdom?.capital;
        EmpireCore core = seat == null || seat.isRekt() ? null : seat.GetEmpireCore();
        return core == null
            ? null
            : EmpireCoreManager.GetEmpires(core).FirstOrDefault(empire => empire != null && !empire.IsArchived());
    }

    public static bool MeetsBaseRequirements(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return false;
        if (Compatibility.AncientWarfareCompatibility.BlocksEmpireFormation(kingdom)) return false;
        if (EmpireCraftWorldLawLibrary.empirecraft_law_ban_empire.isEnabled()) return false;
        if (kingdom.GetMoney() < 0) return false;
        if (!kingdom.hasKing() || kingdom.king == null || kingdom.king.isRekt()) return false;
        if (kingdom.IsEmpire() || kingdom.IsInEmpire()) return false;
        // 城邦没有法理，不以主法理作为称帝门槛
        if (!kingdom.HasMainTitle() && !CityStateService.IsCityState(kingdom)) return false;
        // 城邦一城一国，人口门槛按整个城邦同盟计算
        int population = CityStateService.IsCityState(kingdom) && kingdom.hasAlliance()
            ? kingdom.getAlliance().kingdoms_hashset.Where(member => member != null && !member.isRekt())
                .Sum(member => member.countUnits())
            : kingdom.countUnits();
        if (population < MinimumPopulation) return false;
        // 一城小国不能抢先称帝；城邦一城一国，按整个城邦同盟的城数计
        int cities = GetBlocMembers(kingdom).Sum(member => member.cities?.Count ?? 0);
        if (cities < MinimumCities) return false;
        string culture = CultureService.GetRealmCulture(kingdom);
        // 同文化已有帝国也可以称帝，能否称帝由正统路线决定。
        // 主法理(城邦为都城)已属于某个现存帝国的核心时，称帝即为"僭越"：与该帝国并立并互为正统对手
        return CultureService.IsValidCulture(culture);
    }

    #endregion

    #region 称帝之后

    // 新帝国建立后调用(剧情路线)：定初始天命、处理原同盟成员、同文化不服者记仇与结盟。
    public static void CompleteFormation(Empire empire, Kingdom founder, EmpireFormationRoute route)
    {
        KingdomExtension.KingdomExtraData data = founder.GetOrCreate();
        data.empire_formation_route = EmpireFormationRoute.None;
        data.empire_formation_started_timestamp = -1d;
        if (empire == null) return;

        empire.data.Mandate = GetInitialMandate(route);
        string content = string.Format(LM.Get("empire_formation_route_history"), founder.king?.getName() ?? "",
            GetRouteName(route), empire.GetEmpireName());
        empire.RecordHistory(directContent: content, actorId: founder.king?.id ?? -1L, kingdomId: founder.id);
        ActionLibrary.showWhisperTip(content);

        var dissenters = new List<Kingdom>();
        ResolveAllianceMembers(empire, founder, dissenters);
        // 帝国与同盟不兼容：成员表态完毕后，开国者原来的同盟立即解散
        if (founder.hasAlliance()) World.world.alliances.dissolveAlliance(founder.getAlliance());

        string culture = CultureService.GetRealmCulture(founder);
        foreach (Kingdom other in GetSameCultureIndependents(founder, culture))
        {
            if (other.HasTakenAlliance() || dissenters.Contains(other)) continue;
            if (GetOpinion(other, founder) <= 0) dissenters.Add(other);
        }
        int strength = route == EmpireFormationRoute.Hegemony ? HegemonyGrudgeStrength : GrudgeStrength;
        foreach (Kingdom dissenter in dissenters)
        {
            KingdomExtension.KingdomExtraData grudge = dissenter.GetOrCreate();
            grudge.proclamation_grudge_empire_id = empire.id;
            grudge.proclamation_grudge_timestamp = World.world.getCurWorldTime();
            grudge.proclamation_grudge_strength = strength;
        }
        FormAntiEmpireAlliances(empire, dissenters.Where(kingdom =>
            string.Equals(CultureService.GetRealmCulture(kingdom), culture, StringComparison.Ordinal)).ToList());
    }

    // 原同盟成员按对新帝国的好感表态：高则归附为诸侯，中等改为朝贡，负则退出同盟(并算作不服者)
    private static void ResolveAllianceMembers(Empire empire, Kingdom founder, List<Kingdom> dissenters)
    {
        Alliance alliance = founder.hasAlliance() ? founder.getAlliance() : null;
        if (alliance?.kingdoms_hashset == null) return;
        foreach (Kingdom member in alliance.kingdoms_hashset.ToList())
        {
            if (member == null || member == founder || !member.isAlive()) continue;
            int opinion = GetOpinion(member, founder);
            string key;
            // 与开国者同一位君主的(共主联盟、城邦同盟)直接归附
            if (opinion >= AllyJoinOpinion || (member.king != null && member.king == founder.king))
            {
                member.SetIndependentValue(50);
                empire.join(member, pForce: true);
                key = "empire_formation_ally_joined";
            }
            else if (opinion >= 0 && FeudalConquestService.HasTributeInstitution(empire))
            {
                alliance.leave(member);
                member.JoinTakenAlliance(empire, pForce: true);
                key = "empire_formation_ally_tributary";
            }
            else if (opinion >= 0)
            {
                // 没有朝贡体系：不称臣也不结怨，只是退出同盟
                alliance.leave(member);
                key = "empire_formation_ally_parted";
            }
            else
            {
                alliance.leave(member);
                dissenters.Add(member);
                key = "empire_formation_ally_left";
            }
            empire.RecordHistory(directContent: string.Format(LM.Get(key), member.GetKingdomName(),
                empire.GetEmpireName()), kingdomId: member.id);
        }
    }

    // 不服者中彼此陆地接壤的连成一片，每一片(至少两国)结成一个反帝同盟；已在别的同盟里的不拉入
    private static void FormAntiEmpireAlliances(Empire empire, List<Kingdom> dissenters)
    {
        var pool = dissenters.Where(kingdom => kingdom != null && kingdom.isAlive() && !kingdom.hasAlliance())
            .ToHashSet();
        while (pool.Count > 0)
        {
            Kingdom seed = pool.First();
            var group = new List<Kingdom>();
            var frontier = new Queue<Kingdom>();
            frontier.Enqueue(seed);
            pool.Remove(seed);
            while (frontier.Count > 0)
            {
                Kingdom current = frontier.Dequeue();
                group.Add(current);
                foreach (Kingdom neighbour in GetLandNeighbours(current).Where(pool.Contains).ToList())
                {
                    pool.Remove(neighbour);
                    frontier.Enqueue(neighbour);
                }
            }
            if (group.Count < 2) continue;
            Alliance alliance = World.world.alliances.newAlliance(group[0], group[1]);
            if (alliance == null) continue;
            ModAllianceService.Mark(alliance);
            alliance.setName(string.Format(LM.Get("empire_formation_anti_alliance_name"), empire.GetEmpireName()));
            foreach (Kingdom member in group.Skip(2)) alliance.join(member, true, true);
            empire.RecordHistory(directContent: string.Format(LM.Get("empire_formation_anti_alliance"),
                string.Join("、", group.Select(kingdom => kingdom.GetKingdomName())), empire.GetEmpireName()));
        }
    }

    private static IEnumerable<Kingdom> GetLandNeighbours(Kingdom kingdom)
    {
        if (kingdom?.cities == null) yield break;
        var seen = new HashSet<Kingdom>();
        foreach (City city in kingdom.cities)
        {
            if (city?.neighbours_kingdoms == null) continue;
            foreach (Kingdom neighbour in city.neighbours_kingdoms)
                if (neighbour != null && neighbour != kingdom && seen.Add(neighbour)) yield return neighbour;
        }
    }

    // 好感修正 opinion_empire_proclamation_grudge：对称帝不服的国家，对那个帝国的核心王国好感下降，
    // 二十年内线性恢复
    public static int GetGrudgeOpinion(Kingdom from, Kingdom to)
    {
        if (from == null || to == null) return 0;
        KingdomExtension.KingdomExtraData data = from.GetOrCreate();
        if (data.proclamation_grudge_empire_id < 0 || data.proclamation_grudge_timestamp < 0) return 0;
        Empire empire = to.GetEmpire();
        if (empire == null || empire.id != data.proclamation_grudge_empire_id || empire.CoreKingdom != to) return 0;
        float years = Date.getYearsSince(data.proclamation_grudge_timestamp);
        if (years >= GrudgeYears) return 0;
        return -(int)(data.proclamation_grudge_strength * (1f - years / GrudgeYears));
    }

    #endregion
}

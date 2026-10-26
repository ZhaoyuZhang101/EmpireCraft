using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 部落之间的战争(用户 2026-10-17 "纳入部落这个战争行为也要先研究了部落联合以后才可以""此前的战争只能是赔款夺取物资"
// "无法吞并"；问答选"战后全还，只拿赔款""成为共事的成员部落")。部落一部一城，城一破就等于灭国，所以部落之间破城不易主：
//   · 本文化还没施行「部落联合」：守方赔款求和(国库三成归胜者)，战事就此了结，占的地全数退还；
//   · 施行了「部落联合」以后：守方奉胜者为共主(成为共事的成员部落，见 TribalFissionService.Incorporate)，同样了结战事、
//     退还占地；胜者由此称部落联盟(见 DynastyNaming.HuaxiaOriginEmpireKey)。胜者管不过来、离得太远就照旧只拿赔款。
//   只管双方都是原始政体的部落(见 CityPatch.FinishedCapture，原版小人破城与模组占领都走那里)。
//   华夏开局就能联合诸部(用户 2026-10-22 "开局就可以部落联合")，原「部落联合」节点改为设立诸部议事会(见 TribalCouncilSystem)。
//   战功换天命(用户 2026-10-26 选"战功换天命和爵位")：胜者所在的部落联盟记战功、加天命(见 Concede)，败者的法理并进共主的帝国法理
//   (见 TribalFissionService.AbsorbTitles)；进宗法分封时战功与共事的部落数定王室，共事年数定爵(见 TribalFissionService.SettleRoyalHouse)。
public static class TribalWarSystem
{
    private const float TributeShare = 0.3f;

    // 本文化会不会联合诸部：华夏看「部落联合」(之后的家天下也算)，日本看弥生小国，游牧看诸部联合(之后的汗国也算)，
    // 阿拉伯看部族联合(之后的哈里发国也算；用户 2026-10-17 选"游牧、阿拉伯也从部落起步")；别的文化没有这一步，照旧
    public static bool Unified(Kingdom kingdom)
    {
        string culture = CultureService.GetRealmCulture(kingdom);
        string line = InstitutionSystem.GetCultureLine(culture);
        bool Known(string node) => InstitutionSystem.IsEnacted(culture, node) || InstitutionSystem.IsSuperseded(culture, node);
        return line switch
        {
            "Huaxia" => true,
            "Japan" => Known("japan_yayoi_kuni") || Known("japan_yamato_kingship"),
            "Youmu" => Known("youmu_clan_union") || Known("youmu_tribal_confederation"),
            "Arabic" => Known("arabic_tribal_union") || Known("arabic_tribal_shura"),
            _ => true
        };
    }

    // 正在由本系统了结的战争(战争目标不再结算一遍，见 WarGoalSystem.Settle)
    private static readonly HashSet<long> Settled = new();

    public static bool Settling(War war) => war?.data != null && Settled.Contains(war.data.id);

    // 破城时调用：是部落之间的仗就按上面的规矩了结，返回 true(城不易主)
    public static bool TrySubmit(City city, Kingdom winner)
    {
        Kingdom loser = city?.kingdom;
        if (loser == null || winner == null || loser == winner || !TribalFissionService.IsTribal(loser) ||
            !TribalFissionService.IsTribal(winner)) return false;
        War war = loser.getWars().FirstOrDefault(candidate => candidate != null && !candidate.hasEnded() &&
                                                             (candidate.isAttacker(winner) && candidate.isDefender(loser) ||
                                                              candidate.isDefender(winner) && candidate.isAttacker(loser)));
        if (war == null) return false;
        try
        {
            Concede(loser, winner);
            ReturnLand(loser, winner);
            // 败者退出这场仗(是主角就整场了结，原版 endWar 当场结算)；这一下了结的不再按战争目标结算一遍
            // (赔款、纳为共事已在 Concede 里办了)。没了结的(还有别家在打)照旧，日后了结时正常结算
            Settled.Add(war.data.id);
            try
            {
                war.lostWar(loser);
                if (!war.hasEnded() && (war.isAttacker(loser) || war.isDefender(loser))) war.leaveWar(loser);
            }
            finally
            {
                Settled.Remove(war.data.id);
            }
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][部落之战] {loser.GetKingdomName()} 求和结算失败: {exception.Message}");
        }
        return true;
    }

    // 战功换天命(用户 2026-10-26 选"战功换天命和爵位")：胜者所在的部落联盟(胜者自己是共主，或胜者共事的共主)每胜一场记一分战功，
    // 新收服一部再记一分，败者带着自己的共事部落一同归附的每部再记一分(EmpireData.tribal_war_merit)；天命按这一仗新记的战功每分 +1，
    // 一仗至多 MeritMandateMax，只加到 MeritMandateCeiling 为止(推戴立的部落联盟起点 70，见 EmpireFormationService)。
    // 战功在进宗法分封时按资历定王室(见 TribalFissionService.SettleRoyalHouse)。胜者不在部落联盟里(只拿赔款)的不记
    private const int MeritMandateMax = 4, MeritMandateCeiling = 85;

    // 败者服输：赔款(国库三成)；施行了部落联合的奉胜者为共主(见 TribalFissionService.Incorporate)。统一战争收服部落也走这里
    public static void Concede(Kingdom loser, Kingdom winner)
    {
        string loserName = loser.GetKingdomName(), winnerName = winner.GetKingdomName();
        int tribute = global::System.Math.Max(0, (int)(loser.GetMoney() * TributeShare));
        if (tribute > 0)
        {
            loser.SubMoney(tribute, TreasuryCategory.Reparations);
            winner.AddMoney(tribute, TreasuryCategory.Tribute);
        }
        Empire before = TribalFissionService.OverlordOf(loser);
        int followed = 0;
        bool incorporated = Unified(winner) && TribalFissionService.Incorporate(winner, loser, out followed);
        Empire league = TribalFissionService.LeagueOf(winner);
        // 共事的部落反败共主：战功不记给败了的共主(它随后自立，见 TribalAssemblySystem.OnConcede)
        if (league?.CoreKingdom == loser) league = null;
        // 共主讨伐不臣、重新收回的不算新收服
        bool fresh = incorporated && league != null && before != league;
        int merit = Award(league, 1 + (fresh ? 1 + followed : 0));
        string key = incorporated ? "tribal_war_incorporated" : "tribal_war_tribute";
        if (tribute <= 0) key += "_none";
        string content = string.Format(LM.Get(key), loserName, winnerName, tribute);
        // 史书记进收服败者的共主、胜者所在的部落联盟；都没有(胜者是独立的部落，只拿赔款)记到胜者名下(见 EventRecorder.Record)
        Empire chronicle = TribalFissionService.OverlordOf(loser) ?? league ?? winner.GetEmpire();
        if (chronicle != null)
        {
            HelperFunc.TranslateHelper.LogEventOnly(content, winner);
            chronicle.RecordHistory(directContent: content, kingdomId: loser.id);
        }
        else EventRecorder.Record(winner, content);
        LogService.LogInfo("[EmpireCraft][部落之战] " + content +
                           (merit > 0 ? $"(战功 +{merit}，共 {league.data.tribal_war_merit}，天命 {league.data.Mandate})" : ""));
        // 年度会盟：共主讨伐不臣打赢的，败者不臣清零；部落反败共主的就此自立(见 TribalAssemblySystem.OnConcede)
        TribalAssemblySystem.OnConcede(loser, winner);
    }

    // 记战功、换天命；返回记下的战功
    private static int Award(Empire league, int points)
    {
        if (league?.data == null || points <= 0) return 0;
        league.data.tribal_war_merit += points;
        int gain = global::System.Math.Min(global::System.Math.Min(points, MeritMandateMax), MeritMandateCeiling - league.data.Mandate);
        if (gain > 0) league.AddMandate(gain);
        return points;
    }

    // 退还胜者一方(胜者与它的友军)在败者各城占的地；不相干的第三方照旧
    private static void ReturnLand(Kingdom loser, Kingdom winner)
    {
        foreach (City own in loser.cities?.ToList() ?? new List<City>())
        {
            Dictionary<long, List<int>> status = own?.GetOrCreate().OccupiedStatus;
            if (status == null || status.Count == 0) continue;
            foreach (long occupierId in status.Keys.ToList())
            {
                Kingdom occupier = World.world.kingdoms.get(occupierId);
                if (occupier == null || occupier != winner && !FrontSystem.Friendly(winner, occupier)) continue;
                foreach (int zoneId in status[occupierId]?.ToList() ?? new List<int>())
                {
                    TileZone zone = own.zones?.FirstOrDefault(candidate => candidate != null && candidate.id == zoneId);
                    if (zone != null) own.RemoveOccupiedTileZone(occupier, zone);
                }
            }
        }
    }
}

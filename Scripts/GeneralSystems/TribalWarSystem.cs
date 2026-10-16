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
public static class TribalWarSystem
{
    private const float TributeShare = 0.3f;

    // 本文化会不会联合诸部：华夏看「部落联合」(之后的家天下也算)，日本看弥生小国；别的文化没有这一步，照旧
    public static bool Unified(Kingdom kingdom)
    {
        string culture = CultureService.GetRealmCulture(kingdom);
        string line = InstitutionSystem.GetCultureLine(culture);
        bool Known(string node) => InstitutionSystem.IsEnacted(culture, node) || InstitutionSystem.IsSuperseded(culture, node);
        return line switch
        {
            "Huaxia" => Known("huaxia_tribal_alliance") || Known("huaxia_hereditary_kingship"),
            "Japan" => Known("japan_yayoi_kuni") || Known("japan_yamato_kingship"),
            _ => true
        };
    }

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
            string loserName = loser.GetKingdomName(), winnerName = winner.GetKingdomName();
            int tribute = (int)(loser.GetMoney() * TributeShare);
            if (tribute > 0)
            {
                loser.SubMoney(tribute, TreasuryCategory.Reparations);
                winner.AddMoney(tribute, TreasuryCategory.Tribute);
            }
            bool incorporated = Unified(winner) && TribalFissionService.Incorporate(winner, loser);
            string content = string.Format(LM.Get(incorporated ? "tribal_war_incorporated" : "tribal_war_tribute"),
                loserName, winnerName, tribute);
            // 占的地全数退还(破城那座由 CityPatch 清掉)，战事了结
            foreach (City own in loser.cities?.ToList() ?? new global::System.Collections.Generic.List<City>())
                own?.ClearOccupiedStatus();
            if (!war.hasEnded())
                World.world.wars.endWar(war, war.isAttacker(winner) ? WarWinner.Attackers : WarWinner.Defenders);
            HelperFunc.TranslateHelper.LogEventOnly(content, winner);
            (TribalFissionService.OverlordOf(loser) ?? winner.GetEmpire())?.RecordHistory(directContent: content, kingdomId: loser.id);
            LogService.LogInfo("[EmpireCraft][部落之战] " + content);
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][部落之战] {loser.GetKingdomName()} 求和结算失败: {exception.Message}");
        }
        return true;
    }
}

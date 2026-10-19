using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诊断(玩家反馈 2026-10-16"游牧不称帝，也不打仗")：每年给日志记几行，查是哪一关卡住了——
//   · [称帝诊断] 二十年以上没有帝国的文化：最强的独立王国卡在称帝的哪一关；
//   · [开战诊断] 整个文化二十年没有一个独立王国打过仗：最强的两家卡在原版"开战"剧情的哪一关。
// 只记录，不改任何东西
public static class FormationDiagnostics
{
    private const float QuietYears = 20f;

    public static void Yearly()
    {
        if (World.world?.kingdoms == null || ModClass.EMPIRE_MANAGER == null) return;
        try
        {
            var groups = World.world.kingdoms.list
                .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.hasKing() && !kingdom.IsEmpire() &&
                                  !kingdom.IsInEmpire())
                .GroupBy(kingdom => CultureService.GetRealmCulture(kingdom))
                .Where(group => CultureService.IsValidCulture(group.Key));
            foreach (var group in groups)
            {
                string culture = group.Key;
                List<Kingdom> ranked = group.OrderByDescending(EmpireFormationService.GetBlocPower).ToList();
                float noEmpire = EmpireFormationService.GetYearsWithoutEmpire(culture);
                if (noEmpire >= QuietYears && !HasEmpire(culture))
                {
                    Kingdom best = ranked[0];
                    LogService.LogInfo($"[EmpireCraft][称帝诊断] {culture} 无帝国 {noEmpire:0} 年，最强 {best.GetKingdomName()}" +
                                       $"(城{best.cities?.Count ?? 0} 人口{best.countUnits()} 国库{best.GetMoney()})：" +
                                       EmpireFormationService.Diagnose(best));
                }
                if (ranked.Any(kingdom => kingdom.hasEnemies() || YearsSinceWar(kingdom) < QuietYears)) continue;
                foreach (Kingdom kingdom in ranked.Take(2))
                    LogService.LogInfo($"[EmpireCraft][开战诊断] {culture} {kingdom.GetKingdomName()} {YearsSinceWar(kingdom):0} 年没打仗：" +
                                       WarGate(kingdom));
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][称帝诊断] 失败: {exception.Message}");
        }
    }

    private static bool HasEmpire(string culture) => ModClass.EMPIRE_MANAGER.Any(empire =>
        empire != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom != null &&
        string.Equals(CultureService.GetRealmCulture(empire.CoreKingdom), culture, StringComparison.Ordinal));

    // 上次打仗至今几年；从没打过的按建国至今算
    private static float YearsSinceWar(Kingdom kingdom)
    {
        double last = kingdom.data.timestamp_last_war;
        return last >= 0d ? Date.getYearsSince(last) : kingdom.getAge();
    }

    // 原版"开战"剧情(独立王国走原版)：剧情门槛、要不要打(isWarNeeded)、找不找得到对象
    private static string WarGate(Kingdom kingdom)
    {
        Actor king = kingdom.king;
        PlotAsset war = PlotsLibrary.new_war ?? AssetManager.plots_library.get("new_war");
        if (king == null) return "没有君主";
        if (war == null) return "原版开战剧情不存在";
        if (kingdom.GetRegime()?.IsAllowDiplomacy() == false) return "政体不许自主外交";
        if (king.hasPlot()) return $"君主正忙别的剧情({king.plot?.getAsset()?.id})";
        // 无小人模式下独立王国不看君主本人的钱、等级、武略与国家声望(见 PlotAssetPatch.checkIsPossible)
        if (!EmpireCraft.Scripts.AI.EmpireCraftPlotsAddition.SkipsWarPlotRulerGate(king))
        {
            if (!king.hasEnoughMoney(war.money_cost)) return $"君主的钱不够({king.money}/{war.money_cost})";
            if (king.level < war.min_level) return $"君主等级 {king.level} < {war.min_level}";
            if (kingdom.getRenown() < war.min_renown_kingdom) return $"国家声望 {kingdom.getRenown()} < {war.min_renown_kingdom}";
            if (king.warfare < war.min_warfare) return $"君主武略 {king.warfare} < {war.min_warfare}";
        }
        if (!war.isAllowedByWorldLaws()) return "世界法则关了外交";
        if (king.hasTrait("pacifist") || king.hasCulture() && king.culture.hasTrait("serenity_now")) return "君主或文化好和平";
        CityPopulationSystem.EnterVanillaScale();
        try
        {
            if (!DiplomacyHelpers.isWarNeeded(kingdom))
            {
                int warriors = kingdom.countTotalWarriors();
                // 人口与 isWarNeeded 同一口径(古代每户 100 人，见 NoCommonersPatch.WarNeedPopulation)
                float people = CityPopulationSystem.AbstractPopulationEnabled
                    ? EmpireCraft.Scripts.GamePatches.NoCommonersPatch.WarNeedPopulation(kingdom)
                    : kingdom.getPopulationPeople();
                float possible = kingdom.getPopulationTotalPossible();
                return $"原版判定不需要打仗(兵{warriors}、人口{people:0}/{possible:0}、城{kingdom.countCities()}；" +
                       "兵太少，或不到四城且没住满六成)";
            }
            Kingdom target = DiplomacyHelpers.getWarTarget(kingdom);
            if (target == null)
                return $"找不到开战对象(兵{kingdom.countTotalWarriors()}；对象要兵不比自己多、走得到、关系不好、上次停战已满年限)";
            return $"条件都满足(对象 {target.GetKingdomName()})，等原版剧情掷骰";
        }
        finally
        {
            CityPopulationSystem.ExitVanillaScale();
        }
    }
}

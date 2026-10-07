using System;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 失都改称(模拟宋室南渡称"南宋"、晋室东迁称"东晋")：
//   统一王朝(有帝国核心)的核心首都落到别国手里满 LostYears 年，始终没能夺回，
//   国号前加方位——按现今都城在旧都的哪个方向：在南称"南"、在东称"东"……；
//   原来的前缀(如"大")先记下，日后收复旧都就去掉方位、恢复原称。每年检查一次(见 CityPatch 的年度扫描)。
public static class CapitalLossNamingSystem
{
    private const int LostYears = 50;
    private static double _lastScan = -1d;

    private static readonly string[] Directions = { "Eastern", "Western", "Southern", "Northern" };

    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.EMPIRE_MANAGER == null) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            try
            {
                Check(empire, now);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 失都改称检查失败: {exception.Message}");
            }
        }
    }

    private static void Check(Empire empire, double now)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived()) return;
        Kingdom core = empire.CoreKingdom;
        if (core == null || core.isRekt() || core.capital == null || core.HasCustomCountryNaming()) return;
        City oldCapital = EmpireCoreManager.Get(empire)?.GetCoreCapital();
        if (oldCapital == null || oldCapital.isRekt()) return;
        EmpireData data = empire.data;
        bool lost = oldCapital.kingdom == null || oldCapital.kingdom.GetEmpire() != empire;
        if (!lost)
        {
            data.core_capital_lost_since = -1d;
            if (!data.capital_loss_prefix_applied) return;
            // 还于旧都：去掉方位，恢复原称
            string southern = empire.GetEmpireFullName();
            data.directPre = data.capital_loss_previous_prefix ?? "";
            data.capital_loss_prefix_applied = false;
            data.capital_loss_previous_prefix = "";
            TranslateHelper.LogEventMessage(string.Format(LM.Get("capital_loss_restored"), southern,
                oldCapital.GetCityName(), empire.GetEmpireFullName()), core);
            return;
        }
        if (data.core_capital_lost_since < 0d || now < data.core_capital_lost_since)
        {
            data.core_capital_lost_since = now;
            return;
        }
        if (data.capital_loss_prefix_applied || Date.getYearsSince(data.core_capital_lost_since) < LostYears) return;
        if (core.capital == oldCapital) return;
        string before = empire.GetEmpireFullName();
        data.capital_loss_previous_prefix = Array.IndexOf(Directions, data.directPre) >= 0 ? "" : data.directPre ?? "";
        data.directPre = DirectionFrom(oldCapital.city_center, core.capital.city_center);
        data.capital_loss_prefix_applied = true;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("capital_loss_renamed"), before, oldCapital.GetCityName(),
            LostYears, empire.GetEmpireFullName()), core);
    }

    // 行在：华夏王朝的核心王国(直隶)在核心首都失陷期间(不必满五十年)，类别改称"行在"
    public static bool IsTemporaryCapital(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv()) return false;
        Empire empire = kingdom.GetEmpire();
        if (empire?.data == null || empire.CoreKingdom != kingdom || empire.IsArchived()) return false;
        if (!kingdom.GetKingdomType().ToString().EndsWith("_centre", StringComparison.Ordinal)) return false;
        City oldCapital = EmpireCoreManager.Get(empire)?.GetCoreCapital();
        if (oldCapital == null || oldCapital.isRekt() || kingdom.capital == oldCapital) return false;
        if (oldCapital.kingdom != null && oldCapital.kingdom.GetEmpire() == empire) return false;
        return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) == "Huaxia";
    }

    // 新都在旧都的哪个方向(地图 y 轴朝北)
    private static string DirectionFrom(Vector2 oldPos, Vector2 newPos)
    {
        float dx = newPos.x - oldPos.x, dy = newPos.y - oldPos.y;
        if (Mathf.Abs(dx) > Mathf.Abs(dy)) return dx > 0f ? "Eastern" : "Western";
        return dy > 0f ? "Northern" : "Southern";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Data
{
    public class ConfigActions
    {
        public static void SwitchTitleFreezeCallBack(bool on)
        {
            ModClass.KINGDOM_TITLE_FREEZE = on;
        }
        public static void TextTitleBeenDestroyCallBack(string time)
        {
            ModClass.TITLE_BEEN_DESTROY_TIME = int.Parse(time);
        }
        public static void TitleMaxCitiesCallBack(string count)
        {
            ModClass.TITLE_MAX_CITIES = int.TryParse(count, out int value) ? Math.Max(0, value) : 0;
        }
        public static void CityMaxZonesCallBack(string count)
        {
            ModClass.CITY_MAX_ZONES = int.TryParse(count, out int value) ? Math.Max(0, value) : 50;
        }
        private static int SettlementValue(string text, int fallback, int min, int max = 100000) =>
            int.TryParse(text, out int value) ? Math.Min(max, Math.Max(min, value)) : fallback;
        public static void SettlementBaseGoldCallBack(string v) => ModClass.SETTLEMENT_BASE_GOLD = SettlementValue(v, 200, 50);
        public static void SettlementPerCityGoldCallBack(string v) => ModClass.SETTLEMENT_PER_CITY_GOLD = SettlementValue(v, 25, 0);
        public static void SettlementReserveGoldCallBack(string v) => ModClass.SETTLEMENT_RESERVE_GOLD = SettlementValue(v, 200, 0);
        public static void SettlementWoodCallBack(string v) => ModClass.SETTLEMENT_WOOD = SettlementValue(v, 20, 1);
        public static void SettlementStoneCallBack(string v) => ModClass.SETTLEMENT_STONE = SettlementValue(v, 10, 1);
        public static void SettlementMinHouseholdsCallBack(string v) => ModClass.SETTLEMENT_MIN_HOUSEHOLDS = SettlementValue(v, 10, 10, 1000);
        public static void SettlementTargetHouseholdsCallBack(string v) => ModClass.SETTLEMENT_TARGET_HOUSEHOLDS = SettlementValue(v, 20, 10, 1000);
        public static void SettlementRetainHouseholdsCallBack(string v) => ModClass.SETTLEMENT_RETAIN_HOUSEHOLDS = SettlementValue(v, 30, 30, 1000);
        public static void SettlementMigrationPercentCallBack(string v) => ModClass.SETTLEMENT_MAX_MIGRATION_PERCENT = SettlementValue(v, 25, 1, 25);
        public static void SettlementFoodYearsCallBack(string v) => ModClass.SETTLEMENT_FOOD_YEARS = SettlementValue(v, 2, 1, 20);
        public static void SettlementPreparationMonthsCallBack(string v) => ModClass.SETTLEMENT_PREPARATION_MONTHS = SettlementValue(v, 6, 1, 120);
        public static void SettlementCooldownMonthsCallBack(string v) => ModClass.SETTLEMENT_COOLDOWN_MONTHS = SettlementValue(v, 36, 1, 1200);
        public static void FeudalUnionRealmLimitCallBack(string count)
        {
            ModClass.FEUDAL_UNION_REALM_LIMIT = int.TryParse(count, out int value) ? Math.Max(1, value) : 3;
        }
        public static void WarEndYearCallBack(string time)
        {
            ModClass.WAR_END_YEAR = int.Parse(time);
        }
        // 0 = 永远不承认分治
        public static void WarlordEraYearsCallBack(string years)
        {
            if (int.TryParse(years, out int value)) ModClass.WARLORD_ERA_YEARS = global::System.Math.Max(0, value);
        }
        public static void saveFreezeCallBack(bool on)
        {
            ModClass.SAVE_FREEZE = on;
        }
        public static void HighPopulationPerformanceCallBack(bool on)
        {
            ModClass.PERFORMANCE_HIGH_POPULATION_MODE = on;
        }
        public static void AdaptiveThroughputPerformanceCallBack(bool on)
        {
            ModClass.PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE = on;
            EmpireCraft.Scripts.System.EmpireCraftStrategicScheduler.Reset();
        }
        public static void HiddenVisualsPerformanceCallBack(bool on)
        {
            ModClass.PERFORMANCE_SKIP_HIDDEN_VISUALS = on;
        }
        public static void NameplateOverlapPerformanceCallBack(bool on)
        {
            ModClass.PERFORMANCE_SKIP_NAMEPLATE_OVERLAP = on;
        }
    }
}

using System.Globalization;

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

        public static void WarEndYearCallBack(string time)
        {
            ModClass.WAR_END_YEAR = int.Parse(time);
        }

        public static void AIDetectIntervalCallBack(string time)
        {
            if (string.IsNullOrWhiteSpace(time))
            {
                ModClass.AI_DETECT_INTERVAL = 0.2f;
                return;
            }

            string normalized = time.Trim().Replace(',', '.');
            float value;
            if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                value = 0.2f;
            }

            if (value < 0.01f)
            {
                value = 0.01f;
            }

            ModClass.AI_DETECT_INTERVAL = value;
        }

        public static void KingdomAIFullScanDurationCallBack(string time)
        {
            ModClass.KINGDOM_AI_FULL_SCAN_DURATION = ParsePositiveFloat(time, 1f);
        }

        public static void CityAIFullScanDurationCallBack(string time)
        {
            ModClass.CITY_AI_FULL_SCAN_DURATION = ParsePositiveFloat(time, 2f);
        }

        public static void saveFreezeCallBack(bool on)
        {
            ModClass.SAVE_FREEZE = on;
        }

        private static float ParsePositiveFloat(string time, float fallback)
        {
            if (string.IsNullOrWhiteSpace(time))
            {
                return fallback;
            }

            string normalized = time.Trim().Replace(',', '.');
            float value;
            if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                value = fallback;
            }

            if (value < 0.01f)
            {
                value = 0.01f;
            }

            return value;
        }
    }
}

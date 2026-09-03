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
        public static void WarEndYearCallBack(string time)
        {
            ModClass.WAR_END_YEAR = int.Parse(time);
        }
        public static void saveFreezeCallBack(bool on)
        {
            ModClass.SAVE_FREEZE = on;
        }
        public static void HighPopulationPerformanceCallBack(bool on)
        {
            ModClass.PERFORMANCE_HIGH_POPULATION_MODE = on;
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

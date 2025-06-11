using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class KingdomWindowPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        // UnitWindow类的补丁
        new Harmony(nameof(set_stats_rows)).Patch(
            AccessTools.Method(typeof(KingdomWindow), nameof(KingdomWindow.showStatsRows)),
            prefix: new HarmonyMethod(GetType(), nameof(set_stats_rows))
        );
        LogService.LogInfo("王国窗口补丁加载成功");
    }

    private static void set_stats_rows(KingdomWindow __instance)
    {
        countryLevel country_level = __instance.meta_object.GetCountryLevel();
        __instance.showStatRow(LM.Get("CountryLevel"), LM.Get("default_" + country_level.ToString()), MetaType.None, -1L, null, LM.Get("CountryLevel"), null);
    }
}

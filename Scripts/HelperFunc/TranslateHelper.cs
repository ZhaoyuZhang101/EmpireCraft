using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.TipAndLog;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class TranslateHelper
    {
        public static string GetPeerageTranslate(PeeragesLevel pl)
        {
            return LM.Get("default_" + pl.ToString());
        }
        public static void LogNewEmpire(Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.become_new_empire_log, empire.empire.king.name, empire.empire.data.name)
            {
                color_special1 = empire.empire.getColor().getColorText(),
                color_special2 = empire.empire.getColor().getColorText(),
            }.add();
        }

        public static void LogMinistorTryAqcuireEmpire(Actor ministor, Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.ministor_try_aqcuire_empire_log, ministor.GetTitle(), ministor.name, empire.name)
            {
                color_special1 = ministor.getColor()._colorText,
                color_special2 = ministor.getColor()._colorText,
                color_special3 = empire.getColor()._colorText,
            }.add();
        }

        /// <summary>
        /// 记录“大臣获封称号”
        ///     $empire$ → 帝国名
        ///     $minister$ → 大臣名
        ///     $title$ → 新称号
        /// </summary>
        public static void LogPowerfulMinisterAcquireTitle(Actor minister, Empire empire, string title)
        {
            LogService.LogInfo("记录大臣获取称号: " + title + " " + minister.data.name + " " + empire.GetEmpireName());
            new WorldLogMessage(EmpireCraftWorldLogLibrary.empire_powerful_minister_aquire_title,
                minister.data.name,
                empire.GetEmpireName(),
                title)
            {
                color_special1 = minister.kingdom.getColor().getColorText(),
                color_special2 = empire.empire.getColor().getColorText()

            }.add();
        }

        /// <summary>
        /// 记录“大臣获取了天命”
        ///     $title$ → 称号
        ///     $minister$ → 大臣名
        ///     $empire$ → 新帝国名
        /// </summary>
        public static void LogMinistorAqcuireEmpire(Actor minister, Empire new_empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.ministor_aqcuire_empire_log,
                minister.GetTitle(),
                minister.name,
                new_empire.GetEmpireName())
            {
                color_special1 = minister.getColor()._colorText,
                color_special2 = new_empire.empire.getColor()._colorText,
            }.add();
        }
        /// <summary>
        /// 记录“恢复历史帝国”
        ///     $clan$ -> 家族
        ///     $empire$ → 帝国名
        /// </summary>
        public static void LogRestoreHistoricalEmpire(Clan clan, Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.restore_historcial_empire,
                empire.name)
            {
                color_special1 = clan.getColor()._colorText,
                color_special2 = empire.empire.getColor()._colorText
            }.add();
        }
    }
}

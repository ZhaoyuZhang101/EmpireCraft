using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
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
        public static void SampleLog(Kingdom kingdom)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.become_new_empire_log, kingdom.king.name, kingdom.data.name)
            {
                color_special1 = kingdom.getColor().getColorText(),
                color_special2 = kingdom.getColor().getColorText(),
                color_special3 = kingdom.getColor().getColorText(),
            }.add();
        }
    }
}

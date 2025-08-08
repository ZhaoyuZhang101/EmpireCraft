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
    public static class WorldLogHelper
    {
        public static void SampleLog(Kingdom kingdom, string testMessage)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.sample_log, kingdom.king.name, kingdom.data.name, testMessage)
            {
                color_special1 = kingdom.getColor().getColorText(),
                color_special2 = kingdom.getColor().getColorText()
            }.add();
        }
    }
}

using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftWorldLogLibrary
{
    public static WorldLogAsset sample_log;

    public static void init()
    {
        WorldLogLibrary wl = AssetManager.world_log_library;
        sample_log = wl.add(new WorldLogAsset
        {
            id = nameof(sample_log),
            group = "kings",
            path_icon = "crown2",
            color = Toolbox.color_log_good,
            text_replacer = delegate (WorldLogMessage pMessage, ref string pText)
            {
                wl.updateText(ref pText, pMessage, "$actor$", 1);
                wl.updateText(ref pText, pMessage, "$kingdom$", 2);
                wl.updateText(ref pText, pMessage, "$testMessage$", 3);
            }
        });
    }

}

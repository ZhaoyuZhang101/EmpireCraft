using EpPathFinding.cs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftHistoryDataLibrary
{
    public static void init()
    {
        // Initialize the library or perform any setup required
        // This could include loading data, setting up connections, etc.
        HistoryDataLibrary hl = AssetManager.history_data_library;
        hl.add(new HistoryDataAsset
        {
            id = "empire",
            statistics_asset = "kingdoms",
            color_hex = "#FF8500",
            path_icon = "ChineseCrown",
            max = true,
            category_group = GraphCategoryGroup.Noosphere
        }
        );
        hl.add(new HistoryDataAsset
        {
            id = "kingdomTitle",
            statistics_asset = "kingdoms",
            color_hex = "#FF8500",
            path_icon = "ChineseCrown",
            max = true,
            category_group = GraphCategoryGroup.Noosphere
        }
        );
    }
}

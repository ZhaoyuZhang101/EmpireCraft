using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Layer;
public static class YearName
{
    public static string generateName()
    {
        string CharacterSetPath = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures", "YearName1.csv");
        string CharacterSet2Path = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures", "YearName2.csv");
        List<string> names1 = OnomasticsHelper.getKeysFromPath(CharacterSetPath);
        List<string> names2 = OnomasticsHelper.getKeysFromPath(CharacterSet2Path);
        return "" + LM.Get(names1.GetRandom()) + LM.Get(names2.GetRandom());
    }
}

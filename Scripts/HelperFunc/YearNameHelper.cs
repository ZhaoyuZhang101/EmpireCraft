using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.HelperFunc;
public static class YearNameHelper
{
    public static string generateName()
    {
        string CharacterSetPath = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures", "YearName1.csv");
        string CharacterSet2Path = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures", "YearName2.csv");
        List<string> names1 = OnomasticsHelper.getKeysFromPath(CharacterSetPath);
        List<string> names2 = OnomasticsHelper.getKeysFromPath(CharacterSet2Path);
        // 两个字不重(以前会出"华华"这样的叠字年号)
        string first = LM.Get(names1.GetRandom()), second = LM.Get(names2.GetRandom());
        for (int tries = 0; tries < 8 && second == first; tries++) second = LM.Get(names2.GetRandom());
        return first + second;
    }
}

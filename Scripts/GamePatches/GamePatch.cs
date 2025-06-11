using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches
{
    public interface GamePatch
    {
        public ModDeclare declare { get; set; }
        public void Initialize();
    }

    public static class HelperFunc
    {
        public static string getFamilyName(string pName)
        {
            string[] namePart = pName.Split(' ');
            if (namePart.Length >= 2)
            {
                return namePart[namePart.Length - 2];
            } else
            {
                return namePart[0];
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class OverallHelperFunc
    {
        public static bool IsEmpireLayerOn()
        {
            return PlayerConfig.dict["map_empire_layer"].boolVal;
        }
        
    }
}

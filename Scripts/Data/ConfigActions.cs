using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Data
{
    class ConfigActions
    {
        void SwitchTitleFreezeCallBack(bool on)
        {
            ModClass.KINGDOM_TITLE_FREEZE = on;
        }
        void TextTitleBeenDestroyCallBack(string time)
        {
            ModClass.TITLE_BEEN_DESTROY_TIME = int.Parse(time);
        }
    }
}

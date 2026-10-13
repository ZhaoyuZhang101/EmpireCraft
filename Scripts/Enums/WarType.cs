using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Enums
{
    // 开战目标(见 WarGoalSystem)：None 是旧存档或有自己结局的战争
    public enum WarGoal
    {
        None,
        吞并,
        割地,
        赔款,
        劫掠
    }

    public enum EmpireWarType
    {
        获取帝国,
        伐不臣,
        地方独立,
        攘夷,
        神圣,
        统一,
        派系叛乱,
        地方叛乱,
        民族叛乱,
        宗教叛乱,
        迫使朝贡,
        藩王索取皇位,
        帝国正统,
        去帝号,
        劫掠,
        游牧扩张,
        帝国扩张,
        清君侧,
        索取法理,
        None,
        不奉诏, // 追加在 None 之后，保持旧存档里的枚举值不变
        附庸独立
    }
}

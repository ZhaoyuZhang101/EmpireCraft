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
        劫掠,
        收复,
        迫使附庸,
        // 和约新条款(用户 2026-10-19 选"改宗、开港通商、扶立君主")；追加在末尾以保持旧存档枚举值不变
        改宗,
        通商,
        扶立
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
        附庸独立,
        征伐不服, // 周制天子讨伐不服王化的同文化之国(见 ZhouEraSystem.MarkSubdueWar)
        私战, // 封建制帝国诸侯为法理互争边城(见 HolyRomanSystem.Feuds)；追加在末尾，旧值不变
        对立国王, // 选帝制双重选举后对立国王的争位之战(见 HolyRomanSystem.Election)
        草场之争, // 相邻游牧政权为牧场打的小仗，只争边城、限期议和(见 SteppeOrderSystem)；追加在末尾，旧值不变
        逼冕 // 西方帝国兴兵逼教宗加冕、授普世之名(见 RealmNamingSystem.Universal)；追加在末尾，旧值不变
    }
}

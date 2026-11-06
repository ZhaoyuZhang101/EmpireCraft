using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.Data;

// 伊斯兰诸分裂(见 IslamicSchismSystem)，存在宗教的 ReligionExtraData.Islamic(旧档为空，头一次用时建)
public sealed class IslamicSchismState
{
    // 本教是怎么分出来的："" 本教(或旧档未记)、"shia" 什叶派、"ismaili" 伊斯玛仪派、"rival" 对立哈里发自成一派
    public string branch = "";
    // 本教分出的什叶派(-1 = 未查；-2 = 分出过但已找不到，见 IslamicSchismSystem.ShiaOf)
    public long shia_id = -1L;
    // 本教(什叶派)已分出过伊斯玛仪派；本教已有对立哈里发自成一派(各一次)
    public bool sub_split;
    public bool rival_branch_split;
    // 本教的对立哈里发(含已终结的，新的在后)
    public List<RivalCaliphateData> rivals = new();
    // 上次有对立哈里发终结的时间(此后 IslamicSchismSystem.RivalCooldownYears 年本教不再另立)
    public double last_rival_end_at = -1d;
    // 历任哈里发(圣地之主)的宗族：其后裔以"先代哈里发之裔"自居，可举什叶之旗(见 IslamicSchismSystem.Alid)
    public List<long> caliph_houses = new();
    // 复古运动(新的在后)与上次兴起的时间
    public List<ReformMovementData> movements = new();
    public double last_movement_at = -1d;
}

// 对立哈里发(科尔多瓦后倭马亚式)：一国之君自称哈里发，与圣地之主分庭抗礼，各哈里发国择一奉之(求册封、交天课)
public sealed class RivalCaliphateData
{
    public long id = -1L;
    // 哈里发座所在的国(其君主即对立哈里发)、所在帝国(-1 = 不是帝国中央)、都城(陷落即亡)
    public long seat_kingdom_id = -1L;
    public long seat_empire_id = -1L;
    public long seat_city_id = -1L;
    // 宣告时的都城名、国号、自立者(记事、窗口用)
    public string seat_name = "";
    public string realm_name = "";
    public long founder_id = -1L;
    public string founder_name = "";
    // 自立者的世系(宗族 id，-1 = 不可考)：易姓即绝
    public long clan_id = -1L;
    public double proclaimed_at = -1d;
    // 奉此哈里发的帝国(id，自立者本国在内)
    public List<long> adherents = new();
    // 历任对立哈里发(同 ReligionHeads 的写法)
    public List<ReligionExtension.ReligionHeadRecord> heads = new();
    // 终结：时间(-1 = 仍在)、缘由(文字键后缀，见 IslamicSchismSystem.End*)、自成一派时分出的宗教
    public double ended_at = -1d;
    public string end = "";
    public long branch_religion_id = -1L;
}

// 复古运动(穆拉比特、穆瓦希德式)：斥本朝宽纵，举教起事；立国之后以圣战为务
public sealed class ReformMovementData
{
    public long id = -1L;
    public string name = "";
    // 起事立的国、起事所在之国(帝国按中央)、起事之城的名字
    public long kingdom_id = -1L;
    public long origin_kingdom_id = -1L;
    public string city_name = "";
    public double founded_at = -1d;
    // 阶段：0 起事、1 圣战之国、2 已平息；成圣战之国的时间、发动过几次圣战
    public int stage;
    public double state_at = -1d;
    public int jihads;
    public double ended_at = -1d;
}

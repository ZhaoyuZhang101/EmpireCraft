using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 骑士团(见 PapacySystem.Orders)，存在宗教的 ReligionExtraData.Orders
public sealed class KnightOrderData
{
    public long id = -1L;
    public string name = "";
    // 驻在国(军驻其国都)、大团长、团库
    public long host_kingdom_id = -1L;
    public long grand_master_id = -1L;
    public long treasury;
    public double founded_at = -1d;
    // 在圣地立的骑士团国(-1 = 没有)
    public long state_kingdom_id = -1L;
    public bool dissolved;
}

// 西方帝国与教廷的关系(见 PapacySystem)，存在 EmpireData.papal
public sealed class PapalRelationState
{
    // 教宗加冕过的君主(换了君主要重新加冕)；自行加冕(脱离罗马、世俗化之后)另记
    public long crowned_emperor_id = -1L;
    public bool self_crowned;
    public double crowned_at = -1d;
    // 叙任权：0 教会叙任 / 1 政教协约 / 2 君主叙任(制度定下的档位，叙任权之争打赢也会改)
    public int investiture;
    // 叙任权之争打赢了(君主叙任)
    public bool investiture_won;
    // 叙任权之争：开始时间(-1 = 没有)
    public double investiture_contest_at = -1d;
    // 绝罚：开始时间(-1 = 没有)、被绝罚的君主、缘由(本地化键)
    public double excommunicated_at = -1d;
    public long excommunicated_ruler_id = -1L;
    public string excommunication_reason = "";
    // 这次绝罚以来倒戈的教皇党成员国数
    public int defections;
    // 宗教改革另立的国教会(-1 = 没有)；圣座帝国已改国号
    public long national_church_id = -1L;
    public bool holy_see_named;
    // 什一税：上次结算时间与上缴额
    public double last_tithe_at = -1d;
    public long tithe_paid;
    // 普世帝国之名(见 RealmNamingSystem.Universal)：所取的天下(主核心 id，-1 = 没有)、取得途径
    // (RealmNamingSystem.Via*)、取得时间、当时的皇帝；绝罚期间暂停(名字退回本名，解除后恢复)
    public long universal_lead_id = -1L;
    public int universal_via;
    public double universal_at = -1d;
    public long universal_emperor_id = -1L;
    public bool universal_suspended;
    // 授名时写下的国号本名(之后被别处改过即作罢)；施压、开战逼冕的时间(教廷记恨二十年)
    public string universal_name = "";
    public double coerced_at = -1d;
    // 逼冕之战打输的时间(十年内不再逼冕)
    public double coerce_failed_at = -1d;
    // 神圣之名(见 PapacySystem.GrantHoly)：授名前国号原有的前缀(方位、"大"等)，去掉神圣之名时还原
    public string holy_previous_prefix = "";
    // 西方大分裂(见 PapacySystem.Antipope)：这次加冕出自对立教宗；这次绝罚出自对立教宗(换了服从、公会议弥合时作废)
    public bool crowned_by_rival;
    public bool banned_by_rival;
    // 宗教改革(见 PapacySystem.Reformation)：上次有改奉新教的成员国起兵的时间(-1 = 没有)
    public double reform_revolt_at = -1d;
}

// 西方大分裂(对立教宗，见 PapacySystem.Antipope)，存在宗教的 ReligionExtraData.PapalSchism：没闹过是 null；
// 闹过的留着(记收场与结束时间，冷却、窗口用)
public sealed class PapalSchismData
{
    // 起因(PapacySystem.SchismByCaptivity / SchismByWeakPope / SchismByLegacy)、开始与结束时间(ended_at = -1 进行中)、
    // 怎么收场(PapacySystem.SchismEnd*)、公会议或分立之地
    public int cause;
    public double started_at = -1d;
    public double ended_at = -1d;
    public int outcome;
    public string outcome_place = "";
    // 对立教廷：所在城(扶立者的都城，原型阿维尼翁)与城名、扶立者(国，帝国按中央)
    public long see_city_id = -1L;
    public string see_city_name = "";
    public long patron_id = -1L;
    // 现任对立教宗(-1 = 出缺)与对立一线历任(旧的在前)
    public long pope_id = -1L;
    public List<RivalPopeRecord> line = new();
    // 奉对立教宗的国(独立王国与帝国中央，成员随中央)
    public List<long> obedience = new();
    // 上次年度结算
    public double updated_at = -1d;
}

// 对立一线的一任教宗：人、在位起止(to = -1 在位)
public sealed class RivalPopeRecord
{
    public long actor_id = -1L;
    public string name = "";
    public double from = -1d;
    public double to = -1d;
}

// 分出的教会从哪来(见 PapacySystem.Reformation、PapacySystem.Antipope)，存在宗教的 ReligionExtraData.SplitOrigin：
// 旧档、东西大分裂、争位分出的是 null
public sealed class SplitOriginData
{
    // PapacySystem.Origin*：1 教士在城中张贴论纲、2 诸侯率国改宗、3 君主另立国教会、4 对立教宗一线分立
    public int kind;
    // 发起人(改革者、诸侯、君主、对立教宗)与发端之城(名字记下，人死城亡后照样显示)
    public long founder_id = -1L;
    public string founder_name = "";
    public long city_id = -1L;
    public string city_name = "";
    public double at = -1d;
}

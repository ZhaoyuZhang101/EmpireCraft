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
}

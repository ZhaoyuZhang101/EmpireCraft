namespace EmpireCraft.Scripts.Data;

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
    // 什一税：上次结算时间与上缴额
    public double last_tithe_at = -1d;
    public long tithe_paid;
}

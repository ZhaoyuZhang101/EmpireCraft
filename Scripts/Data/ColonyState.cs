namespace EmpireCraft.Scripts.Data;

// 殖民地(见 GeneralSystems.ColonySystem)：宗主国在海外的附属政权——总督府或特许公司。
// 记在殖民地这个国家上；宗主国一方只记下回派殖民船的时间(KingdomExtraData.colonial_voyage_at)
public sealed class ColonyState
{
    public const int Governorate = 0, Company = 1;

    public long metropole_id = -1L;
    public int kind = Governorate;
    public double founded_at = -1d;
    // 现任总督与上任时间(总督府任期六年由宗主国改派；公司由董事会选)
    public long governor_id = -1L;
    public double governor_since = -1d;
    // 离心(0~100)：路远、宗主衰弱、立得久就涨，满了闹自治、独立
    public float autonomy;
    // 上回解送岁入、分红的时间；公司历年分红合计
    public double remitted_at = -1d;
    public long dividends;
    // 独立战争开打的时间(打完前不再起)
    public double rising_at = -1d;
}

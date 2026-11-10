namespace EmpireCraft.Scripts.Data;

// 殖民地与属地(见 GeneralSystems.ColonySystem)：宗主国的附属政权。
// 记在殖民地这个国家上；宗主国一方只记下回派殖民船的时间(KingdomExtraData.colonial_voyage_at)
public sealed class ColonyState
{
    // 总督府(直辖殖民地)、特许公司；属地(殖民地的延伸，用户 2026-11-10 "美属、英属这种属国前缀和属国机制要做""应该算殖民地的延申")：
    // 保护国(本地君主照旧、外交归宗主)、自治领(自治、和平独立)、委任统治地(托管、期满独立)、租借地(租期满交还出租国)。追加在末尾
    public const int Governorate = 0, Company = 1, Protectorate = 2, Dominion = 3, Mandate = 4, Lease = 5;

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
    // 地名("简体|英文|繁体")：国号"某属某地"的后半；空的取首府所在法理名
    public string place = "";
    // 租借地：出租的国家与租期届满的时间
    public long lessor_id = -1L;
    public double lease_until = -1d;
}

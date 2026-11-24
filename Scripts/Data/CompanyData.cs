using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 公司大事记的一条
public sealed class CompanyNote
{
    public double at = -1d;
    public string text = "";
}

// 一家公司(见 GeneralSystems.CompanySystem)：经营一片或几片厂区(FactorySite.company_id 指回来)，国籍看总部那座城眼下归谁
public sealed class CompanyData
{
    public int id;
    // 全名(带"股份有限公司""株式会社"这类后缀)与地图上标的简称
    public string name = "";
    public string short_name = "";
    // 主营门类(厂的 id，见 FactorySystem.Types)；起名时所在城的文化
    public string sector = "";
    public string culture = "";
    // 国企(国库出钱办的、收归国有的)；私企为 false
    public bool state;
    public long city_id = -1L;
    public double founded = -1d;
    // 东家的宗族(SpecificClan，-1 = 没有)与成立时记下的姓；所属财阀的宗族(-1 = 独立)
    public long clan_id = -1L;
    public string owner = "";
    public long group_clan_id = -1L;
    // 外资：控股的外国(帝国编号，-1 = 本国资本)与出面收购的那家外国公司
    public long foreign_empire_id = -1L;
    public int foreign_company_id;
    public double foreign_since = -1d;
    // 国家在这家私企里的股份(0~0.9，见 CompanySystem.BuyStake)：三成起外资买不走，五成起是国家控股
    public float state_share;
    // 声誉 0~100；资本(抽象的账，并购、熬过萧条靠它)；上一年的盈利、年产值、厂的档次合计
    public float reputation = 30f;
    public float capital;
    public float profit;
    public float value;
    public int scale;
    // 股市(见 StockMarketSystem)：是否上市、何时上的、股价(每家按一百股算)、近三十年的年末股价
    public bool listed;
    public double listed_at = -1d;
    public float price;
    public List<float> prices = new();
    // 大事记(只留最近八条)
    public List<CompanyNote> notes = new();
}

// 一国的证券交易所：开业时间、股指(开业那年为 100)与近三十年的年末指数
public sealed class StockExchangeData
{
    public long empire_id = -1L;
    public double since = -1d;
    public float index = 100f;
    public float base_cap;
    public List<float> history = new();
}

public sealed class CompanyBook
{
    public int next_id = 1;
    public List<CompanyData> companies = new();
    public List<StockExchangeData> exchanges = new();
}

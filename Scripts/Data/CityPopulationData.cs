using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Data;

// 城市人口数据层(参考维多利亚 3 的 Pop)：把城里的居民按"阶层 × 文化 × 物种 × 理念"分成若干人口组，
// 只记人数，不对应具体单位。这样城市的人口与阶层分布可以脱离实体单位独立存在。
//
// 每个人口组的人数 = 背景人口(没有实体单位的普通人) + 实体单位(上次校准时数到的"名人")。
// 现阶段原版单位照常生成，背景人口恒为 0，数据层就是实体单位的统计镜像；
// 开启无小人模式后，背景人口才会脱离单位自行增减(见 CityPopulationSystem)。
public class PopGroup
{
    public SocialClass social_class = SocialClass.Peasant;
    public string culture = "";
    public string species = "";
    public PartyIdeology ideology = PartyIdeology.Conservatism;
    // 总人数(背景 + 实体)。背景人口按年连续增减，所以用小数累积
    public float size;
    // 上次校准时这一组里数到的实体单位数
    public int named;

    [JsonIgnore] public float Background => size > named ? size - named : 0f;

    public bool SameKind(SocialClass socialClass, string cultureKey, string speciesId, PartyIdeology partyIdeology) =>
        social_class == socialClass && ideology == partyIdeology &&
        string.Equals(culture ?? "", cultureKey ?? "", global::System.StringComparison.Ordinal) &&
        string.Equals(species ?? "", speciesId ?? "", global::System.StringComparison.Ordinal);
}

public class CityPopulationData
{
    public List<PopGroup> groups = new List<PopGroup>();
    // 上次用实体单位校准的世界时间
    public double last_census = -1d;
    // 上次结算背景人口自然增减的世界时间
    public double last_growth = -1d;
    // 存档期间关闭无小人模式时，重新启用不能补算停用期间的增长和消耗。
    public bool abstract_population_suspended;
    // 上次校准时城里的实体单位数
    public int named_units;
    // 军团里的人(无小人模式)：本城征召的每个实体士兵代表一个军团，这里记军团除士兵本人以外的人数，
    // 随士兵的生命值折损(见 CityPopulationSystem.UpdateLegions)，计入城市人口
    public float levied;
    // 背景人口里农民阶层无地的比例(0~1，无小人模式，见 LandEconomySystem.UpdateBackgroundLand)
    public float background_landless;
    // 背景人口的阶层结构是否已经调整过一次(旧存档背景人口几乎全是农民，第一次直接调整到位)
    public bool classes_initialized;
    // 上次结算土地时的背景农民人数(人口减少时空出的地回到幸存者手里)
    public float background_peasants_last = -1f;
    // 背景人口里商人、地主攒下的买地钱(见 LandEconomySystem.UpdateBackgroundLand)
    public float background_land_fund;

    // ---- 人口经济(无小人模式，见 PopulationEconomySystem) ----
    // 上次结算产出与消耗的世界时间
    public double last_economy = -1d;
    // 未满一个整数单位的产出/消耗，留到下次结算累加
    public Dictionary<string, float> output_carry = new Dictionary<string, float>();
    // 产出零头中尚未入库、尚未计税的数量。自给/救济物资不计入；旧档缺省为空，
    // 原 output_carry 已在旧模型计过产值，不能迁移后再次计税。
    public Dictionary<string, float> output_income_carry;
    public float food_need_carry;
    public float construction_carry;
    // 上次结算城市建设(施工)的世界时间
    public double last_construction = -1d;
    // 上一次结算的结果(每年折算)，给界面和日志看
    public float last_jobs;
    public float last_workforce;
    public float last_food_output;
    public float last_food_eaten;
    public float last_food_shortage;
    // 上次结算的皮革消耗与缺口(按户数，见 PopulationEconomySystem.ConsumeGoods)
    public float last_leather_used;
    public float last_leather_shortage;
    public float leather_need_carry;
    // 背景人口生产估值：不是实际销售现金；由旧抽象生产税使用，见 PopulationEconomySystem.Deposit。
    public float produced_value;
    public Dictionary<string, float> produced_sector_values = new();
    public Dictionary<string, double> sector_tax_carry = new();
    public float last_income;
    public float farm_jobs;
    public List<PopulationEconomyPeriod> economy_periods;
    // 土地交易等途径实缴的背景税，也要进入同一统计窗口。
    public float other_background_tax;
    // 民间混合资产(含存货估值，非真实现金，见 PopulationEconomySystem.Savings)：
    // -1 表示还没建账；last_consumption为上次结算的年消费。
    public float private_savings = -1f;
    // 普通实体并入、宗族销户时保管的已存在钱包，不属于混合资产估值或国库。
    // 按城市汇总，不为普通人口逐人创建账户；消费/迁移接入前不能当额外收入发放。
    public WalletReserve civilian_wallet_reserve;
    public bool ShouldSerializecivilian_wallet_reserve() => civilian_wallet_reserve != null &&
        (civilian_wallet_reserve.cash != 0L || civilian_wallet_reserve.loot != 0L);
    // 公家的建材(资源 id → 数量)：城市国库从市场买进的木石金属，公家用时不用再付给百姓
    public Dictionary<string, int> public_stock = new();
    // 开城物资在没有可用仓库时临时保管，计入可用库存；不会凭空增产。
    public Dictionary<string, int> settlement_supplies;
    // 未送达的市场货物退回原所有者；不等于新城启动物资，不生成现金或产出。
    // 仓库暂时无空间时保管，读档及切换人口模式后仍可使用。
    public Dictionary<string, int> resource_returns;
    // 自发聚落及空城安置可以不足一户，不能用保底人口把迁来的少数居民扩成整户。
    public bool spontaneous_settlement;
    // 空城安置每月至多参与一次；出发地与目的地都记录，帝国/封国调度与读档不能重复迁人。
    public double last_resettlement = -1d;
    // 上次结算以来公家用掉百姓的建材付的钱、没付上的钱(国库不够)
    public float public_paid;
    public float public_unpaid;
    public float last_consumption;
    // 背景人口纳税(见 PopulationEconomySystem.PayTaxes)：零头与上次结算的年税收
    public float tax_carry;
    // 市场(见 MarketSystem)：上个月买进的总价值；上个月卖出的总价值(当月累计中)
    public float last_market_bought;
    // 牧群头数与上月畜牧产肉(见 AnimalHusbandrySystem)
    public float herd;
    // 矿藏储量(资源 id → 已采出的量)与枯竭时间(资源 id → 枯竭时的世界时间)，见 MineralResourceSystem
    public Dictionary<string, float> deposit_mined = new();
    public Dictionary<string, double> deposit_depleted_at = new();
    // 本城有没有某种矿藏：第一次判定后固定下来(资源 id → 有无)，不随砍树、挖湖等地形变化而忽有忽无
    public Dictionary<string, bool> deposits_fixed = new();
    public float last_husbandry_meat;
    public float last_market_sold;
    public float market_sold_month;
    public double market_sold_since = -1d;
    // 围困断粮的月数(被围封锁且闹饥荒，见 MarketSystem.CheckSiegeSurrender)
    public int siege_famine_months;
    public float last_tax_income;
}

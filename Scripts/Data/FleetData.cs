namespace EmpireCraft.Scripts.Data;

// 舰队(借鉴钢铁雄心的海军)：战舰、运输船都只是数字，地图上最多一艘旗舰实体(随「军团统领实体」开关)。
// 存在所属王国的 KingdomExtraData.fleets 里，随存档保存；结算见 NavySystem
public sealed class FleetData
{
    public long id = -1L;
    // 本国第几舰队(番号)
    public int serial;
    public long kingdom_id = -1L;
    // 母港(造舰、休整、旗舰挂靠的港口城)
    public long home_city_id = -1L;
    public int warships;
    public int transports;
    // 组织度 0~100：海战下降、在港休整恢复
    public float org = 100f;
    // 任务：port 驻港 / patrol 巡逻(争制海) / escort 护航(运兵航线) / blockade 封锁敌港 / retreat 回港休整
    public string task = "port";
    // 所在海区与要去的海区(见 SeaRegionSystem)；zone_id 是画兵牌的位置(海上的地块)
    public int region_id = -1;
    public int target_region_id = -1;
    public int zone_id = -1;
    // 造舰进度(满 1 下水一艘)
    public float build_progress;
    // 旗舰实体(开关打开时才有)
    public long flagship_id = -1L;
    // 水师余威(见 NavySystem.ReachRadius)：最近一次有战舰时的战力与时间；战舰一时打光，十年内辐射照这个算
    public float reach_power;
    public double reach_power_at = -1d;
}

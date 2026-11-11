using System.Collections.Generic;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Data;

// 建筑台账里的一笔：某种建筑(原版建筑资产 id)几座；zone 是它所在的地块(手动城区的城才记，自动的城为 -1)
public class WorkLot
{
    public string id;
    public int zone = -1;
    public int n = 1;
}

// 一座城的建筑台账(见 CityLedger)：建成的建筑只记"哪种、几座、(手动城区)在哪块地"，不对应原版的建筑实体；
// 在建的至多一座(同原版一城一处工地)：资产 id、落在哪块地、已投的施工点
public class CityWorks
{
    public List<WorkLot> lots = new List<WorkLot>();
    public string building;
    public int building_zone = -1;
    public int progress;

    public bool ShouldSerializebuilding() => building != null;
    public bool ShouldSerializebuilding_zone() => building != null && building_zone >= 0;
    public bool ShouldSerializeprogress() => building != null && progress != 0;

    // 汇总(按类型、按资产的座数，住房位、许盖的民居数)：改动后置空，用时重算(见 CityLedger.SummaryOf)
    [JsonIgnore] public WorksSummary summary;
}

public class WorksSummary
{
    public readonly Dictionary<string, int> by_type = new Dictionary<string, int>();
    public readonly Dictionary<string, int> by_id = new Dictionary<string, int>();
    public int total, housing, max_houses;
}

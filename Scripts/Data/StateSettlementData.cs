using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 国家建城项目只保存 ID 和结算进度，不能保存 Unity 对象或后台搜索队列。
public class StateSettlementData
{
    public double last_checked = -1d;
    public double last_completed = -1d;
    public long last_city = -1L;
    public string status = "idle";
    public int reserved_project_gold;
    public StateSettlementProject project;
    public CityResettlementProject resettlement;
}

// 安置已有城市，不经过原版建城入口，也不改变该城市的政权归属。
public class CityResettlementProject
{
    public long target = -1L;
    public long target_owner = -1L;
    public long source = -1L;
    public long source_owner = -1L;
    public StateSettlementProject journey;
    public bool arrived;
    public float people;
    public List<SettlementCargo> cargo = new();
}

public class StateSettlementProject
{
    public bool spontaneous;
    public string cause;
    public long owner = -1L;
    public long source = -1L;
    public int zone = -1;
    public double started = -1d;
    public bool overseas;
    public long embarkation_city = -1L;
    public int travel_months;
    public SettlementRoutePoint landfall;
    public List<SettlementRoutePoint> sea_route = new();
    public long city = -1L;
    public bool committing;
    public bool paid;
    public bool startup_paid;
    public bool initialized;
    public bool native_initialized;
    public bool politics_done;
    public bool create_fief;
    public long empire = -1L;
    public long fief = -1L;
    public int fief_regime = -1;
    public int gold;
    public int startup_gold;
    public float people;
    public int granary_food;
    public string granary_resource;
    public bool granary_taken;
    public List<SettlementMigration> migrants = new();
    public List<SettlementCargo> cargo = new();
}

public class SettlementRoutePoint
{
    public int x;
    public int y;
}

public class SettlementMigration
{
    public long source;
    public float people;
    public bool done;
}

public class SettlementCargo
{
    public long source;
    public string resource;
    public int amount;
    public float price;
    public bool taken;
    public int in_transit;
    public int public_in_transit;
    public bool delivered;
}

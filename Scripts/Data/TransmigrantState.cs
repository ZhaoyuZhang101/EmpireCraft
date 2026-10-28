using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 穿越政体的国家记录(见 GeneralSystems.TransmigrantSystem)：降临、元老院席位、执委会主席任期、阶段、出版指导小组刊行过的书
public sealed class TransmigrantState
{
    public double landed_at = -1d;
    public int landed_year;
    // 落脚的城；国号前缀(地图上的临高)；临高是当地原有的城或村镇时为真(落脚的营地另叫百仞)
    public long base_city_id = -1L;
    public string lingao_name = "";
    public bool lingao_native;
    // 元老院五百二十一席(席位固定，元老故去由子女一人继承，见 TransmigrantSystem.Succeed)
    public List<SenatorSeat> seats = new();
    // 执委会主席：席位号、当选时间、连任到第几届、上一位连任两届卸任的主席(隔一届才能再选)
    public int chairman_seat = -1;
    public double chairman_elected_at = -1d;
    public int chairman_terms;
    public int barred_seat = -1;
    // 已宣布的阶段：1 降临·临时执委会，2 第一届执委会，3 共同纲领，4 大宋澳洲行在，5 元老院内阁，6 北伐功成(国号大宋)
    public int stage = 1;
    // 出版指导小组：下回刊行的时间、刊行过的技术
    public double next_publish_at = -1d;
    public List<string> released_techs = new();
    // 扩张：下回可以用兵的时间、开拓的新城数(见 TransmigrantSystem.Growth)
    public double next_campaign_at = -1d;
    public int towns_founded;
    // 伪装：名义上的宗主(帝国编号，-1 没有)、是否已撕破脸(见 TransmigrantSystem.Reveal)
    public long disguise_lord_id = -1L;
    public bool revealed;
}

public sealed class SenatorSeat
{
    public int seat;
    public string surname = "";
    public string given = "";
    public bool male = true;
    // 按游戏年记的出生年
    public int birth_year;
    // 1 是原班穿越者，往下是继承席位的子孙
    public int generation = 1;
    // 落成了实体的元老(主席、驻县办主任、名臣名将)；-1 只在名册上
    public long actor_id = -1L;
    // 原著名册的行号(Scripts/Regimes/Configs/Transmigrant/Senators.csv)；-1 是自动起名的元老或继承人
    public int roster = -1;
}

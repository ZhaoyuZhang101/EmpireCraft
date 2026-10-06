using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Layer;

public class EmpireCore
{
    public long id { get; set; }
    public long empire_id { get; set; } = -1L;
    public long false_core_against_empire_id { get; set; } = -1L;
    public long culture { get; set; }
    // Stable EmpireCraft culture key inherited by de jure kingdom titles.
    public string default_culture { get; set; } = "";
    public bool culture_locked { get; set; }
    public string name { get; set; }
    public double create_timestamp { get; set; }
    public long CoreCapital { get; set; }
    public List<(double time, long titleId)> titlesRecord;
    public List<long> empire_history_ids = new List<long>();
    // —— 军阀时期(见 WarlordEraSystem) ——
    // 第一次被现代政体统一(控制 80% 以上城市)的时间；-1 = 从未被现代政体统一过
    public double modern_unified_at { get; set; } = -1d;
    // 统一后再次分裂的起点；-1 = 当前处于统一状态
    public double fragmented_since { get; set; } = -1d;
    // 分治已成定局(分裂满设定年数)：各国恢复正常国号
    public bool split_recognized { get; set; }
    // 帝国核心范围内的中央政府；与之对立、由不同理念军阀联合组建的另一个中央政府
    public long central_empire_id { get; set; } = -1L;
    public long rival_central_empire_id { get; set; } = -1L;
    // 另立中央组建政府时会临时生成自己的帝国核心；该字段指向它正在争夺的原核心。
    // 临时核心不单独参与军阀时期扫描，胜利后销毁并继承原核心，承认分治后则解除该关系。
    public long warlord_parent_core_id { get; set; } = -1L;
    // 旧存档兼容字段。联合政府现由同一 Empire 的成员国表示，不再依赖原版 Alliance。
    public long rival_alliance_id { get; set; } = -1L;
    // 民族统一战线(见 NationalSentimentSystem)：开始时间(-1 = 没有)、最近一次仍有外敌的时间
    public double united_front_since { get; set; } = -1d;
    public double united_front_last_threat { get; set; } = -1d;

    public bool SetCoreCapital(City city)
    {
        if (city.isRekt())  return false;
        CoreCapital = city.id;
        return true;
    }
    public City GetCoreCapital()
    {
        return World.world.cities.get(CoreCapital);
    }
    public bool AddTitle(KingdomTitle title)
    {
        if (title == null || title.isRekt()) return false;
        titlesRecord ??= new List<(double time, long titleId)>();
        if (titlesRecord.Any(a => a.titleId == title.id)) return false;
        titlesRecord.Add((World.world.getCurWorldTime(), title.id));
        return true;
    }
    public bool RemoveTitle(KingdomTitle title)
    {
        if (title == null) return false;
        titlesRecord ??= new List<(double time, long titleId)>();
        if (titlesRecord.All(a => a.titleId != title.id)) return false;
        titlesRecord.RemoveAll(c => c.titleId == title.id);
        return true;
    }
}

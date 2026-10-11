using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 周制的天下大势(见 ZhouEraSystem)：西周 → 春秋 → 战国 → 统一
public sealed class ZhouEraState
{
    // 0 西周 / 1 春秋 / 2 战国 / 3 统一
    public int era;
    public double era_since = -1d;
    // 天子王畿所在(东迁判断用)
    public long royal_capital_id = -1L;
    // 霸主(春秋)：诸侯纳贡、霸主调停诸侯战争、率诸侯攘夷勤王
    public long hegemon_id = -1L;
    public double hegemon_since = -1d;
    public int conferences;
    // 称王的诸侯(春秋僭越、战国相王)、推行变法的诸侯
    public List<long> kings = new();
    public List<long> reforms = new();
    // 合纵所抗之国与合纵各国
    public long coalition_target = -1L;
    public List<long> coalition = new();
    // 春秋开始时的诸侯数、已灭之国数、是否发生过权臣瓜分大国
    public int vassals_at_spring;
    public int annexed;
    public bool partitioned;
    public double last_year = -1d;
    public double last_month = -1d;
    public List<CourtLogEntry> log = new();
}

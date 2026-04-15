using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

public enum LawCategory
{
    王权与国家安全,
    官制与行政,
    刑法,
    财产与经济,
    土地与农业,
    军事,
    婚姻与家族,
    宗教与礼制,
    商贸与契约,
    城市与公共秩序
}
public enum LawType
{
    谋反 = 0,
    叛国 = 1,
    篡位 = 2,
    私通敌国 = 3,
    伪造诏令 = 4,
    煽动叛乱 = 5,

    贪污 = 6,
    受贿 = 7,
    买官 = 8,
    卖官 = 9,
    滥用职权 = 10,
    玩忽职守 = 11,
    冒充官员 = 12,

    杀人 = 13,
    故意伤害 = 14,
    强奸 = 15,
    绑架 = 16,
    放火 = 17,
    投毒 = 18,
    陷害 = 19,
    抢劫 = 20,
    盗窃 = 21,

    诈骗 = 22,
    伪造货币 = 23,
    偷税漏税 = 24,
    走私 = 25,
    哄抬物价 = 26,
    非法侵占土地 = 27,

    破坏农田 = 28,
    破坏水利 = 29,
    隐瞒田亩 = 30,
    逃避徭役 = 31,

    临阵脱逃 = 32,
    违抗军令 = 33,
    私卖军械 = 34,
    谎报军功 = 35,
    抢掠平民 = 36,

    重婚 = 37,
    遗弃家庭 = 38,
    虐待亲属 = 39,
    非法继承 = 40,
    伪造血统 = 41,

    亵渎神庙 = 42,
    破坏祭祀 = 43,
    冒充神职 = 44,
    宣扬异端 = 45,

    违约 = 46,
    伪造契约 = 47,
    欺诈交易 = 48,
    缺斤少两 = 49,

    非法持械 = 50,
    聚众斗殴 = 51,
    违反宵禁 = 52,
    扰乱集市 = 53,
    污染水源 = 54,
    散布恐慌谣言 = 55
}

public enum PunishmentLevel
{
    无罪,
    罚金,
    笞刑,
    杖刑,
    监禁,
    流放,
    没收财产,
    剥夺爵位,
    剥夺官职,
    死刑,
    夷三族
}

public class EmpireLawConfig
{
    public List<Law> Laws { get; set; } = new();
}
public class Law
{
    public LawType Type { get; set; }
    public LawCategory Category { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool IsForbidden { get; set; } = false;
    public List<PunishmentLevel> Punishments { get; set; }
    public string Note { get; set; }
}

public static class EmpireLawSystem
{
    public static Dictionary<LawType, Law> Laws { get; set; } = new();

    public static void init()
    {
        LoadFromJson(Path.Combine(ModClass._declare.FolderPath, "Scripts", "GeneralSystems", "EmpireLaw", "EmpireLawConfig.json"));
    }
    
    public static void LoadFromJson(string path)
    {
        string json = File.ReadAllText(path);
        var config = JsonConvert.DeserializeObject<EmpireLawConfig>(json);
        Laws = new Dictionary<LawType, Law>();
        config.Laws.ForEach(l => Laws.Add(l.Type, l));
    }

    public static Law GetConfig(this LawType type)
    {
        var success = Laws.TryGetValue(type, out var l);
        if (!success) return Laws.FirstOrDefault().Value;
        return l;
    }
}
using System.Collections.Generic;

namespace EmpireCraft.Scripts.Data;

// 草原(见 NomadRaidSystem)：每国一份，存在 KingdomExtraData.steppe 里，随存档保存；旧档没有这项，第一次结算时补出来
public sealed class SteppeState
{
    // 上次结算的草原压力与四项构成(纪元、牲畜粮食、国库、互市岁币)、结算时间；-1 = 还没算过
    public float pressure;
    public float era, herd, treasury, market;
    public double pressure_at = -1d;
    // 头一次被看到的时间(互市断绝从这时起算)
    public double watch_since = -1d;
    // 盟主威望：率众劫掠得胜几回(打输清零)；记的是哪位首领，换了人从零算
    public int prestige;
    public long prestige_ruler_id = -1L;
    // 上次出兵劫掠(发起或应召)的时间、正在打的劫掠战；-1 = 没有
    public double last_raid_at = -1d;
    public long raid_war_id = -1L;
    // 岁币：上次看到的出钱帝国；岁币停了(到期、付不起)记下原来出钱的国(核心)与时间
    public long tribute_payer_id = -1L;
    public long tribute_cut_from = -1L;
    public double tribute_cut_at = -1d;
    // 互市：上次得市易之利的时间、对方(见 FrontierRelations)
    public double market_at = -1d;
    public long market_partner_id = -1L;
    // 买和开的互市：与哪国(定居一方的核心)，到什么时候；期间劫掠过也照样通市
    public long market_treaty_with = -1L;
    public double market_treaty_until = -1d;
    // 劫掠收场后待办：成雄主转汗国(下个月办，不在战争收尾那一帧里改帝国)
    public bool hero_pending;
}

// 劫掠之战(见 NomadRaidSystem)：存在 WarExtraData.raid 里
public sealed class RaidWarState
{
    public long leader_id = -1L;
    public string leader_name = "";
    public string target_name = "";
    // 出兵各部与出兵多少(开战时的兵力，战利品照此分)
    public List<long> members = new();
    public Dictionary<long, float> troops = new();
    // 要洗劫几城才算抢够；已洗劫几城(屯田的边城算半座)、洗劫过的城
    public float goal = 1f;
    public float sacked;
    public List<long> sacked_cities = new();
    // 守方长城关隘之固(0~1)：抢得少、折损大
    public float defense;
    // 战利品合计(金帛、粮、人口)
    public int loot_money;
    public int loot_food;
    public int loot_people;
    // 买和：0 没买；1 金帛；2 互市；3 和亲岁币
    public int bought_off;
    public int ransom;
    public bool settled;
}

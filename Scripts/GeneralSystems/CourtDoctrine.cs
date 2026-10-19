using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using T = EmpireCraft.Scripts.Regimes.TemporaryFactions.TemporaryFactionType;

namespace EmpireCraft.Scripts.GeneralSystems;

// 朝堂的利与义(用户 2026-10-17："阶级党和这些学说党之间怎么去博弈"，"这些阶级党派都是永远存在并且他们才是真正决定帝国兴衰的")：
//   · 利：阶级派系(天子派、诸侯派、后党、阉党)按议题对自己这一身份的得失表态——削藩宗室得利、贵族受损；
//   · 义：学说朋党(清流、变法派、宽政派……)按自己奉行的学说表态——法家主削藩、黄老主轻徭薄赋、儒家恶宦官；
// 两条线交叉，同盟与对头自然生成：变法派与天子派同主削藩，清流与阉党义利俱冲，后党与天子派争亲政。
// 分值约 -10 ~ 10；在 CourtIssues.Stance 里放大后与各阶层亲疏一起算立场。
public static class CourtDoctrine
{
    private static readonly Dictionary<PartyIdeology, Dictionary<T, int>> Interests = new()
    {
        // 寒门近臣与宦官(天子派)：皇权集中、寒门出路；宗室另有宗室派。天下归一即皇权所及，谋求统一与削藩同重
        // (以前 4，排在天子派议题末尾，天下分裂也难得上奏)
        [PartyIdeology.ImperialPower] = new()
        {
            [T.削藩] = 10, [T.夺取诸侯开战权] = 8, [T.允许诸侯自由开战] = -10, [T.确认诸侯特权] = -8, [T.设置行政区] = 6,
            [T.分封] = -4, [T.供养宗室] = -2, [T.开科取士] = 8, [T.整顿吏治] = 5, [T.国_打击贪腐] = 4, [T.修氏族志] = 6,
            [T.抑豪强] = 8, [T.转世袭] = -6, [T.太后临朝] = -8, [T.封赏后族] = -6, [T.宦官掌兵] = -4, [T.设立内阁] = 5,
            [T.推行三省六部] = 3, [T.土地改革] = 3, [T.提高赋税] = 3, [T.谋求统一] = 8,
            [T.诛宦官] = -3, [T.党锢] = 3, [T.兴修水利] = 3, [T.盐铁专营] = 6, [T.屯田实边] = 4, [T.封禅] = 8, [T.裁省用度] = -6,
            [T.大赦天下] = 2, [T.重定律令] = 5
        },
        // 贵族与世家：封地、门第、田产、世袭
        [PartyIdeology.GreatClans] = new()
        {
            [T.削藩] = -10, [T.夺取诸侯开战权] = -8, [T.允许诸侯自由开战] = 8, [T.确认诸侯特权] = 10, [T.设置行政区] = -6,
            [T.分封] = 4, [T.供养宗室] = -3, [T.开科取士] = -8, [T.整顿吏治] = -3, [T.修氏族志] = -6, [T.抑豪强] = -10,
            [T.土地改革] = -10, [T.转世袭] = 8, [T.转周制] = 6, [T.降低赋税] = 4, [T.提高赋税] = -5, [T.宦官掌兵] = -6,
            [T.设立内阁] = -3, [T.诛宦官] = 5, [T.党锢] = -4, [T.兴修水利] = 2, [T.盐铁专营] = -6, [T.屯田实边] = -2, [T.封禅] = -2,
            [T.裁省用度] = 2, [T.重定律令] = -5, [T.重农抑商] = 3
        },
        // 宗室：宗亲的供养、封国与世袭
        [PartyIdeology.RoyalKin] = new()
        {
            [T.供养宗室] = 10, [T.分封] = 10, [T.削藩] = -8, [T.转世袭] = 4, [T.册立储君] = 3, [T.太后临朝] = -4,
            [T.封赏后族] = -4, [T.裁省用度] = -8, [T.宦官掌兵] = -4, [T.诛宦官] = 2, [T.确认诸侯特权] = 3, [T.封禅] = 3
        },
        // 外戚：太后与后族的权位
        [PartyIdeology.ConsortKin] = new()
        {
            [T.太后临朝] = 10, [T.封赏后族] = 10, [T.册立储君] = 4, [T.宦官掌兵] = -8, [T.整顿吏治] = -4, [T.国_打击贪腐] = -5,
            [T.设立内阁] = -3, [T.诛宦官] = 6, [T.裁省用度] = -4, [T.大赦天下] = 3, [T.封禅] = 2
        },
        // 宦官：近侍掌兵、免于查办
        [PartyIdeology.Eunuchs] = new()
        {
            [T.宦官掌兵] = 10, [T.整顿吏治] = -6, [T.国_打击贪腐] = -6, [T.太后临朝] = -6, [T.封赏后族] = -4, [T.开科取士] = -3,
            [T.提高赋税] = 3, [T.设立内阁] = -4, [T.诛宦官] = -10, [T.党锢] = 10, [T.裁省用度] = -5, [T.盐铁专营] = 3, [T.封禅] = 3
        },
        // 军功(将领与武职)：靠战功进身——主用兵、开边、一统，耻于岁币割地；恶宦官掌兵，嫌文臣专权、科举抑武
        [PartyIdeology.MilitaryMerit] = new()
        {
            [T.对外扩张] = 10, [T.谋求统一] = 10, [T.迫使朝贡] = 8, [T.屯田实边] = 8, [T.提供岁币] = -10, [T.割让城池] = -10,
            [T.撤销军府] = -8, [T.转军府] = 4, [T.宦官掌兵] = -8, [T.诛宦官] = 3, [T.提高赋税] = 2, [T.裁省用度] = -4,
            [T.开科取士] = -3, [T.设立内阁] = -3, [T.盐铁专营] = 2, [T.大赦天下] = -2, [T.封禅] = 2
        }
    };

    private static readonly Dictionary<PartyIdeology, Dictionary<T, int>> Doctrines = new()
    {
        // 儒家(清流)：仁政、礼法、用贤，恶阉宦外戚
        [PartyIdeology.Confucianism] = new()
        {
            [T.降低赋税] = 6, [T.提高赋税] = -6, [T.赈济灾荒] = 8, [T.开科取士] = 8, [T.整顿吏治] = 6, [T.宦官掌兵] = -10,
            [T.太后临朝] = -6, [T.封赏后族] = -5, [T.对外扩张] = -4, [T.册立储君] = 4, [T.加强神权] = -4, [T.分封] = 3,
            [T.诛宦官] = 10, [T.党锢] = -10, [T.兴修水利] = 4, [T.盐铁专营] = -6, [T.封禅] = 3, [T.裁省用度] = 4, [T.大赦天下] = 4,
            [T.重定律令] = -3, [T.重农抑商] = 4
        },
        // 法家(变法派)：集权、法治、富国强兵，并兼天下
        [PartyIdeology.Legalism] = new()
        {
            [T.削藩] = 8, [T.设置行政区] = 8, [T.夺取诸侯开战权] = 8, [T.提高赋税] = 6, [T.对外扩张] = 6, [T.整顿吏治] = 8,
            [T.国_打击贪腐] = 8, [T.抑豪强] = 8, [T.开科取士] = 4, [T.确认诸侯特权] = -8, [T.分封] = -8, [T.降低赋税] = -5,
            [T.提供岁币] = -6, [T.转周制] = -10, [T.转世袭] = -8, [T.诛宦官] = 4, [T.党锢] = 2, [T.兴修水利] = 4, [T.盐铁专营] = 10,
            [T.屯田实边] = 6, [T.封禅] = -2, [T.大赦天下] = -8, [T.重定律令] = 10, [T.重农抑商] = 6, [T.谋求统一] = 8
        },
        // 黄老(宽政派)：清静无为、与民休息
        [PartyIdeology.HuangLao] = new()
        {
            [T.降低赋税] = 10, [T.提高赋税] = -10, [T.赈济灾荒] = 4, [T.对外扩张] = -8, [T.提供岁币] = 4, [T.削藩] = -3,
            [T.抑豪强] = -3, [T.土地改革] = -2, [T.盐铁专营] = -8, [T.屯田实边] = -2, [T.封禅] = -4, [T.裁省用度] = 4,
            [T.大赦天下] = 6, [T.重定律令] = -4, [T.兴修水利] = -2
        },
        // 墨家(节用派)：节用、非攻、兼爱
        [PartyIdeology.Mohism] = new()
        {
            [T.供养宗室] = -8, [T.对外扩张] = -10, [T.赈济灾荒] = 8, [T.降低赋税] = 4, [T.封赏后族] = -6, [T.宦官掌兵] = -4,
            [T.提供岁币] = 2, [T.兴修水利] = 6, [T.封禅] = -8, [T.裁省用度] = 10, [T.诛宦官] = 4, [T.党锢] = -4
        },
        // 农家(重农派)：均田、劝农
        [PartyIdeology.Agrarianism] = new()
        {
            [T.土地改革] = 10, [T.赈济灾荒] = 8, [T.降低赋税] = 6, [T.抑豪强] = 6, [T.提高赋税] = -6, [T.对外扩张] = -4,
            [T.确认诸侯特权] = -4, [T.兴修水利] = 10, [T.屯田实边] = 4, [T.重农抑商] = 10, [T.盐铁专营] = -4
        },
        // 兵家(将门)：用兵、不输岁币、不割地
        [PartyIdeology.Strategists] = new()
        {
            [T.对外扩张] = 10, [T.迫使朝贡] = 8, [T.谋求统一] = 8, [T.提供岁币] = -10, [T.割让城池] = -10, [T.降低赋税] = -3,
            [T.宦官掌兵] = -6, [T.屯田实边] = 10, [T.封禅] = 2, [T.盐铁专营] = 3
        },
        // 纵横家(邦交派)：以外交代兵
        [PartyIdeology.Diplomatists] = new()
        {
            [T.提供岁币] = 6, [T.迫使朝贡] = 6, [T.谋求统一] = 3, [T.割让城池] = 2, [T.对外扩张] = -2, [T.重农抑商] = -8,
            [T.盐铁专营] = -4, [T.大赦天下] = 2
        },
        // 阴阳家(方士)：天命、祭祀
        [PartyIdeology.YinYang] = new()
        {
            [T.加强神权] = 10, [T.确立国教] = 6, [T.册立储君] = 3, [T.整顿吏治] = -2, [T.封禅] = 10, [T.大赦天下] = 4
        },
        // 泛灵论(巫祝)
        [PartyIdeology.Animism] = new()
        {
            [T.加强神权] = 8, [T.确立国教] = 6, [T.开科取士] = -4, [T.封禅] = 6
        }
    };

    // 朝堂上有利义之分的议题(算派系之间相近与否)
    private static readonly T[] KeyIssues = Interests.Values.Concat(Doctrines.Values).SelectMany(table => table.Keys).Distinct().ToArray();

    // 阶级派系算利、学说朋党算义；其余派系(没有朋党的文化、近代政党)为 null
    public static int? Weigh(FixedFaction faction, T issue)
    {
        if (faction == null || !faction.IsClique) return null;
        var tables = IdeologyFamilies.IsIdentityStance(faction.Ideology) ? Interests : Doctrines;
        if (!tables.TryGetValue(faction.Ideology, out Dictionary<T, int> table)) return null;
        return table.TryGetValue(issue, out int value) ? value : 0;
    }

    // 两派在利义议题上的一致程度(-1 ~ 1)；不是朋党就返回 null
    public static float? Agreement(FixedFaction a, FixedFaction b)
    {
        if (Weigh(a, T.削藩) == null || Weigh(b, T.削藩) == null) return null;
        float dot = 0f, na = 0f, nb = 0f;
        foreach (T issue in KeyIssues)
        {
            float x = Weigh(a, issue) ?? 0, y = Weigh(b, issue) ?? 0;
            dot += x * y;
            na += x * x;
            nb += y * y;
        }
        return na <= 0f || nb <= 0f ? 0f : dot / (float)Math.Sqrt(na * nb);
    }
}

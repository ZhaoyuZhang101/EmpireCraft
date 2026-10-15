using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 藩王一系(用户 2026-10-16："为什么这么多旁支入继却没有一个是藩王或者藩王后代")：
// 皇统绝嗣、旁支入继时先从藩王(受封为王的宗室)和藩王的子孙里找，按亲疏；宗室里一个藩王一系都没有才找别的族人
// (在朝做官没封王的那几支)。以前只按亲疏，常立到太尉、太仆一类在朝宗室的子侄，受封各地的诸王反倒轮不上。
// 王号记在族谱上(PersonalClanIdentity.prince_title)：受封时记下，人没了、转为虚拟族人后也留着；
// 旧档读档时按个人经历里的"获授某王"补上。世系图上这样入继的写"某王入继""某王之子入继"
public static class PrinceLineage
{
    // 藩王的子孙往下认几代
    private const int Generations = 3;

    private static string KingSuffix => LM.Get("default_peerages_2") ?? "王";

    // 受封(LogPeerageGranted)：封的是王就记下王号
    public static void NoteGrant(Actor actor, string titleName)
    {
        PersonalClanIdentity person = actor?.GetPersonalIdentity();
        if (person == null || string.IsNullOrWhiteSpace(titleName)) return;
        if (titleName.EndsWith(KingSuffix) || actor.GetPeeragesLevel() == PeeragesLevel.peerages_2) person.prince_title = titleName;
    }

    // 是不是藩王：族谱记着王号，或者眼下是王爵(受封为王的宗室、帝国里的封国国君)
    public static bool IsPrince(PersonalClanIdentity person)
    {
        if (person == null) return false;
        if (!string.IsNullOrWhiteSpace(person.prince_title)) return true;
        if (!string.IsNullOrEmpty(person.PeeragesLevel) && person.PeeragesLevel.EndsWith("peerages_2")) return true;
        Actor actor = person.is_alive && !person.is_virtual ? person._actor : null;
        return actor?.data != null && !actor.isRekt() && actor.GetPeeragesLevel() == PeeragesLevel.peerages_2;
    }

    // 本人是藩王，或父祖(往上几代)里有藩王
    public static bool InLine(PersonalClanIdentity person)
    {
        PersonalClanIdentity current = person;
        for (int up = 0; current != null && up <= Generations; up++)
        {
            if (IsPrince(current)) return true;
            current = current.father > 0 ? SpecificClanManager.getPerson(current.father) : null;
        }
        return false;
    }

    // 宗室入继的人选：有藩王一系的只留藩王一系
    public static List<T> PreferPrinces<T>(List<T> pool, global::System.Func<T, PersonalClanIdentity> person)
    {
        if (pool == null || pool.Count == 0) return pool;
        List<T> princes = pool.Where(item => InLine(person(item))).ToList();
        return princes.Count > 0 ? princes : pool;
    }

    // 皇位空缺、先君没有子孙又未立储：藩王本就是正统、向来是皇位人选(用户 2026-10-16："藩王本来就是正统，
    // 向来都可以是皇位候选人，何须入继"，"明朝皇帝不也没入继")——有藩王一系的，最近的一位直接即位，
    // 不过继、不交群臣议立。按亲疏，同样亲疏的先成年、再年长
    public static (Actor actor, string relation) DirectPrince(Kingdom core, PersonalClanIdentity predecessor)
    {
        if (core == null || predecessor == null) return (null, "");
        var pool = EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckHeir.CollateralPool(core, predecessor);
        foreach ((PersonalClanIdentity person, int kinship) in pool.Where(item => InLine(item.person))
                     .OrderBy(item => item.kinship).ThenByDescending(item => item.person.age >= 18)
                     .ThenByDescending(item => item.person.age).ThenBy(item => item.person.id))
        {
            Actor actor = person.is_virtual ? person.Realize() : person._actor;
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() ||
                EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckHeir.BarredBySex(core, actor)) continue;
            return (actor, Describe(predecessor, person));
        }
        return (null, "");
    }

    // 世系图、即位记事：对先君的亲属称谓，受封过王的再写上王号(如"叔·湘王""第三子·南越王")
    public static string Describe(PersonalClanIdentity predecessor, PersonalClanIdentity person)
    {
        string kin = LineageKinship.Term(predecessor, person);
        string title = person?.prince_title;
        if (string.IsNullOrWhiteSpace(title)) return kin;
        return string.IsNullOrEmpty(kin) ? title : kin + "·" + title;
    }

    // 读档：旧档没记王号的，按个人经历里最近一次"获授某王"补上
    public static int Backfill(IEnumerable<PersonalClanIdentity> people)
    {
        string template = LM.Get("personal_history_peerage_granted");
        if (string.IsNullOrEmpty(template) || !template.Contains("{1}")) return 0;
        // 模板"受{0}册封，获授{1}。"：取"获授"与结尾之间的部分
        int at = template.IndexOf("{1}");
        string before = template.Substring(template.IndexOf("{0}") + 3, at - template.IndexOf("{0}") - 3), after = template.Substring(at + 3);
        int filled = 0;
        foreach (PersonalClanIdentity person in people)
        {
            if (person == null || !string.IsNullOrWhiteSpace(person.prince_title) || person.personal_history == null) continue;
            for (int i = person.personal_history.Count - 1; i >= 0; i--)
            {
                string content = person.personal_history[i]?.content;
                if (string.IsNullOrEmpty(content)) continue;
                int start = content.IndexOf(before);
                if (start < 0 || !content.EndsWith(after)) continue;
                string title = content.Substring(start + before.Length, content.Length - after.Length - start - before.Length);
                if (!title.EndsWith(KingSuffix)) continue;
                person.prince_title = title;
                filled++;
                break;
            }
        }
        return filled;
    }
}

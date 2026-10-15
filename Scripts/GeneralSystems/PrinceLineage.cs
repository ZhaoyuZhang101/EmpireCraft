using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
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

    // 皇子不论当不当官都封王(用户 2026-10-16)：华夏一系帝国每年一轮，在位皇帝的儿子(储君除外)还没有王号的封王。
    // 以前王爵只封给地图上有实体的人，而且要有空着的法理头衔——无小人、纯净地图下皇子多是虚拟族人，法理头衔又有限，
    // 许多皇子到死也没封王，绝嗣时藩王一系就轮不到他们。王号取本国还没有在世藩王用的法理头衔名，没有就取本国城名；
    // 有空着的法理王爵、本人又在地图上的，以后照原来的法理分封改授实封(见 Empire.ProcessLegalPeerageSuccession)
    public static void GrantPrinces(Empire empire)
    {
        Actor emperor = empire?.Emperor;
        Kingdom core = empire?.CoreKingdom;
        PersonalClanIdentity father = emperor?.GetPersonalIdentity();
        if (father == null || core == null || EnfeoffmentHelper.RegimeForbids(empire) ||
            !EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckHeir.MaleOnlyThrone(core)) return;
        long heir = core.HasHeir() ? core.GetHeir()?.GetPersonalIdentity()?.id ?? -1L : -1L;
        List<PersonalClanIdentity> sons = SpecificClanManager.getChildren(father).Select(pair => pair.Item2)
            .Where(son => son != null && son.is_alive && son.is_main && son.sex == ActorSex.Male && son.id != heir && !IsPrince(son) &&
                          son._actor?.isKing() != true)
            .OrderBy(son => son.id).ToList();
        if (sons.Count == 0) return;
        HashSet<string> used = UsedTitles(empire);
        foreach (PersonalClanIdentity son in sons)
        {
            string place = NextPlace(empire, used);
            if (place == null) return;
            used.Add(place);
            Grant(empire, son, place + KingSuffix);
        }
    }

    private static void Grant(Empire empire, PersonalClanIdentity son, string title)
    {
        son.prince_title = title;
        Actor actor = son.is_alive && !son.is_virtual ? son._actor : null;
        if (actor?.data != null && !actor.isRekt())
        {
            actor.SetPeeragesLevel(PeeragesLevel.peerages_2);
            TranslateHelper.LogPeerageGranted(actor, empire, title);
            return;
        }
        // 虚拟族人：只记在族谱的个人经历里(不报世界提示，免得年年一串)
        son.personal_history ??= new List<PersonalHistoryRecord>();
        son.personal_history.Add(new PersonalHistoryRecord
        {
            timestamp = World.world.getCurWorldTime(),
            date = Date.getDate(World.world.getCurWorldTime()),
            content = string.Format(LM.Get("personal_history_peerage_granted"), empire.GetEmpireFullName(), title),
            kingdom_name = empire.CoreKingdom?.GetKingdomName() ?? "",
            empire_id = empire.id,
            owner_personal_identity_id = son.id
        });
    }

    // 本朝在世藩王已用的地名(王号去掉"王")
    private static HashSet<string> UsedTitles(Empire empire)
    {
        var used = new HashSet<string>();
        foreach (SpecificClan clan in SpecificClanManager.LineageClans(empire.EmpireSpecificClan))
        foreach (PersonalClanIdentity person in clan.SnapshotPeople())
            if (person != null && person.is_alive && !string.IsNullOrWhiteSpace(person.prince_title))
                used.Add(person.prince_title.EndsWith(KingSuffix) ? person.prince_title.Substring(0, person.prince_title.Length - KingSuffix.Length) : person.prince_title);
        used.Add(empire.GetEmpireName() ?? "");
        return used;
    }

    // 下一个王号用的地名：本国的法理头衔(城多的先)，再是本国的城(大的先)；都城那块留给天子
    private static string NextPlace(Empire empire, HashSet<string> used)
    {
        Kingdom core = empire.CoreKingdom;
        string capitalTitle = core?.capital?.GetTitle()?.data?.name;
        if (ModClass.KINGDOM_TITLE_MANAGER != null)
            foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER
                         .Where(title => title?.data != null && !title.isRekt() && title.title_capital?.kingdom?.GetEmpire() == empire)
                         .OrderByDescending(title => title.getCities().Count()).ThenBy(title => title.id))
            {
                string name = title.data.name;
                if (!string.IsNullOrWhiteSpace(name) && name != capitalTitle && !used.Contains(name)) return name;
            }
        foreach (City city in empire.getCities().Where(city => city?.data != null && !city.isRekt() && city != core?.capital)
                     .OrderByDescending(city => CityPopulationSystem.GetBackgroundTotal(city)))
        {
            string name = city.GetCityName();
            if (!string.IsNullOrWhiteSpace(name) && !used.Contains(name)) return name;
        }
        return null;
    }

    // 藩王身后定谥(用户 2026-10-16："藩王去世后也有谥号的")：王号里的地名 + 单字谥 + 王，如"湘献王"；
    // 用字同封国国君(见 PosthumousNameGenerator.DecideVassal：早夭殇、冲、悼，死于非命烈、愍、庄，其余孝、恭、惠……)，
    // 同一王号先前的藩王用过的谥加"后"区别。在位的帝王、封国国君另有谥法，不走这里
    public static void OnPrinceDied(PersonalClanIdentity person, bool killed)
    {
        if (person == null || string.IsNullOrWhiteSpace(person.prince_title) || !string.IsNullOrWhiteSpace(person.posthumous_name)) return;
        if (person._specificClan?.Patrilineal() != true) return;
        string title = person.prince_title;
        string place = title.EndsWith(KingSuffix) ? title.Substring(0, title.Length - KingSuffix.Length) : title;
        if (string.IsNullOrWhiteSpace(place)) return;
        // 同一王号先前的藩王用过的谥字
        var used = new HashSet<string>();
        foreach (SpecificClan clan in SpecificClanManager.LineageClans(person._specificClan))
        foreach (PersonalClanIdentity other in clan.SnapshotPeople())
        {
            string name = other?.posthumous_name;
            if (other == person || string.IsNullOrEmpty(name) || !name.StartsWith(place) || !name.EndsWith(KingSuffix)) continue;
            string middle = name.Substring(place.Length, name.Length - place.Length - KingSuffix.Length);
            string later = LM.Get("posthumous_later_prefix") ?? "";
            if (!string.IsNullOrEmpty(later) && middle.StartsWith(later)) middle = middle.Substring(later.Length);
            used.Add(middle);
        }
        int age = person.recordedAge > 0 ? person.recordedAge : person.age;
        var decision = PosthumousNameGenerator.DecideVassal(-1, age, killed, 0, used);
        string shi = LM.Get(decision.shi);
        if (string.IsNullOrWhiteSpace(shi)) return;
        string posthumous = place + (used.Contains(shi) ? LM.Get("posthumous_later_prefix") ?? "" : "") + shi + KingSuffix;
        person.posthumous_name = posthumous;
        person.RecordPersonalHistory(string.Format(LM.Get("feudal_posthumous_history"), posthumous, LM.Get(decision.reason)));
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

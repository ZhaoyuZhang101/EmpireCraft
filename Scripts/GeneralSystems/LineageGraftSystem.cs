using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 世系接续：宗室远支入继、族谱缺人时，新君在族谱里和本朝先君连不上，世系图上只能画一条"旁支入继"的线。
// 这里按年龄差往上补几代祖先，把他接到本朝一位先君名下，世系图就能一路连上去——比如"太祖—齐宣王—南诏侯—今上"：
//   · 从新君沿父系(父亲；没有父亲时同宗的母亲)找到他最上面的已知祖先；
//   · 在本朝同宗的先君里挑一位比这位祖先年长十八岁以上、年纪最接近的(没有就用最早的一位)；
//   · 中间按每代二十五年补人：先君之子封王(华夏用古国名加谥字，如"齐宣王")，再往下封侯(本国城名，如"南诏侯")；
//     补上的人都已故，只记名字、生卒、封号，不生成实体。
// 新君即位时调用(见 KingdomPatch.new_emperor)；打开世系图时也把本朝历代补一遍，旧存档同样连得上。
public static class LineageGraftSystem
{
    private const int MaxTrace = 12, MinParentGap = 18, YearsPerGeneration = 25, MaxGraftGenerations = 6;
    private const double SecondsPerYear = 60d;

    private static readonly string[] AncientStates =
        { "齐", "楚", "燕", "赵", "韩", "魏", "秦", "晋", "吴", "越", "鲁", "卫", "郑", "宋", "陈", "蔡", "梁", "代", "淮南", "长沙", "河间", "中山" };
    private static readonly string[] PosthumousChars =
        { "宣", "靖", "献", "恭", "康", "简", "孝", "庄", "惠", "安", "顺", "僖", "元", "懿", "哀", "怀" };

    // 新君即位：和本朝先君连不上就补上中间几代
    public static void EnsureLinked(Empire empire, Actor emperor)
    {
        if (empire?.data == null || emperor?.data == null) return;
        try
        {
            emperor.CheckSpecificClan(false);
            PersonalClanIdentity person = emperor.GetPersonalIdentity();
            if (person == null) return;
            List<PersonalClanIdentity> rulers = DynastyRulers(empire, empire.data.currentHistory)
                .Where(ruler => ruler.id != person.id).ToList();
            Graft(empire, person, rulers);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世系接续] 补接失败: {exception.Message}");
        }
    }

    // 世系图打开时：本朝历代君主依次检查，每位都要能接到他之前的某位先君
    public static void RepairDynasty(Empire empire, IList<EmpireCraftHistory> reigns)
    {
        if (empire?.data == null || reigns == null || reigns.Count < 2) return;
        try
        {
            Dictionary<long, PersonalClanIdentity> byActor = IdentitiesOf(reigns);
            var earlier = new List<PersonalClanIdentity>();
            foreach (EmpireCraftHistory history in reigns)
            {
                if (history == null || !byActor.TryGetValue(history.id, out PersonalClanIdentity person))
                    continue;
                if (earlier.Count > 0 && !history.is_first) Graft(empire, person, earlier.ToList());
                if (!earlier.Contains(person)) earlier.Add(person);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世系接续] 补接本朝世系失败: {exception.Message}");
        }
    }

    private static void Graft(Empire empire, PersonalClanIdentity person, List<PersonalClanIdentity> earlier)
    {
        // 只接同宗的先君(易姓、外家入继另成一系)
        List<PersonalClanIdentity> rulers = earlier.Where(ruler => ruler != null && ruler.id != person.id &&
                                                                   ruler.specific_clan_id == person.specific_clan_id).ToList();
        if (rulers.Count == 0) return;
        Dictionary<long, int> mine = Ancestors(person);
        foreach (PersonalClanIdentity ruler in rulers)
        {
            if (mine.ContainsKey(ruler.id)) return;
            if (Ancestors(ruler).Keys.Any(id => id != ruler.id && mine.ContainsKey(id))) return;
        }
        // 最上面的已知祖先
        PersonalClanIdentity top = person;
        for (int guard = 0; guard < MaxTrace; guard++)
        {
            PersonalClanIdentity parent = LineageParent(top);
            if (parent == null) break;
            top = parent;
        }
        if (top.father > 0 && SpecificClanManager.getPerson(top.father) != null) return;
        if (!HistoryDateFormatter.TryParseTime(top.birthday, out double topBorn)) return;
        // 挑先君：比这位祖先年长十八岁以上、最接近的；都不够就用最早出生的
        PersonalClanIdentity anchor = null;
        double anchorBorn = 0d;
        foreach (PersonalClanIdentity ruler in rulers)
        {
            if (ruler.id == top.id || !HistoryDateFormatter.TryParseTime(ruler.birthday, out double born)) continue;
            if ((topBorn - born) / SecondsPerYear < MinParentGap) continue;
            if (anchor == null || born > anchorBorn)
            {
                anchor = ruler;
                anchorBorn = born;
            }
        }
        if (anchor == null) return;
        double gapYears = (topBorn - anchorBorn) / SecondsPerYear;
        int generations = Math.Max(1, Math.Min(MaxGraftGenerations, (int)Math.Round(gapYears / YearsPerGeneration)));
        SpecificClan clan = person._specificClan;
        if (clan == null) return;
        PersonalClanIdentity upper = anchor;
        var used = new HashSet<string>();
        for (int step = 1; step < generations; step++)
        {
            double born = anchorBorn + (topBorn - anchorBorn) * step / generations;
            PersonalClanIdentity ancestor = CreateAncestor(empire, clan, person, upper, born, step, used);
            upper = ancestor;
        }
        top.setParent(upper, recordHistory: false);
        if (top.generation <= upper.generation) top.generation = upper.generation + 1;
        LogService.LogInfo($"[EmpireCraft][世系接续] {person.name} 经 {generations - 1} 代补接到先君 {anchor.name} 名下");
    }

    private static PersonalClanIdentity CreateAncestor(Empire empire, SpecificClan clan, PersonalClanIdentity heir,
        PersonalClanIdentity parent, double born, int step, HashSet<string> used)
    {
        double now = World.world?.getCurWorldTime() ?? born;
        int lifespan = UnityEngine.Random.Range(42, 66);
        double died = Math.Min(born + lifespan * SecondsPerYear, now - SecondsPerYear);
        var ancestor = new PersonalClanIdentity
        {
            id = OverallHelperFunc.IdGenerator.NextId(),
            specific_clan_id = clan.id,
            actor_id = -1L,
            is_alive = false,
            is_virtual = false,
            recordedAge = Math.Max(1, (int)((died - born) / SecondsPerYear)),
            birthday = Date.getDate(born),
            deathday = Date.getDate(died),
            sex = clan.isMalePriority() ? ActorSex.Male : ActorSex.Female,
            species = heir.species,
            culture = heir.culture,
            generation = parent.generation + 1,
            is_main = true,
            death_history_recorded = true
        };
        string given = GivenName(ancestor.id, heir.culture);
        ancestor.given_name = given;
        ancestor.surname = !string.IsNullOrWhiteSpace(heir.surname) ? heir.surname : clan.name;
        ancestor.name = string.IsNullOrWhiteSpace(ancestor.surname) ? given
            : OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(heir.culture ?? "", out var rule) && rule?.Unit?.is_invert == true
                ? OverallHelperFunc.JoinNameParts(given, ancestor.surname)
                : OverallHelperFunc.JoinNameParts(ancestor.surname, given);
        string title = Title(empire, heir, step, used);
        if (!string.IsNullOrWhiteSpace(title)) ancestor.ownedTitleNames.Add(title);
        clan.Upsert(ancestor);
        SpecificClanManager._globalPersonLookup[ancestor.id] = ancestor;
        ancestor.setParent(parent, recordHistory: false);
        return ancestor;
    }

    private static string GivenName(long id, string culture)
    {
        string generated = CultureService.GenerateCulturalName(MetaType.Unit, id, culture);
        string[] parts = (generated ?? "").SplitNameParts();
        string given = parts.Length > 0 ? parts[parts.Length - 1] : "";
        return string.IsNullOrWhiteSpace(given) ? generated ?? "" : given;
    }

    // 封号：先君之子封王(华夏：古国名 + 谥字，如"齐宣王"；其他文化：本国城名 + 王)，再往下封侯(本国城名)
    private static string Title(Empire empire, PersonalClanIdentity heir, int step, HashSet<string> used)
    {
        bool chinese = LocalizedTextManager.instance?.language is "cz" or "ch";
        bool huaxia = heir.culture == "Huaxia" && chinese;
        string place = null;
        if (step == 1 && huaxia)
            place = Pick(AncientStates, used);
        else
        {
            List<string> cities = empire.AllCities()?.Where(city => city != null && !city.isRekt())
                .Select(city => city.GetCityName()).Where(name => !string.IsNullOrWhiteSpace(name)).ToList() ?? new List<string>();
            place = cities.Count > 0 ? Pick(cities, used) : Pick(AncientStates, used);
        }
        if (string.IsNullOrWhiteSpace(place)) return "";
        if (step == 1)
            return string.Format(LM.Get("lineage_graft_prince"), place, huaxia ? Pick(PosthumousChars, null) : "");
        return string.Format(LM.Get("lineage_graft_marquis"), place);
    }

    private static string Pick(IList<string> options, HashSet<string> used)
    {
        if (options == null || options.Count == 0) return "";
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string option = options[UnityEngine.Random.Range(0, options.Count)];
            if (used == null || used.Add(option)) return option;
        }
        return options[UnityEngine.Random.Range(0, options.Count)];
    }

    // 帝系那一边的父母：父亲；没有父亲时与本人同宗的母亲
    private static PersonalClanIdentity LineageParent(PersonalClanIdentity person)
    {
        PersonalClanIdentity father = person.father > 0 ? SpecificClanManager.getPerson(person.father) : null;
        if (father != null) return father;
        PersonalClanIdentity mother = person.mother > 0 ? SpecificClanManager.getPerson(person.mother) : null;
        return mother != null && mother.specific_clan_id == person.specific_clan_id ? mother : null;
    }

    // 往上的祖先(父母两边，含本人)
    private static Dictionary<long, int> Ancestors(PersonalClanIdentity start)
    {
        var distance = new Dictionary<long, int> { [start.id] = 0 };
        var queue = new Queue<PersonalClanIdentity>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            PersonalClanIdentity person = queue.Dequeue();
            int depth = distance[person.id];
            if (depth >= MaxTrace) continue;
            foreach (long parentId in new[] { person.father, person.mother })
            {
                if (parentId <= 0 || distance.ContainsKey(parentId)) continue;
                PersonalClanIdentity parent = SpecificClanManager.getPerson(parentId);
                if (parent == null) continue;
                distance[parentId] = depth + 1;
                queue.Enqueue(parent);
            }
        }
        return distance;
    }

    // 本朝(与 current 同朝代)历代君主的族谱人物
    private static List<PersonalClanIdentity> DynastyRulers(Empire empire, EmpireCraftHistory current)
    {
        var reigns = new List<EmpireCraftHistory>();
        if (empire.data.history != null) reigns.AddRange(empire.data.history.Where(history => history != null && !history.is_republic));
        if (current != null && !reigns.Contains(current) && !current.is_republic) reigns.Add(current);
        if (current != null)
        {
            string dynasty = DynastyOf(current);
            reigns = reigns.Where(history => DynastyOf(history) == dynasty).ToList();
        }
        Dictionary<long, PersonalClanIdentity> byActor = IdentitiesOf(reigns);
        return reigns.Select(history => byActor.TryGetValue(history.id, out var person) ? person : null)
            .Where(person => person != null).Distinct().ToList();
    }

    private static Dictionary<long, PersonalClanIdentity> IdentitiesOf(IEnumerable<EmpireCraftHistory> reigns)
    {
        var ids = new HashSet<long>(reigns.Where(history => history != null && history.id > 0).Select(history => history.id));
        var result = new Dictionary<long, PersonalClanIdentity>();
        foreach (PersonalClanIdentity identity in SpecificClanManager._globalPersonLookup.Values)
            if (identity != null && ids.Contains(identity.actor_id) && !result.ContainsKey(identity.actor_id))
                result[identity.actor_id] = identity;
        return result;
    }

    private static string DynastyOf(EmpireCraftHistory history) =>
        !string.IsNullOrWhiteSpace(history.dynasty_name) ? history.dynasty_name
        : !string.IsNullOrWhiteSpace(history.royal_surname) ? history.royal_surname
        : history.empire_name ?? "";
}

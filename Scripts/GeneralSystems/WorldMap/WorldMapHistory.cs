using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 世界地图的前史(用户 2026-10-28："之前的历史数据记得补全……汉朝时代也要补足先帝和从周到现在的全部帝国历史""还有族谱表""西方的历史也需要")：
// 1. 本朝先帝：开局年以前本朝的君主写进本国帝国史书(王国写进原版历代君主)，年号、庙号谥号、在位年数照史；
// 2. 先朝：已亡的称帝王朝做成已亡帝国档案(名录、史书归档)，挂到都城所在地区的帝国法理上，帝国列表"已亡"一栏、帝国法理的历代史书里看得到；
// 3. 族谱：本朝历代君主与不在位的祖先做成已故族人，挂进今上的宗族，父子相连一直接到今上(世系图、族谱树都看得到)。
// 一年 60 秒；开局年为第 1 年，之前的年份记负的时间
internal static class WorldMapHistory
{
    internal sealed class Realm
    {
        public WorldMapPolity data;
        public Kingdom kingdom;
        public Empire empire;
        public List<(Actor actor, WorldMapPerson person)> kin = new();
    }

    private const double SecondsPerYear = 60d;

    // 族谱编号 → 这位君主在史书里的记录编号(族谱里的已故族人记这个编号，宗族窗口就显示庙号谥号，见 SpecificClanWindow.Post)
    private static readonly Dictionary<string, long> _recordIds = new();

    public static void Apply(WorldMapEra era, List<Realm> realms, IReadOnlyDictionary<string, EmpireCore> coreByRegion)
    {
        if (WorldMapData.Dynasties.Count == 0) return;
        RegisterTexts();
        _recordIds.Clear();
        var used = new HashSet<string>(realms.Select(realm => realm.data?.dynasty).Where(id => !string.IsNullOrEmpty(id)));
        // 开局还活着的君主(前史里另有一段在位的)：别家族谱里不把他记成已故
        var living = new HashSet<string>(realms.Select(realm => realm.data?.ruler?.self).Where(id => !string.IsNullOrEmpty(id)));
        foreach (Realm realm in realms.Where(realm => !string.IsNullOrEmpty(realm.data?.dynasty)))
        {
            WorldMapDynasty dynasty = WorldMapData.Dynasty(realm.data.dynasty);
            if (dynasty == null || realm.kingdom == null || realm.kingdom.isRekt()) continue;
            List<WorldMapReign> prior = dynasty.rulers.Where(reign => reign.a < era.year && reign.b <= era.year).ToList();
            try
            {
                if (realm.empire?.data != null)
                {
                    List<EmpireCraftHistory> records = prior.Select((reign, i) => Record(dynasty, reign, era.year, i == 0)).ToList();
                    realm.empire.data.history ??= new List<EmpireCraftHistory>();
                    realm.empire.data.history.InsertRange(0, records);
                    if (records.Count > 0) NotFounder(realm.empire, records[0]);
                }
                else
                {
                    realm.kingdom.data.past_rulers ??= new List<LeaderEntry>();
                    // 原版历代君主最多留 30 条(末一条是今上)
                    realm.kingdom.data.past_rulers.InsertRange(0, prior.Skip(Math.Max(0, prior.Count - 29)).Select(reign => new LeaderEntry
                    {
                        id = -1L,
                        color_id = realm.kingdom.data.color_id,
                        name = WorldMapData.PickParts(reign.n),
                        timestamp_ago = (reign.a - era.year) * SecondsPerYear,
                        timestamp_end = (reign.b - era.year) * SecondsPerYear
                    }));
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] {dynasty.id} 先帝入史失败: {exception.Message}");
            }
        }
        // 先朝：已亡的称帝王朝，按开国先后挂到帝国法理的历代帝国里：都城所在地区的帝国法理，
        // 连同同文化的现存帝国的帝国法理(汉朝看得到周、秦、新，三国各家都看得到两汉)
        var fallenByCore = new Dictionary<EmpireCore, List<long>>();
        var heirs = realms.Where(realm => realm.empire?.data != null && realm.kingdom != null && !realm.kingdom.isRekt())
            .Select(realm => (culture: CultureService.GetRealmCulture(realm.kingdom), core: EmpireCoreManager.Get(realm.empire)))
            .Where(heir => heir.core != null && !string.IsNullOrEmpty(heir.culture))
            .ToList();
        foreach (WorldMapDynasty dynasty in WorldMapData.Dynasties
                     .Where(d => d.emp && !used.Contains(d.id) && d.rulers.Count > 0 && d.rulers.Max(r => r.b) <= era.year)
                     .OrderBy(d => d.rulers.Min(r => r.a)))
        {
            try
            {
                long id = OverallHelperFunc.IdGenerator.NextId();
                List<EmpireCraftHistory> histories = dynasty.rulers.Select((reign, i) => Record(dynasty, reign, era.year, i == 0)).ToList();
                int start = dynasty.rulers.Min(r => r.a), end = dynasty.rulers.Max(r => r.b);
                ModClass.ALL_HISTORY_DATA[id] = histories;
                FallenEmpireRecords.Records.Add(new FallenEmpireRecord
                {
                    id = id,
                    name = dynasty.FullName,
                    culture = dynasty.c,
                    established_at = (start - era.year) * SecondsPerYear,
                    fallen_at = (end - era.year) * SecondsPerYear,
                    years = Math.Max(1, end - start),
                    last_ruler = histories.LastOrDefault()?.emperor ?? ""
                });
                IEnumerable<EmpireCore> cores = dynasty.seat
                    .Select(seat => coreByRegion.TryGetValue(seat, out EmpireCore core) ? core : null)
                    .Concat(heirs.Where(heir => heir.culture == dynasty.c).Select(heir => heir.core));
                foreach (EmpireCore core in cores.Where(core => core != null).Distinct())
                {
                    if (!fallenByCore.TryGetValue(core, out List<long> ids)) fallenByCore[core] = ids = new List<long>();
                    ids.Add(id);
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] 先朝 {dynasty.id} 失败: {exception.Message}");
            }
        }
        foreach (KeyValuePair<EmpireCore, List<long>> entry in fallenByCore)
            entry.Key.empire_history_ids = entry.Value.Concat(entry.Key.empire_history_ids ?? new List<long>()).Distinct().ToList();
        foreach (Realm realm in realms.Where(realm => realm.data?.ruler != null && realm.kingdom != null && !realm.kingdom.isRekt()))
        {
            try
            {
                NameHouse(realm);
                if (!string.IsNullOrEmpty(realm.data.dynasty)) Genealogy(realm, era.year, living);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][世界地图] {realm.data.dynasty} 族谱失败: {exception}");
            }
        }
    }

    // 皇室、王室的宗族叫剧本的家族名(哈布斯堡氏、萨法维氏)：以前取的是造人时原版按种族起的氏族名(施赖伯氏、大流士班家)
    private static void NameHouse(Realm realm)
    {
        string family = WorldMapData.PickParts(realm.data.ruler.fam);
        Actor ruler = realm.kingdom.king;
        if (string.IsNullOrWhiteSpace(family) || ruler == null || ruler.isRekt()) return;
        ruler.CheckSpecificClan();
        SpecificClan clan = ruler.GetSpecificClan();
        if (clan == null || clan.name == family) return;
        clan.name = family;
        if (ruler.clan != null) SpecificClanManager.SyncVanillaClanName(ruler.clan);
    }

    // 中途开局(117 年的汉安帝)：开国之君是史书里的第一位，今上不算开国(没有开国之君特质、开国宽限，身后不按开国定谥)，按常例重评
    private static void NotFounder(Empire empire, EmpireCraftHistory founder)
    {
        empire.data.founder_actor_id = founder.id;
        empire.data.dynasty_founder_actor_id = founder.id;
        if (empire.data.currentHistory != null) empire.data.currentHistory.is_first = false;
        Actor emperor = empire.Emperor;
        if (emperor == null || emperor.isRekt() || !emperor.hasTrait(RulerTraitSystem.Founder)) return;
        emperor.removeTrait(RulerTraitSystem.Founder);
        RulerTraitSystem.OnCrowned(empire, emperor);
    }

    // 一条在位记录：华夏有谥号的拼成"汉高帝""唐高祖 · 唐神尧大圣大光孝皇帝"(谥号、庙号按键存，键见 ShiKey/MiaoKey)；
    // 没有中式谥号的(周天子、罗马皇帝……)记剧本称号，不再自动定谥(见 PosthumousNameGenerator.ScriptedTitle)
    private static EmpireCraftHistory Record(WorldMapDynasty dynasty, WorldMapReign reign, int eraYear, bool first)
    {
        bool chinese = WorldMapData.ChineseUi;
        bool traditional = Language() == "ch";
        string dynastyName = string.IsNullOrEmpty(reign.dyn) ? dynasty.Name : WorldMapData.PickParts(reign.dyn);
        var history = new EmpireCraftHistory
        {
            // 编号与人物编号不相撞(人物编号从小数起)，史书各处按编号去重
            id = OverallHelperFunc.IdGenerator.NextId(),
            empire_name = dynasty.Name,
            empire_full_name = dynasty.FullName,
            dynasty_name = dynastyName,
            royal_surname = WorldMapData.PickParts(dynasty.fam),
            emperor = WorldMapData.PickParts(reign.n),
            year_name = chinese && !string.IsNullOrEmpty(reign.era) ? (traditional && !string.IsNullOrEmpty(reign.era_tw) ? reign.era_tw : reign.era) : "—",
            total_time = Math.Max(1, reign.b - reign.a),
            is_first = first,
            miaohao_name = "",
            miaohao_suffix = "",
            office_title = "",
            descriptions = new List<HistoryDescription>
            {
                new()
                {
                    time = YearText(reign.a), cities = new List<string>(),
                    description = string.Format(LM.Get("world_map_history_reign"), YearText(reign.a), YearText(reign.b))
                }
            }
        };
        if (!string.IsNullOrEmpty(reign.p)) _recordIds[reign.p] = history.id;
        if (chinese && !string.IsNullOrEmpty(reign.shi))
        {
            history.shihao_name = ShiKey(reign.shi);
            if (!string.IsNullOrEmpty(reign.miao) && reign.miao.Length >= 2)
            {
                history.miaohao_name = MiaoKey(reign.miao);
                history.miaohao_suffix = reign.miao.EndsWith("祖") ? "miaohaosuffixes_zu" : "miaohaosuffixes_zong";
            }
        }
        else
        {
            history.shihao_name = PosthumousNameGenerator.ScriptedTitle;
            history.office_title = string.IsNullOrEmpty(reign.t) ? "" : WorldMapData.PickParts(reign.t);
        }
        return history;
    }

    // 谥号、庙号前字的语言键：史书各处按 LM.Get(键) 显示，存档里只有键，所以启动时、每次换语言后都把剧本里的全部谥号庙号登记一遍
    private const string ShiPrefix = "world_map_shi_", MiaoPrefix = "world_map_miao_";
    private static string ShiKey(string shi) => ShiPrefix + shi;
    private static string MiaoKey(string miao) => MiaoPrefix + miao.Substring(0, miao.Length - 1);

    public static void RegisterTexts()
    {
        try
        {
            if (LocalizedTextManager.instance == null || ModClass._declare == null) return;
            WorldMapData.EnsureLoaded();
            bool traditional = Language() == "ch";
            foreach (WorldMapReign reign in WorldMapData.Dynasties.SelectMany(dynasty => dynasty.rulers))
            {
                if (!string.IsNullOrEmpty(reign.shi))
                    LM.AddToCurrentLocale(ShiKey(reign.shi), traditional && !string.IsNullOrEmpty(reign.shi_tw) ? reign.shi_tw : reign.shi);
                if (!string.IsNullOrEmpty(reign.miao) && reign.miao.Length >= 2)
                {
                    string miao = traditional && !string.IsNullOrEmpty(reign.miao_tw) ? reign.miao_tw : reign.miao;
                    LM.AddToCurrentLocale(MiaoKey(reign.miao), miao.Substring(0, miao.Length - 1));
                }
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 谥号登记失败: {exception.Message}");
        }
    }

    private static string Language()
    {
        try
        {
            return PlayerConfig.dict["language"].stringVal;
        }
        catch
        {
            return "en";
        }
    }

    private static string YearText(int year) => year < 0
        ? string.Format(LM.Get("world_map_year_bc"), -year)
        : string.Format(LM.Get("world_map_year_ad"), year);

    // 族谱：本朝(及世系经过的别朝)的历代君主、祖先做成已故族人，挂进今上的宗族，父子相连到今上
    private static void Genealogy(Realm realm, int eraYear, HashSet<string> living)
    {
        string fatherPid = realm.data.ruler?.father;
        Actor ruler = realm.kingdom.king;
        if (string.IsNullOrEmpty(fatherPid) || ruler == null || ruler.isRekt()) return;
        ruler.CheckSpecificClan();
        SpecificClan clan = ruler.GetSpecificClan();
        PersonalClanIdentity me = ruler.GetPersonalIdentity();
        if (clan == null || me == null) return;

        // 全部族谱编号 → 族人(朝代、姓名、父亲、称号、卒年)
        var people = new Dictionary<string, Ancestor>();
        foreach (WorldMapDynasty dynasty in WorldMapData.Dynasties)
        {
            foreach (WorldMapAncestor ancestor in dynasty.people)
                if (!string.IsNullOrEmpty(ancestor.p))
                    people[ancestor.p] = new Ancestor { dynasty = dynasty, name = ancestor.n, father = ancestor.f, title = WorldMapData.PickParts(ancestor.t) };
            foreach (WorldMapReign reign in dynasty.rulers)
            {
                if (string.IsNullOrEmpty(reign.p)) continue;
                if (!people.TryGetValue(reign.p, out Ancestor known))
                    people[reign.p] = known = new Ancestor { dynasty = dynasty, name = reign.n, father = reign.f };
                known.died = Math.Max(known.died, reign.b);
                if (string.IsNullOrEmpty(known.title))
                    known.title = !string.IsNullOrEmpty(reign.t) ? WorldMapData.PickParts(reign.t) : dynasty.Name + LM.Get("king");
            }
        }
        if (!people.ContainsKey(fatherPid)) return;
        // 入谱：今上直系往上的每一代，连同这几朝开局前已在位、父系往上接得到这条直系的先帝和他们那一支的祖先(兄弟叔伯、各支先帝)；
        // 开局后才即位的不入，同一朝里接不上的别家(罗马、神罗换过的家族)也不入
        var dynasties = new HashSet<WorldMapDynasty>();
        var members = new HashSet<string>();
        int steps = 0;
        for (string pid = fatherPid; !string.IsNullOrEmpty(pid) && people.ContainsKey(pid) && members.Add(pid) && steps++ < 80; pid = people[pid].Father)
            dynasties.Add(people[pid].dynasty);
        foreach (string seed in dynasties.SelectMany(dynasty => dynasty.rulers)
                     .Where(reign => !string.IsNullOrEmpty(reign.p) && reign.a < eraYear && reign.b <= eraYear).Select(reign => reign.p))
        {
            var branch = new List<string>();
            string pid = seed;
            steps = 0;
            while (!string.IsNullOrEmpty(pid) && people.ContainsKey(pid) && !members.Contains(pid) && steps++ < 80)
            {
                branch.Add(pid);
                pid = people[pid].Father;
            }
            if (!string.IsNullOrEmpty(pid) && members.Contains(pid)) members.UnionWith(branch);
        }

        // 今上自己在前史里的那段在位：就是他本人，不另做已故族人
        string selfPid = realm.data.ruler?.self;
        members.ExceptWith(living);
        var made = new Dictionary<string, PersonalClanIdentity>();
        foreach (string pid in members)
        {
            Ancestor ancestor = people[pid];
            WorldMapDynasty dynasty = ancestor.dynasty;
            string name = ancestor.name;
            // 姓：名字以本朝姓开头的才算(西方君主、谱上的外姓先人没有姓)
            string full = WorldMapData.PickParts(name);
            string surname = WorldMapData.PickParts(dynasty.fam);
            if (string.IsNullOrEmpty(surname) || !full.StartsWith(surname) || full.Length <= surname.Length) surname = "";
            var person = new PersonalClanIdentity
            {
                id = OverallHelperFunc.IdGenerator.NextId(),
                specific_clan_id = clan.id,
                is_alive = false,
                is_virtual = false,
                recordedAge = -1,
                sex = ActorSex.Male,
                species = me.species,
                culture = me.culture,
                is_main = true,
                name = full,
                surname = surname,
                given_name = surname.Length > 0 ? full.Substring(surname.Length) : full,
                // 当过皇帝的连到史书记录(宗族窗口显示庙号谥号)；没有史书记录的(王国的先王、不在位的祖先)记剧本称号
                actor_id = _recordIds.TryGetValue(pid, out long record) ? record : -1L,
                posthumous_name = _recordIds.ContainsKey(pid) ? "" : ancestor.title ?? "",
                deathday = ancestor.died > int.MinValue ? YearText(ancestor.died) : ""
            };
            clan.Upsert(person);
            SpecificClanManager._globalPersonLookup[person.id] = person;
            made[pid] = person;
        }
        foreach (KeyValuePair<string, PersonalClanIdentity> entry in made.ToList())
        {
            string father = people[entry.Key].Father;
            if (!string.IsNullOrEmpty(selfPid) && father == selfPid)
            {
                Adopt(me, entry.Value);
                continue;
            }
            if (string.IsNullOrEmpty(father) || !made.TryGetValue(father, out PersonalClanIdentity parent)) continue;
            entry.Value.father = parent.id;
            if (!parent.children.Contains(entry.Value.id)) parent.children.Add(entry.Value.id);
        }
        PersonalClanIdentity dad = made[fatherPid];
        // 今上和剧本里的兄弟姐妹认这位父亲(子孙早已认今上为父)
        var household = new List<PersonalClanIdentity> { me };
        foreach ((Actor actor, WorldMapPerson person) in realm.kin)
        {
            if (actor == null || actor.isRekt()) continue;
            actor.CheckSpecificClan();
            PersonalClanIdentity identity = actor.GetPersonalIdentity();
            if (identity == null || household.Contains(identity)) continue;
            household.Add(identity);
            if (person?.rel is "brother" or "sister") Adopt(dad, identity);
        }
        Adopt(dad, me);
        // 辈分：始祖为第一世往下数；今上一家整体挪到今上正好比父亲低一辈
        var depth = new Dictionary<string, int>();
        int Depth(string pid, int guard)
        {
            if (depth.TryGetValue(pid, out int known)) return known;
            string father = people[pid].Father;
            int value = guard > 60 || string.IsNullOrEmpty(father) || !made.ContainsKey(father) ? 1 : Depth(father, guard + 1) + 1;
            depth[pid] = value;
            return value;
        }
        foreach (KeyValuePair<string, PersonalClanIdentity> entry in made) entry.Value.generation = Depth(entry.Key, 0);
        int shift = dad.generation + 1 - me.generation;
        if (shift != 0)
            foreach (PersonalClanIdentity identity in household) identity.generation += shift;
        // 始祖记为宗族始祖
        string root = fatherPid;
        for (int guard = 0; guard < 60 && !string.IsNullOrEmpty(people[root].Father) && made.ContainsKey(people[root].Father); guard++)
            root = people[root].Father;
        clan.founder = made[root].id;
        clan.InvalidatePeople();
    }

    private sealed class Ancestor
    {
        public WorldMapDynasty dynasty;
        public string name, father, title;
        public int died = int.MinValue;
        public string Father => father;
    }

    private static void Adopt(PersonalClanIdentity father, PersonalClanIdentity child)
    {
        child.father = father.id;
        if (!father.children.Contains(child.id)) father.children.Add(child.id);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧情事件的存档(见 ScenarioEventSystem)：事件表、剧本里的国/城/地区编号对上的国家/城/王国法理编号、剧本人物的名字对上的人物编号、
// 开局时刻、触发过的事件
public sealed class ScenarioEventSave
{
    public List<WorldMapEvent> events = new();
    public Dictionary<string, long> realms = new();
    public Dictionary<string, long> cities = new();
    public Dictionary<string, long> titles = new();
    public Dictionary<string, long> people = new();
    public double started = -1d;
    public double last_check = -1d;
    public int era_year;
    public List<int> fired = new();
    // 开局不任官、等事件起事的人(famous 的 post 写 none，见 Reserved)
    public List<long> reserved = new();
    // 没做成、每年再试的结果：事件序号 → 第几条(见 ScenarioPlotRules.Retriable)
    public Dictionary<int, List<int>> retry = new();
    // 起兵的领头人(见 WatchRebelLeaders)
    public List<ScenarioRebelLeader> rebel_leaders = new();
    // 号召归附(见 Rally)
    public ScenarioRally rally;
    // 以战养战的流寇：国家编号(见 Roving；领头人死了也照旧，所以不记在 rebel_leaders 上)
    public List<long> roving = new();
}

// 号召归附：入侵者、受投奔的朝廷(都是国家编号)
public sealed class ScenarioRally
{
    public long conqueror;
    public long court;
}

// 起兵的领头人：人、他立的国、他反的国(都是编号)
public sealed class ScenarioRebelLeader
{
    public long actor;
    public long rebel;
    public long origin;
}

// 剧情事件(剧本编辑器第四期，见 Docs/plan-scenario-editor.md；用户 10-30 "剧情事件：要，选项拼搭")：
//   自定剧本开局时记下事件表与剧本里的国、城、地区各对上了哪个国家、城、王国法理(随存档保存)，之后每年查一次——
//   开局满 after 年、条件全部满足就触发：照结果一条条做，写进史书、上屏幕；不可重复的只触发一次。
//   条件：某国据有某城、某国据有某王国法理过半、某国已亡、某国尚在、某国已称帝、两国交战中、某国王朝积弊高于某数、
//     开局未满几年(before：只写文字的史事要卡年份，不然条件晚了十几年才凑齐，史书上就错了年份)；
//   结果：称帝(可定国号)、改国号、迁都、两国之间加一条关系(交战、停战、结盟、和亲、通好、保护、朝贡、附庸、关税同盟、旧谊、宿怨，
//     走 DiplomacyActions.ApplyScenario 同一套判定)、改文化、采纳华夏制度(入主中原，国号照写的定，见 CompositeEmpireService.ScriptedConquestName)、
//     起兵割据(剧本点名的人据城另立一国、与帝国开战，见 Rebel；用户 10-30 "唐朝剧本的安史之乱呢")、
//     献城改投(某城易手、某人连军投奔，见 Cede；明末、宋金剧本的甲申城破、祖大寿降清)、人物身故(见 Dies)、立储。
//   国家没了、城没了的条件算不满足；结果做不了的跳过、记一行日志。
public static class ScenarioEventSystem
{
    public static readonly string[] ConditionKinds = { "holds_city", "holds_region", "gone", "alive", "empire", "at_war", "strain_above", "before" };
    public static readonly string[] EffectKinds = { "relation", "proclaim", "rename", "move_capital", "culture", "adopt_huaxia", "rebel", "cede", "dies", "heir", "grant_title", "rally",
        "banner_city", "banner_enclosure", "banner_law", "puppet", "ideology", "exile" };

    private static ScenarioEventSave _state;
    // 剧本人物的编号；还没演完戏份的人护到第几年；还没起事、不让朝廷派官的人(都从存档现算，见 RebuildPlot)
    private static readonly HashSet<long> _scripted = new();
    private static Dictionary<long, int> _plot = new();
    private static readonly HashSet<long> _reservedNow = new();
    // 事件亲手安排的身故，不挡
    private static bool _scriptedDeath;

    public static bool Active => _state?.events?.Count > 0;

    // people：剧本人物(君主、宗室后妃、名将名臣)的名字(简体、繁体、英文各一个键)对上的人物编号，起兵割据按名字找人
    public static void Begin(WorldMapEra era, Dictionary<string, long> realms, Dictionary<string, long> cities, Dictionary<string, long> titles,
        Dictionary<string, long> people = null, List<long> reserved = null)
    {
        _state = null;
        if (era?.events == null || era.events.Count == 0 || World.world == null) return;
        _state = new ScenarioEventSave
        {
            events = era.events.Select(Clone).ToList(), realms = realms ?? new(), cities = cities ?? new(), titles = titles ?? new(),
            people = people ?? new(),
            started = World.world.getCurWorldTime(), last_check = World.world.getCurWorldTime(), era_year = era.year,
            reserved = reserved ?? new List<long>()
        };
        RebuildPlot();
        LogService.LogInfo($"[EmpireCraft][剧情事件] {era.Name}：{_state.events.Count} 条事件");
    }

    public static void Load(ScenarioEventSave saved)
    {
        ResetWorldState();
        if (saved?.events == null || saved.events.Count == 0) return;
        saved.realms ??= new Dictionary<string, long>();
        saved.cities ??= new Dictionary<string, long>();
        saved.titles ??= new Dictionary<string, long>();
        saved.people ??= new Dictionary<string, long>();
        saved.fired ??= new List<int>();
        saved.reserved ??= new List<long>();
        saved.retry ??= new Dictionary<int, List<int>>();
        saved.rebel_leaders ??= new List<ScenarioRebelLeader>();
        _state = saved;
        RebuildPlot();
    }

    public static ScenarioEventSave Save() => Active ? _state : null;

    private static bool _namesRepaired;

    public static void ResetWorldState()
    {
        _namesRepaired = false;
        _state = null;
        _scripted.Clear();
        _plot = new Dictionary<long, int>();
        _reservedNow.Clear();
    }

    #region 剧情人物

    // 按现在的进度算：谁是剧本人物、谁的戏还没演完、谁还在等着起事
    private static void RebuildPlot()
    {
        _scripted.Clear();
        _reservedNow.Clear();
        _plot = new Dictionary<long, int>();
        if (_state?.events == null) return;
        foreach (long id in _state.people.Values) _scripted.Add(id);
        _plot = ScenarioPlotRules.ProtectUntil(_state.events.Select((item, index) => (
            item?.after ?? 0,
            item == null || !item.repeat && _state.fired.Contains(index),
            (item?.then ?? new List<WorldMapEventStep>()).Select(step => step.person))), _state.people);
        // 还点着名要起事、投奔的才算等着：事情过了就和别人一样能任官
        var pending = new HashSet<long>();
        for (int i = 0; i < _state.events.Count; i++)
        {
            WorldMapEvent item = _state.events[i];
            if (item?.then == null || _state.fired.Contains(i)) continue;
            foreach (WorldMapEventStep step in item.then)
                if (step.kind is "rebel" or "cede" or "puppet")
                    foreach (string name in ScenarioPlotRules.Names(step.person))
                        if (_state.people.TryGetValue(name, out long id)) pending.Add(id);
        }
        foreach (long id in _state.reserved)
            if (pending.Contains(id)) _reservedNow.Add(id);
    }

    private static int YearsSinceStart() =>
        _state == null || _state.started < 0d ? 0 : Math.Max(0, Date.getYearsSince(_state.started));

    // 剧本人物留在地图上，不并入背景人口：并了事件就按编号找不到人了(卢象升开局半年就被并了)
    public static bool IsScripted(Actor actor) => actor?.data != null && _scripted.Contains(actor.data.id);

    // 还没起事的流寇：朝廷别派他们当官、统兵(李自成当过锦州知县，张献忠当过南京布政使)
    public static bool IsReserved(Actor actor) => actor?.data != null && _reservedNow.Contains(actor.data.id);

    // 戏份没演完的人：刀兵、老病、政争都不死，血补满。事件安排的身故放行；过了预定年份太久也放手(见 ScenarioPlotRules)
    public static bool Shield(Actor actor, bool countDeath)
    {
        if (!countDeath || _scriptedDeath || _state == null || !IsProtected(actor)) return false;
        actor.data.health = actor.getMaxHealth();
        return true;
    }

    public static bool IsProtected(Actor actor) =>
        actor?.data != null && _plot.Count > 0 && ScenarioPlotRules.Protected(_plot, actor.data.id, YearsSinceStart());

    #endregion

    private static WorldMapEvent Clone(WorldMapEvent source) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<WorldMapEvent>(Newtonsoft.Json.JsonConvert.SerializeObject(source));

    public static void Tick()
    {
        if (!Active || World.world == null) return;
        WatchRebelLeaders();
        double now = World.world.getCurWorldTime();
        if (_state.last_check >= 0d && now >= _state.last_check && Date.getYearsSince(_state.last_check) < 1) return;
        _state.last_check = now;
        NotePeaks();
        if (!_namesRepaired)
        {
            _namesRepaired = true;
            RepairProclaimedNames();
        }
        int years = _state.started < 0d ? 0 : Math.Max(0, Date.getYearsSince(_state.started));
        for (int i = 0; i < _state.events.Count; i++)
        {
            WorldMapEvent item = _state.events[i];
            if (item == null || years < item.after) continue;
            bool fired = _state.fired.Contains(i);
            List<int> pending = null;
            if (fired && !item.repeat)
            {
                if (!_state.retry.TryGetValue(i, out pending)) continue;
                if (years - item.after > ScenarioPlotRules.GraceYears)
                {
                    _state.retry.Remove(i);
                    continue;
                }
            }
            try
            {
                if (!(item.when ?? new List<WorldMapEventStep>()).All(Holds)) continue;
                List<int> failed = Fire(item, pending);
                if (!item.repeat && failed.Count > 0) _state.retry[i] = failed;
                else _state.retry.Remove(i);
                if (!fired) _state.fired.Add(i);
                RebuildPlot();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][剧情事件] {item.name} 出错: {exception.Message}");
            }
        }
        try
        {
            Rally();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][剧情事件] 号召归附出错: {exception.Message}");
        }
    }

    #region 对上

    private static Kingdom Realm(string id)
    {
        if (string.IsNullOrEmpty(id) || !_state.realms.TryGetValue(id, out long kingdomId)) return null;
        Kingdom kingdom = World.world.kingdoms.get(kingdomId);
        return kingdom?.data == null || kingdom.isRekt() ? null : kingdom;
    }

    private static City CityOf(string id)
    {
        if (string.IsNullOrEmpty(id) || !_state.cities.TryGetValue(id, out long cityId)) return null;
        City city = World.world.cities.get(cityId);
        return city?.data == null || city.isRekt() ? null : city;
    }

    private static KingdomTitle Title(string id)
    {
        if (string.IsNullOrEmpty(id) || !_state.titles.TryGetValue(id, out long titleId) || ModClass.KINGDOM_TITLE_MANAGER == null) return null;
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(titleId);
        return title?.data == null || title.isRekt() ? null : title;
    }

    // 剧本人物(还活着的)：按名字找
    private static Actor Person(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_state.people.TryGetValue(name.Trim(), out long id)) return null;
        Actor actor = World.world.units.get(id);
        return actor?.data == null || actor.isRekt() || !actor.isAlive() ? null : actor;
    }

    // 剧本人物的名字键：姓名"简体|英文|繁体"各拼一个(简体、繁体姓在前连写，英文姓名之间空一格；没有姓的只用名)
    public static IEnumerable<string> NameKeys(string family, string given)
    {
        string[] fam = (family ?? "").Split('|'), giv = (given ?? "").Split('|');
        for (int i = 0; i < Math.Max(fam.Length, giv.Length); i++)
        {
            string f = (i < fam.Length ? fam[i] : fam[0]).Trim(), g = (i < giv.Length ? giv[i] : giv[0]).Trim();
            if (g.Length == 0) continue;
            yield return f.Length == 0 ? g : i == 1 ? f + " " + g : f + g;
        }
    }

    // 城在不在这国手里(帝国连同它的成员国都算；殖民地、傀儡算宗主国、主国的，日本据有京城、台北、新京)
    private static bool Holds(Kingdom kingdom, City city) => city?.kingdom != null && Controls(kingdom, city.kingdom);

    // holder 是不是 kingdom 这一方：本国、本帝国的成员、本国(帝国中央)的殖民地与傀儡
    private static bool Controls(Kingdom kingdom, Kingdom holder)
    {
        if (kingdom == null || holder == null) return false;
        if (holder == kingdom) return true;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && kingdom.IsEmpire() && holder.GetEmpire() == empire) return true;
        Kingdom lord = ColonySystem.MetropoleOf(holder) ?? PuppetStateSystem.Master(holder);
        return lord != null && lord != holder && Controls(kingdom, lord);
    }

    #endregion

    #region 条件

    private static bool Holds(WorldMapEventStep step)
    {
        if (step == null) return true;
        Kingdom a = Realm(step.a);
        switch (step.kind)
        {
            case "holds_city":
                return Holds(a, CityOf(step.city));
            case "holds_region":
                KingdomTitle title = Title(step.region);
                if (a == null || title == null) return false;
                List<City> members = title.getCities().Where(city => city?.data != null && !city.isRekt()).ToList();
                return members.Count > 0 && members.Count(city => Holds(a, city)) * 2 > members.Count;
            case "gone":
                return !string.IsNullOrEmpty(step.a) && a == null;
            case "alive":
                return a != null;
            case "empire":
                return a != null && a.IsEmpire();
            case "at_war":
                Kingdom b = Realm(step.b);
                return a != null && b != null && a.isEnemy(b);
            case "strain_above":
                Empire empire = a?.GetEmpire();
                return empire != null && DynasticCycleSystem.GetPressure(empire) > step.value;
            case "before":
                return _state != null && _state.started >= 0d && Date.getYearsSince(_state.started) < step.value;
            case "dead":
                // 点名的人都不在世了(大顺余部要等李自成死了才归明)
                return !string.IsNullOrWhiteSpace(step.person) && People(step.person).Count == 0;
        }
        return false;
    }

    #endregion

    #region 结果

    // 这件事里迁了都的帝国：(帝国, 原记的京师, 新都)，整件事做完再定是定都还是行在(见 SettleCapitals)
    private static readonly List<(long empire, long from, long to)> Moved = new();

    // only：只做这几条(重试)，空为全做。返回没做成、下一年再试的是第几条
    private static List<int> Fire(WorldMapEvent item, List<int> only = null)
    {
        Moved.Clear();
        var done = new List<string>();
        var again = new List<int>();
        Kingdom first = null;
        List<WorldMapEventStep> steps = item.then ?? new List<WorldMapEventStep>();
        for (int index = 0; index < steps.Count; index++)
        {
            if (only != null && !only.Contains(index)) continue;
            WorldMapEventStep step = steps[index];
            Kingdom a = Realm(step.a);
            first ??= a;
            string failed = Apply(step, a);
            if (failed == null) done.Add(step.kind);
            else if (failed != ScenarioPlotRules.AlreadyAtWar)
            {
                LogService.LogInfo($"[EmpireCraft][剧情事件] {item.name}：{step.kind} 没做成({failed})");
                if (ScenarioPlotRules.Retriable(step.kind, failed)) again.Add(index);
            }
        }
        SettleCapitals();
        // 一条结果都没做成的(点名的人早死了、城早丢了)不上屏幕，免得史书写了没发生的事
        bool happened = done.Count > 0 || only == null && steps.Count == 0;
        // 只出文字的史事(济南之陷、开封水灌)：记在条件里头一个还在的国名下，照样进世界日志
        first ??= (item.when ?? new List<WorldMapEventStep>()).Select(step => Realm(step?.a)).FirstOrDefault(realm => realm != null);
        string text = Text(string.IsNullOrWhiteSpace(item.text) ? item.name : item.text);
        if (happened && !string.IsNullOrWhiteSpace(text))
        {
            string line = string.Format(LM.Get("scenario_event_fired"), text);
            if (first != null) TranslateHelper.LogMajor(line, first);
            else WorldTip.showNow(line, false, "top", 5f);
        }
        LogService.LogInfo($"[EmpireCraft][剧情事件] 触发 {item.name}：{string.Join("、", done)}");
        return again;
    }

    // 剧本迁都，整件事做完再看(用户 2026-11-10 "这大请不对""没剃发易服也没有入关""但是明明都打下全境了")：
    //   · 原记的京师还在本帝国手里 —— 定都(清迁都北京、元都大都、燕都洛阳)：帝国记的京师改成新都。
    //     以前只挪了都城没改记录，下个月"还于旧都"(CoreCapitalRestorationSystem)又把清迁回盛京，坐盛京就一直算没入关，
    //     剃发易服、圈地这些入关之政一样没有；
    //   · 京师已失(甲申之变先迁南京、再献北京；南京、福州、广州相继失守)—— 行在：京师照旧，收复了还于旧都，南明照旧看京师
    private static void SettleCapitals()
    {
        foreach ((long empireId, long fromId, long toId) in Moved)
        {
            Empire empire = ModClass.EMPIRE_MANAGER?.get(empireId);
            Kingdom core = empire?.CoreKingdom;
            City from = World.world.cities.get(fromId), to = World.world.cities.get(toId);
            EmpireCore empireCore = EmpireCoreManager.Get(empire);
            if (empireCore == null || core?.data == null || core.isRekt() || to?.data == null || core.capital != to) continue;
            if (from?.data == null || from.isRekt() || from.kingdom == null || !EmpireMembershipService.BelongsTo(empire, from.kingdom))
            {
                LogService.LogInfo($"[EmpireCraft][剧情事件] {empire.GetEmpireName()} 行在 {to.GetCityName()}，京师仍记 {from?.GetCityName()}");
                continue;
            }
            empireCore.former_capital = from.data.id;
            empireCore.SetCoreCapital(to);
            LogService.LogInfo($"[EmpireCraft][剧情事件] {empire.GetEmpireName()} 定都 {to.GetCityName()}(旧京 {from.GetCityName()})");
        }
        Moved.Clear();
    }

    // 做一条结果；做不了返回原因
    private static string Apply(WorldMapEventStep step, Kingdom a)
    {
        // 人物身故不看国在不在：张献忠的大西亡了，他照样该死于凤凰山
        if (step.kind == "dies") return Dies(step);
        if (a == null) return "国家不在";
        switch (step.kind)
        {
            case "relation":
            {
                Kingdom b = Realm(step.b);
                if (b == null) return "对方不在";
                WarGoal goal = Enum.TryParse(step.goal ?? "", out WarGoal parsed) ? parsed : WarGoal.夺城;
                // 写了城的宣战：那座城就是目标(见 WarObjective.Aim)
                return DiplomacyActions.ApplyScenario(step.relation, a, b, goal, step.value,
                    step.relation == "war" && !string.IsNullOrEmpty(step.city) ? CityOf(step.city) : null);
            }
            case "proclaim":
            {
                if (!a.IsEmpire() && !a.IsInEmpire())
                    ModClass.EMPIRE_MANAGER?.NewEmpire(a, allowCultureRival: true, forceNewCore: true);
                Empire empire = a.GetEmpire();
                if (empire == null || empire.CoreKingdom != a) return "称不了帝";
                if (!string.IsNullOrWhiteSpace(step.name)) Proclaim(empire, a, step.name);
                return null;
            }
            case "rename":
                if (string.IsNullOrWhiteSpace(step.name)) return "没写国号";
                Rename(a, Text(step.name));
                return null;
            case "move_capital":
            {
                City city = CityOf(step.city);
                if (city == null || !Holds(a, city)) return "城不在本国";
                // 帝国的城多在行政区手里(南京在南直隶)：先收归中央再迁
                if (city.kingdom != a) city.joinAnotherKingdom(a);
                if (city.kingdom != a) return "城收不回来";
                City recorded = a.IsEmpire() ? EmpireCoreManager.Get(a.GetEmpire())?.GetCoreCapital() : null;
                if (recorded?.data != null && recorded != city) Moved.Add((a.GetEmpire().id, recorded.data.id, city.data.id));
                a.setCapital(city);
                return null;
            }
            case "culture":
                if (!CultureService.IsValidCulture(step.culture)) return "文化不对";
                RealmCultureSystem.Convert(a, step.culture, wholeEmpire: a.IsEmpire());
                return null;
            case "adopt_huaxia":
            {
                // 入主中原：记下剧本国号与"入关"之城(就是本国都城)，复合帝国的采纳判定下一回就走兼用、照这个国号定(见 CompositeEmpireService)
                if (!a.IsEmpire() && !a.IsInEmpire())
                    ModClass.EMPIRE_MANAGER?.NewEmpire(a, allowCultureRival: true, forceNewCore: true);
                if (a.capital == null) return "没有都城";
                var extra = a.GetOrCreate();
                extra.scripted_conquest_name = string.IsNullOrWhiteSpace(step.name) ? a.GetKingdomName() : Text(step.name);
                extra.scripted_conquest_city_id = a.capital.data.id;
                return null;
            }
            case "rebel":
                return Rebel(step, a);
            case "cede":
                return Cede(step, a);
            case "puppet":
                return Puppet(step, a);
            case "ideology":
                return ScenarioModernPolitics.ChangeIdeology(a, step.name, Text(step.party), step.oneParty);
            case "exile":
            {
                // 政府流亡(捷克、奥地利被吞并，见 ExileGovernmentSystem)：a 的政府流亡到 b，c 是占领者；
                // a 眼下的城连同 region 地区里的城记作待收复。写在献城(cede)之前，随员先出城
                Kingdom host = Realm(step.b);
                if (host == null || host == a) return "东道国不在";
                if (ExileGovernmentSystem.IsExiled(a)) return null;
                List<long> claims = (a.cities ?? new List<City>()).Concat(RegionCities(step.region))
                    .Where(city => city?.data != null).Select(city => city.data.id).Distinct().ToList();
                ExileGovernmentSystem.Begin(a, host, Realm(step.c), claims, null);
                return null;
            }
            case "banner_city":
            {
                if (!BannerSystem.Practices(a)) return "不是八旗多元制";
                int built = StepCities(step, a).Count(city => BannerSystem.BuildGarrison(a, city));
                return built > 0 ? null : "城都不在本国或已有满城";
            }
            case "banner_enclosure":
                if (!BannerSystem.Practices(a)) return "不是八旗多元制";
                return BannerSystem.Enclose(a, StepCities(step, a)) > 0 ? null : "城都不在本国或圈到头了";
            case "banner_law":
                if (!BannerSystem.Practices(a)) return "不是八旗多元制";
                BannerSystem.EnactFugitiveLaw(a);
                return null;
            case "grant_title":
            {
                // 封官授爵：王号照写的(平西王)，存在族谱上(见 PrinceLineage.NoteGrant)，不占法理；人留在原任
                Empire empire = a.GetEmpire();
                if (empire == null) return "不是帝国";
                Actor person = People(step.person).FirstOrDefault(actor => actor.kingdom == a || actor.kingdom?.GetEmpire() == empire);
                if (person == null) return "人不在本国";
                string title = Text(step.name);
                if (string.IsNullOrWhiteSpace(title)) return "没写爵位";
                person.SetPeeragesLevel(PeeragesLevel.peerages_2);
                TranslateHelper.LogPeerageGranted(person, empire, title);
                return null;
            }
            case "rally":
            {
                Kingdom court = Realm(step.b);
                if (court == null) return "对方不在";
                _state.rally = new ScenarioRally { conqueror = a.data.id, court = court.data.id };
                return null;
            }
            case "submit":
            {
                // 起义军归附朝廷，共抗入侵者(大顺余部、大西军归明抗清)：a 连同手下的成员一起归附 b 的帝国，c 是入侵者
                Kingdom court = Realm(step.b);
                if (court == null) return "朝廷不在";
                Empire target = court.GetEmpire();
                if (target == null || target.isRekt() || target.IsArchived() || target.CoreKingdom != court) return "朝廷不是帝国";
                if (a.GetEmpire() == target) return null;
                Kingdom enemy = string.IsNullOrEmpty(step.c) ? null : Realm(step.c);
                // 走投无路才来投；没到这步每年再试(见 ScenarioPlotRules.Desperate)
                if (enemy != null && !Desperate(a, enemy, ConquestPressure(FrontSystem.Realm(enemy) ?? enemy, CultureService.GetRealmCulture(court))))
                    return ScenarioPlotRules.NotDesperate;
                Empire own = a.GetEmpire();
                List<Kingdom> members = own == null || own.CoreKingdom != a ? new List<Kingdom>()
                    : own.kingdoms_list.Where(member => member?.data != null && !member.isRekt() && member != a).ToList();
                if (!Submit(a, enemy ?? court, court, target, 0f)) return "没并进去";
                foreach (Kingdom member in members)
                {
                    if (member.isRekt() || member.GetEmpire() == target) continue;
                    DiplomacyActions.EndWars(member, court);
                    target.join(member, true, pForce: true);
                }
                return null;
            }
            case "heir":
            {
                // 立储：写在君主身故之前就是"立某人为君"(南京另立、海陵弑君)；人接到都城来，
                // 城一易手城里的人全归新国(原版 forceUnitsIntoThisKingdom)，留在陷落的京城就成了别国的人。
                // 写了城的是候补：人到那座城候着，排在继承名单最后(唐王在福州、桂王在广州，南京陷落不被一锅端)。
                // 名单优先于继承法(见 EmpireCraftKingdomBehCheckHeir.ScriptedHeir)：以前只记储君，君主一死按继承法重排，
                // 崇祯开局后添的皇子顶替了福王，南明没立起来
                Actor heir = People(step.person).FirstOrDefault(actor => actor.kingdom == a || a.IsEmpire() && actor.kingdom?.GetEmpire() == a.GetEmpire());
                if (heir == null) return "人不在本国";
                City post = CityOf(step.city);
                bool reserve = post != null && Holds(a, post);
                if (heir.kingdom != a) heir.joinKingdom(a);
                City stay = reserve ? post : a.capital;
                if (stay != null && heir.city != stay) heir.joinCity(stay);
                ScenarioPlotRules.AddHeir(a.GetOrCreate().scripted_heirs ??= new List<long>(), heir.getID(), reserve);
                if (!reserve) a.SetHeir(heir);
                return null;
            }
        }
        return "不认得的结果";
    }

    // 起兵割据：person 里第一位领头、其余为部将("安禄山、史思明")，据 city 与 cities 所列之城(第一座为治所)脱离 a 另立一国，
    // 国号照 name，与 a 所在的帝国(没有帝国就是 a)开战；b 写了的，新国记在这个编号下，之后的条件、结果用得上(洛阳称帝)。
    // 照拥兵自立(ThroneSuspicion.Rebel)：只带走还在 a 手里的城，帝国京城不带；领头的死了、投了别国，由治所的城主领头；
    // 驻在这些城的军、领头与部将统的军都改投新国，再按新国军制当场编满(范阳兵是节度使自己的兵)
    private static string Rebel(WorldMapEventStep step, Kingdom origin)
    {
        Empire empire = origin.GetEmpire();
        if (empire != null && (empire.isRekt() || empire.IsArchived())) empire = null;
        Kingdom target = empire?.CoreKingdom ?? origin;
        bool Ours(Kingdom kingdom) => kingdom != null && (kingdom == origin || empire != null && kingdom.GetEmpire() == empire);
        var seats = new List<City>();
        foreach (string id in new[] { step.city }.Concat(step.cities ?? new List<string>()))
        {
            City city = CityOf(id);
            if (city != null && Ours(city.kingdom) && city != target.capital && !seats.Contains(city)) seats.Add(city);
        }
        if (seats.Count == 0) return "城都不在本国";
        City seat = seats[0];
        List<Actor> people = (step.person ?? "").Split(new[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Person).Where(actor => actor != null && Ours(actor.kingdom) && actor != target.king).Distinct().ToList();
        Actor leader = people.FirstOrDefault(actor => actor.isAdult() && actor.CanFoundCivKingdom());
        if (leader == null && seat.leader is Actor holder && holder.isAlive() && holder.isAdult() && holder.CanFoundCivKingdom() &&
            Ours(holder.kingdom) && holder != target.king && MayLead(holder, seat)) leader = holder;
        if (leader == null) return "没人领头";
        // 领头、部将统的军，驻在这些城的军：先记下(改投之后按国家找不到了)
        var corps = new List<(Kingdom from, CorpsData corps)>();
        foreach ((Kingdom kingdom, CorpsData entry) in CorpsSystem.All().ToList())
        {
            if (!Ours(kingdom) || entry.militia || entry.order_id >= 0) continue;
            Actor general = entry.general_id < 0 ? null : World.world.units.get(entry.general_id);
            if (people.Contains(general) || seats.Contains(CorpsSystem.Home(entry))) corps.Add((kingdom, entry));
        }
        Kingdom rebel;
        if (leader.isKing())
        {
            // 帝国的成员国君领头：整个成员国随他起兵
            if (leader.kingdom == target || empire == null) return "领头的是君主";
            rebel = leader.kingdom;
            empire.leave(rebel, true, true);
        }
        else
        {
            Leave(leader);
            leader.joinCity(seat);
            rebel = seat.makeOwnKingdom(leader, pRebellion: true);
        }
        if (rebel == null) return "立国失败";
        if (empire != null && rebel.GetEmpire() == empire) empire.leave(rebel, true, true);
        rebel.RememberRebellionOrigin(empire);
        CultureService.ApplyLocalCulture(rebel, seat);
        rebel.ClearAdministrativeTitle();
        rebel.SetKingdomType(KingdomType.default_country_post);
        rebel.GetRegime()?.SetAllowDiplomacy(true);
        rebel.GetRegime()?.SetAllowArmy(true);
        rebel.GetRegime()?.SetAllowSupportCenterArmy(false);
        foreach (City city in seats.Skip(1))
            if (city.kingdom != rebel && Ours(city.kingdom) && !city.isRekt()) city.joinAnotherKingdom(rebel, pRebellion: true);
        // 国号：新国没有法理，不定的话按都城叫(幽州)；照剧本定国号
        string name = Text(step.name);
        if (!string.IsNullOrEmpty(name))
        {
            rebel.SetKingdomCoreName(name, name);
            rebel.SetCustomCountryName(name);
        }
        // 部将随领头改投新国(人在新国的城里的留在原城，否则到治所)
        foreach (Actor follower in people.Where(actor => actor != leader && actor.kingdom != rebel && !actor.isKing()))
        {
            Leave(follower);
            follower.joinKingdom(rebel);
            if (follower.city?.kingdom != rebel) follower.joinCity(seat);
        }
        foreach ((Kingdom from, CorpsData entry) in corps)
        {
            // 统这支军的不是起兵的人(朝廷派来的将领)：卸去兵权留在本朝，军照样改投
            Actor general = entry.general_id < 0 ? null : World.world.units.get(entry.general_id);
            if (general?.data != null && general.kingdom != rebel) CorpsSystem.LoseGeneral(general);
            City home = CorpsSystem.Home(entry);
            CorpsSystem.Transfer(entry, from, rebel, home != null && home.kingdom == rebel ? home : seat);
        }
        if (CorpsSystem.Enabled) CorpsSystem.Prefill(rebel);
        else RebellionStartupService.RaiseUprisingMilitia(rebel, 1f);
        foreach (Actor follower in people.Where(actor => actor != leader && actor.kingdom == rebel && !CorpsSystem.IsGeneral(actor)))
        {
            CorpsData free = CorpsSystem.Of(rebel).Where(entry => entry.general_id < 0 && !entry.militia)
                .OrderByDescending(entry => entry.strength).FirstOrDefault();
            if (free != null) CorpsSystem.AssignGeneral(rebel, free, follower);
        }
        War war = DiplomacyHelpers.wars.newWar(rebel, target, WarTypeLibrary.normal);
        if (war != null)
        {
            war.SetEmpireWarType(EmpireWarType.地方独立);
            // 战争名：两人以上取领头与第一位部将的姓(安史之乱)，否则称领头之名(某某之乱)
            Actor second = people.FirstOrDefault(actor => actor != leader && actor.kingdom == rebel);
            string a = Surname(leader), b = Surname(second);
            war.data.name = second != null && a.Length > 0 && b.Length > 0 && a != b
                ? string.Format(LM.Get("faction_war_pair"), a, b)
                : string.Format(LM.Get("faction_rebellion_war_name"), leader.getName());
        }
        if (!string.IsNullOrEmpty(step.b)) _state.realms[step.b] = rebel.data.id;
        _state.rebel_leaders.Add(new ScenarioRebelLeader { actor = leader.getID(), rebel = rebel.data.id, origin = target.data.id });
        // 流寇以战养战(用户 2026-11-09 "那大顺也应该稍微加强一点"：剧本起兵的顺、西打的是地方独立之战，不算起义军，
        // 养兵看国库，可新国年入是零，起兵三个月后军费系数掉到 0，五千兵剩三百，西安又被明军收复，大顺没建成国)
        if (step.roving && !(_state.roving ??= new List<long>()).Contains(rebel.data.id)) _state.roving.Add(rebel.data.id);
        // 雄主(用户 2026-11-09 "给李自成也加上开国雄主的特质")：起兵就带上，称帝以后生效
        if (step.hero && !leader.hasTrait(RulerTraitSystem.Founder)) leader.addTrait(RulerTraitSystem.Founder);
        string targetName = empire?.GetEmpireName() ?? target.GetKingdomName();
        string text = string.Format(LM.Get("scenario_rebel_history"), leader.getName(), seat.GetCityName(), rebel.GetKingdomName(), targetName);
        if (empire != null) EventRecorder.Record(empire, text, leader, rebel);
        else EventRecorder.Record(target, text, leader);
        BiographySystem.Note(leader, string.Format(LM.Get("scenario_rebel_bio"), seat.GetCityName(), rebel.GetKingdomName(), targetName), target.king);
        return null;
    }

    // 号召归附：入侵者兵锋越盛，同族的独立势力越容易奉表归附朝廷(明清鼎革：清军越逼越紧，各路势力越肯投南明)。
    // 每年结算，一年至多 RallyMaxPerYear 家；压力见 ConquestPressure，机会见 ScenarioPlotRules.RallyChance
    private const int RallyMaxPerYear = 2;

    private static void Rally()
    {
        if (_state.rally == null) return;
        Kingdom conqueror = World.world.kingdoms.get(_state.rally.conqueror), court = World.world.kingdoms.get(_state.rally.court);
        if (conqueror?.data == null || conqueror.isRekt() || court?.data == null || court.isRekt())
        {
            _state.rally = null;
            return;
        }
        Empire target = court.GetEmpire();
        if (target == null || target.isRekt() || target.IsArchived() || target.CoreKingdom != court) return;
        string culture = CultureService.GetRealmCulture(court);
        float pressure = ConquestPressure(conqueror, culture);
        if (pressure < ScenarioPlotRules.RallyMinPressure) return;
        int joined = 0;
        foreach (Kingdom kingdom in World.world.kingdoms.ToList())
        {
            if (joined >= RallyMaxPerYear) break;
            if (!RallyCandidate(kingdom, conqueror, court, target, culture, pressure)) continue;
            float chance = ScenarioPlotRules.RallyChance(pressure, WarReach.Reachable(conqueror, kingdom), kingdom.isInWarWith(conqueror));
            if (UnityEngine.Random.value >= chance) continue;
            if (Submit(kingdom, conqueror, court, target, pressure)) joined++;
        }
    }

    // 剧本点名的流寇(闯军、西军，起兵时写了 roving)：打仗时以战养战，照起义军养兵(见 CorpsSystem.Insurgent)；
    // 归附朝廷以后不算(见 Submit)
    internal static bool Roving(Kingdom kingdom) =>
        _state?.roving != null && kingdom?.data != null && _state.roving.Contains(kingdom.data.id);

    // 剧本正在为这个朝廷号召归附(甲申之变的 rally)：南明结成就不再另掷一遍(见 SouthernRetreatSystem)
    internal static bool RallyingFor(Kingdom court) =>
        _state?.rally != null && court?.data != null && _state.rally.court == court.data.id;

    // 压力：同族的城里入侵者(帝国连同成员)占了几成
    internal static float ConquestPressure(Kingdom conqueror, string culture)
    {
        int total = 0, held = 0;
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || !CultureService.SameKin(CultureService.GetMainCulture(city, initialize: false), culture)) continue;
            total++;
            if (Holds(conqueror, city)) held++;
        }
        return total == 0 ? 0f : (float)held / total;
    }

    // 独立势力：同族、自己做主(不是帝国的成员、附庸、殖民地)、够得着朝廷、国力比朝廷小；称了帝的也算(大顺、大西去帝号来投)；
    // 起兵的领头人还在世的不来(见 LedByRebelFounder)；要是从本朝出去的(反过本朝、或从本朝分出去)，又走投无路(见 Desperate)。
    // 以前朝鲜这样的外藩也"迫于清兵势"并进了明
    private static bool RallyCandidate(Kingdom kingdom, Kingdom conqueror, Kingdom court, Empire target, string culture, float pressure)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv() || !kingdom.hasKing() || (kingdom.cities?.Count ?? 0) == 0 ||
            kingdom == court || kingdom == conqueror || LedByRebelFounder(kingdom)) return false;
        Empire own = kingdom.GetEmpire();
        if (own != null && (own.CoreKingdom != kingdom || own == target || own == conqueror.GetEmpire())) return false;
        if (FeudalVassalService.GetOverlord(kingdom) != null || ColonySystem.IsColony(kingdom) || TransmigrantSystem.IsTransmigrant(kingdom)) return false;
        if (!CultureService.SameKin(CultureService.GetRealmCulture(kingdom), culture) || MembershipCooldown.BlocksJoin(kingdom, target)) return false;
        if (!kingdom.HasRebelledAgainst(target) && kingdom.GetOrCreate().left_empire_id != target.id) return false;
        return WarSituation.Power(kingdom) < WarSituation.Power(court) && WarReach.Reachable(court, kingdom) &&
               Desperate(kingdom, conqueror, pressure);
    }

    // 一国(帝国连同成员)现有几城，顺手记下见过的最多城数
    private static int RealmCities(Kingdom kingdom)
    {
        Empire empire = kingdom.GetEmpire();
        int cities = empire != null && empire.CoreKingdom == kingdom ? empire.countCities() : kingdom.cities?.Count ?? 0;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (cities > data.peak_realm_cities) data.peak_realm_cities = cities;
        return cities;
    }

    // 走投无路(见 ScenarioPlotRules.Desperate)：正和入侵者交战、兵力差两倍以上，入侵者已打到南方(占同族之城一半)或自己丢了一半的城。
    // 也给南明来投用(见 SouthernRetreatSystem)
    internal static bool Desperate(Kingdom kingdom, Kingdom invader, float pressure)
    {
        if (kingdom?.data == null || kingdom.isRekt() || invader?.data == null || invader.isRekt()) return false;
        Kingdom realm = FrontSystem.Realm(invader) ?? invader;
        int cities = RealmCities(kingdom);
        return ScenarioPlotRules.Desperate(FrontSystem.Hostile(kingdom, realm), WarSituation.Power(realm), WarSituation.Power(kingdom),
            pressure, cities, kingdom.GetOrCreate().peak_realm_cities);
    }

    // 每年记下剧本各国见过的最多城数(顺、西打下北方以后再丢一半，才算被逼得没退路)
    private static void NotePeaks()
    {
        foreach (long id in _state.realms.Values.Distinct().ToList())
        {
            Kingdom kingdom = World.world.kingdoms.get(id);
            if (kingdom?.data != null && !kingdom.isRekt()) RealmCities(kingdom);
        }
    }

    // 起兵的领头人还在世、还领着这个国(李自成、张献忠)：不会奉表归附。大顺余部、大西军都是领头人死后才联明抗清
    // (用户 2026-11-10 "清军才拿下北京而已, 顺就投了?")；号召归附、南明来投都看它
    internal static bool LedByRebelFounder(Kingdom kingdom)
    {
        if (_state?.rebel_leaders == null || kingdom?.data == null) return false;
        foreach (ScenarioRebelLeader entry in _state.rebel_leaders)
        {
            if (entry.rebel != kingdom.data.id) continue;
            Actor leader = World.world.units.get(entry.actor);
            if (leader?.data != null && !leader.isRekt() && leader.isAlive() && leader.kingdom == kingdom) return true;
        }
        return false;
    }

    // 也给南明结成用(见 SouthernRetreatSystem)
    internal static bool Submit(Kingdom kingdom, Kingdom conqueror, Kingdom court, Empire target, float pressure)
    {
        // 和朝廷打着的先罢兵；称了帝的先去帝号，手下的成员各自散去
        foreach (Kingdom member in target.kingdoms_list.ToList())
            if (member?.data != null && !member.isRekt()) DiplomacyActions.EndWars(member, kingdom);
        Empire own = kingdom.GetEmpire();
        if (own != null) ModClass.EMPIRE_MANAGER.dissolveEmpire(own);
        // 反过朝廷的也算：大敌当前，既往不咎
        target.join(kingdom, true, pForce: true, pLegitimacyTransfer: true);
        if (kingdom.GetEmpire() != target) return false;
        // 撤了原来的国号，在这个帝国里按都城称呼(见 KingdomExtension.GetAutomaticKingdomName)
        kingdom.GetOrCreate().submitted_empire_id = target.id;
        // 归了朝廷，不再是以战养战的流寇
        _state?.roving?.Remove(kingdom.data.id);
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        string text = string.Format(LM.Get("scenario_rally_history"), kingdom.GetKingdomName(), conqueror.GetKingdomName(), target.GetEmpireName());
        EventRecorder.Record(target, text, kingdom.king, kingdom);
        TranslateHelper.LogEventMessage(text, kingdom);
        LogService.LogInfo($"[EmpireCraft][剧情事件] 号召归附：压力 {pressure:P0}，{kingdom.GetKingdomName()} 归附 {target.GetEmpireName()}");
        return true;
    }

    // 起兵的领头人落到朝廷手里怎么办(见 ScenarioPlotRules.FateOfRebelLeader)；名单就几个人，每帧看也花不了什么
    private static void WatchRebelLeaders()
    {
        List<ScenarioRebelLeader> leaders = _state.rebel_leaders;
        for (int i = leaders.Count - 1; i >= 0; i--)
        {
            ScenarioRebelLeader entry = leaders[i];
            Actor leader = World.world.units.get(entry.actor);
            if (leader?.data == null || leader.isRekt() || !leader.isAlive())
            {
                leaders.RemoveAt(i);
                continue;
            }
            Kingdom rebel = World.world.kingdoms.get(entry.rebel), origin = World.world.kingdoms.get(entry.origin);
            bool rebelAlive = rebel?.data != null && !rebel.isRekt();
            bool withOrigin = origin?.data != null && !origin.isRekt() && leader.kingdom != null &&
                              (leader.kingdom == origin || origin.GetEmpire() != null && leader.kingdom.GetEmpire() == origin.GetEmpire());
            switch (ScenarioPlotRules.FateOfRebelLeader(rebelAlive && leader.kingdom == rebel, withOrigin, rebelAlive, rebelAlive && rebel.capital != null))
            {
                case ScenarioPlotRules.RebelFate.Return:
                    Leave(leader);
                    leader.joinKingdom(rebel);
                    leader.joinCity(rebel.capital);
                    if (!rebel.hasKing()) rebel.setKing(leader);
                    LogService.LogInfo($"[EmpireCraft][剧情事件] {leader.getName()} 城破后逃回 {rebel.GetKingdomName()}");
                    break;
                case ScenarioPlotRules.RebelFate.Execute:
                    _scriptedDeath = true;
                    try
                    {
                        BiographySystem.Kill(leader, LM.Get("scenario_rebel_executed"));
                    }
                    finally
                    {
                        _scriptedDeath = false;
                    }
                    leaders.RemoveAt(i);
                    break;
            }
        }
    }

    // 点名的人没了，由城主代领：华夏不立女主
    private static bool MayLead(Actor actor, City seat) =>
        !actor.isSexFemale() || InstitutionSystem.GetCultureLine(CultureService.GetMainCulture(seat, initialize: false)) != "Huaxia";

    // 结果点名的城里还在 a 手里的
    private static List<City> StepCities(WorldMapEventStep step, Kingdom a) =>
        new[] { step.city }.Concat(step.cities ?? new List<string>()).Select(CityOf)
            .Where(city => city != null && Holds(a, city)).Distinct().ToList();

    // 地区(王国法理)里的城；没写或对不上的为空
    private static IEnumerable<City> RegionCities(string region)
    {
        KingdomTitle title = Title(region);
        return title == null ? Enumerable.Empty<City>() : title.getCities().Where(city => city?.data != null && !city.isRekt()).ToList();
    }

    // 扶植傀儡政权(伪满、蒙疆、华北临时政府、汪伪)：a(占领者一方)手里的这些城(city、cities，或 region 地区里的)交给新立的 b，
    // 立为 a 的傀儡(见 PuppetStateSystem)。person 里头一位还活着的成年人领头，可以是别国的人(投敌的汪精卫，连同部将改投)，
    // 都找不到由治所城主领头；name 国号；culture 写了改新国文化。a 的京城不给
    private static string Puppet(WorldMapEventStep step, Kingdom master)
    {
        Kingdom core = master.GetEmpire()?.CoreKingdom ?? master;
        List<City> seats = new[] { step.city }.Concat(step.cities ?? new List<string>()).Select(CityOf).Concat(RegionCities(step.region))
            // 别的傀儡手里的城也能拿来(汪伪国民政府接收维新政府的南京)
            .Where(city => city != null && Holds(master, city) && city != core.capital)
            .Distinct().ToList();
        if (seats.Count == 0) return "城都不在本国";
        City seat = seats[0];
        List<Actor> people = People(step.person).Where(actor => !actor.isKing()).ToList();
        Actor leader = people.FirstOrDefault(actor => actor.isAdult() && actor.CanFoundCivKingdom());
        if (leader == null && seat.leader is Actor holder && holder.isAlive() && holder.isAdult() && holder.CanFoundCivKingdom() &&
            !holder.isKing() && MayLead(holder, seat)) leader = holder;
        if (leader == null) return "没人领头";
        string name = Text(step.name);
        Kingdom puppet = PuppetStateSystem.Found(master, seat, seats.Where(city => Holds(master, city)), leader, name, step.culture,
            people.Where(actor => actor != leader && !actor.isKing()));
        if (puppet == null) return "立国失败";
        if (!string.IsNullOrEmpty(step.b)) _state.realms[step.b] = puppet.data.id;
        string text = string.Format(LM.Get("scenario_puppet_history"), master.GetKingdomName(), leader.getName(), seat.GetCityName(),
            puppet.GetKingdomName());
        EventRecorder.Record(master, text, leader);
        BiographySystem.Note(leader, string.Format(LM.Get("scenario_puppet_bio"), seat.GetCityName(), puppet.GetKingdomName(),
            master.GetKingdomName()), master.king);
        return null;
    }

    // 剧本人物的名单：顿号、逗号隔开，找得到又还活着的，按写的顺序
    private static List<Actor> People(string names) =>
        (names ?? "").Split(new[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(Person).Where(actor => actor != null).Distinct().ToList();

    // 献城改投：city、cities 里还在 a(帝国连同成员国)手里的城归 b，person 里的人连同所统之军投奔 b，驻在这些城的军也跟着走。
    // 只写 person 不写城就是单纯投奔。都城也能给，要留朝廷的先写一条 move_capital 迁走(甲申城破、南京监国)。
    // 城里的人全归 b：不想让谁跟着走，先把事情做完再献城
    // 写了 all 的是 a 这一方手里的全部城(傀儡覆灭、战败交还)；写了 region 的另加那片地区里 a 这一方手里的城(1945 交还朝鲜、东北)。
    // a 这一方连同它的殖民地、傀儡(日本连同朝鲜总督府、台湾总督府，见 Controls)
    private static string Cede(WorldMapEventStep step, Kingdom origin)
    {
        Kingdom to = Realm(step.b);
        if (to == null) return "对方不在";
        bool Ours(Kingdom kingdom) => Controls(origin, kingdom);
        if (Ours(to)) return "本是一家";
        List<City> cities = new[] { step.city }.Concat(step.cities ?? new List<string>()).Select(CityOf)
            .Concat(RegionCities(step.region))
            .Concat(step.all ? World.world.cities.list.Where(city => city?.data != null && !city.isRekt()) : Enumerable.Empty<City>())
            .Where(city => city != null && Ours(city.kingdom)).Distinct().ToList();
        List<Actor> people = People(step.person).Where(actor => Ours(actor.kingdom) && !actor.isKing()).ToList();
        if (cities.Count == 0 && people.Count == 0) return "城和人都不在本国";
        // 要走的军先记下：改投之后按国家找不到了
        var corps = new List<(Kingdom from, CorpsData corps)>();
        foreach ((Kingdom kingdom, CorpsData entry) in CorpsSystem.All().ToList())
        {
            if (!Ours(kingdom) || entry.militia || entry.order_id >= 0) continue;
            Actor general = entry.general_id < 0 ? null : World.world.units.get(entry.general_id);
            if (people.Contains(general) || cities.Contains(CorpsSystem.Home(entry))) corps.Add((kingdom, entry));
        }
        foreach (City city in cities) city.joinAnotherKingdom(to);
        City seat = cities.FirstOrDefault(city => city.kingdom == to) ?? to.capital;
        foreach (Actor person in people)
        {
            // 人本来就在献出去的城里的留在原城，空着的城主之位归他(祖大寿仍守锦州)
            City home = person.city != null && cities.Contains(person.city) && person.city.kingdom == to ? person.city : seat;
            Leave(person);
            if (person.kingdom != to) person.joinKingdom(to);
            if (home != null && person.city != home) person.joinCity(home);
            if (home != null && cities.Contains(home) && home.leader == null)
            {
                home.GetOffice()?.SetActor(person);
                if (home.leader != person) home.setLeader(person, true);
            }
            BiographySystem.Note(person, string.Format(LM.Get("scenario_cede_bio"), to.GetKingdomName()), origin.king);
        }
        foreach ((Kingdom from, CorpsData entry) in corps)
        {
            Actor general = entry.general_id < 0 ? null : World.world.units.get(entry.general_id);
            if (general?.data != null && general.kingdom != to) CorpsSystem.LoseGeneral(general);
            City home = CorpsSystem.Home(entry);
            City station = home != null && home.kingdom == to ? home : seat;
            if (station != null) CorpsSystem.Transfer(entry, from, to, station);
        }
        return null;
    }

    // 人物身故：person 里还活着的按写的顺序走(储君写在君主前面，君主一死就轮到后面的人继位)；走正常的死亡与继位流程
    private static string Dies(WorldMapEventStep step)
    {
        List<Actor> people = People(step.person);
        if (people.Count == 0) return "人都不在";
        _scriptedDeath = true;
        try
        {
            foreach (Actor person in people) BiographySystem.Kill(person, Text(step.name));
        }
        finally
        {
            _scriptedDeath = false;
        }
        return null;
    }

    // 卸去本国官职、城主之位(改投新国前)
    private static void Leave(Actor actor)
    {
        if (actor.IsOnOffice())
        {
            actor.GetOffice()?.RemoveActor();
            actor.EndOffice();
        }
        if (actor.isCityLeader() && actor.city != null) actor.city.removeLeader();
    }

    private static string Surname(Actor actor)
    {
        if (actor?.data == null) return "";
        string surname = actor.GetModName()?.familyName;
        if (string.IsNullOrWhiteSpace(surname)) surname = SurnameInheritanceSystem.SurnameOf(actor.GetPersonalIdentity());
        return string.IsNullOrWhiteSpace(surname) ? "" : surname.Trim();
    }

    // 事件文字、国号可写"简体|英文|繁体"(预设剧本)，按界面语言取；自定剧本里写的一种文字照用
    private static string Text(string value) => WorldMapData.PickParts((value ?? "").Trim()).Trim();

    // 称帝定国号：只定国号，不改法理的名字(以前连王国法理一并改成"大顺"，法理图层上冒出一块"大顺")；
    // 写成"大某"的，"大"是美号(同开局的大明，见 RealmNamingSystem.GrandPrefix)，国号本名是"某"
    private static void Proclaim(Empire empire, Kingdom kingdom, string parts)
    {
        bool grand = ScenarioPlotRules.SplitGrand(parts, out string plain);
        if (grand) empire.data.directPre = RealmNamingSystem.GrandPrefix;
        empire.data.name_source = grand ? RealmNamingSystem.SourceGrandName : RealmNamingSystem.SourceScenario;
        empire.data.name_place_title = "";
        empire.data.name_blocked_core = "";
        string name = Text(plain);
        // 起兵时剧本给的国号是自定名(新国没有法理，不定的话按都城叫)，自定名不走模组起名，称帝后只显示光秃秃的"顺""西"
        // (用户 2026-11-10 "为什么顺和西不走模组的命名方式""不是大顺朝和大西朝吗")：称帝改由帝国起名(美号大 + 顺 + 朝)
        ClearScriptedCustomName(kingdom, name);
        empire.SetEmpireName(name);
        kingdom.SetKingdomName(name);
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
    }

    // 剧本起兵定的自定名就是这个国号时撤掉(玩家另起的名字不动)
    private static void ClearScriptedCustomName(Kingdom kingdom, string name)
    {
        if (kingdom?.data == null || string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(kingdom.GetCustomCountrySuffix())) return;
        if (string.Equals(kingdom.GetCustomCountryName(), name, StringComparison.Ordinal)) kingdom.SetCustomCountryName("");
    }

    // 旧档：已经称帝的顺、西还挂着起兵时的自定名，读档后第一次结算时撤掉
    private static void RepairProclaimedNames()
    {
        // 只看剧本起兵立的国(rebel 步骤的 b)
        foreach (string key in _state.events.Where(item => item?.then != null).SelectMany(item => item.then)
                     .Where(step => step?.kind == "rebel" && !string.IsNullOrEmpty(step.b)).Select(step => step.b).Distinct().ToList())
        {
            if (!_state.realms.TryGetValue(key, out long id)) continue;
            Kingdom kingdom = World.world.kingdoms.get(id);
            Empire empire = kingdom?.data == null || kingdom.isRekt() ? null : kingdom.GetEmpire();
            if (empire?.data == null || empire.CoreKingdom != kingdom) continue;
            if (empire.data.name_source != RealmNamingSystem.SourceGrandName && empire.data.name_source != RealmNamingSystem.SourceScenario) continue;
            string custom = kingdom.GetCustomCountryName();
            if (string.IsNullOrWhiteSpace(custom)) continue;
            ClearScriptedCustomName(kingdom, custom);
            LogService.LogInfo($"[EmpireCraft][剧情事件] {empire.GetEmpireFullName()} 撤去起兵时的自定国号 {custom}");
        }
    }

    // 改国号：帝国改帝号，王国改主法理名(国名照主法理定)
    private static void Rename(Kingdom kingdom, string name)
    {
        Empire empire = kingdom.GetEmpire();
        if (empire != null && empire.CoreKingdom == kingdom)
        {
            empire.data.name_source = RealmNamingSystem.SourceScenario;
            empire.SetEmpireName(name);
        }
        KingdomTitle main = kingdom.GetMainTitle();
        if (main?.data != null) main.data.name = name;
        kingdom.SetKingdomName(name);
        // 剧本锁定了国号的(近代国家、傀儡、流亡政府，见 ScenarioModernPolitics)：锁定的国号一并改
        if (!string.IsNullOrWhiteSpace(kingdom.GetCustomCountryName())) kingdom.SetCustomCountryName(name);
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
    }

    #endregion
}

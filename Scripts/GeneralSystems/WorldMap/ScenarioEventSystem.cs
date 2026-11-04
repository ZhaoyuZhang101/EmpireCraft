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
}

// 剧情事件(剧本编辑器第四期，见 Docs/plan-scenario-editor.md；用户 10-30 "剧情事件：要，选项拼搭")：
//   自定剧本开局时记下事件表与剧本里的国、城、地区各对上了哪个国家、城、王国法理(随存档保存)，之后每年查一次——
//   开局满 after 年、条件全部满足就触发：照结果一条条做，写进史书、上屏幕；不可重复的只触发一次。
//   条件：某国据有某城、某国据有某王国法理过半、某国已亡、某国尚在、某国已称帝、两国交战中、某国王朝积弊高于某数；
//   结果：称帝(可定国号)、改国号、迁都、两国之间加一条关系(交战、停战、结盟、和亲、通好、保护、朝贡、附庸、关税同盟、旧谊、宿怨，
//     走 DiplomacyActions.ApplyScenario 同一套判定)、改文化、采纳华夏制度(入主中原，国号照写的定，见 CompositeEmpireService.ScriptedConquestName)、
//     起兵割据(剧本点名的人据城另立一国、与帝国开战，见 Rebel；用户 10-30 "唐朝剧本的安史之乱呢")、
//     献城改投(某城易手、某人连军投奔，见 Cede；明末、宋金剧本的甲申城破、祖大寿降清)、人物身故(见 Dies)、立储。
//   国家没了、城没了的条件算不满足；结果做不了的跳过、记一行日志。
public static class ScenarioEventSystem
{
    public static readonly string[] ConditionKinds = { "holds_city", "holds_region", "gone", "alive", "empire", "at_war", "strain_above" };
    public static readonly string[] EffectKinds = { "relation", "proclaim", "rename", "move_capital", "culture", "adopt_huaxia", "rebel", "cede", "dies", "heir",
        "banner_city", "banner_enclosure", "banner_law" };

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
        _state = saved;
        RebuildPlot();
    }

    public static ScenarioEventSave Save() => Active ? _state : null;

    public static void ResetWorldState()
    {
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
                if (step.kind is "rebel" or "cede")
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
        double now = World.world.getCurWorldTime();
        if (_state.last_check >= 0d && now >= _state.last_check && Date.getYearsSince(_state.last_check) < 1) return;
        _state.last_check = now;
        int years = _state.started < 0d ? 0 : Math.Max(0, Date.getYearsSince(_state.started));
        for (int i = 0; i < _state.events.Count; i++)
        {
            WorldMapEvent item = _state.events[i];
            if (item == null || years < item.after || !item.repeat && _state.fired.Contains(i)) continue;
            try
            {
                if (!(item.when ?? new List<WorldMapEventStep>()).All(Holds)) continue;
                Fire(item);
                if (!_state.fired.Contains(i)) _state.fired.Add(i);
                RebuildPlot();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][剧情事件] {item.name} 出错: {exception.Message}");
            }
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

    // 城在不在这国手里(帝国连同它的成员国都算)
    private static bool Holds(Kingdom kingdom, City city)
    {
        if (kingdom == null || city?.kingdom == null) return false;
        if (city.kingdom == kingdom) return true;
        Empire empire = kingdom.GetEmpire();
        return empire != null && kingdom.IsEmpire() && city.kingdom.GetEmpire() == empire;
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
        }
        return false;
    }

    #endregion

    #region 结果

    private static void Fire(WorldMapEvent item)
    {
        var done = new List<string>();
        Kingdom first = null;
        foreach (WorldMapEventStep step in item.then ?? new List<WorldMapEventStep>())
        {
            Kingdom a = Realm(step.a);
            first ??= a;
            string failed = Apply(step, a);
            if (failed != null) LogService.LogInfo($"[EmpireCraft][剧情事件] {item.name}：{step.kind} 没做成({failed})");
            else done.Add(step.kind);
        }
        // 一条结果都没做成的(点名的人早死了、城早丢了)不上屏幕，免得史书写了没发生的事
        bool happened = done.Count > 0 || (item.then?.Count ?? 0) == 0;
        string text = Text(string.IsNullOrWhiteSpace(item.text) ? item.name : item.text);
        if (happened && !string.IsNullOrWhiteSpace(text))
        {
            string line = string.Format(LM.Get("scenario_event_fired"), text);
            if (first != null) TranslateHelper.LogMajor(line, first);
            else WorldTip.showNow(line, false, "top", 5f);
        }
        LogService.LogInfo($"[EmpireCraft][剧情事件] 触发 {item.name}：{string.Join("、", done)}");
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
                return DiplomacyActions.ApplyScenario(step.relation, a, b, goal, step.value);
            }
            case "proclaim":
            {
                if (!a.IsEmpire() && !a.IsInEmpire())
                    ModClass.EMPIRE_MANAGER?.NewEmpire(a, allowCultureRival: true, forceNewCore: true);
                Empire empire = a.GetEmpire();
                if (empire == null || empire.CoreKingdom != a) return "称不了帝";
                if (!string.IsNullOrWhiteSpace(step.name)) Rename(a, Text(step.name));
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
            case "heir":
            {
                // 立储：写在君主身故之前就是"立某人为君"(南京另立、海陵弑君)；人接到都城来，
                // 城一易手城里的人全归新国(原版 forceUnitsIntoThisKingdom)，留在陷落的京城就成了别国的人
                Actor heir = People(step.person).FirstOrDefault(actor => actor.kingdom == a || a.IsEmpire() && actor.kingdom?.GetEmpire() == a.GetEmpire());
                if (heir == null) return "人不在本国";
                if (heir.kingdom != a) heir.joinKingdom(a);
                if (a.capital != null && heir.city != a.capital) heir.joinCity(a.capital);
                a.SetHeir(heir);
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
        string targetName = empire?.GetEmpireName() ?? target.GetKingdomName();
        string text = string.Format(LM.Get("scenario_rebel_history"), leader.getName(), seat.GetCityName(), rebel.GetKingdomName(), targetName);
        if (empire != null) EventRecorder.Record(empire, text, leader, rebel);
        else EventRecorder.Record(target, text, leader);
        BiographySystem.Note(leader, string.Format(LM.Get("scenario_rebel_bio"), seat.GetCityName(), rebel.GetKingdomName(), targetName), target.king);
        return null;
    }

    // 点名的人没了，由城主代领：华夏不立女主
    private static bool MayLead(Actor actor, City seat) =>
        !actor.isSexFemale() || InstitutionSystem.GetCultureLine(CultureService.GetMainCulture(seat, initialize: false)) != "Huaxia";

    // 结果点名的城里还在 a 手里的
    private static List<City> StepCities(WorldMapEventStep step, Kingdom a) =>
        new[] { step.city }.Concat(step.cities ?? new List<string>()).Select(CityOf)
            .Where(city => city != null && Holds(a, city)).Distinct().ToList();

    // 剧本人物的名单：顿号、逗号隔开，找得到又还活着的，按写的顺序
    private static List<Actor> People(string names) =>
        (names ?? "").Split(new[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(Person).Where(actor => actor != null).Distinct().ToList();

    // 献城改投：city、cities 里还在 a(帝国连同成员国)手里的城归 b，person 里的人连同所统之军投奔 b，驻在这些城的军也跟着走。
    // 只写 person 不写城就是单纯投奔。都城也能给，要留朝廷的先写一条 move_capital 迁走(甲申城破、南京监国)。
    // 城里的人全归 b：不想让谁跟着走，先把事情做完再献城
    private static string Cede(WorldMapEventStep step, Kingdom origin)
    {
        Kingdom to = Realm(step.b);
        if (to == null) return "对方不在";
        Empire empire = origin.GetEmpire();
        if (empire != null && (empire.isRekt() || empire.IsArchived())) empire = null;
        bool Ours(Kingdom kingdom) => kingdom != null && (kingdom == origin || empire != null && kingdom.GetEmpire() == empire);
        if (Ours(to)) return "本是一家";
        List<City> cities = new[] { step.city }.Concat(step.cities ?? new List<string>()).Select(CityOf)
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

    // 改国号：帝国改帝号，王国改主法理名(国名照主法理定)
    private static void Rename(Kingdom kingdom, string name)
    {
        Empire empire = kingdom.GetEmpire();
        if (empire != null && empire.CoreKingdom == kingdom) empire.SetEmpireName(name);
        KingdomTitle main = kingdom.GetMainTitle();
        if (main?.data != null) main.data.name = name;
        kingdom.SetKingdomName(name);
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
    }

    #endregion
}

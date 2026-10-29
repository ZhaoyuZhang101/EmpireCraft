using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧情事件的存档(见 ScenarioEventSystem)：事件表、剧本里的国/城/地区编号对上的国家/城/王国法理编号、开局时刻、触发过的事件
public sealed class ScenarioEventSave
{
    public List<WorldMapEvent> events = new();
    public Dictionary<string, long> realms = new();
    public Dictionary<string, long> cities = new();
    public Dictionary<string, long> titles = new();
    public double started = -1d;
    public double last_check = -1d;
    public int era_year;
    public List<int> fired = new();
}

// 剧情事件(剧本编辑器第四期，见 Docs/plan-scenario-editor.md；用户 10-30 "剧情事件：要，选项拼搭")：
//   自定剧本开局时记下事件表与剧本里的国、城、地区各对上了哪个国家、城、王国法理(随存档保存)，之后每年查一次——
//   开局满 after 年、条件全部满足就触发：照结果一条条做，写进史书、上屏幕；不可重复的只触发一次。
//   条件：某国据有某城、某国据有某王国法理过半、某国已亡、某国已称帝、两国交战中、某国王朝积弊高于某数；
//   结果：称帝(可定国号)、改国号、迁都、两国之间加一条关系(交战、停战、结盟、和亲、通好、保护、朝贡、附庸、关税同盟、旧谊、宿怨，
//     走 DiplomacyActions.ApplyScenario 同一套判定)、改文化、采纳华夏制度(入主中原，国号照写的定，见 CompositeEmpireService.ScriptedConquestName)。
//   国家没了、城没了的条件算不满足；结果做不了的跳过、记一行日志。
public static class ScenarioEventSystem
{
    public static readonly string[] ConditionKinds = { "holds_city", "holds_region", "gone", "empire", "at_war", "strain_above" };
    public static readonly string[] EffectKinds = { "relation", "proclaim", "rename", "move_capital", "culture", "adopt_huaxia" };

    private static ScenarioEventSave _state;

    public static bool Active => _state?.events?.Count > 0;

    public static void Begin(WorldMapEra era, Dictionary<string, long> realms, Dictionary<string, long> cities, Dictionary<string, long> titles)
    {
        _state = null;
        if (era?.events == null || era.events.Count == 0 || World.world == null) return;
        _state = new ScenarioEventSave
        {
            events = era.events.Select(Clone).ToList(), realms = realms ?? new(), cities = cities ?? new(), titles = titles ?? new(),
            started = World.world.getCurWorldTime(), last_check = World.world.getCurWorldTime(), era_year = era.year
        };
        LogService.LogInfo($"[EmpireCraft][剧情事件] {era.Name}：{_state.events.Count} 条事件");
    }

    public static void Load(ScenarioEventSave saved)
    {
        ResetWorldState();
        if (saved?.events == null || saved.events.Count == 0) return;
        saved.realms ??= new Dictionary<string, long>();
        saved.cities ??= new Dictionary<string, long>();
        saved.titles ??= new Dictionary<string, long>();
        saved.fired ??= new List<int>();
        _state = saved;
    }

    public static ScenarioEventSave Save() => Active ? _state : null;

    public static void ResetWorldState() => _state = null;

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
        string text = string.IsNullOrWhiteSpace(item.text) ? item.name : item.text;
        if (!string.IsNullOrWhiteSpace(text))
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
                if (!string.IsNullOrWhiteSpace(step.name)) Rename(a, step.name.Trim());
                return null;
            }
            case "rename":
                if (string.IsNullOrWhiteSpace(step.name)) return "没写国号";
                Rename(a, step.name.Trim());
                return null;
            case "move_capital":
            {
                City city = CityOf(step.city);
                if (city == null || city.kingdom != a) return "城不在本国";
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
                extra.scripted_conquest_name = string.IsNullOrWhiteSpace(step.name) ? a.GetKingdomName() : step.name.Trim();
                extra.scripted_conquest_city_id = a.capital.data.id;
                return null;
            }
        }
        return "不认得的结果";
    }

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

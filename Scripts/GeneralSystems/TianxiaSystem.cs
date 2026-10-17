using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 天下(帝国核心之上的一层，仿 CK3 的霸权，用户 2026-10-17 定)：
//   · 一个帝国核心自成一个天下；玩家可以用神力把几个帝国核心并成一个联合天下(主核心记名号与天命)。
//   · 自动并分(用户："可以给个天下id，玩家可以在文化配置里面标记上，这样就可以自动了")：文化配置的 tianxia 标识
//     相同的相邻帝国法理每年自动并成一个天下(比如华夏、中国、山海都标 Huaxia)；联合天下里文化标识已经不同的法理自动退出。
//     只并文化配置里明写了同一标识(用户："如果标注了同一个id的文化可以")、同一块陆地上领地接壤的(不跨大陆、不隔海)——没写标识的文化一个帝国法理一个天下，
//     不论开不开固定法理，都不会自动滚成横跨几个大陆、几种文化、几个法理的天下，古典时代尤其不该有(固定法理开着时帝国法理也不再扩张)。
//     玩家用神力动过的法理不再自动并分。
//     统一战争在天下里打(见 UnificationSystem.Survey)：天下 = 这些核心的法理城市，不再按文化圈临时圈出来，
//     城市文化变了、被外族占了，天下也还是那个天下；还没有帝国核心的地方照旧按文化连片。
//   · 天命：据有天下七成五以上的国家受天命、为天下共主(外族入主也算)，正统 +8；
//     天下共主地盘跌破一半、或别家占得比它多、或国家亡了，天命去之，天下无主——
//     天下仍在、头衔悬空，原朝廷不解体，只是失去共主身份、成为群雄之一，正统 -5；谁再据有七成五谁受天命。
//   · 天命周期三段(仿 CK3 王朝周期，与历史周期律合用)：
//       - 治世：有共主，据有七成五以上、正统不低于三十、积弊不到六十——共主正统 +8；
//       - 动荡：有共主，但地盘跌到七成五以下、正统低于三十或积弊六十以上(天命动摇)——共主正统只 +2；
//       - 乱世：天下无主，群雄逐鹿。
//     有共主的天下(治世、动荡)里别家不能起统一战争问鼎，只有乱世才有统一战争(见 UnificationSystem)。
//   · 天下朝贡：治世时天下以外、与共主接壤、国力不到共主四分之一的国家每年一成五机会来朝(走原有朝贡，
//     见 KingdomExtension.JoinTakenAlliance，朝贡制度的要求照旧)，不论文化；来朝两国以上共主正统 +2(万邦来朝)；
//     动荡时来朝诸国每年一成散去，失了天命一齐散去。
public static class TianxiaSystem
{
    public const float ClaimShare = 0.75f, KeepShare = 0.5f, SealClaimShare = 0.6f;
    private const int HolderLegitimacy = 8, ShakenLegitimacy = 2, LostLegitimacy = -5;
    private const int ShakenMandate = 30;
    private const float TributeChance = 0.15f, TributeLeaveChance = 0.1f, TributePowerRatio = 0.25f;
    private const int TributeLegitimacy = 2, TributeLegitimacyMin = 2;
    private const float ShakenPressure = 60f;

    public const int Peace = 0, Unrest = 1, Chaos = 2;

    // 天命周期：0 治世、1 动荡、2 乱世(年度结算定下，见 Settle)
    public static int Phase(EmpireCore lead) => lead == null ? Chaos : lead.tianxia_phase < 0
        ? Holder(lead) == null ? Chaos : Peace
        : lead.tianxia_phase;

    public static string PhaseName(int phase) => LM.Get("tianxia_phase_" + phase);

    // 有共主的天下里，别家不能问鼎(起统一战争)
    public static bool BarsContender(EmpireCore lead, Kingdom contender)
    {
        Kingdom holder = Holder(lead);
        return holder != null && holder != contender;
    }

    #region 结构

    // 天下的主核心：并入联合天下的核心指向主核心；军阀时期另立政府的临时子核心归原核心的天下
    public static EmpireCore Lead(EmpireCore core)
    {
        if (core == null) return null;
        if (core.warlord_parent_core_id >= 0 && EmpireCoreManager.Get(core.warlord_parent_core_id) is EmpireCore parent && parent != core)
            core = parent;
        if (core.tianxia_lead_id >= 0 && core.tianxia_lead_id != core.id &&
            EmpireCoreManager.Get(core.tianxia_lead_id) is EmpireCore lead && lead.tianxia_lead_id < 0)
            return lead;
        return core;
    }

    public static EmpireCore LeadOf(City city) => city == null ? null : Lead(city.GetEmpireCore());

    // 天下里的各帝国核心(主核心在前；不含临时子核心)
    public static List<EmpireCore> Members(EmpireCore lead)
    {
        var result = new List<EmpireCore>();
        if (lead == null) return result;
        result.Add(lead);
        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values)
            if (core != null && core != lead && core.warlord_parent_core_id < 0 && Lead(core) == lead) result.Add(core);
        return result;
    }

    public static bool IsJoint(EmpireCore lead) => Members(lead).Count > 1;

    // 所有天下的主核心(不含临时子核心)
    public static IEnumerable<EmpireCore> Leads() =>
        EmpireCoreManager.EmpireCores.Values.Where(core => core != null && core.warlord_parent_core_id < 0 && Lead(core) == core);

    // 天下里的城(图层每帧要用：一帧只按城扫一遍)
    private static int _citiesFrame = -1;
    private static object _citiesWorld;
    private static readonly Dictionary<long, List<City>> CitiesByLead = new();

    public static List<City> Cities(EmpireCore lead)
    {
        if (lead == null || World.world?.cities == null) return new List<City>();
        if (_citiesFrame != UnityEngine.Time.frameCount || !ReferenceEquals(_citiesWorld, World.world))
        {
            _citiesFrame = UnityEngine.Time.frameCount;
            _citiesWorld = World.world;
            CitiesByLead.Clear();
            foreach (City city in World.world.cities.list)
            {
                if (city?.data == null || city.isRekt()) continue;
                EmpireCore owner = LeadOf(city);
                if (owner == null) continue;
                if (!CitiesByLead.TryGetValue(owner.id, out List<City> list)) CitiesByLead[owner.id] = list = new List<City>();
                list.Add(city);
            }
        }
        return CitiesByLead.TryGetValue(lead.id, out List<City> found) ? found : new List<City>();
    }

    // 天下里各家(宗主)据有的城数与城的总数
    public static (Dictionary<Kingdom, int> held, int total) Holdings(EmpireCore lead)
    {
        List<City> cities = Cities(lead);
        return (EmpireCoreControl.CountHolders(cities), cities.Count);
    }

    public static string Name(EmpireCore lead)
    {
        if (lead == null) return "";
        if (!string.IsNullOrWhiteSpace(lead.tianxia_name)) return lead.tianxia_name.Trim();
        return string.Format(LM.Get("tianxia_default_name"), EmpireCoreManager.GetDisplayName(lead));
    }

    public static void Rename(EmpireCore core, string name)
    {
        EmpireCore lead = Lead(core);
        if (lead != null) lead.tianxia_name = name?.Trim() ?? "";
    }

    // 把 core 所在的天下整个并进 target 所在的天下(target 的主核心作主，名号、天命照它的)
    public static bool Join(EmpireCore core, EmpireCore target, bool manual = true)
    {
        EmpireCore from = Lead(core), into = Lead(target);
        if (from == null || into == null || from == into) return false;
        foreach (EmpireCore member in Members(from))
        {
            member.tianxia_lead_id = into.id;
            if (manual) member.tianxia_locked = true;
            ClearState(member);
        }
        into.tianxia_lead_id = -1L;
        return true;
    }

    // 退出联合天下，自成一个天下(天命要重新据有七成五才受)；退出的是主核心就由下一个核心接着作主
    public static bool Leave(EmpireCore core, bool manual = true)
    {
        if (core == null) return false;
        if (manual) core.tianxia_locked = true;
        EmpireCore lead = Lead(core);
        List<EmpireCore> members = Members(lead);
        if (members.Count <= 1) return false;
        if (lead == core) Handover(lead, Largest(members.Where(member => member != lead)), members);
        core.tianxia_lead_id = -1L;
        core.tianxia_name = "";
        ClearState(core);
        return true;
    }

    // 城最多的核心(交接天下时由它作主)
    private static EmpireCore Largest(IEnumerable<EmpireCore> cores) =>
        cores.Where(core => core != null).OrderByDescending(core => EmpireCoreManager.GetCities(core).Count).ThenBy(core => core.id)
            .FirstOrDefault();

    // 天下交给 heir 作主：名号、天命、历代共主、天命阶段、天下朝贡国一并移交，其余成员改挂 heir
    private static void Handover(EmpireCore lead, EmpireCore heir, List<EmpireCore> members)
    {
        if (lead == null || heir == null || heir == lead) return;
        heir.tianxia_lead_id = -1L;
        heir.tianxia_name = lead.tianxia_name;
        heir.mandate_kingdom_id = lead.mandate_kingdom_id;
        heir.mandate_at = lead.mandate_at;
        heir.mandate_holder_name = lead.mandate_holder_name;
        heir.mandate_lost_at = lead.mandate_lost_at;
        heir.mandate_former_kingdom_id = lead.mandate_former_kingdom_id;
        heir.mandate_history = lead.mandate_history;
        heir.tianxia_phase = lead.tianxia_phase;
        lead.mandate_history = null;
        foreach (EmpireCore member in members)
            if (member != null && member != lead && member != heir) member.tianxia_lead_id = heir.id;
        MoveTributaries(lead.id, heir.id);
    }

    private static void MoveTributaries(long from, long to)
    {
        if (World.world?.kingdoms == null) return;
        foreach (Kingdom kingdom in World.world.kingdoms.list)
            if (kingdom?.data != null && kingdom.GetOrCreate().tianxia_tributary_lead == from)
                kingdom.GetOrCreate().tianxia_tributary_lead = to;
    }

    // 帝国核心被销毁(军阀、法统之争、现代国家、神力……，见 EmpireCoreManager.DestroyEmpireCore)：它作主的天下交给
    // 城最多的成员接着作主，名号天命不丢；没有别的成员就散了，天下朝贡国的标记一并清掉
    public static void OnCoreDestroyed(EmpireCore core)
    {
        if (core == null || Lead(core) != core) return;
        List<EmpireCore> members = Members(core);
        EmpireCore heir = Largest(members.Where(member => member != core));
        if (heir != null) Handover(core, heir, members);
        else MoveTributaries(core.id, -1L);
    }

    // 每年(自动并分之前)：指向已不存在、或已不作主的核心的天下归属理顺——以前主核心被销毁后成员一直挂着失效的编号，
    // 天命、正统、封禅、造玺都判不成
    private static void Repair()
    {
        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values.ToList())
        {
            if (core == null || core.tianxia_lead_id < 0 || core.tianxia_lead_id == core.id) continue;
            EmpireCore target = EmpireCoreManager.Get(core.tianxia_lead_id);
            EmpireCore lead = target == null ? null : Lead(target);
            core.tianxia_lead_id = lead == null || lead == core ? -1L : lead.id;
        }
    }

    private static void ClearState(EmpireCore core)
    {
        core.mandate_kingdom_id = -1L;
        core.mandate_at = -1d;
        core.mandate_holder_name = "";
        core.mandate_lost_at = -1d;
        core.mandate_former_kingdom_id = -1L;
        core.tianxia_phase = -1;
    }

    #endregion

    #region 自动并分

    // 文化配置里的天下标识；explicitOnly 时没写标识的返回空
    public static string TianxiaId(EmpireCore core, bool explicitOnly = false)
    {
        string culture = core?.default_culture;
        if (!CultureService.IsValidCulture(culture)) return "";
        string id = culture.GetTianxiaId();
        return explicitOnly && id == culture ? "" : id;
    }

    // 年度(统计天下之前)：标识不同的退出，标识相同的相邻天下并成一个
    public static void AutoArrange()
    {
        if (EmpireCoreManager.EmpireCores == null || World.world?.cities == null) return;
        Repair();
        // 退出：联合天下里文化标识与主核心不同的(玩家动过的不管)
        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values.ToList())
        {
            if (core == null || core.tianxia_locked || core.warlord_parent_core_id >= 0) continue;
            EmpireCore lead = Lead(core);
            if (lead == core) continue;
            string own = TianxiaId(core), theirs = TianxiaId(lead);
            if (own != "" && theirs != "" && own != theirs) Leave(core, manual: false);
        }
        // 相邻：同一块陆地上两座接壤的城分属两个天下(用户："跨大陆跨海洋就算了"——隔海的不并，标了同一标识也不并)
        var pairs = new HashSet<(long, long)>();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.neighbours_cities == null) continue;
            EmpireCore a = LeadOf(city);
            if (a == null) continue;
            object island = city.getTile()?.region?.island;
            foreach (City next in city.neighbours_cities)
            {
                if (next?.data == null || next.isRekt() || island == null || !ReferenceEquals(island, next.getTile()?.region?.island)) continue;
                EmpireCore b = LeadOf(next);
                if (b == null || b == a) continue;
                pairs.Add(a.id < b.id ? (a.id, b.id) : (b.id, a.id));
            }
        }
        foreach ((long first, long second) in pairs)
        {
            EmpireCore a = Lead(EmpireCoreManager.Get(first)), b = Lead(EmpireCoreManager.Get(second));
            if (a == null || b == null || a == b) continue;
            string id = TianxiaId(a, explicitOnly: true);
            if (id == "" || id != TianxiaId(b, explicitOnly: true)) continue;
            // 小的并进大的(城多的作主)；任一边被玩家动过(加入、退出都会锁)就不自动合并——以前锁了的一边反而把对面并进来：
            // 刚退出的小核心第二年就把原来的大天下吞了、抹掉大天下的天命
            List<EmpireCore> ma = Members(a), mb = Members(b);
            if (ma.Any(core => core.tianxia_locked) || mb.Any(core => core.tianxia_locked)) continue;
            int ca = ma.Sum(core => EmpireCoreManager.GetCities(core).Count), cb = mb.Sum(core => EmpireCoreManager.GetCities(core).Count);
            if (ca < cb || ca == cb && a.id > b.id) Join(a, b, manual: false);
            else Join(b, a, manual: false);
        }
    }

    #endregion

    #region 天命

    public static Kingdom Holder(EmpireCore lead)
    {
        if (lead == null || lead.mandate_kingdom_id < 0 || World.world?.kingdoms == null) return null;
        Kingdom kingdom = World.world.kingdoms.get(lead.mandate_kingdom_id);
        return kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild ? kingdom : null;
    }

    // 年度(UnificationSystem.Yearly 统计完天下以后)：各家据有的城数 → 受天命、失天命
    public static void Settle(EmpireCore lead, Dictionary<Kingdom, int> held, int total, string name)
    {
        if (lead == null || total <= 0) return;
        double now = World.world.getCurWorldTime();
        Kingdom holder = Holder(lead);
        if (lead.mandate_kingdom_id >= 0)
        {
            int ours = holder != null && held.TryGetValue(holder, out int count) ? count : 0;
            bool keeps = holder != null && ours >= total * KeepShare && !held.Any(pair => pair.Key != holder && pair.Value > ours);
            if (keeps)
            {
                SetPhase(lead, Shaken(holder, ours / (float)total) ? Unrest : Peace, name, holder);
                Tribute(lead, holder, name);
                return;
            }
            string former = holder?.GetKingdomName() ?? lead.mandate_holder_name;
            lead.mandate_former_kingdom_id = lead.mandate_kingdom_id;
            lead.mandate_kingdom_id = -1L;
            lead.mandate_lost_at = now;
            MandateRecord open = lead.mandate_history?.LastOrDefault(record => record.lost_at < 0d);
            if (open != null) open.lost_at = now;
            Record(holder, string.Format(LM.Get("tianxia_mandate_lost_history"), former, name));
            DisperseTributaries(lead, holder, name, 1f);
            InvalidateLegitimacy(holder);
        }
        KeyValuePair<Kingdom, int> top = held.Count == 0 ? default : held.OrderByDescending(pair => pair.Value).First();
        float claimShare = top.Key != null && ImperialSealSystem.Holds(top.Key) ? SealClaimShare : ClaimShare;
        if (held.Count == 0 || top.Value < total * claimShare || top.Key?.data == null || top.Key.isRekt() || top.Key.wild)
        {
            SetPhase(lead, Chaos, name, null);
            return;
        }
        Claim(lead, top.Key, top.Value, total, name, now);
    }

    private static void Claim(EmpireCore lead, Kingdom kingdom, int count, int total, string name, double now)
    {
        lead.mandate_kingdom_id = kingdom.id;
        lead.mandate_at = now;
        lead.mandate_holder_name = kingdom.GetKingdomName();
        lead.mandate_lost_at = -1d;
        lead.mandate_history ??= new List<MandateRecord>();
        lead.mandate_history.Add(new MandateRecord
        {
            kingdom_id = kingdom.id, kingdom_name = lead.mandate_holder_name,
            dynasty = kingdom.GetEmpire()?.EmpireSpecificClan?.name ?? "", claimed_at = now
        });
        Record(kingdom, string.Format(LM.Get("tianxia_mandate_claim_history"), lead.mandate_holder_name, name,
            $"{count / (float)total:P0}"));
        lead.tianxia_phase = Peace;
        lead.tianxia_phase_since = now;
        InvalidateLegitimacy(kingdom);
    }

    // 统一完成即受天命(用户 2026-10-17 选"以天命为准"：统一了就是治世的开头)，不论据有几成；之后照常过半才守得住
    public static void Grant(EmpireCore lead, Kingdom unifier, int count, int total, string name)
    {
        if (lead == null || unifier?.data == null || unifier.isRekt() || unifier.wild || total <= 0 ||
            lead.mandate_kingdom_id >= 0 || World.world == null) return;
        Claim(lead, unifier, count, total, name, World.world.getCurWorldTime());
    }

    // 这一国受着哪个天下的天命(没有为空)
    public static EmpireCore MandateOf(Kingdom kingdom) => kingdom == null ? null
        : EmpireCoreManager.EmpireCores.Values.FirstOrDefault(lead => lead != null && lead.tianxia_lead_id < 0 && lead.mandate_kingdom_id == kingdom.id);

    // 这一国所处天下的天命周期(受天命的看它那个天下，否则看都城所在的天下；不在天下里算乱世)
    public static int PhaseOf(Kingdom kingdom)
    {
        EmpireCore lead = MandateOf(kingdom) ?? LeadOf(kingdom?.capital);
        return lead == null ? Chaos : Phase(lead);
    }

    // 来朝诸国(仰慕这个天下的共主而来的、还在朝贡的)
    public static List<Kingdom> Tributaries(EmpireCore lead)
    {
        var result = new List<Kingdom>();
        if (lead == null || World.world?.kingdoms == null) return result;
        foreach (Kingdom kingdom in World.world.kingdoms.list)
            if (kingdom?.data != null && !kingdom.isRekt() && kingdom.GetOrCreate().tianxia_tributary_lead == lead.id) result.Add(kingdom);
        return result;
    }

    private static void Tribute(EmpireCore lead, Kingdom holder, string name)
    {
        Empire empire = holder.GetEmpire();
        // 已不朝贡的(战争、改投、被灭)去掉标记
        foreach (Kingdom kingdom in Tributaries(lead))
            if (empire == null || kingdom.GetTakenAllianceEmpire() != empire) kingdom.GetOrCreate().tianxia_tributary_lead = -1L;
        if (empire == null) return;
        if (Phase(lead) == Unrest)
        {
            DisperseTributaries(lead, holder, name, TributeLeaveChance);
            return;
        }
        if (Phase(lead) != Peace) return;
        double power = WarSituation.Power(empire);
        foreach (Kingdom neighbour in empire.GetKingdomNeighbours().ToList())
        {
            if (neighbour?.data == null || neighbour.isRekt() || neighbour.wild || neighbour.IsInEmpire() ||
                neighbour.HasTakenAlliance() || EmpireCoreControl.Sovereign(neighbour) != neighbour) continue;
            if (neighbour.capital != null && LeadOf(neighbour.capital) == lead) continue;
            if (WarSituation.Power(neighbour) > power * TributePowerRatio || UnityEngine.Random.value >= TributeChance) continue;
            neighbour.JoinTakenAlliance(empire, pForce: true);
            if (neighbour.GetTakenAllianceEmpire() != empire) continue;
            neighbour.GetOrCreate().tianxia_tributary_lead = lead.id;
            Record(holder, string.Format(LM.Get("tianxia_tribute_history"), neighbour.GetKingdomName(), holder.GetKingdomName(), name));
            InvalidateLegitimacy(holder);
        }
    }

    private static void DisperseTributaries(EmpireCore lead, Kingdom holder, string name, float chance)
    {
        var left = new List<string>();
        foreach (Kingdom kingdom in Tributaries(lead))
        {
            if (chance < 1f && UnityEngine.Random.value >= chance) continue;
            kingdom.GetOrCreate().tianxia_tributary_lead = -1L;
            if (kingdom.HasTakenAlliance()) kingdom.RemoveTakenAlliance();
            left.Add(kingdom.GetKingdomName());
        }
        if (left.Count > 0)
            Record(holder, string.Format(LM.Get("tianxia_tribute_leave_history"), string.Join("、", left), name));
    }

    // 天命动摇：地盘跌到七成五以下、正统低于三十、积弊六十以上
    private static bool Shaken(Kingdom holder, float share)
    {
        Empire empire = holder?.GetEmpire();
        if (share < ClaimShare) return true;
        if (empire == null) return false;
        return empire.Legitimacy < ShakenMandate || DynasticCycleSystem.GetPressure(empire) >= ShakenPressure;
    }

    // 换段：治世转动荡记"天命动摇"、动荡回治世记"复归治世"(乱世由失天命那一笔记过)
    private static void SetPhase(EmpireCore lead, int phase, string name, Kingdom holder)
    {
        int old = lead.tianxia_phase;
        if (old == phase) return;
        lead.tianxia_phase = phase;
        lead.tianxia_phase_since = World.world.getCurWorldTime();
        if (old < 0 || holder == null) return;
        if (phase == Unrest && old == Peace)
            Record(holder, string.Format(LM.Get("tianxia_unrest_history"), name, holder.GetKingdomName()));
        else if (phase == Peace && old == Unrest)
            Record(holder, string.Format(LM.Get("tianxia_peace_history"), name, holder.GetKingdomName()));
        InvalidateLegitimacy(holder);
    }

    // 正统：天下共主 +8；失了天命、天下仍无主的旧朝 -5
    public static IEnumerable<(string key, int value)> LegitimacyItems(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null) yield break;
        bool holds = false, lost = false, shaken = false;
        foreach (EmpireCore lead in EmpireCoreManager.EmpireCores.Values)
        {
            if (lead == null || lead.tianxia_lead_id >= 0) continue;
            if (lead.mandate_kingdom_id == core.id)
            {
                holds = true;
                shaken |= lead.tianxia_phase == Unrest;
            }
            else if (lead.mandate_kingdom_id < 0 && lead.mandate_lost_at >= 0d && lead.mandate_former_kingdom_id == core.id)
                lost = true;
        }
        if (holds && EmpireCoreManager.EmpireCores.Values.Any(lead => lead != null && lead.tianxia_lead_id < 0 &&
                lead.mandate_kingdom_id == core.id && Tributaries(lead).Count >= TributeLegitimacyMin))
            yield return ("mandate_tianxia_tribute", TributeLegitimacy);
        if (holds && shaken) yield return ("mandate_tianxia_shaken", ShakenLegitimacy);
        else if (holds) yield return ("mandate_tianxia_holder", HolderLegitimacy);
        else if (lost) yield return ("mandate_tianxia_lost", LostLegitimacy);
    }

    // 界面上的天命状况
    public static string MandateText(EmpireCore lead)
    {
        if (lead == null) return "";
        Kingdom holder = Holder(lead);
        if (holder != null)
            return string.Format(LM.Get("tianxia_mandate_held"), holder.GetKingdomName(), Date.getYearsSince(lead.mandate_at)) +
                   " · " + PhaseName(Phase(lead));
        if (lead.mandate_lost_at >= 0d)
            return string.Format(LM.Get("tianxia_mandate_vacant"), lead.mandate_holder_name, Date.getYearsSince(lead.mandate_lost_at));
        return LM.Get("tianxia_mandate_never");
    }

    private static void InvalidateLegitimacy(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire != null) MonarchyLegitimacy.Invalidate(empire);
    }

    private static void Record(Kingdom kingdom, string text)
    {
        if (kingdom?.data == null || kingdom.isRekt()) TranslateHelper.LogEventMessage(text);
        else EventRecorder.Record(kingdom, text);
    }

    #endregion
}

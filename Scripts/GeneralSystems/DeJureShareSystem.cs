using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 一份兼属：另挂的上级是谁(王国法理、帝国核心、天下主核心的 id)、从什么时候起、是不是神力手动挂的(手动的不过期)
public class DeJureShare
{
    public long id = -1L;
    public double since = -1d;
    public bool manual;
}

// 旧存档的争议地带宣称(城另记的别家帝国核心)：读档时折成兼属(见 DeJureShareSystem.AfterLoad)
public class ContestClaim
{
    public long core = -1L;
    public double since = -1d;
    public bool manual;
}

// 兼属(用户 2026-10-22 "对刚刚的争议地带，允许一个城市拥有两个王国法理就是了""帝国核心和天下本来就是基于王国法理来的"
// "一个王国法理可以拥有多个帝国法理""帝国法理也可以属于多个天下"；问答选：三层按钮按层排、另设「兼属」按钮、
// 各层图层画斜纹叠色、旧争议地带并进来)。城→王国法理→帝国法理→天下，每一层除了本来的归属，另可兼属至多两个上级：
//   · 城兼属王国法理：记在城上(CityExtraData.shared_titles)；那个王国法理的城、它所在帝国法理与天下的城都算这座城；
//   · 王国法理兼属帝国法理：记在帝国核心上(EmpireCore.shared_titles)；那个帝国法理的城都算这个王国法理的城；
//   · 帝国法理兼属天下：记在帝国核心上(EmpireCore.shared_tianxia)；那个天下的城都算这个帝国法理的城。
//   城自己记的核心(empire_core_id)、法理自己的城单、核心自己的法理单都不变——兼属只是另算进上级，
//   所以法理同步、核心同步不会两边互抢(见 EmpireCoreManager.EnumerateOwnCities)。
//   · 效果：上级的城数、控制占比、天下的城与统一占比都算它；宣战时兼属方算收复(见 WarGoalSystem.Claimed)；
//     各层图层上画两三家颜色相间的斜纹(王国法理图层看王国法理、帝国法理显示看帝国法理、天下图层看天下，纯净地图下)；
//   · 来由：神力「兼属」手动挂、撤(冻结法理时照样能用)；每年看一遍，城换了王国法理而原法理归别的帝国法理、就在隔壁的，
//     原法理留作兼属；王国法理换了帝国法理而原帝国法理就在隔壁的，原帝国法理留作兼属(故地)；
//   · 失效：上级没了、成了本来的归属；自动留下的满百年淡忘(手动挂的不过期)；开着冻结法理时一律不淡忘，永远挂着双法理。
public static class DeJureShareSystem
{
    public const int MaxShares = 2;
    private const int ForgetYears = 100;
    private static double _lastScan = -1d;

    // 反查表(哪些城兼属这个王国法理、哪些帝国法理兼收这个王国法理)：任何改动、读档都作废重建
    private static int _version;
    private static int _indexVersion = -1;
    private static object _indexWorld;
    private static readonly Dictionary<long, List<City>> CitiesByTitle = new();
    private static readonly Dictionary<long, List<EmpireCore>> CoresByTitle = new();
    private static bool _anyTianxia;

    public static void ResetWorldState()
    {
        _lastScan = -1d;
        _version++;
    }

    private static void Changed() => _version++;

    private static void EnsureIndex()
    {
        if (_indexVersion == _version && ReferenceEquals(_indexWorld, World.world)) return;
        _indexVersion = _version;
        _indexWorld = World.world;
        CitiesByTitle.Clear();
        CoresByTitle.Clear();
        _anyTianxia = false;
        if (World.world?.cities != null)
            foreach (City city in World.world.cities.list)
            {
                List<DeJureShare> shares = city?.data == null ? null : city.GetOrCreate().shared_titles;
                if (shares == null) continue;
                foreach (DeJureShare share in shares)
                {
                    if (share == null) continue;
                    if (!CitiesByTitle.TryGetValue(share.id, out List<City> list)) CitiesByTitle[share.id] = list = new List<City>();
                    if (!list.Contains(city)) list.Add(city);
                }
            }
        if (EmpireCoreManager.EmpireCores != null)
            foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values)
            {
                if (core == null) continue;
                _anyTianxia |= core.shared_tianxia?.Count > 0;
                if (core.shared_titles == null) continue;
                foreach (DeJureShare share in core.shared_titles)
                {
                    if (share == null) continue;
                    if (!CoresByTitle.TryGetValue(share.id, out List<EmpireCore> list)) CoresByTitle[share.id] = list = new List<EmpireCore>();
                    if (!list.Contains(core)) list.Add(core);
                }
            }
    }

    // 世上有没有兼属(没有就什么都不用多算)
    public static bool Any
    {
        get
        {
            EnsureIndex();
            return CitiesByTitle.Count > 0 || CoresByTitle.Count > 0 || _anyTianxia;
        }
    }

    // 记一份(已有就不重记；满了挤掉最早的)
    private static bool Add(List<DeJureShare> shares, long id, bool manual, double since)
    {
        DeJureShare existing = shares.Find(share => share?.id == id);
        if (existing != null)
        {
            existing.manual |= manual;
            return false;
        }
        while (shares.Count >= MaxShares) shares.RemoveAt(0);
        shares.Add(new DeJureShare { id = id, since = since >= 0d ? since : World.world.getCurWorldTime(), manual = manual });
        return true;
    }

    private static KingdomTitle TitleOf(long id)
    {
        KingdomTitle title = id < 0 ? null : ModClass.KINGDOM_TITLE_MANAGER?.get(id);
        return title == null || title.isRekt() ? null : title;
    }

    private static bool Valid(City city) => city?.data != null && !city.isRekt();

    #region 城兼属王国法理

    public static List<KingdomTitle> SharedTitles(City city)
    {
        var result = new List<KingdomTitle>();
        List<DeJureShare> shares = Valid(city) ? city.GetOrCreate().shared_titles : null;
        if (shares == null) return result;
        long own = city.GetTitleID();
        foreach (DeJureShare share in shares)
            if (share != null && share.id != own && TitleOf(share.id) is KingdomTitle title && !result.Contains(title))
                result.Add(title);
        return result;
    }

    // 兼属这个王国法理的城(不含它本来的城)
    public static List<City> SharedCities(KingdomTitle title)
    {
        var result = new List<City>();
        if (title == null) return result;
        EnsureIndex();
        if (!CitiesByTitle.TryGetValue(title.id, out List<City> list)) return result;
        foreach (City city in list)
            if (Valid(city) && city.GetTitleID() != title.id) result.Add(city);
        return result;
    }

    // 王国法理的城连同兼属它的城
    public static IEnumerable<City> AllCities(KingdomTitle title)
    {
        if (title == null) yield break;
        foreach (City city in title.getCities()) yield return city;
        foreach (City city in SharedCities(title)) yield return city;
    }

    public static bool ShareTitle(City city, KingdomTitle title, bool manual, double since = -1d)
    {
        if (!Valid(city) || title == null || title.isRekt() || city.GetTitleID() == title.id) return false;
        var data = city.GetOrCreate();
        data.shared_titles ??= new List<DeJureShare>();
        if (!Add(data.shared_titles, title.id, manual, since)) return false;
        Touch(city);
        Changed();
        return true;
    }

    public static bool UnshareTitle(City city, KingdomTitle title)
    {
        List<DeJureShare> shares = Valid(city) ? city.GetOrCreate().shared_titles : null;
        if (shares == null || title == null || shares.RemoveAll(share => share?.id == title.id) == 0) return false;
        Touch(city);
        Changed();
        return true;
    }

    public static bool ClearTitles(City city)
    {
        List<DeJureShare> shares = Valid(city) ? city.GetOrCreate().shared_titles : null;
        if (shares == null || shares.Count == 0) return false;
        shares.Clear();
        Touch(city);
        Changed();
        return true;
    }

    #endregion

    #region 王国法理兼属帝国法理

    // 兼属这个帝国法理的王国法理(不含它本来的)
    public static List<KingdomTitle> SharedTitles(EmpireCore core)
    {
        var result = new List<KingdomTitle>();
        if (core?.shared_titles == null) return result;
        foreach (DeJureShare share in core.shared_titles)
            if (share != null && TitleOf(share.id) is KingdomTitle title && !EmpireCoreManager.ContainsTitle(core, title) &&
                !result.Contains(title))
                result.Add(title);
        return result;
    }

    // 兼收这个王国法理的帝国法理(不含它本来所在的)
    public static List<EmpireCore> SharedCores(KingdomTitle title)
    {
        var result = new List<EmpireCore>();
        if (title == null) return result;
        EnsureIndex();
        if (!CoresByTitle.TryGetValue(title.id, out List<EmpireCore> list)) return result;
        foreach (EmpireCore core in list)
            if (core != null && EmpireCoreManager.Get(core.id) == core && !EmpireCoreManager.ContainsTitle(core, title))
                result.Add(core);
        return result;
    }

    public static bool ShareCore(KingdomTitle title, EmpireCore core, bool manual, double since = -1d)
    {
        if (title == null || title.isRekt() || core == null || EmpireCoreManager.ContainsTitle(core, title)) return false;
        core.shared_titles ??= new List<DeJureShare>();
        if (!Add(core.shared_titles, title.id, manual, since)) return false;
        Touch(title);
        Changed();
        return true;
    }

    public static bool UnshareCore(KingdomTitle title, EmpireCore core)
    {
        if (title == null || core?.shared_titles == null || core.shared_titles.RemoveAll(share => share?.id == title.id) == 0)
            return false;
        Touch(title);
        Changed();
        return true;
    }

    public static bool ClearCores(KingdomTitle title)
    {
        bool removed = false;
        foreach (EmpireCore core in SharedCores(title)) removed |= UnshareCore(title, core);
        return removed;
    }

    #endregion

    #region 帝国法理兼属天下

    // 这个帝国法理兼属的天下(主核心；不含它本来所在的)
    public static List<EmpireCore> SharedLeads(EmpireCore core)
    {
        var result = new List<EmpireCore>();
        if (core?.shared_tianxia == null) return result;
        EmpireCore own = TianxiaSystem.Lead(core);
        foreach (DeJureShare share in core.shared_tianxia)
        {
            EmpireCore lead = share == null ? null : TianxiaSystem.Lead(EmpireCoreManager.Get(share.id));
            if (lead != null && lead != own && !result.Contains(lead)) result.Add(lead);
        }
        return result;
    }

    // 兼属这个天下的帝国法理
    public static List<EmpireCore> SharedMembers(EmpireCore lead)
    {
        var result = new List<EmpireCore>();
        if (lead == null || !Any || !_anyTianxia) return result;
        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values)
            if (core?.shared_tianxia?.Count > 0 && SharedLeads(core).Contains(lead)) result.Add(core);
        return result;
    }

    public static bool ShareTianxia(EmpireCore core, EmpireCore lead, bool manual, double since = -1d)
    {
        lead = TianxiaSystem.Lead(lead);
        if (core == null || lead == null || TianxiaSystem.Lead(core) == lead) return false;
        core.shared_tianxia ??= new List<DeJureShare>();
        if (!Add(core.shared_tianxia, lead.id, manual, since)) return false;
        Touch(core);
        Changed();
        return true;
    }

    public static bool UnshareTianxia(EmpireCore core, EmpireCore lead)
    {
        lead = TianxiaSystem.Lead(lead);
        if (core?.shared_tianxia == null || lead == null ||
            core.shared_tianxia.RemoveAll(share => TianxiaSystem.Lead(EmpireCoreManager.Get(share?.id ?? -1L)) == lead) == 0)
            return false;
        Touch(core);
        Changed();
        return true;
    }

    public static bool ClearTianxia(EmpireCore core)
    {
        if (core?.shared_tianxia == null || core.shared_tianxia.Count == 0) return false;
        core.shared_tianxia.Clear();
        Touch(core);
        Changed();
        return true;
    }

    #endregion

    #region 城的各层归属

    // 这座城的归属牵涉兼属没有(没有就只有本来的那一份，天下每帧扫城、图层每块问一遍，先用它挡掉)
    public static bool Involved(City city)
    {
        if (!Valid(city) || !Any) return false;
        if (city.GetOrCreate().shared_titles?.Count > 0) return true;
        KingdomTitle own = city.GetTitle();
        if (own != null && CoresByTitle.ContainsKey(own.id)) return true;
        return city.GetEmpireCore()?.shared_tianxia?.Count > 0;
    }

    // 城所在的帝国法理：本来的在前，再是兼属的王国法理所在的、兼收它的王国法理的
    public static List<EmpireCore> CoresOf(City city)
    {
        var result = new List<EmpireCore>();
        if (!Valid(city)) return result;
        void Put(EmpireCore core)
        {
            if (core != null && !result.Contains(core)) result.Add(core);
        }
        Put(city.GetEmpireCore());
        if (!Involved(city)) return result;
        KingdomTitle own = city.GetTitle();
        if (own != null && !own.isRekt())
            foreach (EmpireCore core in SharedCores(own)) Put(core);
        foreach (KingdomTitle title in SharedTitles(city))
        {
            Put(EmpireCoreManager.LawfulCoreOf(title));
            foreach (EmpireCore core in SharedCores(title)) Put(core);
        }
        return result;
    }

    // 城所在的天下(主核心)：本来的在前
    public static List<EmpireCore> LeadsOf(City city)
    {
        var result = new List<EmpireCore>();
        if (!Valid(city)) return result;
        void Put(EmpireCore lead)
        {
            if (lead != null && !result.Contains(lead)) result.Add(lead);
        }
        Put(TianxiaSystem.LeadOf(city));
        if (!Involved(city)) return result;
        foreach (EmpireCore core in CoresOf(city))
        {
            Put(TianxiaSystem.Lead(core));
            foreach (EmpireCore lead in SharedLeads(core)) Put(lead);
        }
        return result;
    }

    // 兼属方的帝国里有攻方一方(帝国按整个帝国算)、城又不在攻方手里：开战时算收复(见 WarGoalSystem.Claimed)
    public static bool ClaimedBy(City city, Kingdom realm)
    {
        if (realm == null || !Valid(city) || !Any || city.kingdom != null && FrontSystem.Realm(city.kingdom) == realm) return false;
        foreach (KingdomTitle title in SharedTitles(city))
        {
            Kingdom holder = title.main_kingdom ?? title.owner?.kingdom;
            if (holder?.data != null && !holder.isRekt() && FrontSystem.Realm(holder) == realm) return true;
        }
        List<EmpireCore> cores = CoresOf(city);
        for (int i = 1; i < cores.Count; i++)
            foreach (Empire empire in EmpireCoreManager.GetEmpires(cores[i]))
                if (empire?.CoreKingdom != null && !empire.isRekt() && !empire.IsArchived() &&
                    FrontSystem.Realm(empire.CoreKingdom) == realm)
                    return true;
        return false;
    }

    #endregion

    #region 年度：故地留作兼属、过期的淡忘

    public static void TryYearlyScan()
    {
        if (World.world?.cities == null || ModClass.KINGDOM_TITLE_MANAGER == null) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan < 0d || now < _lastScan)
        {
            _lastScan = now;
            return;
        }
        if (Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        var lawful = new Dictionary<long, EmpireCore>();
        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER.ToList())
            if (title != null && !title.isRekt() && EmpireCoreManager.LawfulCoreOf(title) is EmpireCore core)
                lawful[title.id] = core;

        // 城换了王国法理，原法理归别的帝国法理、就在隔壁：原法理留作兼属
        foreach (City city in World.world.cities.list.ToList())
        {
            if (!Valid(city)) continue;
            var data = city.GetOrCreate();
            long current = city.GetTitleID(), last = data.dejure_last_title;
            if (current >= 0) data.dejure_last_title = current;
            if (last >= 0 && current >= 0 && last != current && TitleOf(last) is KingdomTitle previous &&
                lawful.TryGetValue(last, out EmpireCore before) &&
                (!lawful.TryGetValue(current, out EmpireCore after) || after != before) && Borders(city, previous))
                ShareTitle(city, previous, false);
            if (data.shared_titles?.Count > 0 && Prune(data.shared_titles, now,
                    share => share.id == current || TitleOf(share.id) == null)) Touch(city);
        }

        // 王国法理换了帝国法理，原帝国法理就在隔壁：原帝国法理留作兼属
        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER.ToList())
        {
            if (title?.data == null || title.isRekt()) continue;
            long current = lawful.TryGetValue(title.id, out EmpireCore core) ? core.id : -1L;
            long last = title.data.dejure_last_core;
            if (current >= 0) title.data.dejure_last_core = current;
            if (last >= 0 && current >= 0 && last != current && EmpireCoreManager.Get(last) is EmpireCore previous &&
                Borders(title, previous))
                ShareCore(title, previous, false);
        }

        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values.ToList())
        {
            if (core == null) continue;
            if (core.shared_titles?.Count > 0 && Prune(core.shared_titles, now, share =>
                    TitleOf(share.id) is not KingdomTitle title || EmpireCoreManager.ContainsTitle(core, title)))
                Touch(core);
            if (core.shared_tianxia?.Count > 0 && Prune(core.shared_tianxia, now, share =>
                    TianxiaSystem.Lead(EmpireCoreManager.Get(share.id)) is not EmpireCore lead || lead == TianxiaSystem.Lead(core)))
                Touch(core);
        }
    }

    private static bool Prune(List<DeJureShare> shares, double now, global::System.Predicate<DeJureShare> invalid)
    {
        bool frozen = ModClass.KINGDOM_TITLE_FREEZE;
        int removed = shares.RemoveAll(share => share == null || invalid(share) ||
                                                !frozen && !share.manual && share.since >= 0d && now >= share.since &&
                                                Date.getYearsSince(share.since) >= ForgetYears);
        if (removed > 0) Changed();
        return removed > 0;
    }

    // 本城有地块挨着这个王国法理(本来的城)
    private static bool Borders(City city, KingdomTitle title)
    {
        foreach (City other in Neighbours(city))
            if (other.GetTitleID() == title.id) return true;
        return false;
    }

    // 王国法理有城挨着这个帝国法理(城本来记的核心)
    private static bool Borders(KingdomTitle title, EmpireCore core)
    {
        foreach (City city in title.getCities())
            foreach (City other in Neighbours(city))
                if (other.GetEmpireCoreID() == core.id) return true;
        return false;
    }

    private static IEnumerable<City> Neighbours(City city)
    {
        if (city?.zones == null) yield break;
        foreach (TileZone zone in city.zones)
            foreach (TileZone next in new[] { zone.zone_up, zone.zone_down, zone.zone_left, zone.zone_right })
            {
                City other = next?.city;
                if (other != null && other != city && !other.isRekt()) yield return other;
            }
    }

    // 读档后：旧存档的争议地带宣称折成兼属——城兼属宣称方帝国法理里挨着它(没有就离它最近)的王国法理
    public static void AfterLoad()
    {
        Changed();
        if (World.world?.cities == null) return;
        int converted = 0;
        foreach (City city in World.world.cities.list)
        {
            if (!Valid(city)) continue;
            var data = city.GetOrCreate();
            if (data.contested_claims == null) continue;
            foreach (ContestClaim claim in data.contested_claims)
            {
                EmpireCore core = claim == null ? null : EmpireCoreManager.Get(claim.core);
                if (core == null) continue;
                List<KingdomTitle> titles = EmpireCoreManager.GetTitles(core).Where(title => title != null && !title.isRekt()).ToList();
                KingdomTitle pick = titles.FirstOrDefault(title => Borders(city, title)) ??
                                    titles.Where(title => title.title_capital != null && !title.title_capital.isRekt())
                                        .OrderBy(title => Toolbox.SquaredDistTile(title.title_capital.getTile(), city.getTile()))
                                        .FirstOrDefault();
                if (pick != null && ShareTitle(city, pick, claim.manual, claim.since)) converted++;
            }
            data.contested_claims = null;
        }
        if (converted > 0) NeoModLoader.services.LogService.LogInfo($"[EmpireCraft][兼属] 旧存档的争议地带折成兼属 {converted} 份");
    }

    #endregion

    #region 地图：斜纹

    // 变了：下一轮重画(斜纹)
    private static void Touch(City city)
    {
        foreach (TileZone zone in city?.zones ?? new List<TileZone>()) zone?.resetRenderHelpers();
    }

    private static void Touch(KingdomTitle title)
    {
        foreach (City city in title?.getCities() ?? Enumerable.Empty<City>()) Touch(city);
        foreach (City city in SharedCities(title)) Touch(city);
    }

    private static void Touch(EmpireCore core)
    {
        foreach (City city in EmpireCoreManager.GetCities(core)) Touch(city);
    }

    // 一帧里同一座城只算一次(平滑国界每块都要问一遍缓存键)
    private static int _stripeFrame = -1;
    private static object _stripeLayer;
    private static readonly Dictionary<long, Color32[]> StripeCache = new();

    // 当前图层上这座城另画的颜色(至多两种，本色之外)；王国法理图层(按法理)看王国法理、帝国法理显示看帝国法理、天下图层看天下。
    // 别的图层、没有兼属的为空
    public static Color32[] StripeColors(City city)
    {
        if (World.world == null || !Involved(city)) return null;
        MetaTypeAsset layer = World.world.zone_calculator?._mode_asset ?? World.world.getCachedMapMetaAsset();
        if (layer == null) return null;
        if (_stripeFrame != Time.frameCount || !ReferenceEquals(_stripeLayer, layer))
        {
            _stripeFrame = Time.frameCount;
            _stripeLayer = layer;
            StripeCache.Clear();
        }
        if (StripeCache.TryGetValue(city.id, out Color32[] cached)) return cached;
        Color32[] colors = Compute(city, layer);
        StripeCache[city.id] = colors;
        return colors;
    }

    private static Color32[] Compute(City city, MetaTypeAsset layer)
    {
        int option = layer.getZoneOptionState();
        var colors = new List<Color32>();
        void Put(Color32? color, Color32? own)
        {
            if (color is not Color32 c || colors.Count >= MaxShares) return;
            if (own is Color32 o && o.r == c.r && o.g == c.g && o.b == c.b) return;
            if (colors.Any(other => other.r == c.r && other.g == c.g && other.b == c.b)) return;
            colors.Add(c);
        }
        if (layer.map_mode == MetaTypeExtension.KingdomTitle && option == 1)
        {
            Color32? own = Opaque(city.GetTitle());
            foreach (KingdomTitle title in SharedTitles(city)) Put(Opaque(title), own);
        }
        else if (layer.map_mode == MetaTypeExtension.KingdomTitle && option == 0)
        {
            List<EmpireCore> cores = CoresOf(city);
            Color32? own = cores.Count > 0 && cores[0] == city.GetEmpireCore() ? Opaque(CoreMeta(cores[0])) : null;
            for (int i = own == null ? 0 : 1; i < cores.Count; i++) Put(Opaque(CoreMeta(cores[i])), own);
        }
        else if (layer.map_mode == MetaTypeExtension.Tianxia && option == 0)
        {
            List<EmpireCore> leads = LeadsOf(city);
            Color32? own = leads.Count > 0 ? Hex(leads[0]) : null;
            for (int i = 1; i < leads.Count; i++) Put(Hex(leads[i]), own);
        }
        return colors.Count == 0 ? null : colors.ToArray();
    }

    // 帝国法理在法理图层上显示成什么：它的帝国还在就是帝国，否则是代表它的王国法理
    private static object CoreMeta(EmpireCore core)
    {
        if (core == null) return null;
        Empire empire = ModClass.EMPIRE_MANAGER?.get(core.empire_id);
        return empire != null && !empire.isRekt() && !empire.IsArchived() ? empire : EmpireCoreManager.GetColorTitle(core);
    }

    private static Color32? Opaque(object meta)
    {
        if (meta == null) return null;
        ColorAsset color = CustomColorSystem.Of(meta) ?? (meta as IMetaObject)?.getColor();
        if (color == null) return null;
        Color32 fill = color.getColorBorderInsideAlpha32();
        fill.a = byte.MaxValue;
        return fill;
    }

    private static Color32? Hex(EmpireCore lead) =>
        lead != null && ColorUtility.TryParseHtmlString(TianxiaMap.HexOf(lead), out Color color) ? (Color32)color : null;

    // 平滑国界的上色缓存键(见 SmoothBorders.KeyOf)：斜纹的颜色变了要重画
    public static int StripeKey(City city)
    {
        Color32[] colors = StripeColors(city);
        if (colors == null) return 0;
        int key = colors.Length << 28;
        foreach (Color32 c in colors) key = key * 31 + (c.r | c.g << 8 | c.b << 16);
        return key == 0 ? 1 : key;
    }

    // 斜纹：每两格换一种颜色，本色与另画的颜色轮流；0 = 本色，k = 第 k 种另画的颜色
    public static int Band(WorldTile tile, int extra) => (tile.x + tile.y) / 2 % (extra + 1);

    #endregion
}

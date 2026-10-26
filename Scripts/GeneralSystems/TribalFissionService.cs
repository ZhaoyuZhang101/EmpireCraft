using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 部落分迁(用户 2026-10-16："为什么部落扩展城市不能像城邦一样直接扩一个国家，因为生产力有限""而是直接纳入第一个国家了这不合理")：
// 原始政体的部落、方国生产力有限，管不了第二座城——一部一城，多出来的城就是分出去的一支新部落：
//   · 族人新建的城当场分出(见 OnCityBuilt)；打下来的、割让的、归附的、开局就有的，每月分出一座(见 EnforceOneCity，
//     用户追问"开局三个城市还是被纳入了一个国家""这个不合理，因为这是部落")；
//   · 建城的族人或城主(都不合适就是城里威望最高的成年人)立为首领，政体照旧是原始政体。
// 天下共主(用户："弄一个新的部落联盟主国家为帝国，其余国家类似于朝贡""其实就是诸侯国，战国的那种"
// "不是说是共主就像战国诸侯国一样为什么又纳入到帝国去了")：
//   · 共主是部落联盟(帝国)，帝国里只有共主自己；各部落是帝国之外的独立之国，只记着奉谁为共主
//     (KingdomExtraData.tribal_overlord_id，铭牌标"某某诸侯国")——有自己的颜色、国库、军队、外交，可以互相攻伐，
//     同战国时离开帝国的周诸侯(见 ZhouEraSystem.Detach)；
//   · 母部落是共主或奉某共主，分出去的新部落奉同一个共主；母部落两者都不是，就此结成部落联盟、自为共主；
//   · 部落联盟名下原有的成员(称帝时并入的、以前分出去并入的)每月离开帝国，改为奉它为共主；只结了同盟的部落，
//     由同盟里最强的一家结成部落联盟，其余奉它为共主；
//   · 共主改行宗法分封(政体不再是原始政体)：奉它为共主的部落并入帝国，成为分封的诸侯(西周)；共主亡了就各自独立；
//   · 共事同一共主的部落与共主同一色系(像周室诸侯，见 KingdomColorService.ApplyTribalFamilies)；
//   · 共主不打共事它的部落，部落也不打共主(用户："怎么主国家就可以打下面的小弟了，这也不合理……应该只打不服的或者脱离联盟的")，
//     共主只征伐不服的、脱离的；部落之间照样可以互相攻伐(见 Bound)；铭牌标"共事某某"；
//   · 脱离：国力到了共主六成以上、或对共主怨恨极深的部落，每年约有一成二的机会不再共事、自立一方(见 Breakaway)；
//   · 上限(用户："另外共主联盟部落也可以存在上限")：共主管得过来的部落数按原始改革的阶段，环壕聚落 3 部、部落中心 5 部、
//     早期城址 7 部；离共主都城 60 格以外(每进一段再远 20 格)的不共事。新分出的部落遇上共主已满或太远就自立；
//     已经超出的每月放走离得最远的一部(见 Overflow)。
// 原始改革制度按文化施行，各部落一样跟着改(由谁推见下 2026-10-26 改)。
// 2026-10-17 改(用户："可以先一开始每个城邦都可以称帝, 后缀就是单纯的部落(不要方国这个称呼了)""之后发起战争赢了纳入另一个部落之后
// 才叫部落联合"；问答选"各自独立成部落""成为共事的成员部落")：
//   · 分出去的新部落各自独立，不再自动共事母部落；每个独立的部落自立为"某某部落"(帝国，FormTribes，10-26 去掉)，能推进原始改革；
//   · 部落之间不再因结盟、分迁就结成部落联盟(去掉 FoldAlliances、OverlordFor)；要本文化施行「部落联合」以后打赢，
//     败者才奉胜者为共主(见 TribalWarSystem、Incorporate)，胜者这才称部落联盟；没有共事的部落时只称部落。
// 2026-10-26 改(用户："原始部落帝国太多会造成卡顿"，选"部落不再是帝国"、"不改玩法的修补")：
//   · 单个部落就是普通的国(国名、铭牌仍叫"某某部落"，见 Origin/OfficialType.csv)，不再自立为帝国(去掉 FormTribes)；
//     原始改革由各部自己推(王国推制度，见 InstitutionSystem.Realm.cs；推制度的开关关了部落照样推)，首领照原版世袭；
//   · 部落联盟才是帝国：打赢、第一次收了共事的部落时才立(见 Incorporate)，禅让、议事会只在联盟里有；
//     原始政体的国不走称帝的路(见 EmpireFormationService.MeetsBaseRequirements)，只能这样成帝国；
//   · 旧档里一部自立的部落帝国读档后静默撤去(见 DissolveSettledTribes)；
//   · 共事关系按月缓存成反查表(见 VassalIndex)，每月整理里的整图重画并成末尾一次(见 Redraw)。
public static class TribalFissionService
{
    public static bool IsTribal(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && RegimeManager.IsPrimitive(kingdom.GetRegime()?.type);

    // 原始政体的部落联盟(共主是原始政体的帝国)
    private static bool IsConfederation(Empire empire) =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() && IsTribal(empire.CoreKingdom);

    // 这一部奉谁为共主：帝国之外、记着的部落联盟还在且仍是原始政体；不是返回 null
    public static Empire OverlordOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.GetEmpire() != null || ModClass.EMPIRE_MANAGER == null) return null;
        long id = kingdom.GetOrCreate().tribal_overlord_id;
        if (id < 0) return null;
        Empire empire = ModClass.EMPIRE_MANAGER.get(id);
        return IsConfederation(empire) && empire.CoreKingdom != kingdom ? empire : null;
    }

    // 原始改革的阶段：0 环壕聚落、1 部落中心、2 早期城址(本文化原始政体的节点走到几成；城市精细图也按它画，见 PureMapPatch.Tribal)
    public static int Stage(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return 0;
        List<string> nodes = InstitutionDefinitionRegistry.GetForLine(InstitutionSystem.GetCultureLine(culture))
            .Where(node => node?.regime is "Origin" or "HuaxiaOrigin").Select(node => node.id).ToList();
        int best = -1;
        for (int i = 0; i < nodes.Count; i++)
            if (InstitutionSystem.IsEnacted(culture, nodes[i])) best = i;
        if (nodes.Count <= 1 || best <= 0) return 0;
        float share = best / (float)(nodes.Count - 1);
        return share < 0.4f ? 0 : share < 0.9f ? 1 : 2;
    }

    private const int CapacityBase = 3, CapacityPerStage = 2;
    private const float ReachBase = 60f, ReachPerStage = 20f;

    private static int StageOf(Empire overlord) => Stage(CultureService.GetRealmCulture(overlord?.CoreKingdom));

    // 共主管得过来几部、多远以内
    public static int Capacity(Empire overlord) => CapacityBase + CapacityPerStage * StageOf(overlord);

    public static float Reach(Empire overlord) => ReachBase + ReachPerStage * StageOf(overlord);

    // 各部落联盟(共主)
    public static List<Empire> Confederations() =>
        ModClass.EMPIRE_MANAGER?.Where(IsConfederation).ToList() ?? new List<Empire>();

    // 共事关系的反查表(共主 id → 共事它的部落，按国家列表的先后)，按月缓存(用户 2026-10-26 嫌卡)：以前 VassalsOf 每次都把
    // 全部王国扫一遍，放走(Overflow)、议事会、禅让、帝号逐个部落联盟地调，是部落联盟数 × 王国数。
    // 取用时再按 OverlordOf 核对一遍：已不共事的(脱离、放走、亡了、入了帝国、共主不在或改了政体)当场剔掉，
    // 所以改成不共事的地方不必作废；新奉共主的(Submit)调 InvalidateVassals 作废，下回取用时重建
    private static Dictionary<long, List<Kingdom>> _vassalIndex;
    private static double _vassalIndexAt = -1d;

    public static void InvalidateVassals() => _vassalIndex = null;

    // 换世界、读档时清掉(见 SaveManagerPatch)
    public static void ResetWorldState() => _vassalIndex = null;

    private static Dictionary<long, List<Kingdom>> VassalIndex()
    {
        double now = World.world?.getCurWorldTime() ?? -1d;
        if (_vassalIndex != null && now >= _vassalIndexAt && Date.getMonthsSince(_vassalIndexAt) < 1) return _vassalIndex;
        var map = new Dictionary<long, List<Kingdom>>();
        if (World.world?.kingdoms != null)
            foreach (Kingdom kingdom in World.world.kingdoms.list)
            {
                Empire overlord = OverlordOf(kingdom);
                if (overlord == null) continue;
                if (!map.TryGetValue(overlord.id, out List<Kingdom> list)) map[overlord.id] = list = new List<Kingdom>();
                list.Add(kingdom);
            }
        _vassalIndex = map;
        _vassalIndexAt = now;
        return map;
    }

    // 各共主名下共事的部落(配色用，见 KingdomColorService)
    public static Dictionary<long, List<Kingdom>> VassalMap()
    {
        var map = new Dictionary<long, List<Kingdom>>();
        if (World.world?.kingdoms == null || ModClass.EMPIRE_MANAGER == null) return map;
        foreach (KeyValuePair<long, List<Kingdom>> entry in VassalIndex().ToList())
        {
            List<Kingdom> vassals = VassalsOf(ModClass.EMPIRE_MANAGER.get(entry.Key));
            if (vassals.Count > 0) map[entry.Key] = vassals;
        }
        return map;
    }

    // 共事 overlord 的部落
    public static List<Kingdom> VassalsOf(Empire overlord)
    {
        if (overlord == null || World.world?.kingdoms == null ||
            !VassalIndex().TryGetValue(overlord.id, out List<Kingdom> cached)) return new List<Kingdom>();
        return cached.Where(kingdom => OverlordOf(kingdom) == overlord).ToList();
    }

    // 离共主都城多远(格)
    private static float DistanceTo(Empire overlord, Kingdom kingdom)
    {
        WorldTile seat = overlord?.CoreKingdom?.capital?.getTile(), there = kingdom?.capital?.getTile();
        return seat == null || there == null ? 0f : UnityEngine.Mathf.Sqrt(Toolbox.SquaredDistTile(seat, there));
    }

    // 共主还收不收得下这一部(已满、太远就不收)
    private static bool CanHold(Empire overlord, Kingdom tribe) =>
        overlord != null && VassalsOf(overlord).Count < Capacity(overlord) && DistanceTo(overlord, tribe) <= Reach(overlord);

    // 共主与共事它的部落(部落之间不算)：彼此不宣战。a 为开战的一方(WarGate、开战谋划、统一战争都按此传)：
    // 屡次不来会盟、记为不臣的部落，共主可以讨伐(只放开共主打它；部落打共主照旧不行，见 TribalAssemblySystem)
    public static bool Bound(Kingdom a, Kingdom b) =>
        a != null && b != null && a != b &&
        (OverlordOf(a) is Empire ea && (b == ea.CoreKingdom || b.GetEmpire() == ea) ||
         OverlordOf(b) is Empire eb && (a == eb.CoreKingdom || a.GetEmpire() == eb) &&
         !(a == eb.CoreKingdom && TribalAssemblySystem.DefiantTo(b, eb)));

    // 铭牌下标的"共事某某"：共主那一部的名字(不带邦、方国这类后缀)
    public static string OverlordName(Empire overlord)
    {
        string name = overlord?.CoreKingdom?.data?.name;
        if (string.IsNullOrWhiteSpace(name)) return overlord?.GetEmpireName() ?? "";
        int cut = name.IndexOf('\u200a');
        return cut > 0 ? name.Substring(0, cut) : name;
    }

    // CityManager.buildNewCity 之后(见 CityPatch.CityBuilt)、国家移民安置完毕后(见 StateSettlementSystem)调用
    public static void OnCityBuilt(City city, Actor founder)
    {
        Kingdom mother = city?.kingdom;
        if (!IsTribal(mother) || mother.cities == null || mother.cities.Count <= 1 || city == mother.capital ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(mother)) return;
        Actor leader = Fit(founder, city) ? founder : Fit(city.leader, city) ? city.leader : CityStateService.FindLocalLeader(city, mother.king);
        if (leader != null) Split(city, mother, leader);
    }

    // 每月(RoyalHouseSystem)：名下不止一座城的部落分出一座(离都城最远的先分)；部落联盟的成员离开帝国、奉其为共主；
    // 旧档的部落帝国撤去(见 DissolveSettledTribes)；共主改行分封的，奉它的部落并入为诸侯
    public static void EnforceOneCity()
    {
        if (World.world?.kingdoms == null) return;
        // 这一轮各步要的整图重画并到末尾，至多一次(见 Redraw)
        _redrawBatch = true;
        try
        {
            MonthlyPass();
        }
        finally
        {
            _redrawBatch = false;
            if (_redrawPending)
            {
                _redrawPending = false;
                World.world?.zone_calculator?.dirtyAndClear();
            }
        }
    }

    private static void MonthlyPass()
    {
        foreach (Kingdom mother in World.world.kingdoms.list.ToList())
        {
            if (!IsTribal(mother) || mother.capital == null || mother.cities == null || mother.cities.Count <= 1 ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(mother)) continue;
            WorldTile seat = mother.capital.getTile();
            City city = mother.cities.Where(candidate => candidate?.data != null && !candidate.isRekt() && candidate != mother.capital &&
                                                         !candidate.GetOrCreate().placed_by_hand)
                .OrderByDescending(candidate => seat == null || candidate.getTile() == null ? 0f : Toolbox.SquaredDistTile(candidate.getTile(), seat))
                .FirstOrDefault();
            if (city == null) continue;
            if (!city.hasLeader()) CityPopulationSystem.EnsureLeader(city);
            Actor leader = Fit(city.leader, city) ? city.leader : CityStateService.FindLocalLeader(city, mother.king);
            if (leader != null) Split(city, mother, leader);
        }
        try
        {
            Migrate();
            TribalCouncilSystem.Monthly();
            ShanrangSystem.Monthly();
            DetachMembers();
            DissolveSettledTribes();
            Enfeoff();
            Breakaway();
            Overflow();
            // 共事同一共主的部落同一色系(见 KingdomColorService.ApplyTribalFamilies)
            if (KingdomColorService.ApplyTribalFamilies()) Redraw();
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][部落分迁] 共主整理失败: {exception.Message}");
        }
    }

    private static bool Fit(Actor actor, City city) =>
        actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && !actor.isKing() && actor.city == city &&
        actor.isUnitFitToRule();

    private static void Split(City city, Kingdom mother, Actor leader)
    {
        try
        {
            string motherName = mother.GetKingdomName();
            Kingdom tribe = city.makeOwnKingdom(leader);
            if (tribe == null || tribe.isRekt()) return;
            // 新部落照母部落的政体(各文化的原始政体，或华夏原始政体)
            RegimeType primitive = mother.GetRegime()?.type == RegimeType.HuaxiaOrigin ? RegimeType.HuaxiaOrigin : RegimeType.Origin;
            if (tribe.GetRegime()?.type != primitive)
            {
                tribe.SetRegimeType(primitive);
                tribe.LoadRegime();
                tribe.SystemChange();
            }
            // 分出去的新部落各自独立(要打赢才能纳入，见 TribalWarSystem)
            string content = string.Format(LM.Get("tribal_fission_history"), motherName, city.GetCityName(), tribe.GetKingdomName());
            LogService.LogInfo("[EmpireCraft][部落分迁] " + content);
            (mother.GetEmpire() ?? OverlordOf(mother))?.RecordHistory(directContent: content, kingdomId: tribe.id);
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][部落分迁] {mother.GetKingdomName()} 分出新部落失败: {exception.Message}");
        }
    }

    // 奉 overlord 为共主：退出同盟、不进帝国，记下共主；改奉新共主的记下归附的时间(共事年数定爵，见 Enfeoff；
    // 共主讨伐不臣、重新收回的照旧从原来算)
    private static void Submit(Kingdom tribe, Empire overlord)
    {
        if (tribe?.data == null || overlord == null) return;
        if (tribe.hasAlliance()) tribe.getAlliance().leave(tribe);
        KingdomExtension.KingdomExtraData data = tribe.GetOrCreate();
        if (data.tribal_overlord_id != overlord.id || data.tribal_submitted_at < 0d)
            data.tribal_submitted_at = World.world?.getCurWorldTime() ?? -1d;
        data.tribal_overlord_id = overlord.id;
        InvalidateVassals();
        tribe.GetRegime()?.SetAllowDiplomacy(true);
    }

    // 结成部落联盟(head 为共主，帝国里只有它自己)。部落联盟是原始政体成帝国的唯一途径(单个部落不再自立为帝国，
    // 以前的 FormTribes 10-26 去掉)。帝国与同盟不兼容，共主之部退出同盟；盟友不跟着奉它为共主——
    // 10-17 起部落不因结盟结成部落联盟，以前每部都是帝国、没有同盟，碰不到这里；如今部落是国、会结盟，照旧不并盟友
    private static Empire Confederation(Kingdom head)
    {
        if (head.IsInEmpire() || ModClass.EMPIRE_MANAGER == null) return null;
        Alliance alliance = head.hasAlliance() ? head.getAlliance() : null;
        if (alliance != null)
        {
            alliance.leave(head);
            alliance = null;
        }
        Empire empire = ModClass.EMPIRE_MANAGER.NewEmpire(head, allowCultureRival: true);
        if (empire == null || empire.isRekt()) return null;
        empire.data.Mandate = EmpireFormationService.GetInitialMandate(EmpireFormationRoute.Acclamation);
        head.GetOrCreate().tribal_overlord_id = -1L;
        string culture = CultureService.GetRealmCulture(head);
        var joined = new List<Kingdom>();
        if (alliance != null && alliance.isAlive())
        {
            foreach (Kingdom member in alliance.kingdoms_hashset.ToList())
                if (member != null && member != head && IsTribal(member) && !member.IsInEmpire() &&
                    string.Equals(CultureService.GetRealmCulture(member), culture, global::System.StringComparison.Ordinal))
                    joined.Add(member);
            // 帝国与同盟不兼容
            World.world.alliances.dissolveAlliance(alliance);
        }
        foreach (Kingdom member in joined) Submit(member, empire);
        string content = string.Format(LM.Get(joined.Count > 0 ? "tribal_confederation_history" : "tribal_tribe_formed_history"),
            head.GetKingdomName(), empire.GetEmpireName());
        LogService.LogInfo("[EmpireCraft][部落分迁] " + content + (joined.Count > 0 ? $"(原同盟 {joined.Count} 家奉为共主)" : ""));
        empire.RecordHistory(directContent: content, kingdomId: head.id);
        Redraw();
        return empire;
    }

    // 部落联盟名下的成员离开帝国、奉其为共主(同战国周诸侯离开帝国，见 ZhouEraSystem.Detach)
    private static void DetachMembers()
    {
        if (ModClass.EMPIRE_MANAGER == null) return;
        bool changed = false;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (!IsConfederation(empire) || empire.kingdoms_list == null) continue;
            int count = 0;
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (member?.data == null || member.isRekt() || member == empire.CoreKingdom) continue;
                KingdomExtension.KingdomExtraData data = member.GetOrCreate();
                var rank = data.kingdomType;
                empire.leave(member, pRecalc: false);
                data.kingdomType = rank;
                Submit(member, empire);
                count++;
            }
            if (count == 0) continue;
            empire.recalculate();
            changed = true;
            LogService.LogInfo($"[EmpireCraft][部落分迁] {empire.GetEmpireName()} 的 {count} 部离开帝国，奉其为共主");
        }
        if (changed) Redraw();
    }

    // 旧档里的部落帝国(以前每个独立的部落都自立为"某某部落"帝国，formed_as_tribe；10-26 起部落不再是帝国)，每月(见 EnforceOneCity)、
    // 读档后(见 AfterLoad)各清一遍：
    //   · 还是原始政体、帝国里只有自己、也没有共事的部落：静默撤去，回到普通的国——不记已亡帝国、不写史书、不发提示，
    //     进行中的原始改革交回本国，随它新建的帝国法理一并销毁(见 EmpireManager.dissolveEmpire 的 silent)；
    //   · 有了共事的部落、或帝国里还有别的国：已是部落联盟，不再算一部自立；
    //   · 走出了原始阶段、又没有并入别的国(帝国里只有自己)：照旧撤掉帝国(记进已亡帝国)，回到普通的国——只有共事部落、诸侯的
    //     才留作帝国(否则进了宗法分封，每个小部落都成了一个一城的周室)。读档时不撤这种，留给每月
    private static void DissolveSettledTribes(bool loading = false)
    {
        if (ModClass.EMPIRE_MANAGER == null) return;
        int silent = 0;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || !empire.data.formed_as_tribe || empire.isRekt() || empire.IsArchived()) continue;
            Kingdom core = empire.CoreKingdom;
            if (core == null || EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(core)) continue;
            bool tribal = IsTribal(core);
            if (empire.kingdoms_list?.Any(member => member != null && member != core) == true ||
                tribal && VassalsOf(empire).Count > 0)
            {
                empire.data.formed_as_tribe = false;
                continue;
            }
            if (!tribal && loading) continue;
            try
            {
                ModClass.EMPIRE_MANAGER.dissolveEmpire(empire, silent: tribal);
                if (tribal) silent++;
            }
            catch (global::System.Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][部落分迁] {core.GetKingdomName()} 撤去部落帝号失败: {exception.Message}");
            }
        }
        if (silent == 0) return;
        LogService.LogInfo($"[EmpireCraft][部落分迁] 旧档一部自立的部落帝国 {silent} 个静默撤去，各回本国(部落不再是帝国)");
        if (!loading) Redraw();
    }

    // 读档后(见 DataManager.LoadAll，帝国、法理、制度都已读进来)：旧档的部落帝国先清一遍，不等第一个月
    public static void AfterLoad()
    {
        InvalidateVassals();
        DissolveSettledTribes(loading: true);
        InvalidateVassals();
    }

    // 胜者所在的部落联盟：胜者共事的共主，或胜者自己是共主；都不是返回 null(战功记在这里，见 TribalWarSystem.Concede)
    public static Empire LeagueOf(Kingdom kingdom)
    {
        Empire overlord = OverlordOf(kingdom);
        if (overlord != null) return overlord;
        Empire own = kingdom?.GetEmpire();
        return IsConfederation(own) && own.CoreKingdom == kingdom ? own : null;
    }

    // 两国都城相距多远(格)
    private static float Distance(Kingdom a, Kingdom b)
    {
        WorldTile here = a?.capital?.getTile(), there = b?.capital?.getTile();
        return here == null || there == null ? 0f : UnityEngine.Mathf.Sqrt(Toolbox.SquaredDistTile(here, there));
    }

    // 部落联合以后打赢(见 TribalWarSystem)：败者奉胜者(或胜者所共事的共主)为共主；胜者还不是部落联盟、又收得下败者才立
    // (部落联盟才是帝国，第一次收了共事的部落时才立)。败者原来的部落(联盟)撤掉，共事它的部落改奉新共主(管不过来、太远的自立)，
    // 败者与随它改奉的部落的法理并进共主的帝国法理(用户 2026-10-26 选"法理随共事转移"，见 AbsorbTitles)。
    // 共主已满、太远就不纳入(只拿赔款)；败者本来就共事这个共主的(共主讨伐不臣，见 TribalAssemblySystem)不算新增，满额照样收回。
    // followed：随败者一同改奉新共主的部数(记战功用)
    public static bool Incorporate(Kingdom winner, Kingdom loser) => Incorporate(winner, loser, out _);

    public static bool Incorporate(Kingdom winner, Kingdom loser, out int followed)
    {
        followed = 0;
        if (!IsTribal(winner) || !IsTribal(loser) || ModClass.EMPIRE_MANAGER == null) return false;
        Empire overlord = LeagueOf(winner);
        if (overlord == null)
        {
            // 还没有共事的部落，不会已满，只看路程(按本文化的原始阶段，同 Reach)
            if (winner.IsInEmpire() ||
                Distance(winner, loser) > ReachBase + ReachPerStage * Stage(CultureService.GetRealmCulture(winner))) return false;
            overlord = Confederation(winner);
        }
        if (overlord == null || overlord.CoreKingdom == loser) return false;
        bool already = OverlordOf(loser) == overlord;
        if (!already && !CanHold(overlord, loser)) return false;
        Empire previous = loser.GetEmpire();
        bool ledLeague = previous != null && previous.CoreKingdom == loser;
        // 败者原来的部落帝国(旧档)、部落联盟的帝国法理：撤去之前先记下，法理随它并过来
        EmpireCore previousCore = ledLeague ? EmpireCoreManager.Get(previous) : null;
        List<Kingdom> followers = ledLeague && IsConfederation(previous) ? VassalsOf(previous) : new List<Kingdom>();
        if (previous != null)
        {
            if (ledLeague) ModClass.EMPIRE_MANAGER.dissolveEmpire(previous);
            else previous.leave(loser);
        }
        Submit(loser, overlord);
        AbsorbTitles(overlord, loser, previousCore);
        foreach (Kingdom follower in followers)
        {
            follower.GetOrCreate().tribal_overlord_id = -1L;
            if (follower == overlord.CoreKingdom || !CanHold(overlord, follower)) continue;
            Submit(follower, overlord);
            AbsorbTitles(overlord, follower, null);
            followed++;
        }
        InvalidateVassals();
        // 有了共事的部落：称号由"部落"改称"部落联盟"(见 DynastyNaming.HuaxiaOriginEmpireKey)，不再算一部自立
        overlord.data.empire_type_key = "";
        overlord.data.formed_as_tribe = false;
        Redraw();
        return true;
    }

    // 法理随共事转移(用户 2026-10-26 选"法理随共事转移")：共事部落的王国法理(主法理)并进共主的帝国法理；oldCore 是它原来的部落帝国(旧档)、
    // 部落联盟的帝国法理，里面的法理一并并过来(EmpireCoreManager.AddTitleToCore 会把法理从原来的帝国法理里移出)，并空了、
    // 没有帝国用着的帝国法理随即销毁。以前共主每年只按帝国成员收法理(见 EmpireCoreManager.TryAbsorbTitle)，共事的部落在帝国之外，
    // 法理又各在自立时建的帝国法理里(那里遇上已在别的帝国法理里的一律不收)，永远收不进共主、周室。
    // 只收同一天下的法理；别的现存帝国(部落联盟除外)法理里的、军阀临时法理、伪法理里的不动(见 YieldsTitle)。固定法理时不收
    public static void AbsorbTitles(Empire overlord, Kingdom kingdom, EmpireCore oldCore)
    {
        if (overlord?.data == null || ModClass.KINGDOM_TITLE_FREEZE) return;
        EmpireCore core = EmpireCoreManager.Get(overlord);
        if (core == null) return;
        var titles = new List<KingdomTitle>();
        if (oldCore != null && oldCore != core) titles.AddRange(EmpireCoreManager.GetTitles(oldCore));
        KingdomTitle main = kingdom?.GetMainTitle();
        if (main != null && !titles.Contains(main)) titles.Add(main);
        string culture = CultureService.GetEmpireDefaultCulture(overlord);
        var emptied = new List<EmpireCore>();
        foreach (KingdomTitle title in titles)
        {
            if (title == null || title.isRekt() || EmpireCoreManager.ContainsTitle(core, title)) continue;
            string titleCulture = CultureService.GetEffectiveTitleCulture(title);
            if (CultureService.IsValidCulture(culture) && CultureService.IsValidCulture(titleCulture) &&
                !CultureService.SameKin(culture, titleCulture)) continue;
            EmpireCore from = EmpireCoreManager.LawfulCoreOf(title);
            if (from != null && from != oldCore && !Releases(from, overlord)) continue;
            if (EmpireCoreManager.AddTitleToCore(core, title) && from != null && !emptied.Contains(from)) emptied.Add(from);
        }
        foreach (EmpireCore from in emptied) DropEmptyCore(from);
    }

    // 别的帝国法理里的法理能不能并给 absorber：那个帝国法理已没有现存帝国用着(部落帝国撤了、部落联盟亡了留下的)或只有部落联盟用着，
    // 同一天下，不是军阀临时法理、争正统的伪法理
    private static bool Releases(EmpireCore other, Empire absorber)
    {
        if (other == null || absorber?.data == null || other.warlord_parent_core_id > 0 || other.false_core_against_empire_id > 0)
            return false;
        string mine = CultureService.GetEmpireDefaultCulture(absorber);
        if (CultureService.IsValidCulture(mine) && CultureService.IsValidCulture(other.default_culture) &&
            !CultureService.SameKin(mine, other.default_culture)) return false;
        return EmpireCoreManager.GetEmpires(other).All(empire => empire == absorber || empire.IsArchived() || IsConfederation(empire));
    }

    // 帝国每年收成员法理时(见 EmpireCoreManager.TryAbsorbTitle)对部落时代放开：部落联盟、周室收共事部落、旧部落帝国留下的法理
    // (Releases)，不因法理已在别的帝国法理里就拒收；别的帝国照旧
    public static bool YieldsTitle(EmpireCore other, Empire absorber) =>
        (IsConfederation(absorber) || ZhouEraSystem.Applies(absorber)) && Releases(other, absorber);

    // 法理并空了、也没有帝国用着的帝国法理销毁(它作主的天下交给别的成员，见 EmpireCoreManager.DestroyEmpireCore)
    public static void DropEmptyCore(EmpireCore core)
    {
        if (core == null || EmpireCoreManager.Get(core.id) != core || EmpireCoreManager.GetTitles(core).Count > 0 ||
            EmpireCoreManager.GetEmpires(core).Count > 0) return;
        EmpireCoreManager.DestroyEmpireCore(core);
    }

    // 共主改了政体、或已不在：奉它的部落并入为它的臣属，或各自独立(用户 2026-10-26 选"其他文明也延续""爵位按资历")：
    //   · 周制(宗法分封)：并入为诸侯，按共事年数定爵——满 SeniorYears 年、或同批并入至少 EarlySeniorBatch 部时最早共事的
    //     EarlySeniors 部为侯，MiddleYears 年以上为伯，不满为子(定下的爵位见 ZhouEraSystem.SeniorPeerage)；宗室照旧为公(政体配置)；
    //     进宗法分封时定下的照定下的(撤了帝国并过来的部落联盟核心为侯、王畿的郡，见 SettleRoyalHouse)；
    //     按资历为侯的(长期共事)当即赐胙(见 ZhouEraSystem.GrantFoundingZuo)；
    //   · 日本律令制：并入为世袭的国(国造；律令制里世袭、有主法理的成员即 LvLing_kingdom，日本称"国")，不通外交；
    //   · 阿拉伯(哈里发国)：并入为世袭的埃米尔(带着领地入伙的，见 EmirateSystem.IsHereditaryEmir)，世袭从并入时算起；
    //     不设考选的行省——部落首领自己持有主法理(月度核对主法理会还给他，见 KingdomExtension.ReconcileMainTitle)，
    //     有主法理的行省永远坐大不了(见 EmirateSystem.Tick)；
    //   · 游牧(汗国)：并入为兀鲁思(世袭、自领兵马、低税，同 EnfeoffmentHelper.TryEnfeoffActor 分兀鲁思)；
    //   · 别的政体照旧各自独立。
    // 并入的部落法理并进帝国法理(见 AbsorbTitles)。以前除了周制一律清掉共主、各自独立，部落时代的战果全丢
    private const int SeniorYears = 30, MiddleYears = 10, EarlySeniors = 2, EarlySeniorBatch = 3;
    // 进宗法分封时定下的(见 SettleRoyalHouse)：撤了帝国并过来的部落联盟核心(侯)、改为王室直辖的郡(王畿)
    private const string PinLeague = "league", PinDomain = "domain";

    // 归附的时间：旧档没记的、记错的按现在
    private static double SubmittedAt(Kingdom kingdom)
    {
        double at = kingdom.GetOrCreate().tribal_submitted_at, now = World.world.getCurWorldTime();
        return at < 0d || at > now ? now : at;
    }

    private static int YearsServed(Kingdom kingdom) => Date.getYearsSince(SubmittedAt(kingdom));

    private static void Enfeoff()
    {
        if (ModClass.EMPIRE_MANAGER == null) return;
        bool changed = false;
        double now = World.world.getCurWorldTime();
        // 各帝国这一批要并入的部落
        var batches = new Dictionary<long, List<Kingdom>>();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
            if (data.tribal_overlord_id < 0) continue;
            Empire empire = ModClass.EMPIRE_MANAGER.get(data.tribal_overlord_id);
            if (kingdom.GetEmpire() != null || kingdom.IsEmpire() || empire?.data == null || empire.isRekt() || empire.IsArchived() ||
                empire.CoreKingdom == kingdom)
            {
                data.tribal_overlord_id = -1L;
                data.tribal_peerage = "";
                changed = true;
                continue;
            }
            if (IsConfederation(empire))
            {
                // 旧档没记归附时间的，从读档后第一次看到时算
                if (data.tribal_submitted_at < 0d) data.tribal_submitted_at = now;
                continue;
            }
            if (!batches.TryGetValue(empire.id, out List<Kingdom> batch)) batches[empire.id] = batch = new List<Kingdom>();
            batch.Add(kingdom);
        }
        foreach (KeyValuePair<long, List<Kingdom>> entry in batches)
        {
            Empire empire = ModClass.EMPIRE_MANAGER.get(entry.Key);
            List<Kingdom> tribes = entry.Value.OrderBy(SubmittedAt).ThenBy(tribe => tribe.id).ToList();
            List<Kingdom> unpinned = tribes.Where(tribe => string.IsNullOrEmpty(tribe.GetOrCreate().tribal_peerage)).ToList();
            var early = new HashSet<Kingdom>(unpinned.Count >= EarlySeniorBatch ? unpinned.Take(EarlySeniors) : Enumerable.Empty<Kingdom>());
            var honoured = new List<Kingdom>();
            foreach (Kingdom tribe in tribes)
            {
                tribe.GetOrCreate().tribal_overlord_id = -1L;
                changed = true;
                try
                {
                    if (Vassalize(empire, tribe, early.Contains(tribe), out bool senior) && senior) honoured.Add(tribe);
                }
                catch (global::System.Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][部落分迁] {tribe.GetKingdomName()} 并入 {empire?.GetEmpireName()} 失败: {exception.Message}");
                }
            }
            if (honoured.Count > 0) ZhouEraSystem.GrantFoundingZuo(empire, honoured);
        }
        if (!changed) return;
        InvalidateVassals();
        Redraw();
    }

    // 并入 empire 为臣属(见 Enfeoff)；返回是否并入了。early：同批里最早共事的；senior：周制里按资历受封为侯(长期共事，赐胙)
    private static bool Vassalize(Empire empire, Kingdom tribe, bool early, out bool senior)
    {
        senior = false;
        Kingdom core = empire?.CoreKingdom;
        if (core == null) return false;
        RegimeType type = core.GetRegime()?.type ?? RegimeType.Origin;
        string line = InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(core));
        bool zhou = type == RegimeType.ZhouFeudalism, kuni = type == RegimeType.LvLing && line == "Japan",
            emir = type == RegimeType.Arabic, ulus = type == RegimeType.YouMu;
        KingdomExtension.KingdomExtraData data = tribe.GetOrCreate();
        string pin = data.tribal_peerage;
        data.tribal_peerage = "";
        // 别的政体：各自独立
        if (!zhou && !kuni && !emir && !ulus) return false;
        int years = YearsServed(tribe);
        // 进不去的(神力放出的保护期、曾反叛过它，见 Empire.join)照旧独立
        empire.join(tribe, pForce: true);
        if (tribe.GetEmpire() != empire) return false;
        if (tribe.GetRegime()?.type != type)
        {
            tribe.SetRegimeType(type);
            tribe.LoadRegimeKeepingRole();
            tribe.SystemChange();
        }
        AbsorbTitles(empire, tribe, null);
        Regime regime = tribe.GetRegime();
        string key;
        if (zhou && pin == PinDomain)
        {
            // 王畿：王室直辖的郡(官员治理、不世袭、不通外交，同郡国并行的郡；春秋战国也仍归王畿，见 ZhouEraSystem.Detach、WeakRoyalty)
            regime?.SetAllowDiplomacy(false);
            regime?.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            key = "tribal_enfeoffed_jun_history";
        }
        else if (zhou)
        {
            regime?.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            bool league = pin == PinLeague;
            senior = !league && (years >= SeniorYears || early);
            KingdomType rank = league || senior ? KingdomType.ZhouFeudalism_hou
                : years >= MiddleYears ? KingdomType.ZhouFeudalism_bo : KingdomType.ZhouFeudalism_zi;
            // 宗室照旧为公(政体配置的 empire_royal)，不记资历定的爵位
            bool royalKin = SpecificClanManager.SameLineage(tribe.GetSpecificClan(), empire.EmpireSpecificClan);
            if (!royalKin) data.tribal_peerage = rank.ToString();
            key = royalKin ? "tribal_enfeoffed_history"
                : league ? "tribal_enfeoffed_league_history"
                : rank == KingdomType.ZhouFeudalism_hou ? "tribal_enfeoffed_hou_history"
                : rank == KingdomType.ZhouFeudalism_bo ? "tribal_enfeoffed_bo_history"
                : "tribal_enfeoffed_zi_history";
        }
        else if (kuni)
        {
            regime?.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            regime?.SetAllowDiplomacy(false);
            regime?.SetTaxLevel(TaxLevel.Low);
            key = "tribal_kuni_history";
        }
        else if (emir)
        {
            regime?.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            regime?.SetAllowDiplomacy(true);
            regime?.SetAllowSupportCenterArmy(false);
            regime?.SetTaxLevel(TaxLevel.Low);
            data.emirate_hereditary_at = World.world.getCurWorldTime();
            key = "tribal_emir_history";
        }
        else
        {
            regime?.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            regime?.SetAllowSupportCenterArmy(false);
            regime?.SetTaxLevel(TaxLevel.Low);
            key = "tribal_ulus_history";
        }
        tribe.ReconcileMainTitle();
        empire.SynchronizeLandedLegalTitles(tribe);
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(tribe);
        string content = string.Format(LM.Get(key), tribe.GetKingdomName(), empire.GetEmpireName(), years);
        LogService.LogInfo("[EmpireCraft][部落分迁] " + content);
        empire.RecordHistory(directContent: content, kingdomId: tribe.id);
        return true;
    }

    // 奉谁为共主：不论那个共主还是不是原始政体(推行宗法分封的共主已改了政体，OverlordOf 不再认它，部落要到 Enfeoff 才并入)
    private static Empire Liege(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.GetEmpire() != null || ModClass.EMPIRE_MANAGER == null) return null;
        long id = kingdom.GetOrCreate().tribal_overlord_id;
        Empire empire = id < 0 ? null : ModClass.EMPIRE_MANAGER.get(id);
        return empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom != null &&
               empire.CoreKingdom != kingdom ? empire : null;
    }

    // 共事 empire 的部落(Liege)，连同帝国名下的成员
    private static List<Kingdom> Followers(Empire empire)
    {
        var result = World.world.kingdoms.list.Where(kingdom => Liege(kingdom) == empire).ToList();
        foreach (Kingdom member in empire.kingdoms_list?.ToList() ?? new List<Kingdom>())
            if (member?.data != null && !member.isRekt() && member != empire.CoreKingdom && !result.Contains(member)) result.Add(member);
        return result;
    }

    // 共事的部落都已离去、帝国里也只有自己的部落联盟(按资历定下的周室除外，见 Migrate)
    private static bool Deserted(Empire empire) =>
        IsConfederation(empire) && !(empire.data.zhou?.royal_settled_at >= 0d) && Followers(empire).Count == 0;

    // 按资历定唯一王室(用户 2026-10-26 选"按资历定唯一王室""王畿不止一城")：本文化第一次施行宗法分封——推行改革的那一家当场改了周制
    // (见 InstitutionSystem.CompleteReform)，其余部落还没改(见 Migrate)——在同一天下的部落联盟与独立部落里选出唯一的周室
    // (以前没有判定，几个联盟同时改了周制就成了几个周室)：
    //   · 候选：各部落联盟(连同推行改革、已改周制的那家；它已不是原始政体，照样算，不因此落选)与不共事谁的独立部落；
    //     分数 = 共事部落数 × RoyalVassalWeight + 战功 × RoyalMeritWeight + 城数，同分先推行改革的、再先立的；
    //   · 周室是部落联盟就用它的帝国；是独立部落就给它立帝国(推戴，天命同部落联盟)；
    //   · 其余部落联盟：离周室都城 RoyalReach 格以内、核心国力不到周室 RoyalRivalShare 的，撤去帝国(不记已亡帝国)，法理并进周室，
    //     核心改奉周室、受封为侯，原来共事它的部落一并改奉周室(按原来的共事年数受封，见 Enfeoff)；太远或太强的不服王化：撤去帝国，
    //     共事它的部落各自独立，等周室讨伐收服(见 ZhouEraSystem.Unsubmitted、SubdueTarget)；独立部落照旧独立，同样算不服王化；
    //   · 王畿：周室自己最早共事、离都城最近的一至三部(要并入周室的部落每 RoyalDomainPer 部多一郡)改为王室直辖的郡(见 Enfeoff)。
    // 只在本文化施行宗法分封 SettleWindowYears 年内、本天下还没有定下的周室时做一次(旧档已在周制里的、别的文化先进了周制的不动)
    private const int RoyalVassalWeight = 3, RoyalMeritWeight = 2, SettleWindowYears = 3, RoyalDomainMax = 3, RoyalDomainPer = 4;
    private const float RoyalRivalShare = 0.6f;

    // 周室的可及范围：部落联盟到了早期城址的管辖路程(见 Reach)
    private static float RoyalReach => ReachBase + ReachPerStage * 2;

    private sealed class RoyalCandidate
    {
        public Kingdom head;
        public Empire league;
        public List<Kingdom> followers;
        public int score;
        public bool pioneer;
        public double since;
    }

    private static void SettleRoyalHouses()
    {
        if (ModClass.EMPIRE_MANAGER == null || World.world?.kingdoms == null) return;
        var done = new List<string>();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.GetRegime()?.type != RegimeType.ZhouFeudalism) continue;
            Empire own = kingdom.GetEmpire();
            if (own != null && own.CoreKingdom != kingdom) continue;
            string culture = CultureService.GetRealmCulture(kingdom);
            if (!CultureService.IsValidCulture(culture) || done.Any(other => CultureService.SameKin(other, culture)) ||
                !ZhouJustEnacted(culture)) continue;
            done.Add(culture);
            if (ZhouEraSystem.HasSettledRealm(culture, SettleWindowYears)) continue;
            SettleRoyalHouse(culture);
        }
    }

    // 本文化(华夏一线)施行宗法分封不到 SettleWindowYears 年
    private static bool ZhouJustEnacted(string culture)
    {
        if (InstitutionSystem.GetCultureLine(culture) != "Huaxia") return false;
        var state = InstitutionSystem.GetOrCreateCultureState(culture);
        if (state?.enacted_timestamps == null) return false;
        double now = World.world.getCurWorldTime();
        foreach (KeyValuePair<string, double> entry in state.enacted_timestamps)
        {
            var node = InstitutionDefinitionRegistry.Get(entry.Key);
            if (node == null || !state.enacted_node_ids.Contains(entry.Key) ||
                !InstitutionDefinitionRegistry.TryGetNodeRegime(node, out RegimeType regime) || regime != RegimeType.ZhouFeudalism) continue;
            return entry.Value <= now && Date.getYearsSince(entry.Value) < SettleWindowYears;
        }
        return false;
    }

    // 正在改周制的：已是周制，或还是原始政体而本文化已解析成周制(Migrate 随即就改)
    private static bool Transitioning(Kingdom kingdom)
    {
        if (kingdom.GetRegime()?.type == RegimeType.ZhouFeudalism) return true;
        return IsTribal(kingdom) && InstitutionSystem.TryResolveCultureRegime(CultureService.GetRealmCulture(kingdom), out RegimeType target) &&
               target == RegimeType.ZhouFeudalism;
    }

    private static void SettleRoyalHouse(string culture)
    {
        double now = World.world.getCurWorldTime();
        bool Kin(Kingdom kingdom) =>
            kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && kingdom.capital != null &&
            !EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(kingdom) &&
            CultureService.SameKin(CultureService.GetRealmCulture(kingdom), culture) && Transitioning(kingdom);
        var candidates = new List<RoyalCandidate>();
        foreach (Empire league in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (league?.data == null || league.isRekt() || league.IsArchived() || !Kin(league.CoreKingdom) ||
                !IsConfederation(league) && !ZhouEraSystem.Applies(league)) continue;
            List<Kingdom> followers = Followers(league);
            candidates.Add(new RoyalCandidate
            {
                head = league.CoreKingdom, league = league, followers = followers,
                score = followers.Count * RoyalVassalWeight + league.data.tribal_war_merit * RoyalMeritWeight +
                        (league.CoreKingdom.cities?.Count ?? 0),
                pioneer = league.CoreKingdom.GetRegime()?.type == RegimeType.ZhouFeudalism,
                since = league.data.created_time
            });
        }
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!Kin(kingdom) || kingdom.GetEmpire() != null || kingdom.IsEmpire() || Liege(kingdom) != null) continue;
            candidates.Add(new RoyalCandidate
            {
                head = kingdom, followers = new List<Kingdom>(), score = kingdom.cities?.Count ?? 0,
                pioneer = kingdom.GetRegime()?.type == RegimeType.ZhouFeudalism, since = double.MaxValue
            });
        }
        if (candidates.Count == 0) return;
        candidates = candidates.OrderByDescending(candidate => candidate.score).ThenByDescending(candidate => candidate.pioneer)
            .ThenBy(candidate => candidate.since).ThenBy(candidate => candidate.head.id).ToList();
        // 周室：部落联盟用它的帝国，独立部落给它立帝国(立不成的往下挑)
        RoyalCandidate royal = null;
        Empire realm = null;
        foreach (RoyalCandidate candidate in candidates)
        {
            realm = candidate.league ?? Proclaim(candidate.head);
            if (realm == null) continue;
            royal = candidate;
            break;
        }
        if (realm == null) return;
        realm.data.formed_as_tribe = false;
        ZhouEraSystem.State(realm).royal_settled_at = now;
        Kingdom seat = realm.CoreKingdom;
        string seatName = seat.GetKingdomName();
        double royalPower = seat.GetNationalPower();
        int enfeoffed = royal.followers.Count;
        var joined = new List<string>();
        var defiant = new List<string>();
        foreach (RoyalCandidate other in candidates)
        {
            if (other == royal || other.league == null) continue;
            bool near = Distance(seat, other.head) <= RoyalReach;
            bool strong = other.head.GetNationalPower() >= royalPower * RoyalRivalShare;
            EmpireCore oldCore = EmpireCoreManager.Get(other.league);
            string name = other.head.GetKingdomName();
            // 帝国名下的成员(推行改革的联盟已把共事部落收进帝国的)先记下去向，撤了帝国再由 Enfeoff 并入
            if (near && !strong)
                foreach (Kingdom follower in other.followers) Pledge(follower, realm, null);
            ModClass.EMPIRE_MANAGER.dissolveEmpire(other.league, continued: true);
            if (near && !strong)
            {
                Pledge(other.head, realm, PinLeague);
                AbsorbTitles(realm, other.head, oldCore);
                foreach (Kingdom follower in other.followers) AbsorbTitles(realm, follower, null);
                enfeoffed += 1 + other.followers.Count;
                joined.Add(name);
                continue;
            }
            foreach (Kingdom follower in other.followers)
            {
                KingdomExtension.KingdomExtraData data = follower.GetOrCreate();
                data.tribal_overlord_id = -1L;
                data.tribal_peerage = "";
            }
            defiant.Add(name);
            string content = string.Format(LM.Get("tribal_royal_defiant_history"), name, seatName);
            LogService.LogInfo("[EmpireCraft][部落分迁] " + content + $"({(near ? "国力" + other.head.GetNationalPower().ToString("0") + "/" + royalPower.ToString("0") : "路远")})");
            EventRecorder.Record(realm, content, logKingdom: other.head);
        }
        foreach (Kingdom follower in royal.followers) AbsorbTitles(realm, follower, null);
        // 王畿：周室自己最早共事的那一半里离都城最近的
        int domain = royal.followers.Count == 0 ? 0 : global::System.Math.Min(RoyalDomainMax, 1 + enfeoffed / RoyalDomainPer);
        List<Kingdom> outside = royal.followers.Where(follower => follower.GetEmpire() == null).ToList();
        foreach (Kingdom follower in outside.OrderBy(SubmittedAt).ThenBy(follower => follower.id)
                     .Take(global::System.Math.Max(domain, (outside.Count + 1) / 2))
                     .OrderBy(follower => Distance(seat, follower)).Take(domain))
            follower.GetOrCreate().tribal_peerage = PinDomain;
        InvalidateVassals();
        string settled = string.Format(LM.Get("tribal_royal_settled_history"), seatName, royal.followers.Count,
            realm.data.tribal_war_merit);
        LogService.LogInfo("[EmpireCraft][部落分迁] " + settled +
                           $"(候选 {candidates.Count}，分 {royal.score}；并入 {joined.Count} 个联盟：{string.Join("、", joined)}；" +
                           $"不服王化 {defiant.Count} 个：{string.Join("、", defiant)}；王畿 {domain} 郡)");
        EventRecorder.Record(realm, settled, logKingdom: seat);
        Redraw();
    }

    // 改奉周室(定王室时，见 SettleRoyalHouse)：pin 为定下的爵位；原来共事别家的照原来的共事年数
    private static void Pledge(Kingdom kingdom, Empire realm, string pin)
    {
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (pin != null || data.tribal_submitted_at < 0d) data.tribal_submitted_at = World.world.getCurWorldTime();
        data.tribal_overlord_id = realm.id;
        if (pin != null) data.tribal_peerage = pin;
    }

    // 给独立的部落立帝国做周室(推戴，天命同部落联盟)：先退出同盟(帝国与同盟不兼容)
    private static Empire Proclaim(Kingdom head)
    {
        if (head.IsInEmpire()) return null;
        if (head.hasAlliance()) head.getAlliance().leave(head);
        Empire empire = ModClass.EMPIRE_MANAGER.NewEmpire(head, allowCultureRival: true, suppressFoundingLog: true);
        if (empire == null || empire.isRekt()) return null;
        empire.data.Mandate = EmpireFormationService.GetInitialMandate(EmpireFormationRoute.Acclamation);
        head.GetOrCreate().tribal_overlord_id = -1L;
        return empire;
    }

    // 脱离：国力到了共主六成以上、或对共主怨恨极深(好感 -50 以下)的部落，每月约百分之一的机会不再共事、自立一方；
    // 当年来朝会盟的减半、屡次不朝记为不臣的加倍(见 TribalAssemblySystem.BreakawayFactor)
    private const float BreakawayPowerShare = 0.6f, BreakawayChance = 0.011f;
    private const int BreakawayOpinion = -50;

    private static void Breakaway()
    {
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            Empire overlord = OverlordOf(kingdom);
            Kingdom core = overlord?.CoreKingdom;
            if (core == null || core.isRekt()) continue;
            int opinion = World.world.diplomacy?.getOpinion(kingdom, core)?.total ?? 0;
            bool strong = kingdom.GetNationalPower() >= core.GetNationalPower() * BreakawayPowerShare;
            if (!strong && opinion > BreakawayOpinion ||
                UnityEngine.Random.value >= BreakawayChance * TribalAssemblySystem.BreakawayFactor(kingdom, overlord)) continue;
            kingdom.GetOrCreate().tribal_overlord_id = -1L;
            string content = string.Format(LM.Get("tribal_breakaway_history"), kingdom.GetKingdomName(), OverlordName(overlord));
            LogService.LogInfo("[EmpireCraft][部落分迁] " + content);
            overlord.RecordHistory(directContent: content, kingdomId: kingdom.id);
            Redraw();
        }
    }

    // 政体跟着文化走(每月)：华夏的部落以前用各文化共用的原始政体，改用华夏原始政体(旧存档、新立的国)；
    // 本文化已经走出原始阶段(施行宗法分封)的，帝国之外还是原始政体的部落一并改过去(帝国由制度系统改，见 InstitutionSystem)。
    // 本文化第一次进宗法分封的，改之前先按资历定下唯一的周室(见 SettleRoyalHouses)。
    // 共事的部落都已离去(脱离、放走、亡了)、帝国里也只有自己的部落联盟，走出原始阶段时撤去帝号(不记已亡帝国)、回到普通的国再改政体
    // (用户 2026-10-26：以前纳入部落时就把 formed_as_tribe 记为 false，这样的旧联盟不会被撤，进了宗法分封成一城的周室)；
    // 按资历定下的周室不撤
    private static void Migrate()
    {
        try
        {
            SettleRoyalHouses();
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][部落分迁] 按资历定王室失败: {exception}");
        }
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!IsTribal(kingdom) || EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(kingdom)) continue;
            Empire empire = kingdom.GetEmpire();
            if (empire != null && empire.CoreKingdom != kingdom) continue;
            string culture = CultureService.GetRealmCulture(kingdom);
            RegimeType current = kingdom.GetRegime().type, target = current;
            if (InstitutionSystem.TryResolveCultureRegime(culture, out RegimeType resolved))
                target = resolved;
            else if (current == RegimeType.Origin && InstitutionSystem.GetCultureLine(culture) == "Huaxia")
                target = RegimeType.HuaxiaOrigin;
            // 别的文化线没有自己的原始节点：照旧用共用的原始政体，不因为解析不出来就改(游牧、阿拉伯从部落起步，
            // 本文化成了汗国、哈里发国，帝国之外的部落一并改过去)
            if (target == current || !RegimeManager.IsPrimitive(target) && current == RegimeType.Origin &&
                InstitutionSystem.GetCultureLine(culture) is not ("Huaxia" or "Youmu" or "Arabic" or "Japan")) continue;
            try
            {
                if (empire != null && !RegimeManager.IsPrimitive(target) && Deserted(empire))
                {
                    ModClass.EMPIRE_MANAGER.dissolveEmpire(empire, continued: true);
                    LogService.LogInfo($"[EmpireCraft][部落分迁] {kingdom.GetKingdomName()} 共事的部落都已离去，走出原始阶段时撤去部落联盟的帝号");
                    empire = null;
                    Redraw();
                }
                if (empire != null) InstitutionSystem.ChangeRegimeForTransition(empire, target);
                else
                {
                    kingdom.SetRegimeType(target);
                    kingdom.LoadRegimeKeepingRole();
                    kingdom.SystemChange();
                }
                LogService.LogInfo($"[EmpireCraft][部落分迁] {kingdom.GetKingdomName()} 政体 {current} 改为 {target}");
            }
            catch (global::System.Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][部落分迁] {kingdom.GetKingdomName()} 改政体失败: {exception.Message}");
            }
        }
    }

    // 超出上限的共主每月放走一部：先放离得最远的(超出路程的先放)
    private static void Overflow()
    {
        if (ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (!IsConfederation(empire)) continue;
            List<Kingdom> vassals = VassalsOf(empire);
            if (vassals.Count == 0) continue;
            float reach = Reach(empire);
            Kingdom farthest = vassals.OrderByDescending(kingdom => DistanceTo(empire, kingdom)).First();
            if (vassals.Count <= Capacity(empire) && DistanceTo(empire, farthest) <= reach) continue;
            farthest.GetOrCreate().tribal_overlord_id = -1L;
            string content = string.Format(LM.Get("tribal_overflow_history"), farthest.GetKingdomName(), OverlordName(empire));
            LogService.LogInfo("[EmpireCraft][部落分迁] " + content + $"(共事 {vassals.Count} 部，上限 {Capacity(empire)}、{reach:0} 格)");
            empire.RecordHistory(directContent: content, kingdomId: farthest.id);
            Redraw();
        }
    }

    // 归属变了：整图重画(铭牌、边框跟着变)。每月整理(EnforceOneCity)当中只记下要画，整轮末尾至多画一次——
    // 以前离开帝国、撤帝号、分封、脱离、放走、配色各画一遍，脱离、放走还是每部一遍，一个月整图重画好几次
    private static bool _redrawBatch, _redrawPending;

    private static void Redraw()
    {
        if (_redrawBatch) _redrawPending = true;
        else World.world?.zone_calculator?.dirtyAndClear();
    }
}

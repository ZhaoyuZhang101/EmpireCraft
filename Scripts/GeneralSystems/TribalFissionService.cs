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
//   · 共主不打共事它的部落，部落也不打共主(用户："怎么主国家就可以打下面的小弟了，这也不合理……应该只打不服的或者脱离联盟的")，
//     共主只征伐不服的、脱离的；部落之间照样可以互相攻伐(见 Bound)；铭牌标"共事某某"；
//   · 脱离：国力到了共主六成以上、或对共主怨恨极深的部落，每年约有一成二的机会不再共事、自立一方(见 Breakaway)。
// 推进原始改革要有帝国(见 InstitutionSystem)：共主的部落联盟推进，制度按文化施行，各部落一样跟着改。
public static class TribalFissionService
{
    public static bool IsTribal(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && kingdom.GetRegime()?.type == RegimeType.Origin;

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

    // 共主与共事它的部落(部落之间不算)：彼此不宣战
    public static bool Bound(Kingdom a, Kingdom b) =>
        a != null && b != null && a != b &&
        (OverlordOf(a) is Empire ea && (b == ea.CoreKingdom || b.GetEmpire() == ea) ||
         OverlordOf(b) is Empire eb && (a == eb.CoreKingdom || a.GetEmpire() == eb));

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
    // 只结同盟的部落结成部落联盟；共主改行分封的，奉它的部落并入为诸侯
    public static void EnforceOneCity()
    {
        if (World.world?.kingdoms == null) return;
        foreach (Kingdom mother in World.world.kingdoms.list.ToList())
        {
            if (!IsTribal(mother) || mother.capital == null || mother.cities == null || mother.cities.Count <= 1 ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(mother)) continue;
            WorldTile seat = mother.capital.getTile();
            City city = mother.cities.Where(candidate => candidate?.data != null && !candidate.isRekt() && candidate != mother.capital)
                .OrderByDescending(candidate => seat == null || candidate.getTile() == null ? 0f : Toolbox.SquaredDistTile(candidate.getTile(), seat))
                .FirstOrDefault();
            if (city == null) continue;
            if (!city.hasLeader()) CityPopulationSystem.EnsureLeader(city);
            Actor leader = Fit(city.leader, city) ? city.leader : CityStateService.FindLocalLeader(city, mother.king);
            if (leader != null) Split(city, mother, leader);
        }
        try
        {
            DetachMembers();
            FoldAlliances();
            Enfeoff();
            Breakaway();
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
            if (tribe.GetRegime()?.type != RegimeType.Origin)
            {
                tribe.SetRegimeType(RegimeType.Origin);
                tribe.LoadRegime();
                tribe.SystemChange();
            }
            Empire overlord = OverlordFor(mother);
            if (overlord != null) Submit(tribe, overlord);
            // 结不成部落联盟(母部落没有首领之类)：先与母部落结盟，下个月再整理(见 FoldAlliances)
            else PersonalUnionService.JoinAlliance(mother, tribe);
            string content = overlord != null
                ? string.Format(LM.Get("tribal_fission_vassal_history"), motherName, city.GetCityName(), tribe.GetKingdomName(),
                    overlord.GetEmpireName())
                : string.Format(LM.Get("tribal_fission_history"), motherName, city.GetCityName(), tribe.GetKingdomName());
            LogService.LogInfo("[EmpireCraft][部落分迁] " + content);
            overlord?.RecordHistory(directContent: content, kingdomId: tribe.id);
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][部落分迁] {mother.GetKingdomName()} 分出新部落失败: {exception.Message}");
        }
    }

    // 分出去的新部落奉谁为共主：母部落是共主就奉母部落，母部落奉谁就奉谁；都不是，母部落就此结成部落联盟、自为共主
    private static Empire OverlordFor(Kingdom mother)
    {
        Empire own = mother.GetEmpire();
        if (own != null) return IsConfederation(own) ? own : null;
        return OverlordOf(mother) ?? Confederation(mother);
    }

    // 奉 overlord 为共主：退出同盟、不进帝国，记下共主
    private static void Submit(Kingdom tribe, Empire overlord)
    {
        if (tribe?.data == null || overlord == null) return;
        if (tribe.hasAlliance()) tribe.getAlliance().leave(tribe);
        tribe.GetOrCreate().tribal_overlord_id = overlord.id;
        tribe.GetRegime()?.SetAllowDiplomacy(true);
    }

    // 结成部落联盟(head 为共主，帝国里只有它自己)；原来同盟里同文化的部落奉它为共主
    private static Empire Confederation(Kingdom head)
    {
        if (head.IsInEmpire() || ModClass.EMPIRE_MANAGER == null) return null;
        Alliance alliance = head.hasAlliance() ? head.getAlliance() : null;
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
        string content = string.Format(LM.Get("tribal_confederation_history"), head.GetKingdomName(), empire.GetEmpireName());
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

    // 只结了同盟、没有共主的部落：同盟里最强的那一家结成部落联盟，其余奉它为共主
    private static void FoldAlliances()
    {
        var done = new HashSet<Alliance>();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!IsTribal(kingdom) || kingdom.IsInEmpire() || !kingdom.hasAlliance()) continue;
            Alliance alliance = kingdom.getAlliance();
            if (alliance == null || !alliance.isAlive() || !done.Add(alliance)) continue;
            string culture = CultureService.GetRealmCulture(kingdom);
            List<Kingdom> tribes = alliance.kingdoms_hashset.Where(member => IsTribal(member) && !member.IsInEmpire() &&
                    string.Equals(CultureService.GetRealmCulture(member), culture, global::System.StringComparison.Ordinal))
                .ToList();
            if (tribes.Count < 2) continue;
            Kingdom head = tribes.OrderByDescending(member => member.GetNationalPower()).First();
            if (head.king == null || head.king.isRekt()) continue;
            // 盟主本就奉某共主：其余盟友也奉那个共主
            Empire overlord = OverlordOf(head);
            if (overlord != null)
            {
                World.world.alliances.dissolveAlliance(alliance);
                foreach (Kingdom member in tribes) Submit(member, overlord);
                continue;
            }
            Confederation(head);
        }
    }

    // 共主改行宗法分封、或已不在：奉它的部落并入为分封的诸侯，或各自独立
    private static void Enfeoff()
    {
        if (ModClass.EMPIRE_MANAGER == null) return;
        bool changed = false;
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
                changed = true;
                continue;
            }
            if (IsConfederation(empire)) continue;
            data.tribal_overlord_id = -1L;
            if (empire.CoreKingdom?.GetRegime()?.type == RegimeType.ZhouFeudalism)
            {
                empire.join(kingdom, pForce: true);
                string content = string.Format(LM.Get("tribal_enfeoffed_history"), kingdom.GetKingdomName(), empire.GetEmpireName());
                LogService.LogInfo("[EmpireCraft][部落分迁] " + content);
                empire.RecordHistory(directContent: content, kingdomId: kingdom.id);
            }
            changed = true;
        }
        if (changed) Redraw();
    }

    // 脱离：国力到了共主六成以上、或对共主怨恨极深(好感 -50 以下)的部落，每月约百分之一的机会不再共事、自立一方
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
            if (!strong && opinion > BreakawayOpinion || UnityEngine.Random.value >= BreakawayChance) continue;
            kingdom.GetOrCreate().tribal_overlord_id = -1L;
            string content = string.Format(LM.Get("tribal_breakaway_history"), kingdom.GetKingdomName(), OverlordName(overlord));
            LogService.LogInfo("[EmpireCraft][部落分迁] " + content);
            overlord.RecordHistory(directContent: content, kingdomId: kingdom.id);
            Redraw();
        }
    }

    // 归属变了：整图重画(铭牌、边框跟着变)
    private static void Redraw() => World.world?.zone_calculator?.dirtyAndClear();
}

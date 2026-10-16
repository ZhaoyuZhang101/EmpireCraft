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
//   · 建城的族人或城主(都不合适就是城里威望最高的成年人)立为首领，政体照旧是原始政体；
//   · 新部落是部落联盟(帝国)里的诸侯(用户："弄一个新的部落联盟主国家为帝国，其余国家类似于朝贡""其实就是诸侯国，战国的那种")：
//     母部落已在部落联盟里的并入同一个；没有的，母部落就此结成部落联盟、为共主(天下共主)，新部落是第一个诸侯。
//     原始政体的诸侯有自己的军队和外交(可以互相攻伐)、不向共主输兵；不再用原版同盟——同盟与帝国不兼容，
//     不在帝国里也推不动改革(见 InstitutionSystem)；
//   · 以前分出去只结同盟的部落：同盟里最强的那一家结成部落联盟，同文化的部落一并入盟为诸侯(见 FoldAlliances)。
// 改行宗法分封(政体不再是原始政体)以后不再分。
public static class TribalFissionService
{
    public static bool IsTribal(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && kingdom.GetRegime()?.type == RegimeType.Origin;

    // CityManager.buildNewCity 之后(见 CityPatch.CityBuilt)、国家移民安置完毕后(见 StateSettlementSystem)调用
    public static void OnCityBuilt(City city, Actor founder)
    {
        Kingdom mother = city?.kingdom;
        if (!IsTribal(mother) || mother.cities == null || mother.cities.Count <= 1 || city == mother.capital ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(mother)) return;
        Actor leader = Fit(founder, city) ? founder : Fit(city.leader, city) ? city.leader : CityStateService.FindLocalLeader(city, mother.king);
        if (leader != null) Split(city, mother, leader);
    }

    // 每月(RoyalHouseSystem)：名下不止一座城的部落分出一座(离都城最远的先分)
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
        FoldAlliances();
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
            Empire empire = Confederation(mother);
            bool inEmpire = empire != null;
            if (inEmpire)
            {
                if (tribe.hasAlliance()) tribe.getAlliance().leave(tribe);
                empire.join(tribe, pForce: true);
            }
            // 结不成部落联盟(母部落没有首领之类)：先与母部落结盟，下个月再并(见 FoldAlliances)
            else PersonalUnionService.JoinAlliance(mother, tribe);
            string content = string.Format(LM.Get(inEmpire ? "tribal_fission_empire_history" : "tribal_fission_history"),
                motherName, city.GetCityName(), tribe.GetKingdomName());
            LogService.LogInfo("[EmpireCraft][部落分迁] " + content);
            if (inEmpire) empire.RecordHistory(directContent: content, kingdomId: tribe.id);
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][部落分迁] {mother.GetKingdomName()} 分出新部落失败: {exception.Message}");
        }
    }

    // 母部落所在的部落联盟；还没有就此结成一个(母部落为共主)，原来同盟里同文化的部落一并入盟为诸侯
    private static Empire Confederation(Kingdom mother)
    {
        Empire empire = mother.GetEmpire();
        if (empire != null && !empire.isRekt() && !empire.IsArchived()) return empire;
        if (mother.IsInEmpire() || ModClass.EMPIRE_MANAGER == null) return null;
        Alliance alliance = mother.hasAlliance() ? mother.getAlliance() : null;
        empire = ModClass.EMPIRE_MANAGER.NewEmpire(mother, allowCultureRival: true);
        if (empire == null || empire.isRekt()) return null;
        empire.data.Mandate = EmpireFormationService.GetInitialMandate(EmpireFormationRoute.Acclamation);
        string culture = CultureService.GetRealmCulture(mother);
        var joined = new List<Kingdom>();
        if (alliance != null && alliance.isAlive())
        {
            foreach (Kingdom member in alliance.kingdoms_hashset.ToList())
            {
                if (member == null || member == mother || !IsTribal(member) || member.IsInEmpire() ||
                    !string.Equals(CultureService.GetRealmCulture(member), culture, global::System.StringComparison.Ordinal)) continue;
                joined.Add(member);
            }
            // 帝国与同盟不兼容：入盟前先散掉原来的同盟
            World.world.alliances.dissolveAlliance(alliance);
            foreach (Kingdom member in joined) empire.join(member, pForce: true);
        }
        string content = string.Format(LM.Get("tribal_confederation_history"), mother.GetKingdomName(), empire.GetEmpireName());
        LogService.LogInfo("[EmpireCraft][部落分迁] " + content + (joined.Count > 0 ? $"(原同盟 {joined.Count} 家入盟)" : ""));
        empire.RecordHistory(directContent: content, kingdomId: mother.id);
        return empire;
    }

    // 只结了同盟、不在部落联盟里的部落：同盟里最强的那一家结成部落联盟，其余同文化的部落入盟(每月)
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
            try
            {
                Confederation(head);
            }
            catch (global::System.Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][部落分迁] {head.GetKingdomName()} 结成部落联盟失败: {exception.Message}");
            }
        }
    }
}

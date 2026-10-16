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
//   · 母部落属于部落联盟(帝国)的，新部落并入同一个部落联盟(原始政体的成员自治)；没有的，与母部落结为同盟——
//     称帝的实力按同盟算(见 EmpireFormationService.GetBlocMembers)，同盟里的城够了就结成部落联盟。
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
            Empire empire = mother.GetEmpire();
            bool inEmpire = empire != null && !empire.isRekt() && !empire.IsArchived();
            if (inEmpire)
            {
                if (tribe.hasAlliance()) tribe.getAlliance().leave(tribe);
                empire.join(tribe, pForce: true);
            }
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
}

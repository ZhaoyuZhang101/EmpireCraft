using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 部落分迁(用户 2026-10-16："为什么部落扩展城市不能像城邦一样直接扩一个国家，因为生产力有限""而是直接纳入第一个国家了这不合理")：
// 原始政体的部落、方国生产力有限，管不了远处新开的聚落——族人新建一座城，就是分出去的一支新部落：
//   · 建城的族人(不在场就是城里威望最高的成年人)立为首领，政体照旧是原始政体；
//   · 母部落属于部落联盟(帝国)的，新部落并入同一个部落联盟(原始政体的成员自治)；没有的，与母部落结为同盟——
//     称帝的实力按同盟算(见 EmpireFormationService.GetBlocMembers)，同盟里的城够了就结成部落联盟；
//   · 只管新建的城；打下来的、和约割让的、归附的照旧归攻方，已经有好几座城的部落也不拆。
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
        Actor leader = founder != null && !founder.isRekt() && founder.isAlive() && founder.isAdult() && !founder.isKing() &&
                       founder.city == city && founder.isUnitFitToRule()
            ? founder
            : CityStateService.FindLocalLeader(city, mother.king);
        if (leader == null) return;
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

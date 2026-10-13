using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 宣战只能就近：原版给独立王国挑宣战目标(DiplomacyHelpers.getWarTarget)只找"最近的、比自己弱的、可达的"国家，
// 不要求接壤，近处没有合适目标时就会远距离宣战。这里要求目标与本国(或本国所在帝国)接壤或隔海相望(见 StraitReach)：
// 原版挑中的目标不近、或原版没挑出目标(隔海的小岛陆路不可达，原版不挑)时，按原版同样的条件改从近邻里挑；没有合适的就不宣战。
// (帝国与帝国成员的宣战走 EmpireCraftPlotsAddition.GetWarTarget，本来就要求接壤)
public class WarTargetPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(WarTargetPatch)).Patch(
            AccessTools.Method(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getWarTarget)),
            postfix: new HarmonyMethod(GetType(), nameof(only_neighbours)));
    }

    private static void only_neighbours(Kingdom pInitiatorKingdom, ref Kingdom __result)
    {
        if (pInitiatorKingdom == null) return;
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(pInitiatorKingdom)) return;
        if (__result != null && IsNeighbour(pInitiatorKingdom, __result) &&
            !GeneralSystems.RoyalMarriageSystem.Allied(pInitiatorKingdom, __result)) return;
        __result = NearestNeighbourTarget(pInitiatorKingdom);
    }

    private static bool IsNeighbour(Kingdom initiator, Kingdom target) => GeneralSystems.StraitReach.Near(initiator, target);

    // 与原版 getWarTarget 相同的条件，只是限定接壤
    private static Kingdom NearestNeighbourTarget(Kingdom initiator)
    {
        if (initiator.capital == null) return null;
        int ours = initiator.hasAlliance() ? initiator.getAlliance().countWarriors() : initiator.countTotalWarriors();
        Kingdom best = null;
        float bestDistance = float.MaxValue;
        using ListPool<Kingdom> neutral = DiplomacyHelpers.wars.getNeutralKingdoms(initiator);
        foreach (Kingdom target in neutral)
        {
            if (target == null || !target.hasCities() || !target.hasCapital() ||
                target.getAge() < SimGlobals.m.minimum_kingdom_age_for_attack || !IsNeighbour(initiator, target))
                continue;
            int theirs = target.hasAlliance() ? target.getAlliance().countWarriors() : target.countTotalWarriors();
            // 好感好的邻国：有继承宣称、或兵力是对方两倍以上才打；姻亲之国不打(见 RoyalMarriageSystem)
            bool friendly = initiator.isOpinionTowardsKingdomGood(target);
            if (ours < theirs || GeneralSystems.RoyalMarriageSystem.Allied(initiator, target) ||
                !initiator.capital.reachableFrom(target.capital) && !GeneralSystems.StraitReach.Across(initiator, target) ||
                Date.getYearsSince(DiplomacyHelpers.diplomacy.getRelation(initiator, target).data.timestamp_last_war_ended) <
                SimGlobals.m.minimum_years_between_wars ||
                friendly && ours < theirs * 2 && !GeneralSystems.RoyalMarriageSystem.HasClaim(initiator, target))
                continue;
            float distance = Kingdom.distanceBetweenKingdom(initiator, target);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = target;
        }
        return best;
    }
}

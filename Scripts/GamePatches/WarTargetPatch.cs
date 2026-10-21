using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 宣战只能就近：原版给独立王国挑宣战目标(DiplomacyHelpers.getWarTarget)只找"最近的、比自己弱的、可达的"国家，
// 不要求接壤，近处没有合适目标时就会远距离宣战。这里要求目标与本国(或本国所在帝国)接壤或隔海相望(见 StraitReach)：
// 原版挑中的目标不近、或原版没挑出目标(隔海的小岛陆路不可达，原版不挑)时，按原版同样的条件改从近邻里挑；没有合适的就不宣战。
// (帝国与帝国成员的宣战走 EmpireCraftPlotsAddition.GetWarTarget，本来就要求接壤)
// 先收法理(世界法则，见 DeJureAgenda)：同样条件下，占着本国法理城的邻国优先于原版挑的目标。
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
        // 帝国、帝国成员、附庸已由 DiplomacyManagerPatch 的前缀改走 EmpireCraftPlotsAddition.GetWarTarget(有自己的校验与先收法理)；
        // 前缀跳过原版后这里照样会跑，以前没目标时会改从近邻里挑，绕过分治兄弟之国、统一战线等不得互攻的规矩
        if (!EmpireCraft.Scripts.AI.EmpireCraftPlotsAddition.UsesVanillaWarPlot(pInitiatorKingdom.IsEmpire(), pInitiatorKingdom.IsInEmpire(),
                GeneralSystems.FeudalVassalService.GetOverlord(pInitiatorKingdom) != null)) return;
        // 打不起仗的不主动开战(见 CorpsSystem.CannotFundWar)
        if (GeneralSystems.CorpsSystem.CannotFundWar(pInitiatorKingdom))
        {
            __result = null;
            return;
        }
        if (GeneralSystems.DeJureAgenda.Enabled)
        {
            Kingdom claim = NearestNeighbourTarget(pInitiatorKingdom, claimsOnly: true);
            if (claim != null)
            {
                __result = claim;
                return;
            }
        }
        // 姻亲、通好、扶立、共事部落、统一战线、兄弟之国、岁币朝贡、封建上下级等不打(见 WarGate.Forbidden)；
        // 没有雄主在位的游牧政权平时只打两城以内的小国(见 SteppeOrderSystem)
        if (__result != null && IsNeighbour(pInitiatorKingdom, __result) &&
            !GeneralSystems.WarGate.Forbidden(pInitiatorKingdom, __result) &&
            GeneralSystems.SteppeOrderSystem.PeacetimeWarAllowed(pInitiatorKingdom, __result)) return;
        __result = NearestNeighbourTarget(pInitiatorKingdom);
    }

    private static bool IsNeighbour(Kingdom initiator, Kingdom target) => GeneralSystems.StraitReach.Near(initiator, target);

    // 与原版 getWarTarget 相同的条件，只是限定接壤；claimsOnly：只挑占着本国法理城的
    private static Kingdom NearestNeighbourTarget(Kingdom initiator, bool claimsOnly = false)
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
            // 好感好的邻国：有继承宣称、或兵力是对方两倍以上才打；姻亲之国等不打(见 WarGate.Forbidden)
            bool friendly = initiator.isOpinionTowardsKingdomGood(target);
            if (ours < theirs || GeneralSystems.WarGate.Forbidden(initiator, target) ||
                !GeneralSystems.SteppeOrderSystem.PeacetimeWarAllowed(initiator, target) ||
                !initiator.capital.reachableFrom(target.capital) && !GeneralSystems.StraitReach.Across(initiator, target) ||
                Date.getYearsSince(DiplomacyHelpers.diplomacy.getRelation(initiator, target).data.timestamp_last_war_ended) <
                SimGlobals.m.minimum_years_between_wars ||
                friendly && ours < theirs * 2 && !GeneralSystems.RoyalMarriageSystem.HasClaim(initiator, target) ||
                claimsOnly && !GeneralSystems.DeJureAgenda.HasClaimOn(initiator, target))
                continue;
            float distance = Kingdom.distanceBetweenKingdom(initiator, target);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = target;
        }
        return best;
    }
}

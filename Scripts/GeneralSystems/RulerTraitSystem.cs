using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GeneralSystems;

// 君主特质：
//   开国雄主 —— 改朝换代后的第一位皇帝(Empire.data.dynasty_founder_actor_id)。寿命更长(特质数值)，在位期间：
//              帝国各城忠诚 +FounderLoyalty(EmpireCraftLoyaltyLibrary "founder_ruler")；
//              开国气象(攻城加速、正统损失减半)一直有效，不限立国头十年(Empire.InFoundingGrace)；
//              对本帝国核心内割据政权的统一战争不受战争年限强制停战(IsFounderUnificationWar)；
//              控制核心过半后，接壤的割据政权每年可能望风归附(WarlordEraSystem.FounderBandwagon)；
//              帝国境内不发生叛乱(ModernStability.PassRebellionGate、KingdomExtension.StartLocalRebelling)。
//   中兴之主 —— 后继皇帝在位时管理(stewardship)达到 RestorerStewardship 即获得(即位时与每年各查一次)。
//              在位期间帝国各城、各国的腐败增长减半(CityExtension/KingdomExtension.AddCorruptionRate)。
//   中兴雄主 —— 获得中兴之主时有 GreatRestorerChance 的概率同时获得(极品)：在保留中兴之主效果的同时，
//              享有开国雄主的全部特性(长寿、忠诚、开国气象、统一战争打到底、望风归附、境内无叛乱)。
// 只有君主制有：共和国元首与现代政体不授予，已有的特质在这些政体下也不生效。
public static class RulerTraitSystem
{
    public const string Founder = "founderRuler";
    public const string Restorer = "restorerRuler";
    public const string GreatRestorer = "greatRestorerRuler";
    public const float GreatRestorerChance = 0.2f;
    public const float RestorerStewardship = 12f;
    public const int FounderLoyalty = 20;
    public const double RestorerCorruptionFactor = 0.5;

    private static bool IsMonarchy(Empire empire) =>
        empire != null && !empire.isRekt() && !RepublicSystem.IsRepublic(empire) &&
        empire.CoreKingdom?.GetRegime()?.type != RegimeType.Modern;

    public static void OnCrowned(Empire empire, Actor actor)
    {
        if (!IsMonarchy(empire) || actor == null || !actor.isAlive()) return;
        if (actor.id == empire.data.dynasty_founder_actor_id)
        {
            if (!actor.hasTrait(Founder)) actor.addTrait(Founder);
            return;
        }
        CheckRestorer(empire, actor);
    }

    public static void CheckRestorer(Empire empire) => CheckRestorer(empire, empire?.Emperor);

    private static void CheckRestorer(Empire empire, Actor actor)
    {
        if (!IsMonarchy(empire) || actor == null || !actor.isAlive() ||
            actor.id == empire.data.dynasty_founder_actor_id ||
            actor.hasTrait(Founder) || actor.hasTrait(Restorer)) return;
        if (actor.stats["stewardship"] < RestorerStewardship) return;
        actor.addTrait(Restorer);
        // 极品：中兴雄主，享有开国雄主的全部特性
        if (UnityEngine.Random.value < GreatRestorerChance) actor.addTrait(GreatRestorer);
    }

    public static bool ReignedBy(Kingdom kingdom, string trait)
    {
        Empire empire = kingdom?.GetEmpire();
        Actor emperor = empire?.Emperor;
        return emperor != null && emperor.isAlive() && emperor.hasTrait(trait) && IsMonarchy(empire);
    }

    // 开国雄主或中兴雄主(享有开国雄主全部特性)是否在位(kingdom 为帝国的任一成员国均可)
    public static bool FounderReigns(Kingdom kingdom) => ReignedBy(kingdom, Founder) || ReignedBy(kingdom, GreatRestorer);

    // 在位雄主的称号(开国雄主 / 中兴雄主)，史书措辞用
    public static string FounderTitle(Actor ruler) =>
        NeoModLoader.General.LM.Get(ruler != null && ruler.hasTrait(GreatRestorer) && !ruler.hasTrait(Founder)
            ? "trait_" + GreatRestorer : "trait_" + Founder);

    // 开国雄主与本帝国核心(法理疆域)内割据政权之间的战争：不论谁先开战，都算统一战争
    public static bool IsFounderUnificationWar(War war)
    {
        if (war == null || war.hasEnded()) return false;
        Kingdom attacker = war.getMainAttacker();
        Kingdom defender = war.getMainDefender();
        return IsFounderAgainstLocalRival(attacker, defender) || IsFounderAgainstLocalRival(defender, attacker);
    }

    private static bool IsFounderAgainstLocalRival(Kingdom founderSide, Kingdom rival)
    {
        Empire empire = founderSide?.GetEmpire();
        if (empire == null || rival == null || empire.CoreKingdom != founderSide || !FounderReigns(founderSide)) return false;
        if (rival.GetEmpire() == empire) return false;
        EmpireCore core = EmpireCoreManager.Get(empire);
        EmpireCore target = rival.capital?.GetEmpireCore();
        return core != null && target != null && (target == core || target.warlord_parent_core_id == core.id);
    }

    // 腐败增长(正数)在中兴之主治下减半；腐败下降不受影响
    public static double ScaleCorruptionGain(Kingdom kingdom, double addition) =>
        addition > 0 && ReignedBy(kingdom, Restorer) ? addition * RestorerCorruptionFactor : addition;
}

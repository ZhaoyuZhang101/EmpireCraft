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
// 只有君主制有：共和国元首与现代政体不授予，已有的特质在这些政体下也不生效。
public static class RulerTraitSystem
{
    public const string Founder = "founderRuler";
    public const string Restorer = "restorerRuler";
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
    }

    public static bool ReignedBy(Kingdom kingdom, string trait)
    {
        Empire empire = kingdom?.GetEmpire();
        Actor emperor = empire?.Emperor;
        return emperor != null && emperor.isAlive() && emperor.hasTrait(trait) && IsMonarchy(empire);
    }

    // 开国雄主是否在位(kingdom 为帝国的任一成员国均可)
    public static bool FounderReigns(Kingdom kingdom) => ReignedBy(kingdom, Founder);

    // 开国雄主对本帝国核心(法理疆域)内割据政权发动的统一战争
    public static bool IsFounderUnificationWar(War war)
    {
        if (war == null || war.hasEnded()) return false;
        Kingdom attacker = war.getMainAttacker();
        Kingdom defender = war.getMainDefender();
        Empire empire = attacker?.GetEmpire();
        if (empire == null || defender == null || empire.CoreKingdom != attacker || !FounderReigns(attacker)) return false;
        if (defender.GetEmpire() == empire) return false;
        EmpireCore core = EmpireCoreManager.Get(empire);
        EmpireCore target = defender.capital?.GetEmpireCore();
        return core != null && target != null && (target == core || target.warlord_parent_core_id == core.id);
    }

    // 腐败增长(正数)在中兴之主治下减半；腐败下降不受影响
    public static double ScaleCorruptionGain(Kingdom kingdom, double addition) =>
        addition > 0 && ReignedBy(kingdom, Restorer) ? addition * RestorerCorruptionFactor : addition;
}

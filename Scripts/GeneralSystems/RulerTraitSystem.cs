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
//              志在一统：每年几乎必定对接壤的本核心割据政权兴兵，同时至多两场(WarlordEraSystem.FounderDrive)；
//              兵锋所向：本国各军战力 ×FounderCombatFactor，战线一月可下两块地(平滑国界下推进加倍)；
//              被讨伐的割据政权地盘被占得越多越可能举国归附，战争很快结束(WarlordEraSystem.FounderCapitulation，每月)；
//              帝国境内不发生叛乱(ModernStability.PassRebellionGate、KingdomExtension.StartLocalRebelling)。
//   中兴之主 —— 成年亲政、无负面资质、管理达标，在已有衰败的朝廷至少治理五年，
//              正统、腐败或六弊较观察起点实质改善，且当前财政、正统过关才获得。
//              在位期间帝国各城、各国的腐败增长减半(CityExtension/KingdomExtension.AddCorruptionRate)。
//   中兴雄主 —— 获得中兴之主时有 GreatRestorerChance 的概率升为此档(只保留一个称号)：在保留中兴之主效果的同时，
//              享有开国雄主的全部特性(长寿、忠诚、开国气象、统一战争打到底、望风归附、境内无叛乱)。
// 只有君主制有：共和国元首与现代政体不授予，已有的特质在这些政体下也不生效。
public static class RulerTraitSystem
{
    public const string Founder = "founderRuler";
    public const string Restorer = "restorerRuler";
    public const string GreatRestorer = "greatRestorerRuler";
    public const float GreatRestorerChance = 0.2f;
    public const float RestorerStewardship = 12f;
    public const float RestorerStrain = 35f;
    public const int FounderLoyalty = 20;
    public const double RestorerCorruptionFactor = 0.5;
    private const int RecoveryYears = 5;

    private static bool IsMonarchy(Empire empire) =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() &&
        empire.CoreKingdom != null && !empire.CoreKingdom.isRekt() &&
        !RepublicSystem.IsRepublic(empire) && !TransmigrantSystem.Governs(empire) && !ModernLegitimacy.Applies(empire) &&
        empire.CoreKingdom.GetRegime()?.type != RegimeType.Modern &&
        (RegimeManager.IsMonarchy(empire.CoreKingdom.GetRegime()?.type) || empire.IsPrincipate() ||
         RegimeManager.IsPrimitive(empire.CoreKingdom.GetRegime()?.type));

    public static void OnCrowned(Empire empire, Actor actor)
    {
        if (!IsMonarchy(empire) || actor == null || !actor.isAlive()) return;
        RulerQualitySystem.Assess(empire, actor);
        if (actor.id == empire.data.dynasty_founder_actor_id)
        {
            if (!actor.hasTrait(Founder)) actor.addTrait(Founder);
            return;
        }
        CheckRestorer(empire, actor);
    }

    // 每年：中兴之主的判定；旧档在位、还没定资质的君主补定
    public static void CheckRestorer(Empire empire)
    {
        if (!IsMonarchy(empire) || empire.Emperor == null) return;
        RulerQualitySystem.Assess(empire, empire.Emperor);
        CheckRestorer(empire, empire.Emperor);
    }

    private static void CheckRestorer(Empire empire, Actor actor)
    {
        if (!IsMonarchy(empire) || actor?.data == null || !actor.isAlive()) return;
        // 旧版用两个特质叠加表示高阶中兴；改为高阶包含低阶效果。
        if (actor.hasTrait(GreatRestorer)) actor.removeTrait(Restorer);
        var recovery = actor.GetOrCreate();
        if (RealisticAgeSystem.IsMinor(actor))
        {
            actor.removeTrait(Restorer);
            actor.removeTrait(GreatRestorer);
            recovery.restorer_realm = -1L;
            return;
        }
        if (actor.id == empire.data.dynasty_founder_actor_id ||
            actor.hasTrait(Founder) || actor.hasTrait(Restorer) || actor.hasTrait(GreatRestorer)) return;
        if (RulerQualitySystem.IsBad(actor) || empire.data.is_been_controlled ||
            DynasticCycleSystem.GetPressure(empire) < RestorerStrain || World.world == null)
        {
            recovery.restorer_realm = -1L;
            return;
        }
        double now = World.world.getCurWorldTime();
        float malady = MaladySystem.Average(empire);
        double corruption = CorruptionSystem.GetRate(empire);
        int legitimacy = empire.Legitimacy;
        if (recovery.restorer_realm != empire.id || recovery.restorer_since < 0d || now < recovery.restorer_since ||
            empire.data.newEmperor_timestamp > recovery.restorer_since)
        {
            recovery.restorer_realm = empire.id;
            recovery.restorer_since = now;
            recovery.restorer_malady = malady;
            recovery.restorer_corruption = corruption;
            recovery.restorer_legitimacy = legitimacy;
            return;
        }
        if (Date.getYearsSince(recovery.restorer_since) < RecoveryYears || actor.stats["stewardship"] < RestorerStewardship ||
            legitimacy < 50 || empire.CurrentMoney < 0 || malady > recovery.restorer_malady ||
            corruption > recovery.restorer_corruption) return;
        bool improved = recovery.restorer_malady - malady >= 10f || recovery.restorer_corruption - corruption >= 0.1d - 1e-6d ||
                        legitimacy - recovery.restorer_legitimacy >= 15;
        if (!improved) return;
        actor.addTrait(UnityEngine.Random.value < GreatRestorerChance ? GreatRestorer : Restorer);
    }

    // 功绩称号不等于当前资质；幼主、荒淫昏暴之君不能靠旧称号继续获得治国加成。
    public static bool HasActiveTrait(Actor actor, string trait) =>
        actor?.data != null && actor.isAlive() && !RealisticAgeSystem.IsMinor(actor) && !RulerQualitySystem.IsBad(actor) &&
        (trait == Restorer ? actor.hasTrait(Restorer) || actor.hasTrait(GreatRestorer) : actor.hasTrait(trait));

    public static bool ReignedBy(Kingdom kingdom, string trait)
    {
        Empire empire = kingdom?.GetEmpire();
        Actor emperor = empire?.Emperor;
        return HasActiveTrait(emperor, trait) && IsMonarchy(empire);
    }

    // 开国之初人心未散：开国雄主登基后头 15 年帝国境内不起叛乱；此后雄主的忠诚、正统加成照旧，叛乱照常检定
    public const int FounderShieldYears = 15;

    public static bool FounderShield(Kingdom kingdom)
    {
        if (!ReignedBy(kingdom, Founder)) return false;
        double since = kingdom.GetEmpire().data.newEmperor_timestamp;
        return since < 0d || World.world == null || World.world.getCurWorldTime() < since ||
               Date.getYearsSince(since) < FounderShieldYears;
    }

    // 开国雄主或中兴雄主(享有开国雄主全部特性)是否在位(kingdom 为帝国的任一成员国均可)
    public static bool FounderReigns(Kingdom kingdom) => ReignedBy(kingdom, Founder) || ReignedBy(kingdom, GreatRestorer);

    // 开国或中兴之君在位(开国雄主、中兴雄主、中兴之主)：正统高时打一般国家推进加快(见 FrontSystem.PushFactor)
    public static bool HeroicReign(Kingdom kingdom) => FounderReigns(kingdom) || ReignedBy(kingdom, Restorer);

    // 在位雄主的称号(开国雄主 / 中兴雄主)，史书措辞用
    public static string FounderTitle(Actor ruler) =>
        NeoModLoader.General.LM.Get(ruler != null && ruler.hasTrait(GreatRestorer) && !ruler.hasTrait(Founder)
            ? "trait_" + GreatRestorer : "trait_" + Founder);

    // 开国雄主与本帝国核心(法理疆域)内割据政权之间的战争：不论谁先开战，都算统一战争
    public static bool IsFounderUnificationWar(War war)
    {
        if (war == null || war.hasEnded()) return false;
        // 天下大势的统一战争同样打到底(见 UnificationSystem)
        if (UnificationSystem.IsUnificationWar(war)) return true;
        Kingdom attacker = war.getMainAttacker();
        Kingdom defender = war.getMainDefender();
        return IsFounderAgainstLocalRival(attacker, defender) || IsFounderAgainstLocalRival(defender, attacker);
    }

    private static bool IsFounderAgainstLocalRival(Kingdom founderSide, Kingdom rival)
    {
        Empire empire = founderSide?.GetEmpire();
        if (empire == null || rival == null || empire.CoreKingdom != founderSide || !FounderReigns(founderSide)) return false;
        if (rival.GetEmpire() == empire) return false;
        EmpireCore target = rival.capital?.GetEmpireCore();
        if (target == null) return false;
        // 帝国自己的核心，或雄主都城所在的核心：自立、僭越称帝的王朝另建了一个核心(多半一座城也没有)，
        // 讨伐却是按都城所在的核心挑的(见 WarlordUnificationSystem.FounderDrive)。以前只认前者，雄主之战几乎都认不出来——
        // 不举国归附、同时两场的上限失效、也躲不过战争年限强制停战
        foreach (EmpireCore core in new[] { EmpireCoreManager.Get(empire), founderSide.capital?.GetEmpireCore() })
            if (core != null && (target == core || target.warlord_parent_core_id == core.id)) return true;
        return false;
    }

    // 开国雄主(中兴雄主)在位时本国各军的战力
    public const float FounderCombatFactor = 1.3f;

    public static float FounderCombat(Kingdom kingdom) => FounderReigns(kingdom) ? FounderCombatFactor : 1f;

    // 腐败增长(正数)在中兴之主治下减半；腐败下降不受影响
    public static double ScaleCorruptionGain(Kingdom kingdom, double addition) =>
        addition > 0 && ReignedBy(kingdom, Restorer) ? addition * RestorerCorruptionFactor : addition;
}

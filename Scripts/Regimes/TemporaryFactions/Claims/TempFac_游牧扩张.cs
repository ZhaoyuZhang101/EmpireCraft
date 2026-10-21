using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Regimes;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 游牧扩张(朝堂诉求)。游牧高扩张(用户 2026-10-20 定的游牧基调"高扩张，低控制")：雄主(开国雄主、中兴雄主)在位时势头更猛——
// 冷却 HeroCooldownYears 年，手上的游牧扩张之战不到 HeroMaxWars 场就能再开，不看过度扩张(打下来管不住的交给低控制
// 转间接统治，见 AdministrativeControlSystem)，可南下打定居帝国的边地。
// 平时不大扩张(用户 2026-10-20 定的草原常态，见 SteppeOrderSystem)：没有雄主在位，NormalCooldownYears 年一次、
// 一场仗都没在打才提，只打 PeaceMaxCities 城以内、不在任何帝国里、兵力不到本国一半的小国——至多小打小闹。
// 这是诉求里的扩张，与天下大势的统一战争(只有雄主能打，见 UnificationSystem)是两回事
public class TempFac_游牧扩张 : TemporaryFaction
{
    private const int NormalCooldownYears = 8, HeroCooldownYears = 2, HeroMaxWars = 2;
    private const double PeacePowerLead = 2d;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_游牧扩张();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }
    
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        var target = GetKingdomTarget();
        if (target != null)
        {
            var war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, target, WarTypeLibrary.normal);
            war?.SetEmpireWarType(EmpireWarType.游牧扩张);
        }
        // 平时八年一次(用户 2026-10-17 选"游牧扩张加冷却比兵力"时是五年，2026-10-20 定平时不大扩张)；雄主在位两年一次
        CountDown = empire?.CoreKingdom != null && RulerTraitSystem.FounderReigns(empire.CoreKingdom) ? HeroCooldownYears : NormalCooldownYears;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || empire.CoreKingdom.isRekt()) return false;
        // 游牧文化才有：按文化特性不按政体(阿兹特克、林地部族同走游牧政体却不游牧；元、清式复合帝国看统治族群)
        if (!ClaimRules.HasTrait(empire, EmpireCraft.Scripts.Data.OnomasticsRule.NomadTrait)) return false;
        Kingdom core = empire.CoreKingdom;
        bool hero = RulerTraitSystem.FounderReigns(core);
        if (hero)
        {
            // 雄主在位：手上的游牧扩张之战不到两场、没被全面侵略、出得起军费就能再开，不看过度扩张
            if (ExpansionWars(core) >= HeroMaxWars || WarGoalSystem.FightingForSurvival(core) || CorpsSystem.CannotFundWar(core) ||
                core.getWars().Count(war => war?.data != null && !war.hasEnded()) > HeroMaxWars) return false;
        }
        else
        {
            if (core.hasEnemies()) return false;
            // 同对外扩张(用户 2026-10-17 "游牧太强了")：打仗太多、发不出军饷不开新仗；只打不在任何帝国里的邻国，
            // 自己给出去、收进来的不打，对方有核自己没有不敢碰，兵力(战力)要占优
            if (WarSituation.TooBusyForWar(core) || CorpsSystem.CannotFundWar(core)) return false;
            // 已过度扩张(直辖城数超出容量，见 AdministrativeControlSystem)不再兴兵扩张(用户 2026-10-20 选"游牧来得快去得快")
            if (core.GetOrCreate().admin_overextension > 0f) return false;
        }
        // 国力里的兵力已按武备废弛打折(见 MaladySystem.LevyFactor、CityPopulationSystem.WarPotential)
        double power = WarSituation.Power(empire);
        foreach (Kingdom kingdom in empire.GetKingdomNeighbours())
        {
            if (kingdom == null || kingdom.isRekt() || empire.given_Kingdoms.Contains(kingdom) ||
                empire.taken_Kingdoms.Contains(kingdom) || empire.CoreKingdom.isOpinionTowardsKingdomGood(kingdom) ||
                empire.CoreKingdom.isInWarWith(kingdom)) continue;
            // 平时只打两城以内、不在帝国里、兵力不到本国一半的小国
            if (!hero && (kingdom.IsInEmpire() || kingdom.IsEmpire() || (kingdom.cities?.Count ?? 0) > SteppeOrderSystem.PeaceMaxCities ||
                          power < WarSituation.Power(kingdom) * PeacePowerLead)) continue;
            if (kingdom.IsInEmpire() && !BorderTarget(empire, kingdom, power)) continue;
            if (EmpireCraft.Scripts.Compatibility.NuclearDoctrineSystem.IsDeterred(empire.CoreKingdom, kingdom)) continue;
            if (!kingdom.IsInEmpire() && power <= WarSituation.Power(kingdom)) continue;
            if (!WarGate.CanDeclare(empire.CoreKingdom, kingdom)) continue;
            SetKingdomTarget(kingdom);
            return true;
        }
        return false;
    }

    // 本国手上的游牧扩张之战(攻方)
    private static int ExpansionWars(Kingdom core) =>
        core.getWars().Count(war => war?.data != null && !war.hasEnded() && war.isAttacker(core) &&
                                    war.GetEmpireWarType() == EmpireWarType.游牧扩张);

    // 南下(用户 2026-10-19 选"游牧扩张可打中原帝国的边地")：定居帝国(统治族群不游牧)的成员——不是它的核心——
    // 也可以打：对这一国要强出一半，对它整个帝国也不能不到一半(中央出兵救援时不至于送死)。
    // 打下的城照常易手，不整国并入、不收朝贡(见 CityPatch)
    public static bool BorderTarget(Empire empire, Kingdom kingdom, double power)
    {
        Empire theirs = kingdom?.GetEmpire();
        if (theirs == null || theirs == empire || theirs.isRekt() || theirs.IsArchived() ||
            theirs.CoreKingdom == null || theirs.CoreKingdom == kingdom ||
            ClaimRules.HasTrait(theirs, EmpireCraft.Scripts.Data.OnomasticsRule.NomadTrait)) return false;
        return power > kingdom.GetNationalPower() * 1.5d && power * 2d >= WarSituation.Power(theirs);
    }
}

using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_扩张地盘 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_扩张地盘();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }
    public override void Init(FixedFaction faction)
    {
        base.Init(faction);
        base.Hide = true;
    }
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        var empire = GetEmpire();
        var kingdom = GetKingdomTarget();
        Kingdom coreKingdom = empire?.CoreKingdom;
        // 挑目标到推动完成之间雄主可能已经不在了：开战前再看一遍平时游牧只打小国(见 SteppeOrderSystem.PeacetimeWarAllowed)
        if (empire != null && !empire.isRekt() && coreKingdom != null && !coreKingdom.isRekt() &&
            kingdom != null && !kingdom.isRekt() && kingdom != coreKingdom &&
            !kingdom.isInWarWith(coreKingdom) && SteppeOrderSystem.PeacetimeWarAllowed(coreKingdom, kingdom))
        {
            War war = DiplomacyHelpers.wars.newWar(coreKingdom, kingdom, WarTypeLibrary.normal);
            war?.SetEmpireWarType(EmpireWarType.帝国扩张);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null || empire.isRekt() || empire.CoreKingdom == null || empire.CoreKingdom.isRekt()) return false;
        // 只在忙不过来(手上已有两场仗、或正被全面侵略)时不提；以前中央手上有一场仗(平叛、边境冲突、游牧来扰)就不提，
        // 大帝国几乎总在打点什么，邻国一直没人去打(用户 2026-10-27 问"为什么迟迟不打燕国"，选"朝堂扩张放宽")
        if (WarSituation.TooBusyForWar(empire.CoreKingdom)) return false;
        Kingdom target = null;
        foreach (var kingdom in World.world.kingdoms)
        {
            if (kingdom.IsInSameEmpire(empire.CoreKingdom)) continue;
            if (empire.given_Kingdoms.Contains(kingdom)) continue;
            if (empire.taken_Kingdoms.Contains(kingdom)) continue;
            // 接壤或隔海相望(见 StraitReach，同对外扩张、谋求统一)：以前只认陆地接壤，岛国(日本)的派系提不出扩张。
            // 用户 2026-10-21 选"日本能出岛"；隔海的目标要在本国水师辐射内，真打起来照跨海登陆的规矩
            if (empire.IsNeighbourWith(kingdom) || StraitReach.Across(empire.CoreKingdom, kingdom))
            {
                if (kingdom.isInWarWith(empire.CoreKingdom)) continue;
                if (empire.CoreKingdom.isOpinionTowardsKingdomGood(kingdom)) continue;
                if (!WarGate.CanDeclare(empire.CoreKingdom, kingdom)) continue;
                // 没有雄主在位的游牧国平时只打两城以内、不在帝国里的小国(用户 2026-10-21 选"堵平时扩张的漏口"：
                // 宣战挑目标早有这道限制，派系诉求这条路以前没查)；雄主在位照旧不限
                if (!SteppeOrderSystem.PeacetimeWarAllowed(empire.CoreKingdom, kingdom)) continue;
                // 先收法理：占着本帝国法理城的邻国优先(见 DeJureAgenda)
                if (DeJureAgenda.HasClaimOn(empire.CoreKingdom, kingdom))
                {
                    target = kingdom;
                    break;
                }
                target ??= kingdom;
            }
        }
        if (target == null) return false;
        SetKingdomTarget(target);
        Acc = 30;
        return true;
    }
}

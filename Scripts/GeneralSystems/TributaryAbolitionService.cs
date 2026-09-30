using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 废除君主制时朝贡体系随之废除(天子没了，朝贡礼制也就不成立)。原朝贡国各自选择：
//   · 加入新政体：对宗主好感高、国力小(不到宗主 1/4)、且同文化或理念相近；
//   · 独立：国力达到宗主一半以上，或对宗主有恶感；
//   · 其余成为新政体的附庸国(废除君主制后任何文化都可以有附庸国，见 FeudalVassalService.IsPostMonarchy)。
// 此后这个国家不再接收朝贡国(KingdomExtension.JoinTakenAlliance)。
public static class TributaryAbolitionService
{
    private const int JoinOpinion = 40;
    private const double JoinPowerShare = 0.25d;
    private const double IndependencePowerShare = 0.5d;
    private const float CloseIdeologyDistance = 50f;

    private enum Choice { Join, Independence, Vassal }

    public static void OnMonarchyAbolished(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.taken_Kingdoms == null || empire.taken_Kingdoms.Count == 0) return;
        int joined = 0, independent = 0, vassals = 0;
        foreach (Kingdom tributary in empire.taken_Kingdoms.ToList())
        {
            // 已经是本帝国成员的(旧存档残留)直接从朝贡名单里去掉，不再结算
            if (tributary == null || tributary.isRekt() || tributary.GetEmpire() == empire)
            {
                empire.taken_Kingdoms.Remove(tributary);
                continue;
            }
            Choice choice = Decide(empire, tributary);
            tributary.RemoveTakenAlliance(recordHistory: false);
            // RemoveTakenAlliance 只从朝贡国自己记着的宗主名单里删；那个记录失效(帝国重建、改绑)时，
            // 本帝国名单里的它就一直删不掉，每次更新都重新结算、刷屏。这里直接从本帝国名单里去掉。
            empire.taken_Kingdoms.Remove(tributary);
            string key;
            switch (choice)
            {
                case Choice.Join:
                    empire.join(tributary, pForce: true);
                    key = "tributary_abolished_join_history";
                    joined++;
                    break;
                case Choice.Vassal when FeudalVassalService.Bind(empire.CoreKingdom, tributary):
                    key = "tributary_abolished_vassal_history";
                    vassals++;
                    break;
                default:
                    key = "tributary_abolished_independent_history";
                    independent++;
                    break;
            }
            EventRecorder.Record(empire, string.Format(LM.Get(key), tributary.GetKingdomFullName(), empire.GetEmpireFullName()));
        }
        if (joined + vassals + independent == 0) return;
        EventRecorder.Record(empire, string.Format(LM.Get("tributary_abolished_history"), empire.GetEmpireFullName(),
            joined, vassals, independent));
    }

    private static Choice Decide(Empire empire, Kingdom tributary)
    {
        int opinion = World.world?.diplomacy?.getOpinion(tributary, empire.CoreKingdom)?.total ?? 0;
        double overlordPower = Math.Max(1d, empire.GetNationalPower());
        double share = tributary.GetNationalPower() / overlordPower;
        if (share >= IndependencePowerShare || opinion < 0) return Choice.Independence;
        bool sameCulture = CultureService.GetRealmCulture(tributary) == InstitutionSystem.GetPrimaryCulture(empire);
        bool closeIdeology = tributary.capital != null && PartySystem.IdeologyDistance(
            IdeologyFamilies.StateIdeology(tributary), IdeologyFamilies.StateIdeology(empire)) < CloseIdeologyDistance;
        if (opinion >= JoinOpinion && share < JoinPowerShare && (sameCulture || closeIdeology)) return Choice.Join;
        return Choice.Vassal;
    }

}

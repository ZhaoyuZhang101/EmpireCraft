using System;
using EmpireCraft.Scripts.GeneralSystems;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI.EmpireAI;

public class EmpireCraftEmpireBehCheckMandate : GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        if (empire?.CoreKingdom == null) return BehResult.Continue;
        RulerTraitSystem.CheckRestorer(empire);
        ReignRecordSystem.OnYear(empire);
        RitesSystem.Yearly(empire);
        GuoyeSystem.Yearly(empire);
        BannerSystem.Yearly(empire);
        ImperialSealSystem.Yearly(empire);
        CliqueSystem.Yearly(empire);
        // 还没入派的在任官员(城主、成员国君主等)按入派规矩补录，朋党归不进去的也在这里补
        ActorAI.EmpireCraftActorCheckFaction.EnrollOfficials(empire);
        AncientSchoolsSystem.Yearly(empire);
        JimiTributeSystem.Yearly(empire);
        // 今上为还没定谥的先帝定谥号、庙号(见 Empire.ConferPosthumousNames)
        if (empire.Emperor != null && empire.IsNeedToSetPosthumous()) empire.ConferPosthumousNames(empire.Emperor);
        if (empire.Emperor != null && empire.IsNeedToIncreaseMandate())
        {
            empire.AddMandate(1);
            empire.data.last_increase_mandate_timestamp = world.getCurWorldTime();
            foreach (var memberKingdom in empire.kingdoms_list.ToList())
            {
                if (memberKingdom == null || memberKingdom.isRekt()) continue;
                foreach (var title in memberKingdom.GetControlledTitle())
                {
                    if (title == null || title.isRekt()) continue;
                    EmpireCoreManager.TryAbsorbTitle(empire, title);
                }
            }
            // 不再用国库购买 99999 好感；成员态度由实际正统、政体与政治关系决定。
            foreach (var member in empire.kingdoms_list)
                if (member != null && !member.isRekt()) member.EndMaintainGoodOpinion();
        }

        return BehResult.Continue;
    }
}

using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 独尊官学(学说朋党的朝堂议题)：本文化已施行"定于一尊"(见 AncientSchoolsSystem)、本朝受着天命、天下处在治世，
// 上奏一派请改尊本家学说为正统——正统传播加快、别家放慢，朝中官员逐年改宗(见 CliqueSystem)，这一派势力 +8(正统) +10；
// 三十年一次。廷议时同学说的赞成、别家学说反对，天子派乐见定于一尊。
public class TempFac_独尊官学 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_独尊官学();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 30;

    private FixedFaction Proposer() => CourtSystem.FindFaction(GetEmpire(), factionID);

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        FixedFaction proposer = Proposer();
        if (empire?.CoreKingdom != null && proposer != null)
        {
            AncientSchoolsSystem.SetOrthodox(empire, proposer.Ideology);
            empire.CoreKingdom.TryIncreaseFactionRatio(proposer, 10);
            CompletionOutcome = string.Format(GeneralSystems.CultureDoctrines.Text("claim_outcome_独尊官学_done", GetEmpire()), PartySystem.GetIdeologyName(proposer.Ideology));
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        FixedFaction proposer = Proposer();
        if (empire?.CoreKingdom == null || proposer == null || !proposer.IsClique || CliqueSystem.IsClassClique(proposer)) return false;
        if (!IdeologyFamilies.IsSchool(proposer.Ideology) || CliqueSystem.OfficialSchool(empire) == proposer.Ideology ||
            !AncientSchoolsSystem.OrthodoxyEnacted(empire)) return false;
        return OffCooldown() && TianxiaSystem.MandateOf(empire.CoreKingdom) != null &&
               TianxiaSystem.PhaseOf(empire.CoreKingdom) == TianxiaSystem.Peace;
    }

    // 同学说的赞成，别家学说反对；天子派乐见定于一尊
    public override float TargetStance(FixedFaction faction)
    {
        FixedFaction proposer = Proposer();
        if (faction == null || proposer == null || !faction.IsClique) return 0f;
        if (faction.Ideology == proposer.Ideology) return 15f;
        if (faction.Ideology == PartyIdeology.ImperialPower) return 6f;
        return CliqueSystem.IsClassClique(faction) ? 0f : -12f;
    }
}

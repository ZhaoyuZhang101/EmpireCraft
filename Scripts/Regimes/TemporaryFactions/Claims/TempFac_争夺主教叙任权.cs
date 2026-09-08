using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_争夺主教叙任权 : TemporaryFaction
{
    public TempFac_争夺主教叙任权()
    {
        canBePushByLocal = true;
    }

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var result = new TempFac_争夺主教叙任权();
        result.Init(faction);
        result.ShowAsPlot = ShowAsPlot;
        result.Hide = Hide;
        result.Active = Active;
        result.canBePushByLocal = canBePushByLocal;
        return result;
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        ReligionLevel? desired = GetDesiredLevel();
        if (HolyRomanClaimRules.IsEligible(empire) && desired.HasValue)
        {
            empire.CoreKingdom.GetRegime().SetReligionLevel(desired.Value);
            CompletionOutcome = LM.Get(desired.Value == ReligionLevel.High
                ? "holy_roman_outcome_investiture_clergy"
                : "holy_roman_outcome_investiture_crown");
            LogService.LogInfo($"执行{type}");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        ReligionLevel? desired = GetDesiredLevel();
        return HolyRomanClaimRules.IsEligible(empire) && desired.HasValue &&
               empire.CoreKingdom.GetRegime().GetReligionLevel() != desired.Value;
    }

    private ReligionLevel? GetDesiredLevel()
    {
        return GetFaction()?.Type switch
        {
            FactionType.中央 => ReligionLevel.Medium,
            FactionType.神权 => ReligionLevel.High,
            _ => null
        };
    }
}

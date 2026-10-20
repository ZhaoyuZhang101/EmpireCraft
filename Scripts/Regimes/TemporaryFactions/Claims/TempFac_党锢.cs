using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 党锢(阉党的朝堂议题)：禁锢朝中势力最大、又不投靠阉党的学说朋党(至少占一成)——党人逐出朝堂，三分之二的势力归阉党，法统 -3(见 CliqueSystem.Proscribe)；二十年一次
public class TempFac_党锢 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_党锢();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 20;

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom != null)
        {
            string name = CliqueSystem.Proscribe(empire, factionID);
            if (!string.IsNullOrEmpty(name)) CompletionOutcome = string.Format(GeneralSystems.CultureDoctrines.Text("claim_outcome_党锢_done", GetEmpire()), name);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && OffCooldown() && CliqueSystem.ProscriptionTarget(empire, factionID) != null;
    }
}

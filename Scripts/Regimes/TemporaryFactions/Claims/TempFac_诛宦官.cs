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

// 诛宦官(清流、诸侯派、后党的朝堂议题，利义见 CourtDoctrine)：阉党有人领头时请诛宦官——宦官伏诛，阉党打回蛰伏，势力归天子派，法统 +5(见 CliqueSystem.PurgeEunuchs)
public class TempFac_诛宦官 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_诛宦官();
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
        if (empire?.CoreKingdom != null)
        {
            string name = CliqueSystem.PurgeEunuchs(empire);
            if (!string.IsNullOrEmpty(name)) CompletionOutcome = string.Format(LM.Get("claim_outcome_诛宦官_done"), name);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && CliqueSystem.CanPurgeEunuchs(empire);
    }
}

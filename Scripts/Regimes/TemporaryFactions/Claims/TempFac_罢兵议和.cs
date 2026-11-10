using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 罢兵议和(用户 2026-11-11 "增加更多的议题，比如开战，或者其他的符合利益的"，问答全选)：本朝主战的仗打满最短战期、又不是统一战争、开国雄主之战、剧本点名的仗、全面战争、军阀内战，
// 久拖不决就照议和台撮合两边最接近的折中罢兵(结局同战争窗口的议和)。商人农民赞成，武官反对
public class TempFac_罢兵议和 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_罢兵议和();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 2;

    public override bool CheckCondition()
    {
        War war = FindWar();
        if (war == null) return false;
        Kingdom core = GetEmpire().CoreKingdom;
        SetKingdomTarget(war.main_attacker == core ? war.main_defender : war.main_attacker);
        return true;
    }

    private War FindWar()
    {
        Kingdom core = GetEmpire()?.CoreKingdom;
        if (core == null || !OffCooldown()) return null;
        return core.getWars().Where(war => war != null && (war.main_attacker == core || war.main_defender == core) && Negotiable(war))
            .OrderByDescending(WarSituation.WarYears).FirstOrDefault();
    }

    private static bool Negotiable(War war) =>
        PeaceTable.Usable(war) && !war.GetOrCreate().scripted_war && !war.isTotalWar() && !UnificationSystem.IsUnificationWar(war) &&
        !RulerTraitSystem.IsFounderUnificationWar(war) && !WarlordEraSystem.IsCivilWar(war) &&
        WarSituation.WarYears(war) >= WarPeaceRules.MinWarYears;

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        War war = FindWar();
        if (war != null)
        {
            Kingdom core = GetEmpire().CoreKingdom;
            Kingdom enemy = war.main_attacker == core ? war.main_defender : war.main_attacker;
            World.world.wars.endWar(war, WarWinner.Peace);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_罢兵议和_done"), enemy?.GetKingdomName() ?? "");
        }
        End();
    }
}

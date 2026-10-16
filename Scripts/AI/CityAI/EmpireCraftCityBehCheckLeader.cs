using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ai;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NCMS.Extensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckLeader : GameAICityBase
{
    public override Type OriginalBeh => typeof(CityBehCheckLeader);
    public override BehResult execute(City pCity)
    {
        if (pCity.isNeutral() || !pCity.units.Any()) return BehResult.Continue;
        CheckLeaderClan(pCity);
        CheckFindLeader(pCity);
        return BehResult.Continue;
    }

    private void CheckLeaderClan(City pCity)
    {
        // 城主不能是小孩：已经在任的卸任，下面另选成年人(君主可以是幼主，但君主不兼城主)
        if (pCity.hasLeader() && !pCity.leader.isAdult() && !pCity.leader.isKing()) pCity.removeLeader();
        if (pCity.hasLeader())
        {
            pCity.EndChoosingHeir();
            Actor leader = pCity.leader;
            leader.CheckSpecificClan();
            pCity.SetPersonalIdentity(leader?.GetPersonalIdentity());
        }
    }

    private async void CheckFindLeader(City pCity)
    {
        if (pCity.hasLeader()) return;
        OfficeObject office = pCity.GetOffice();
        if (office == null) return;
        office.meta_object = pCity;
        office.Select(pCity.kingdom, "城市");
    }

}
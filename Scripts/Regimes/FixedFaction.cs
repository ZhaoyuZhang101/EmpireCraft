using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.Regimes;

public class FactionManager
{
    public Dictionary<string, FixedFaction>  FixedFactions = new();

    public void RecordFactions(FixedFaction faction, Empire empire)
    {
        var id = $"{empire.id}_{faction.GetID()}";
        FixedFactions.Add(id, faction);
    }
}

public enum FactionType
{
    尊王,  //王室为主
    自治,  //诸侯为主
    攘夷,  //主战（同化异族）
    统一,  //主战（统一同族）
    绥靖,  //主和
    血脉, //国王为单一血脉
    僭主, //霸者为王
    共和,  //反移民，发展自己
    民主,  //移民，吸血国外
    革命   //一党，革命输出
}
public class FixedFaction
{
    private string _id;
    //特质需求（拥有该特质的人会更加倾向于加入此派系）
    private List<string> _requiredTraits = new();
    public FactionType Type { get; set; }
    public bool Ban { get; set; } = false;
    public string Name { set; get; }
    public long EmpireId { get; set; } = -1L;
    public List<long> Members = new();
    public double TotalPower => Members.Sum(a=>World.world.units.get(a)?.GetIdentity()?.TotalPerformance??0);
    //倾向于推动的政策
    public List<TemporaryFactionType> TemporaryFactions => ConfigData.FactionConfig.TryGetValue(Type, out var tfList)? tfList : null;
    public long Leader = -1L;

    public void AddMember(Actor pActor)
    {
        Members.Add(pActor.id);
        if (Members.Count == 1)
        {
            Leader = pActor.id;
        }
    }

    public void BanFaction()
    {
        Ban = true;
        Members.Clear();
        Leader = -1L;
    }

    public void RemoveMember(Actor pActor)
    {
        Members.Remove(pActor.id);
        if (pActor.id == Leader)
        {
            Leader = -1L;
        }

        if (Members.Count == 0)
        {
            Leader = -1L;
        }
    }

    public void SetLeader(Actor pActor)
    {
        Leader = pActor.id;
        if (!Members.Contains(pActor.id))
        {
            Members.Add(pActor.id);
        }
    }

    public void RemoveLeader()
    {
        Leader = -1L;
    }

    public string GetID()
    {
        return _id;
    }
}
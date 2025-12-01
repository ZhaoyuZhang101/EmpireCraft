using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public abstract class TemporaryFaction
{
    public TemporaryFactionType type => Enum.TryParse(GetType().ToString().Split('_').Last(), out TemporaryFactionType res) ? res : default;
    public List<long>  kingdoms = new List<long>();
    public FactionType factionType = FactionType.无;
    public long EmpireID = -1L;
    public long targetID = -1L;
    public MetaType targetType;
    public int progress = 0;
    private bool started = false;
    public double timestamp = -1L;

    public void Init(FixedFaction faction)
    {
        factionType = faction.Type;
        EmpireID =  faction.EmpireId;
        timestamp = World.world.getCurWorldTime();
        kingdoms = new List<long>();
    }

    public FixedFaction GetFaction()
    {
        Empire empire = GetEmpire();
        if (empire != null)
        {
            Kingdom kingdom = empire.CoreKingdom;
            Regime regime = kingdom.GetRegime();
            return regime?.Factions?.Find(f => f.Type == factionType);
        }

        return null;
    }

    public Empire GetEmpire()
    {
        return ModClass.EMPIRE_MANAGER.get(EmpireID);
    }
    public void Start(NanoObject ptarget = null)
    {
        targetID = ptarget?.getID()??-1L;
        targetType = ptarget?.meta_type??MetaType.Kingdom;
        started = true;
    }

    public bool IsStarted()
    {
        return started;
    }

    public void End()
    {
        kingdoms.Clear();
        started = false;
        progress = 0;
    }
    public void JoinKingdom(Kingdom kingdom)
    {
        kingdoms.Add(kingdom.id);
    }
    //更新：每年一次共计十年
    public void Update()
    {
        if (started)
        {
            LogService.LogInfo($"当前内阁派系：{GetEmpire().GetCabinetLeader()?.GetFaction()?.Type},进度{progress}");
            if (GetEmpire().GetCabinetLeader()?.GetFaction()?.Type != factionType)
            {
                LogService.LogInfo("触发终止诉求");
                End();
                return;
            }
            progress ++;
            if (progress >= 60) Execute();
        }
        else
        {
            progress = 0;
        }
    }

    public void CheckNeedToUpdate()
    {
        if (Date.getMonthsSince(timestamp) > 1)
        {
            Update();
            timestamp = World.world.getCurWorldTime();
        }
    }
    /// <summary>
    /// 触发条件成功后的执行动作
    /// </summary>
    public abstract void Execute();
    
    /// <summary>
    /// 检查条件是否满足
    /// </summary>
    /// <returns>返回条件是否满足的结果</returns>
    public abstract bool CheckCondition();

    public List<Kingdom> GetMembers()
    {
        return kingdoms.Select(k=>World.world.kingdoms.get(k)).ToList();
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;
[JsonConverter(typeof(TemporaryFactionConverter))]
public abstract class TemporaryFaction
{
    [JsonIgnore]
    public TemporaryFactionType type => Enum.TryParse(GetType().ToString().Split('_').Last(), out TemporaryFactionType res) ? res : default;

    public int Acc = 0;
    public bool Hide = false;
    public bool Active = true;
    public int CountDown = 0;
    public virtual int Budget => 0;
    public List<long>  kingdoms = new List<long>();
    public string factionID = "";
    public bool ShowAsPlot = false;
    [JsonIgnore] 
    public AdvancedButton hideButton;
    [JsonIgnore] 
    public AdvancedButton localPushButton;
    [JsonIgnore] 
    public AdvancedButton activeButton;
    [JsonIgnore] 
    public AdvancedButton showButton;
    public long EmpireID = -1L;
    public long KingdomID = -1L;
    public long TargetID = -1L;
    public bool canBePushByLocal = false;
    public MetaType pusherType = MetaType.None;
    public MetaType TargetType = MetaType.None;
    [JsonIgnore]
    public Empire Empire => GetEmpire();
    public float progress = 0;
    public float progressMax = 60;
    [JsonIgnore]
    public float acceleration => (GetEmpire()?.data.additions.cabinet_acc??0)+(GetEmpire()?.data.additions.addition[OfficerPowerType.审核]??0)+Acc;
    private bool started = false;
    public double timestamp = -1L;
    public double countDownTimestamp = -1L;
    [JsonProperty("started")]
    public bool StartedState
    {
        get => started;
        set => started = value;
    }
    [JsonIgnore]
    public virtual bool RequireCrimeTarget => false;
    [JsonIgnore]
    public virtual bool RequireRenown => false;
    public virtual float RequireRenownMultiplier => 1f;

    public bool OwnEnoughRenown(Kingdom target)
    {
        if ((GetEmpire().Emperor?.renown??0f)==0f) return false;
        return (GetEmpire().Emperor?.renown ?? 0f) >(target?.king?.renown ?? 0f) * RequireRenownMultiplier ;
    }
    public void CopyRuntimeStateFrom(TemporaryFaction other)
    {
        if (other == null) return;

        Acc = other.Acc;
        Hide = other.Hide;
        Active = other.Active;
        CountDown = other.CountDown;
        kingdoms = other.kingdoms != null ? new List<long>(other.kingdoms) : new List<long>();
        factionID = other.factionID;
        ShowAsPlot = other.ShowAsPlot;
        EmpireID = other.EmpireID;
        KingdomID = other.KingdomID;
        TargetID = other.TargetID;
        canBePushByLocal = other.canBePushByLocal;
        pusherType = other.pusherType;
        TargetType = other.TargetType;
        progress = other.progress;
        progressMax = other.progressMax;
        StartedState = other.StartedState;
        timestamp = other.timestamp;
        countDownTimestamp = other.countDownTimestamp;
    }
    public virtual void Init(FixedFaction faction)
    {
        factionID = faction.GetID();
        EmpireID    = faction.EmpireId;
        timestamp   = World.world.getCurWorldTime();
        kingdoms    = new List<long>();
        started     = false;
        countDownTimestamp = World.world.getCurWorldTime();
    }
    public abstract TemporaryFaction Clone(FixedFaction faction);
    public bool IsNeedToCountDown()
    {
        if (Date.getYearsSince(countDownTimestamp) > 1)
        {
            countDownTimestamp =  World.world.getCurWorldTime();
            return true;
        }
        return false;
    }
    public void SetEmpire(Empire pEmpire)
    {
        this.EmpireID = (pEmpire == null || pEmpire.isRekt() || pEmpire.IsArchived()) ? -1L : pEmpire.getID();
    }  
    public void SetKingdom(Kingdom pKingdom)
    {
        this.KingdomID = pKingdom.getID();
    }    
    public Kingdom GetKingdom()
    {
        return World.world.kingdoms.get(KingdomID);
    }    
    
    // 统一入口：设定“国家目标”
    public void SetKingdomTarget(Kingdom k, string reason = "")
    {
        var id = k?.getID() ?? -1L; // 一律用 base_id
        TargetID = id;
        TargetType = MetaType.Kingdom;
    }
    
    protected Kingdom GetKingdomTarget()
    {
        if (TargetType != MetaType.Kingdom || TargetID < 0) return null;
        return World.world.kingdoms.get(TargetID);
    }      
    
    // 统一入口：设定“头衔目标”
    protected void SetTitleTarget(KingdomTitle k, string reason = "")
    {
        var id = k?.getID() ?? -1L; // 一律用 base_id
        TargetID = id;
        TargetType = MetaTypeExtension.KingdomTitle;
    }
    
    protected KingdomTitle GetTitleTarget()
    {
        if (TargetType != MetaTypeExtension.KingdomTitle || TargetID < 0) return null;
        return ModClass.KINGDOM_TITLE_MANAGER.get(TargetID);
    }    
    
    // 统一入口：设定“国家目标”
    protected void SetCityTarget(City k, string reason = "")
    {
        var id = k?.getID() ?? -1L; // 一律用 base_id
        TargetID = id;
        TargetType = MetaType.City;
    }
    
    protected City GetCityTarget()
    {
        if (TargetType != MetaType.City || TargetID < 0) return null;
        return World.world.cities.get(TargetID);
    }   
    
    // 统一入口：设定“宗教目标”
    protected void SetReligionTarget(Religion k, string reason = "")
    {
        var id = k?.getID() ?? -1L; // 一律用 base_id
        TargetID = id;
        TargetType = MetaType.Religion;
    }
    
    protected Religion GetReligionTarget()
    {
        if (TargetType != MetaType.Religion || TargetID < 0) return null;
        return World.world.religions.get(TargetID);
    }
    
    protected void SetActorTarget(Actor pActor)
    {
        this.TargetType = MetaType.Unit;
        this.TargetID = pActor.getID();
    }

    protected Actor GetActorTarget()
    {
        if (TargetType == MetaType.Unit)
        {
            return World.world.units.get(TargetID);
        }

        return null;
    }

    protected bool TrySetTarget(Kingdom kingdom, string reason = "")
    {
        if (!CanUseCrimeRestrictedTarget(kingdom?.king, kingdom))
        {
            return false;
        }

        SetKingdomTarget(kingdom, reason);
        return true;
    }

    protected bool TrySetTarget(Actor actor)
    {
        if (!CanUseCrimeRestrictedTarget(actor, actor?.kingdom))
        {
            return false;
        }

        SetActorTarget(actor);
        return true;
    }

    protected bool TrySetTarget(City city, string reason = "")
    {
        Actor actor = city?.leader ?? city?.kingdom?.king;
        Kingdom kingdom = city?.kingdom;
        if (!CanUseCrimeRestrictedTarget(actor, kingdom))
        {
            return false;
        }

        SetCityTarget(city, reason);
        return true;
    }

    protected bool TrySetTarget(Religion religion, string reason = "")
    {
        if (RequireCrimeTarget)
        {
            return false;
        }

        SetReligionTarget(religion, reason);
        return true;
    }

    protected bool TrySetTarget(KingdomTitle title, string reason = "")
    {
        Kingdom kingdom = title?.title_capital?.kingdom;
        if (!CanUseCrimeRestrictedTarget(kingdom?.king, kingdom))
        {
            return false;
        }

        SetTitleTarget(title, reason);
        return true;
    }

    protected bool HasRequiredCrimeForCurrentTarget()
    {
        if (!RequireCrimeTarget)
        {
            return true;
        }

        GetCrimeScopeForCurrentTarget(out Actor actor, out Kingdom kingdom);
        return CanUseCrimeRestrictedTarget(actor, kingdom);
    }

    protected bool TryEnforceCrimeForCurrentTarget()
    {
        if (!RequireCrimeTarget)
        {
            return true;
        }

        GetCrimeScopeForCurrentTarget(out Actor actor, out Kingdom kingdom);
        return EmpireLawSystem.TryEnforceCrimeForClaim(actor, kingdom) != null;
    }

    public string GetCurrentTargetCrimeName()
    {
        if (!RequireCrimeTarget)
        {
            return null;
        }

        GetCrimeScopeForCurrentTarget(out Actor actor, out Kingdom kingdom);
        return EmpireLawSystem.GetResolvableCrimeName(actor, kingdom);
    }

    private bool CanUseCrimeRestrictedTarget(Actor actor, Kingdom kingdom)
    {
        if (!RequireCrimeTarget)
        {
            return true;
        }

        if (actor == null || actor.isRekt() || kingdom == null || kingdom.isRekt())
        {
            return false;
        }

        return EmpireLawSystem.HasResolvableCrimeRecord(actor, kingdom);
    }

    private void GetCrimeScopeForCurrentTarget(out Actor actor, out Kingdom kingdom)
    {
        actor = null;
        kingdom = null;

        switch (TargetType)
        {
            case MetaType.Kingdom:
                kingdom = GetKingdomTarget();
                actor = kingdom?.king;
                return;
            case MetaType.City:
                City city = GetCityTarget();
                kingdom = city?.kingdom;
                actor = city?.leader ?? kingdom?.king;
                return;
            case MetaType.Unit:
                actor = GetActorTarget();
                kingdom = actor?.kingdom;
                return;
            default:
                if (TargetType == MetaTypeExtension.KingdomTitle)
                {
                    kingdom = GetTitleTarget()?.title_capital?.kingdom;
                    actor = kingdom?.king;
                }

                return;
        }
    }

  

    protected bool CheckRebelling(Kingdom kingdom)
    {
        var targetFaction = kingdom?.king?.GetFaction();
        //全部势力
        var cities = new List<City>();
        if (targetFaction != null)
        {
            var targetMembers = targetFaction.Members.ToList();
            if (targetFaction != GetFaction())
            {
                foreach (var a in targetFaction.Members)
                {
                    if (targetMembers.Contains(a))
                    {
                        Actor actor = World.world.units.get(a);
                        if (actor?.isKing() ?? false)
                        {
                            targetMembers.Remove(a);
                            foreach (var city in actor.kingdom.cities)
                            {
                                if (GetEmpire().CoreKingdom.capital!=city)
                                {
                                    cities.Add(city);
                                }
                                targetMembers.Remove(city.leader?.getID() ?? -1L);
                            }
                        }
                        else
                        {
                            if (actor?.isCityLeader() ?? false)
                            {
                                if (GetEmpire().CoreKingdom.capital!=actor.city)
                                {
                                    cities.Add(actor.city);
                                }
                                targetMembers.Remove(a);
                            }
                        }
                    }
                }
            }
        }
        var totalWarriors = cities.Sum(c => c.countWarriors());
        if (totalWarriors >= (GetEmpire()?.countWarriors()??9999) - totalWarriors || (kingdom?.GetEmpire()?.CoreKingdom?.GetMoney()??9999)<0)
        {
            var leader = targetFaction?.GetLeader()??kingdom?.king;
            var royalMembers = targetFaction?.Members.Select(id => World.world.units.get(id)).ToList().FindAll(a =>
                a.GetSpecificClan() == GetEmpire().EmpireSpecificClan && a != null);
            if (royalMembers?.Any()??false)
            {
                leader = royalMembers?.OrderByDescending(a => a.age).First();
            }

            if (leader != null)
            {
                kingdom.StartFactionRebelling(targetFaction);
                foreach (var c in cities)
                {
                    if (c == kingdom.capital) continue;
                    c.joinAnotherKingdom(kingdom);
                }

                var war = World.world.diplomacy.startWar(kingdom, GetEmpire().CoreKingdom,
                    WarTypeLibrary.normal);
                if (war == null)
                {
                    return false;
                }
                war.SetEmpireWarType(EmpireWarType.派系叛乱);
                if (targetFaction != null)
                {
                    war.data.name = targetFaction.Name + "\u200A" + "叛乱";
                }
                else
                {
                    war.data.name = kingdom.GetOffice().GetName() + "\u200A" + "叛乱";
                }
                return true;
            }
        }
        return false;
    }

    protected FixedFaction GetFaction()
    {
        if (Empire != null)
        {
            Kingdom kingdom = Empire.CoreKingdom;
            Regime regime = kingdom.GetRegime();
            return regime?.GetPlayerFactions()?.Find(f => f.GetID() == factionID);
        }

        return null;
    }

    public Empire GetEmpire()
    {
        return ModClass.EMPIRE_MANAGER.get(EmpireID);
    }
    public void Start()
    {
        if (CountDown <= 0)
        {
            bool shouldLogPreparing = !started;
            started = true;
            timestamp = World.world.getCurWorldTime();
            if (countDownTimestamp < 0)
            {
                countDownTimestamp = timestamp;
            }
            Empire empire = Empire;
            if (empire != null)
            {
                empire.RunningTemporaryFaction = this;
                if (shouldLogPreparing && HasRequiredCrimeForCurrentTarget())
                {
                    TranslateHelper.LogTemporaryFactionPreparing(empire, type.ToString(),
                        TranslateHelper.GetTemporaryFactionTargetText(TargetType, TargetID),
                        GetCurrentTargetCrimeName());
                }
            }
            if (ShowAsPlot)
            {
                var plot = AssetManager.plots_library.basic_plots.Find(p => p.id == "empire_plots");
                var emperor = empire?.Emperor;
                if (emperor != null)
                {
                    if (!emperor.plot?.isSameType(plot) ?? true)
                    {
                        plot?.try_to_start_advanced(emperor, plot, true);
                    } 
                }
            }
        }
    }

    public bool IsStarted()
    {
        return started;
    }

    public void End()
    {
        TargetID = -1L;
        TargetType = MetaType.None;
        kingdoms.Clear();
        started = false;
        progress = 0;
        Acc = 0;
        if (Empire != null)
        {
            if ((!Empire.RunningTemporaryFaction?.IsStarted()) ?? true)
            {
                Empire.RunningTemporaryFaction = null;  
            }
        }
    }

    public void FinishedAction()
    {
        GetEmpire()?.CoreKingdom?.SubMoney(Budget);
    }

    public void JoinKingdom(Kingdom kingdom)
    {
        kingdoms.Add(kingdom.id);
    }
    //更新：每年一次共计十年
    private void Update()
    {
        if (!CheckContinue())
        {
            End();
            return;
        }
        if (started&&Active)
        {
            if (!CheckTarget())
            {
                End();
                return;
            }
            if (GetEmpire().CoreKingdom.GetRegime().has_cabinet)
            {
                if (GetEmpire().CoreKingdom.GetRegime().type != RegimeType.Feudalism)
                {
                    if (GetEmpire().GetCabinetLeader()?.GetFaction().GetID() != factionID)
                    {
                        End();
                        return;
                    } 
                }
            }
            if (!ShowAsPlot)
            {
                progress += (1+((acceleration<0?0:acceleration)/5));
                if (progress >= progressMax) Execute();
            }
        }
        else
        {
            End();
        }
    }

    public bool CheckTarget()
    {
        if (TargetType == MetaType.None) return true;
        if (GetActorTarget().isRekt())
        {
            if (GetKingdomTarget().isRekt())
            {
                if (GetCityTarget().isRekt())
                {
                    if (GetReligionTarget().isRekt())
                    {
                        if (GetTitleTarget().isRekt())
                        {
                            return false;
                        }
                    }
                }
            }
        }
        return true;
    }
    public void CheckNeedToUpdate()
    {
        if (Date.getMonthsSince(timestamp) < 1) return;
        LogService.LogInfo("更新进度");
        Update();
        timestamp = World.world.getCurWorldTime();
    }
    /// <summary>
    /// 触发条件成功后的执行动作
    /// </summary>
    public abstract void Execute();

    /// <summary>
    /// 检测本体条件是否满足
    /// </summary>
    /// <param name="actor">发起主体</param>
    /// <returns></returns>
    public virtual bool CheckLocalCondition(Kingdom actor)
    {
        return CheckLocalContinue(actor);
    }

    public virtual bool CheckLocalContinue(Kingdom actor)
    {
        if (GetEmpire()==null) return false;
        if (actor?.king==null) return false;
        var actorFaction = actor.king.GetFaction();
        if (actorFaction == null) return false;
        if (actorFaction != GetFaction()) return false;
        return true;
    }

    public virtual void LocalEnd(Kingdom actor)
    {
        actor.EndProgress();
    }
    
    /// <summary>
    /// 检查条件是否满足
    /// </summary>
    /// <returns>返回条件是否满足的结果</returns>
    public abstract bool CheckCondition();

    /// <summary>
    /// 检查条件是否满足
    /// </summary>
    /// <returns>返回条件是否满足的结果</returns>
    public virtual bool CheckContinue()
    {
        var emperor = GetEmpire().Emperor;
        if (ShowAsPlot && emperor.isRekt())
        {
            return false;
        }
        return true;
    }

    public List<Kingdom> GetMembers()
    {
        return kingdoms.Select(k=>World.world.kingdoms.get(k)).ToList();
    }
}

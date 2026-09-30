using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCaftActorJudgeClass: GameAIActorBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Actor pActor)
    {
        pActor.SetSocialClass(JudgeClass(pActor));
        return BehResult.Continue;
    }
    // 世袭王国的王族(贵族阶层)。原来每判定一个人就把全部王国复制成列表再逐个比，而判定阶层的地方很多
    // (每个单位的 AI、就业/市民/土地系统统计每个居民)，是 单位数×王国数 的开销。
    // 改成每帧只建一次王族集合，结果与原来完全一致(包括原逻辑里"无王的世袭王国 → 王族为 null"的匹配)。
    private static readonly global::System.Collections.Generic.HashSet<Clan> NobleClans = new();
    private static int _nobleClansFrame = -1;
    private static object _nobleClansWorld;

    private static bool IsNobleClan(Clan clan)
    {
        if (_nobleClansFrame != Time.frameCount || !ReferenceEquals(_nobleClansWorld, world))
        {
            _nobleClansFrame = Time.frameCount;
            _nobleClansWorld = world;
            NobleClans.Clear();
            foreach (Kingdom kingdom in world.kingdoms)
            {
                Regime regime = kingdom?.GetRegime();
                if (regime != null && regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession)
                    NobleClans.Add(kingdom.getKingClan());
            }
        }
        return NobleClans.Contains(clan);
    }

    public static SocialClass JudgeClass( Actor pActor)
    {
        if (IsNobleClan(pActor.clan))
        {
            return SocialClass.Noble;
        }
        if (pActor.IsOnOffice())
        {
            return SocialClass.Officer;
        }
        if (pActor.isWarrior())
        {
            return SocialClass.Army;
        }
        if (IsManualWorkerJob(pActor)) return SocialClass.Labour;
        if (LandEconomySystem.IsLandlord(pActor)) return SocialClass.Landlord;
        if (pActor.GetOrCreate().is_economic_merchant) return SocialClass.Merchant;
        if (UrbanEmploymentSystem.IsEmployed(pActor)) return SocialClass.Labour;
        if (UrbanCitizenSystem.IsCitizen(pActor)) return SocialClass.Citizen;
        return SocialClass.Peasant;
    }

    public static bool IsManualWorkerJob(Actor actor) => actor?.citizen_job?.id switch
    {
        "woodcutter" or "miner" or "miner_deposit" or "road_builder" or "cleaner" or
            "manure_cleaner" or "gatherer_herbs" or "gatherer_bushes" or "gatherer_honey" => true,
        _ => false
    };
}

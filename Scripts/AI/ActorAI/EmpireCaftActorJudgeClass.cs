using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
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
    // 每帧只建一次传统王族集合；无君主、无氏族不能把所有无氏族平民误判成贵族。
    private static readonly global::System.Collections.Generic.HashSet<Clan> NobleClans = new();
    private static int _nobleClansFrame = -1;
    private static object _nobleClansWorld;

    private static bool IsNobleClan(Clan clan)
    {
        if (clan == null || world?.kingdoms == null) return false;
        if (_nobleClansFrame != Time.frameCount || !ReferenceEquals(_nobleClansWorld, world))
        {
            _nobleClansFrame = Time.frameCount;
            _nobleClansWorld = world;
            NobleClans.Clear();
            foreach (Kingdom kingdom in world.kingdoms)
            {
                Regime regime = kingdom?.GetRegime();
                Clan royalClan = kingdom?.getKingClan();
                Empire empire = kingdom?.GetEmpire();
                if (royalClan != null && regime != null && regime.type != RegimeType.Modern &&
                    !RepublicSystem.IsRepublic(empire) && !ConstitutionalSuccessionSystem.IsProtected(empire) &&
                    regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession)
                    NobleClans.Add(royalClan);
            }
        }
        return NobleClans.Contains(clan);
    }

    public static bool IsNoble(Actor actor)
    {
        if (actor == null || actor.isRekt() || !actor.isAlive()) return false;
        Empire empire = actor.kingdom?.GetEmpire();
        if (actor.kingdom?.GetRegime()?.type == RegimeType.Modern || RepublicSystem.IsRepublic(empire)) return false;
        if (!ConstitutionalSuccessionSystem.IsProtected(empire)) return IsNobleClan(actor.clan);

        // 立宪保留皇室与爵位，不把整个氏族及其世代繁衍的远支都算作贵族阶层。
        Actor emperor = empire.Emperor;
        if (actor == emperor) return true;
        if (actor == empire.CoreKingdom?.GetHeir() &&
            SpecificClanManager.SameLineage(actor.GetSpecificClan(), empire.EmpireSpecificClan)) return true;
        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        PersonalClanIdentity monarchIdentity = emperor?.GetPersonalIdentity();
        if (identity != null && monarchIdentity != null)
        {
            if (monarchIdentity.lover.identity == identity.id) return true;
            if (SpecificClanManager.SameLineage(actor.GetSpecificClan(), empire.EmpireSpecificClan) &&
                (identity.father == monarchIdentity.id || identity.mother == monarchIdentity.id)) return true;
        }
        if (actor.HasHonoraryPeerage(empire)) return true;
        if (actor.HasVirtualEnfeoff(empire))
        {
            long titleId = actor.GetOrCreate().virtual_enfeoff_title_id;
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER?.get(titleId);
            if (title != null && !title.isRekt() &&
                empire.data.legal_peerage_holders?.TryGetValue(titleId, out long holderId) == true &&
                holderId == actor.id) return true;
        }
        foreach (long titleId in actor.GetOwnedTitle() ?? Enumerable.Empty<long>())
        {
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER?.get(titleId);
            if (title != null && !title.isRekt() && title.owner == actor &&
                (title.main_kingdom == null ||
                 title.main_kingdom.GetRegime()?.GetLeaderSelectMethod() == LeaderSelectMethod.Succession)) return true;
        }
        // 世袭诸侯本人的贵族身份保留，属下与同族人口不再自动继承他的社会身份。
        return actor.isKing() && actor.kingdom.GetRegime()?.GetLeaderSelectMethod() == LeaderSelectMethod.Succession;
    }

    public static SocialClass JudgeClass( Actor pActor)
    {
        if (pActor == null) return SocialClass.Peasant;
        if (IsNoble(pActor))
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

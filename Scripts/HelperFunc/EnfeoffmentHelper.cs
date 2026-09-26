using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.HelperFunc;

// 把兄弟分封为附庸国的原语,原本只写在 TempFac_分封(周期性诉求,一次只分封一人)里；
// 提炼出来供分割继承法(一次继承事件里批量分封新君的所有合法兄弟)复用，行为完全不变。
public static class EnfeoffmentHelper
{
    // 按法理分封：可分的是核心王国手里的法理头衔(整块封出去)，但王畿不分——都城所在的头衔、
    // 以及跟帝国同名的头衔留给天子。之前是挑一座城，并且跳过帝国核心法理内的所有城市；而帝国
    // 建立时会把当时控制的全部法理都登记为核心，结果一般帝国一座可封的城都找不到，分封从不发生。
    // 城多的头衔排在前面，年长的兄弟先挑。
    public static List<KingdomTitle> FindEnfeoffableTitles(Empire empire)
    {
        Kingdom coreKingdom = empire?.CoreKingdom;
        if (coreKingdom?.capital == null || ModClass.KINGDOM_TITLE_MANAGER == null) return new List<KingdomTitle>();
        KingdomTitle capitalTitle = coreKingdom.capital.GetTitle();
        string empireName = empire.GetEmpireName();
        return ModClass.KINGDOM_TITLE_MANAGER
            .Where(title => title != null && !title.isRekt() && title != capitalTitle &&
                            !string.Equals(title.data?.name, empireName, StringComparison.Ordinal))
            .Where(title => title.title_capital != null && !title.title_capital.isRekt() &&
                            title.title_capital.kingdom == coreKingdom && title.title_capital != coreKingdom.capital &&
                            !title.title_capital.isCapitalCity())
            .OrderByDescending(title => title.getCities().Count(city => city != null && !city.isRekt() &&
                                                                       city.kingdom == coreKingdom))
            .ThenBy(title => title.id)
            .ToList();
    }

    public static KingdomTitle FindEnfeoffableTitle(Empire empire) => FindEnfeoffableTitles(empire).FirstOrDefault();

    // 兼容"分封"诉求等旧调用：返回下一块可封法理的法理首府
    public static City FindEnfeoffableCity(Empire empire, EmpireCore empireCore) =>
        FindEnfeoffableTitle(empire)?.title_capital;

    public static Actor FindEnfeoffmentCandidate(Empire empire)
    {
        PersonalClanIdentity emperorIdentity = empire?.Emperor?.GetPersonalIdentity();
        return SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)
            .Select(item => item.Item2)
            .Where(identity => identity != null && IsEligibleSibling(identity._actor, empire))
            .OrderBy(identity => identity.rank)
            .Select(identity => identity._actor)
            .FirstOrDefault();
    }

    public static bool IsEligibleSibling(Actor actor, Empire empire)
    {
        if (actor == null || actor.isRekt() || actor.isKing() || empire?.Emperor == null)
            return false;
        PersonalClanIdentity emperorIdentity = empire.Emperor.GetPersonalIdentity();
        PersonalClanIdentity actorIdentity = actor.GetPersonalIdentity();
        if (emperorIdentity == null || actorIdentity == null || !actorIdentity.CanHeir(emperorIdentity)) return false;
        if (actorIdentity._specificClan != empire.EmpireSpecificClan) return false;
        if (actor.id == (empire.CoreKingdom?.GetHeir()?.id ?? -1L)) return false;
        if (actor.HasVirtualEnfeoff(empire)) return false;
        if (actor.kingdom?.GetEmpire() != empire) return false;
        return SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)
            .Any(item => item.Item2?.id == actorIdentity.id);
    }

    // 把 actor 分封为附庸国国王；调用方需要自行保证 actor 已经通过 IsEligibleSibling 校验，
    // 这里只重新做一次城池/政体防御性检查(跟 TempFac_分封.Execute 完全一致)。
    public static bool TryEnfeoffActor(Empire empire, Actor actor, int mandateReward = 10)
    {
        Kingdom coreKingdom = empire?.CoreKingdom;
        Regime empireRegime = coreKingdom?.GetRegime();
        KingdomTitle fief = FindEnfeoffableTitle(empire);
        City city = fief?.title_capital;
        if (empireRegime == null || empireRegime.enfeoff_virtual_only ||
            !IsEligibleSibling(actor, empire) || city == null)
        {
            return false;
        }

        Kingdom kingdom = city.makeOwnKingdom(actor);
        if (kingdom == null) return false;
        // 整块法理一起封出去：法理首府立国之后，同一法理下仍归核心王国的城市随之划入封国
        foreach (City other in fief.getCities().ToList())
        {
            if (other == null || other.isRekt() || other == city || other.kingdom != coreKingdom) continue;
            other.joinAnotherKingdom(kingdom);
        }

        kingdom.SetRegimeType(empireRegime.type);
        kingdom.LoadRegime();
        Regime kingdomRegime = kingdom.GetRegime();
        if (kingdomRegime != null)
        {
            kingdomRegime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            kingdomRegime.SetAllowSupportCenterArmy(false);
            kingdomRegime.SetTaxLevel(TaxLevel.None);
        }
        KingdomTitle title = city.GetTitle();
        if (title?.title_capital == city)
        {
            kingdom.SetMainTitle(title);
            kingdom.king?.AddOwnedTitle(title);
        }
        empire.join(kingdom, pForce: true);
        kingdom.ReconcileMainTitle(title == null ? null : new[] { title });
        empire.SynchronizeLandedLegalTitles(kingdom);
        empire.AddMandate(mandateReward);
        actor.CheckSpecificClan(false);
        TranslateHelper.LogPeerageGranted(actor, empire,
            (title?.data?.name ?? kingdom.data.name) + LM.Get("default_peerages_2"));
        return true;
    }

    // 分割继承法在没有城池可分/政体不支持真实分封(如 enfeoff_virtual_only)时的退路:
    // 数一数有几个兄弟本来"够资格"分到一份(除了缺地之外没有别的理由被排除)，
    // 好让调用方用头衔/俸禄+正统性做补偿，而不是直接假装分割继承法没发生。
    public static int CountCompensableSiblings(Empire empire)
    {
        PersonalClanIdentity emperorIdentity = empire?.Emperor?.GetPersonalIdentity();
        if (emperorIdentity == null) return 0;
        return SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)
            .Select(item => item.Item2)
            .Count(identity => identity != null && IsEligibleSibling(identity._actor, empire));
    }

    // 新皇帝即位(不论是立储继承、选举还是拥立)时由 KingdomPatch 的 setKing 补丁调用：
    //   · 分割继承法：诸子分封；
    //   · 本文化已施行「分封建国」且尚未「削藩」：先君一死，新君的兄弟按法理裂土受封。
    // 政体只能虚封(如律令制)或已无法理可分时，按兄弟人数给正统性补偿。
    public static void OnEmperorSucceeded(Empire empire)
    {
        Kingdom coreKingdom = empire?.CoreKingdom;
        if (coreKingdom == null || coreKingdom.isRekt() || empire.Emperor == null) return;
        bool divisionLaw = coreKingdom.GetSuccessionLaw() == SuccessionLawType.分割继承法;
        bool enfeoffmentEnacted =
            InstitutionSystem.HasFeature(empire, InstitutionFeatures.SiblingEnfeoffment) &&
            !InstitutionSystem.HasFeature(empire, InstitutionFeatures.SiblingEnfeoffmentAbolished);
        if (!divisionLaw && !enfeoffmentEnacted) return;

        int enfeoffed = EnfeoffAllEligibleSiblings(empire);
        if (enfeoffed > 0) return;
        int compensable = CountCompensableSiblings(empire);
        if (compensable > 0) empire.AddMandate(Math.Min(compensable, 3) * 3);
    }

    // 分割继承法专用:一次继承事件里把新君的所有合法兄弟依次分封为附庸国，而不是像
    // TempFac_分封 那样按周期一次只分封一个。每分封成功一人，该兄弟自身就不再满足
    // IsEligibleSibling(变成 isKing())，循环靠这个天然终止；没有可分封的城池/候选人时同样终止。
    public static int EnfeoffAllEligibleSiblings(Empire empire, int mandateRewardPerHeir = 5)
    {
        int enfeoffedCount = 0;
        Kingdom coreKingdom = empire?.CoreKingdom;
        Regime empireRegime = coreKingdom?.GetRegime();
        if (empireRegime == null || empireRegime.enfeoff_virtual_only) return 0;

        Actor candidate;
        while ((candidate = FindEnfeoffmentCandidate(empire)) != null)
        {
            if (!TryEnfeoffActor(empire, candidate, mandateRewardPerHeir)) break;
            enfeoffedCount++;
        }
        return enfeoffedCount;
    }
}

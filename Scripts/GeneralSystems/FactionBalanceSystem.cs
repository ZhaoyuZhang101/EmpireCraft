using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 派系平衡：皇帝要维持各派互相牵制，一派独大就积累"失衡"，按这一派走向极端的方向引发经典的帝国乱局，
// 并推高王朝积弊，帝国一步步走向崩毁。适用于走朝堂博弈(没有议会)的帝国(见 CourtSystem.Applies)。
// 失衡度(EmpireData.faction_imbalance，0~100)，每年按最大派系的占比结算：
//   · 占比 ≥ 60：每年 +(占比 - 55) / 3；明君、中兴之主 ×0.5，暴君 ×0.8，庸弱、昏聩、荒淫 ×1.5，天子被挟制再 ×1.3；
//   · 占比 50~60：每年 -3；占比 < 50(几派互相牵制)：每年 -12；独大的派系换了一家，失衡减半；改朝换代只留三成。
// 制衡：皇帝亲政时最大派系占比 ≥ 60，每年有机会扶植次大派系(占比 +3~+8，按治理能力)——
//   明君八成、暴君六成、中主四成半、庸弱两成；幼主、傀儡、怠政之君无从制衡(见 CourtSystem.Decider)。
// 后果(失衡度 30 起，越高越重)：按独大派系走向极端的方向——
//   · 文臣专权(绥靖)：派系首领做权臣更快(权臣 → 挟制天子成傀儡 → 封公封王 → 篡位，见 Empire.UpdatePowerfulMinister)，
//     吏治败坏、冗官冗费、土地兼并、武备废弛的积弊滋长；
//   · 武人跋扈(攘夷、僭主)：禁军忠诚下降(宫廷政变、废立，见 PalaceArmySystem)，加税养兵(苛捐杂税)，各省离心(藩镇割据)；
//   · 近幸专权(宦官，或尊王、中央这类君权派)：吏治败坏、党争，禁军也不稳；
//   · 外戚干政(外戚、血脉)：外戚随时有撑腰、可以专权(见 ConsortKinSystem.Backed)，外戚与本派首领做权臣更快，吏治败坏；
//   · 诸侯坐大(诸侯、自治)：土地兼并，各省离心；
//   · 教权干政(神权)：苛捐杂税(什一税)、党争；
//   · 其余：吏治败坏、党争。
//   各方向都让被排挤的派系积怨(朝堂怨望)，并每年推高王朝积弊(见 DynasticCycleSystem)；
//   失衡度 60 以上、被排挤的派系怨望深重又有本派主政的省，可能以"清君侧"为名起兵(见 FactionPurgeSystem)。
// 失衡度过 40 记一笔"一家独大"，回落到 20 以下记"复归平衡"。帝国主页"朝政积弊"卡里有一行派系失衡。
public static class FactionBalanceSystem
{
    // Eunuch：近幸专权(宦官阉党，或尊王、中央这类君权派独大，宦官近臣弄权)
    public enum Direction { None, Civil, Military, Consort, Regional, Clergy, Generic, Eunuch }

    private const float DominantShare = 60f, BalancedShare = 50f;
    private const float GrowthDivisor = 3f, MildDecay = 3f, BalancedDecay = 12f;
    private const float Onset = 30f, WarnAt = 40f, CalmAt = 20f;
    private const float StrainShare = 0.6f;

    public static bool Applies(Empire empire) =>
        CourtSystem.Applies(empire) && empire.CoreKingdom?.GetRegime()?.type != RegimeType.Modern &&
        !RepublicSystem.IsRepublic(empire);

    public static float Imbalance(Empire empire) => Applies(empire) ? Mathf.Clamp(empire.data.faction_imbalance, 0f, 100f) : 0f;

    // 失衡度换成 0~1 的轻重(30 以下不算)
    private static float Severity(Empire empire) => Mathf.Clamp01((Imbalance(empire) - Onset) / (100f - Onset));

    public static FixedFaction Dominant(Empire empire) =>
        Applies(empire) ? CourtSystem.FindFaction(empire, empire.data.faction_imbalance_id) : null;

    public static Direction DirectionOf(FixedFaction faction) => faction?.Type switch
    {
        null => Direction.None,
        FactionType.绥靖 => Direction.Civil,
        FactionType.攘夷 or FactionType.僭主 => Direction.Military,
        // 外戚干政只算外戚与血脉(重血统的贵戚)；君权派独大则近幸专权(用户问"为什么外戚是血脉")
        FactionType.外戚 or FactionType.血脉 => Direction.Consort,
        FactionType.宦官 or FactionType.尊王 or FactionType.中央 => Direction.Eunuch,
        FactionType.诸侯 or FactionType.自治 => Direction.Regional,
        FactionType.神权 => Direction.Clergy,
        _ => Direction.Generic
    };

    public static Direction Current(Empire empire) => Severity(empire) > 0f ? DirectionOf(Dominant(empire)) : Direction.None;

    public static string DirectionName(Direction direction) => LM.Get("faction_imbalance_" + direction.ToString().ToLowerInvariant());

    #region 每年

    // 每年(ConstitutionalEconomySystem 年度结算，在朝政积弊之前)
    public static void Yearly(Empire empire)
    {
        try
        {
            if (!Applies(empire) || World.world == null) return;
            double now = World.world.getCurWorldTime();
            if (empire.data.last_faction_balance_update >= 0d && now >= empire.data.last_faction_balance_update &&
                Date.getYearsSince(empire.data.last_faction_balance_update) < 1) return;
            empire.data.last_faction_balance_update = now;
            List<FixedFaction> factions = CourtSystem.Factions(empire);
            if (factions.Count == 0) return;
            FixedFaction top = factions.OrderByDescending(faction => faction.CentralRatio).First();
            Advance(empire, top);
            Balance(empire, factions, top);
            Consequences(empire, factions, top);
            Chronicle(empire, top);
            // 被排挤的派系以清君侧为名起兵(见 FactionPurgeSystem)
            FactionPurgeSystem.TryRaise(empire, factions, top);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][派系平衡] 年度结算失败: {exception.Message}");
        }
    }

    private static void Advance(Empire empire, FixedFaction top)
    {
        float imbalance = empire.data.faction_imbalance;
        string id = top.GetID();
        if (!string.IsNullOrEmpty(empire.data.faction_imbalance_id) && empire.data.faction_imbalance_id != id)
            imbalance *= 0.5f;
        float share = top.CentralRatio;
        if (share >= DominantShare) imbalance += (share - 55f) / GrowthDivisor * RulerFactor(empire);
        else if (share < BalancedShare) imbalance -= BalancedDecay;
        else imbalance -= MildDecay;
        empire.data.faction_imbalance = Mathf.Clamp(imbalance, 0f, 100f);
        empire.data.faction_imbalance_id = id;
    }

    private static float RulerFactor(Empire empire)
    {
        Actor ruler = empire.Emperor;
        bool restorer = ruler != null && (ruler.hasTrait(RulerTraitSystem.Restorer) || ruler.hasTrait(RulerTraitSystem.GreatRestorer));
        float factor = restorer ? 0.5f : RulerQualitySystem.Of(ruler) switch
        {
            RulerQualitySystem.Grade.Wise => 0.5f,
            RulerQualitySystem.Grade.Tyrant => 0.8f,
            RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute => 1.5f,
            _ => 1f
        };
        return empire.data.is_been_controlled ? factor * 1.3f : factor;
    }

    // 制衡：扶植次大派系，独大的一派让出占比(派系占比此消彼长，见 KingdomExtension.TryIncreaseFactionRatio)
    private static void Balance(Empire empire, List<FixedFaction> factions, FixedFaction top)
    {
        if (top.CentralRatio < DominantShare || factions.Count < 2) return;
        (Actor decider, string role) = CourtSystem.Decider(empire);
        if (role != "emperor" || decider == null) return;
        float chance = RulerQualitySystem.Of(decider) switch
        {
            RulerQualitySystem.Grade.Wise => 0.8f,
            RulerQualitySystem.Grade.Tyrant => 0.6f,
            RulerQualitySystem.Grade.Weak => 0.2f,
            RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute => 0.05f,
            _ => 0.45f
        };
        if (UnityEngine.Random.value >= chance) return;
        FixedFaction second = factions.Where(faction => faction != top).OrderByDescending(faction => faction.CentralRatio).First();
        int amount = Mathf.Clamp(3 + decider.stewardship / 3, 3, 8);
        if (!empire.CoreKingdom.TryIncreaseFactionRatio(second, amount)) return;
        CourtSystem.Log(CourtSystem.State(empire), "faction_balance_boost", decider.getName(), second.Name, top.Name);
    }

    private static void Consequences(Empire empire, List<FixedFaction> factions, FixedFaction top)
    {
        float s = Severity(empire);
        if (s <= 0f) return;
        // 被排挤的派系积怨
        foreach (FixedFaction faction in factions)
            if (faction != top) CourtSystem.FactionState(empire, faction).grievance =
                Mathf.Min(100f, CourtSystem.FactionState(empire, faction).grievance + 10f * s);
        switch (DirectionOf(top))
        {
            case Direction.Civil:
                MaladySystem.Aggravate(empire, MaladySystem.Graft, 3f * s);
                MaladySystem.Aggravate(empire, MaladySystem.Bloat, 3f * s);
                MaladySystem.Aggravate(empire, MaladySystem.Annexation, 2f * s);
                MaladySystem.Aggravate(empire, MaladySystem.Military, 2f * s);
                break;
            case Direction.Military:
                MaladySystem.Aggravate(empire, MaladySystem.Levies, 4f * s);
                break;
            case Direction.Consort:
                MaladySystem.Aggravate(empire, MaladySystem.Graft, 3f * s);
                break;
            case Direction.Regional:
                MaladySystem.Aggravate(empire, MaladySystem.Annexation, 4f * s);
                break;
            case Direction.Clergy:
                MaladySystem.Aggravate(empire, MaladySystem.Levies, 3f * s);
                MaladySystem.Aggravate(empire, MaladySystem.Factionalism, 2f * s);
                break;
            case Direction.Eunuch:
                MaladySystem.Aggravate(empire, MaladySystem.Graft, 3f * s);
                MaladySystem.Aggravate(empire, MaladySystem.Factionalism, 2f * s);
                break;
            default:
                MaladySystem.Aggravate(empire, MaladySystem.Graft, 2f * s);
                MaladySystem.Aggravate(empire, MaladySystem.Factionalism, 2f * s);
                break;
        }
    }

    // 过 40 记"一家独大"，回落到 20 以下记"复归平衡"
    private static void Chronicle(Empire empire, FixedFaction top)
    {
        float imbalance = empire.data.faction_imbalance;
        if (!empire.data.faction_imbalance_warned && imbalance >= WarnAt)
        {
            empire.data.faction_imbalance_warned = true;
            string text = string.Format(LM.Get("history_faction_dominant"), top.Name, DirectionName(DirectionOf(top)),
                empire.GetEmpireName());
            empire.RecordHistory(directContent: text, actorId: top.GetLeader()?.id ?? -1L);
            TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
        }
        else if (empire.data.faction_imbalance_warned && imbalance < CalmAt)
        {
            empire.data.faction_imbalance_warned = false;
            empire.RecordHistory(directContent: string.Format(LM.Get("history_faction_rebalanced"), empire.GetEmpireName()));
        }
    }

    #endregion

    #region 接到别的系统上

    // 权臣进度(Empire.UpdatePowerfulMinister)：文臣专权、外戚干政或其他方向下，独大派系的人(外戚干政时也算外戚)每月多涨
    public static int MinisterBonus(Empire empire, Actor candidate)
    {
        if (candidate?.data == null) return 0;
        Direction direction = Current(empire);
        if (direction is Direction.None or Direction.Military or Direction.Regional) return 0;
        bool ours = candidate.GetFaction()?.GetID() == empire.data.faction_imbalance_id ||
                    direction == Direction.Consort && ConsortKinSystem.IsConsortKin(empire, candidate);
        return ours ? Mathf.Clamp(Mathf.RoundToInt(Imbalance(empire) / 33f), 1, 3) : 0;
    }

    // 外戚干政：失衡度 40 起外戚随时有撑腰(见 ConsortKinSystem.Backed)
    public static bool BacksConsortKin(Empire empire) => Current(empire) == Direction.Consort && Imbalance(empire) >= WarnAt;

    // 武人跋扈：禁军忠诚的目标值往下压(见 PalaceArmySystem)
    public static float PalaceUnrest(Empire empire) => Current(empire) switch
    {
        Direction.Military => 30f * Severity(empire),
        // 近幸专权：宦官近臣插手禁军
        Direction.Eunuch => 15f * Severity(empire),
        _ => 0f
    };

    // 各省离心(见 ProvincialCollapseSystem.Crisis)：武人跋扈(藩镇)、诸侯坐大
    public static float CollapseFactor(Empire empire) => Current(empire) switch
    {
        Direction.Military => 0.5f * Severity(empire),
        Direction.Regional => 0.6f * Severity(empire),
        _ => 0f
    };

    // 王朝积弊每年多涨多少(见 DynasticCycleSystem)
    public static float StrainDelta(Empire empire) => Imbalance(empire) / 100f * StrainShare;

    // 改朝换代：新朝重组朝局，只留三成
    public static void OnDynastyChanged(Empire empire)
    {
        if (empire?.data == null) return;
        empire.data.faction_imbalance *= 0.3f;
        empire.data.faction_imbalance_warned = false;
    }

    #endregion
}

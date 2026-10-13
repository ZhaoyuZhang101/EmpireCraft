using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 朝政积弊(用户 2026-10-14："皇帝昏庸不理朝政导致积病越来越多")：
//   王朝积弊不再只是一个数，拆成六条具体弊病，各 0~100——吏治败坏、土地兼并、冗官冗费、武备废弛、党争、苛捐杂税。
//   · 滋生(每年)：按君主资质——昏聩 +6、荒淫 +5、庸弱 +3、暴虐 +2、中主 +1、明君 -4、中兴之主 -6；
//     权臣外戚挟制 +1.5、开国气象 -2；各条另看实情：
//       吏治 +中央腐败×8(荒淫 +1)；兼并 +无地农民比例×10(荒淫 +1)；冗费 +王朝年岁/100×2(最多 3，庸弱昏聩 +1)；
//       武备 +禁军空饷×6(昏聩荒淫 +1)；党争 +各派最大怨望/25(庸弱 +2、被挟制 +1)；苛捐 暴虐 +4、荒淫 +2、国库亏空 +3；
//   · 革除：明君、中兴之主在位逐年消退；整顿吏治告成 吏治 -15；改朝换代旧弊一扫而空(各留两成)；
//   · 效果(轻重 s = 积弊/100)：
//       吏治——帝国各城、各成员国腐败每年 +1.2%×s；兼并——有地农民卖地的速度 ×(1+s)；
//       冗费——每年多支 12%×s 年度经常收入(行政开支)；武备——本帝国各军战力 ×(1−0.25s)，禁军空饷再多两成×s；
//       党争——奏议准奏率 -15%×s，各派怨望每年 +8×s；苛捐——苛政 +15×s；
//       合计——王朝积弊(周期压力)每年多涨 六条平均×0.6。
// 只有传统君主制有(同王朝周期，见 DynasticCycleSystem.Applies)。
public static class MaladySystem
{
    public const string Graft = "graft", Annexation = "annexation", Bloat = "bloat", Military = "military",
        Factionalism = "factionalism", Levies = "levies";

    public static readonly string[] All = { Graft, Annexation, Bloat, Military, Factionalism, Levies };

    private const float GraftCorruption = 0.012f, BloatCost = 0.12f, MilitaryPenalty = 0.25f, PhantomShare = 0.2f;
    private const float ApprovalPenalty = 0.15f, FactionGrievance = 8f, LeviesBurden = 15f, StrainShare = 0.6f;

    public static bool Applies(Empire empire) => empire?.data != null && DynasticCycleSystem.Applies(empire);

    public static float Get(Empire empire, string key) =>
        empire?.data?.maladies != null && empire.data.maladies.TryGetValue(key, out float value) ? value : 0f;

    public static float Severity(Empire empire, string key) => Applies(empire) ? Mathf.Clamp01(Get(empire, key) / 100f) : 0f;

    public static float Average(Empire empire) => Applies(empire) ? All.Average(key => Get(empire, key)) : 0f;

    private static void Add(Empire empire, string key, float delta)
    {
        empire.data.maladies ??= new Dictionary<string, float>();
        empire.data.maladies[key] = Mathf.Clamp(Get(empire, key) + delta, 0f, 100f);
    }

    public static void Relieve(Empire empire, string key, float amount)
    {
        if (Applies(empire)) Add(empire, key, -Mathf.Abs(amount));
    }

    // 别的系统加重某条积弊(如派系失衡，见 FactionBalanceSystem)
    public static void Aggravate(Empire empire, string key, float amount)
    {
        if (Applies(empire)) Add(empire, key, Mathf.Abs(amount));
    }

    // 改朝换代：旧弊一扫而空，各留两成
    public static void OnDynastyChanged(Empire empire)
    {
        if (empire?.data?.maladies == null) return;
        foreach (string key in All) empire.data.maladies[key] = Get(empire, key) * 0.2f;
    }

    #region 每年

    public static void Yearly(Empire empire)
    {
        try
        {
            if (!Applies(empire) || World.world == null) return;
            double now = World.world.getCurWorldTime();
            if (empire.data.last_malady_update >= 0d && now >= empire.data.last_malady_update &&
                Date.getYearsSince(empire.data.last_malady_update) < 1) return;
            empire.data.last_malady_update = now;
            Grow(empire);
            Apply(empire);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][积弊] 年度结算失败: {exception.Message}");
        }
    }

    private static void Grow(Empire empire)
    {
        Actor ruler = empire.Emperor;
        RulerQualitySystem.Grade grade = RulerQualitySystem.Of(ruler);
        bool restorer = ruler != null && (ruler.hasTrait(RulerTraitSystem.Restorer) || ruler.hasTrait(RulerTraitSystem.GreatRestorer));
        float common = restorer ? -6f : grade switch
        {
            RulerQualitySystem.Grade.Fatuous => 6f,
            RulerQualitySystem.Grade.Dissolute => 5f,
            RulerQualitySystem.Grade.Weak => 3f,
            RulerQualitySystem.Grade.Tyrant => 2f,
            RulerQualitySystem.Grade.Wise => -4f,
            _ => 1f
        };
        bool controlled = empire.data.is_been_controlled;
        if (controlled) common += 1.5f;
        if (empire.InFoundingGrace) common -= 2f;
        bool dissolute = grade == RulerQualitySystem.Grade.Dissolute;
        bool neglect = grade is RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute;
        bool weak = grade is RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous;
        Kingdom core = empire.CoreKingdom;
        float grievance = CourtSystem.State(empire).factions?.Values.Select(item => item?.grievance ?? 0f).DefaultIfEmpty(0f).Max() ?? 0f;
        float phantom = PalaceArmySystem.Palace(core).Select(corps => corps.phantom).DefaultIfEmpty(0f).Max();
        // 盛世(明君、中兴)时各条按同一速度消退，实情只在滋生时加码
        float Extra(float value) => common > 0f ? value : 0f;
        Add(empire, Graft, common + Extra((float)CorruptionSystem.GetRate(empire) * 8f + (dissolute ? 1f : 0f)));
        Add(empire, Annexation, common + Extra(LandEconomySystem.GetRealmLandlessRatio(core) * 10f + (dissolute ? 1f : 0f)));
        Add(empire, Bloat, common + Extra(Mathf.Min(3f, DynasticCycleSystem.GetAge(empire) / 100f * 2f) + (weak ? 1f : 0f)));
        Add(empire, Military, common + Extra(phantom * 6f + (neglect ? 1f : 0f)));
        Add(empire, Factionalism, common + Extra(grievance / 25f + (grade == RulerQualitySystem.Grade.Weak ? 2f : 0f) +
                                                 (controlled ? 1f : 0f)));
        Add(empire, Levies, common + Extra((grade == RulerQualitySystem.Grade.Tyrant ? 4f : 0f) + (dissolute ? 2f : 0f) +
                                           (EmpireBankruptcySystem.IsBankrupt(empire) ? 3f : 0f)));
    }

    // 每年才结算的效果：吏治(各城腐败)、冗费(行政开支)、党争(各派怨望)
    private static void Apply(Empire empire)
    {
        float graft = Severity(empire, Graft);
        if (graft > 0f)
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (member?.data == null || member.isRekt() || member.GetEmpire() != empire) continue;
                if (member != empire.CoreKingdom) member.AddCorruptionRate(GraftCorruption * graft);
                foreach (City city in member.cities.ToList())
                    if (city?.data != null && !city.isRekt()) city.AddCorruptionRate(GraftCorruption * graft);
            }
        float bloat = Severity(empire, Bloat);
        Kingdom core = empire.CoreKingdom;
        if (bloat > 0f)
        {
            int cost = (int)Math.Min(Math.Max(0L, core.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(core) * BloatCost * bloat));
            if (cost > 0) core.SubMoney(cost, TreasuryCategory.Administration);
        }
        float faction = Severity(empire, Factionalism);
        if (faction > 0f && CourtSystem.State(empire).factions != null)
            foreach (CourtFactionState state in CourtSystem.State(empire).factions.Values)
                if (state != null) state.grievance = Mathf.Min(100f, state.grievance + FactionGrievance * faction);
    }

    #endregion

    #region 各处调用的效果

    private static Empire EmpireOf(Kingdom kingdom) => kingdom?.GetEmpire();

    // 土地兼并：有地农民卖地的速度
    public static float AnnexationFactor(Kingdom kingdom) => 1f + Severity(EmpireOf(kingdom), Annexation);

    // 武备废弛：本帝国各军战力
    public static float MilitaryFactor(CorpsData corps)
    {
        if (corps == null || World.world == null) return 1f;
        return 1f - MilitaryPenalty * Severity(EmpireOf(World.world.kingdoms.get(corps.kingdom_id)), Military);
    }

    // 武备废弛：禁军空饷再多
    public static float PhantomExtra(Empire empire) => PhantomShare * Severity(empire, Military);

    // 党争：奏议准奏率
    public static float ApprovalMalus(Empire empire) => ApprovalPenalty * Severity(empire, Factionalism);

    // 苛捐杂税：苛政
    public static float LeviesHarsh(Kingdom core) => LeviesBurden * Severity(EmpireOf(core), Levies);

    // 合计：王朝积弊(周期压力)每年多涨
    public static float StrainDelta(Empire empire) => Average(empire) / 100f * StrainShare;

    // 界面上每条积弊的后果(本地化参数)
    public static string EffectText(Empire empire, string key)
    {
        float s = Severity(empire, key);
        return string.Format(NeoModLoader.General.LM.Get("malady_effect_" + key), key switch
        {
            Graft => (GraftCorruption * s * 100f).ToString("0.0"),
            Annexation => Mathf.RoundToInt(s * 100f).ToString(),
            Bloat => Mathf.RoundToInt(BloatCost * s * 100f).ToString(),
            Military => Mathf.RoundToInt(MilitaryPenalty * s * 100f).ToString(),
            Factionalism => Mathf.RoundToInt(ApprovalPenalty * s * 100f).ToString(),
            _ => Mathf.RoundToInt(LeviesBurden * s).ToString()
        });
    }

    #endregion
}

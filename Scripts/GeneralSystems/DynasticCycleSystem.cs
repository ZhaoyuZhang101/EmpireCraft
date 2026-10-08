using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class DynasticCycleSystem
{
    public const string HarshSource = "harsh_dynastic_strain";
    public static bool Applies(Empire empire) => MonarchyLegitimacy.UsesTraditionalModel(empire) &&
        !RepublicSystem.IsRepublic(empire);
    public static float GetPressure(Empire empire) => Applies(empire)
        ? Math.Max(0f, Math.Min(100f, empire.data.constitutional_economy?.dynastic_strain ?? 0f)) : 0f;
    public static bool InCrisis(Empire empire) => GetPressure(empire) >= DynasticCycleRules.CrisisPressure;
    public static int GetAge(Empire empire)
    {
        if (World.world == null || empire?.data == null) return 0;
        double since = empire.data.constitutional_economy?.dynastic_since ?? -1d;
        if (since < 0d) since = empire.data.created_time;
        return since >= 0d ? Math.Max(0, Date.getYearsSince(since)) : 0;
    }

    // 每年在苛政结算中调用；传入的负担不包含本系统自己的贡献，改革后可以退出危机。
    public static void Update(Empire empire, ConstitutionalEconomyState state, float governanceBurden)
    {
        if (state == null || !Applies(empire)) return;
        double now = World.world.getCurWorldTime();
        if (state.last_dynastic_update >= 0d && now >= state.last_dynastic_update &&
            Date.getYearsSince(state.last_dynastic_update) < 1) return;
        bool bankrupt = empire.CurrentMoney < 0;
        bool atWar = empire.CoreKingdom.getWars().Any();
        bool interregnum = empire.Emperor == null || empire.Emperor.isRekt() || !empire.Emperor.isAlive();
        double corruption = CorruptionSystem.GetRate(empire);
        if (state.dynastic_since < 0d)
        {
            state.dynastic_since = FindDynastyStart(empire, now);
            bool good = !bankrupt && !atWar && governanceBurden <= 20f && corruption <= 0.25d && !interregnum;
            state.dynastic_strain = DynasticCycleRules.LegacyPressure(GetAge(empire), governanceBurden, good);
        }
        state.last_dynastic_update = now;
        float change = DynasticCycleRules.AnnualChange(GetAge(empire), governanceBurden, bankrupt, atWar,
            corruption, interregnum, RulerTraitSystem.ReignedBy(empire.CoreKingdom, RulerTraitSystem.Restorer),
            empire.InFoundingGrace, state.harsh_other_burden);
        state.dynastic_strain = DynasticCycleRules.Advance(state.dynastic_strain, change);
        MonarchyLegitimacy.Invalidate(empire);
        if (state.dynastic_strain >= DynasticCycleRules.CrisisPressure && !state.dynastic_crisis_announced)
        {
            state.dynastic_crisis_announced = true;
            EventRecorder.Record(empire, string.Format(LM.Get("dynastic_crisis_history"), empire.GetEmpireFullName()));
        }
        else if (state.dynastic_strain <= 40f && state.dynastic_crisis_announced)
        {
            state.dynastic_crisis_announced = false;
            EventRecorder.Record(empire, string.Format(LM.Get("dynastic_recovery_history"), empire.GetEmpireFullName()));
        }
    }

    // 只在真正改朝换代时调用。普通继位、改国号、读档不重置王朝年龄。
    public static void OnDynastyChanged(Empire empire)
    {
        if (empire?.data == null || World.world == null) return;
        ConstitutionalEconomyState state = empire.data.constitutional_economy ??= new ConstitutionalEconomyState();
        state.dynastic_since = World.world.getCurWorldTime();
        state.last_dynastic_update = state.dynastic_since;
        state.last_dynastic_uprising_attempt = -1d;
        int previousBurden = DynasticCycleRules.HarshBurden(state.dynastic_strain);
        state.dynastic_strain = Math.Min(20f, DynasticCycleRules.Advance(state.dynastic_strain, 0f) * 0.25f);
        state.harsh_burden = Math.Max(0f, state.harsh_burden - previousBurden);
        if (state.harsh_main_cause == HarshSource) state.harsh_main_cause = "";
        state.dynastic_crisis_announced = false;
        MonarchyLegitimacy.Invalidate(empire);
    }

    private static double FindDynastyStart(Empire empire, double now)
    {
        // 旧档根据本朝开国皇帝的历史时间恢复起点，只在初始化时读取历史。
        IEnumerable<EmpireCraftHistory> history = empire.data.history ?? new List<EmpireCraftHistory>();
        if (empire.data.currentHistory != null) history = history.Concat(new[] { empire.data.currentHistory });
        List<EmpireCraftHistory> reigns = history.Where(reign => reign != null && !reign.is_republic).ToList();
        EmpireCraftHistory founder = empire.data.dynasty_founder_actor_id > 0
            ? reigns.LastOrDefault(reign => reign.id == empire.data.dynasty_founder_actor_id) : null;
        founder ??= reigns.LastOrDefault(reign => reign.is_first);
        double timestamp = founder?.descriptions?.Where(item => item != null && item.timestamp >= 0d &&
                item.timestamp <= now).Select(item => item.timestamp).DefaultIfEmpty(-1d).Min() ?? -1d;
        return timestamp >= 0d ? timestamp : empire.data.created_time >= 0d && empire.data.created_time <= now
            ? empire.data.created_time : now;
    }
}

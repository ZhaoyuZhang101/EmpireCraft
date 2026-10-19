using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宠臣(用户 2026-10-19 选"功高震主与野心"：皇帝的宠臣专权招人怨，成为清君侧的靶子)。每年(RoyalHouseSystem 年度，每个君主国帝国)：
//   · 皇帝没有宠臣时有几率宠信一名在任官员(庸弱、昏、荒淫三成，暴君一成五，寻常八分，贤君不会)——贪婪、有野心、名望低的更易得宠；
//     得宠时与当朝首辅结怨；
//   · 宠臣在：他的派系每年权势 +2，所在城腐败上升，其余派系怨望 +3(怨望够了就会清君侧，见 FactionPurgeSystem)；
//   · 清君侧得手：宠臣一并被诛；
//   · 换了皇帝：新君半数诛杀先帝宠臣，否则罢黜。
public static class FavouriteSystem
{
    private const int FactionGain = 2;
    private const float GrievanceGain = 3f, Corruption = 0.02f;

    public static Actor Of(Empire empire)
    {
        if (empire?.data == null || empire.data.favourite_id < 0 || World.world == null) return null;
        Actor actor = World.world.units.get(empire.data.favourite_id);
        return actor?.data != null && !actor.isRekt() && actor.isAlive() ? actor : null;
    }

    public static void Yearly(Empire empire)
    {
        Actor emperor = empire?.Emperor;
        Kingdom core = empire?.CoreKingdom;
        if (emperor?.data == null || !emperor.isAlive() || core?.data == null) return;
        try
        {
            Actor favourite = Of(empire);
            if (favourite != null && empire.data.favourite_emperor_id != emperor.id)
            {
                Fall(empire, favourite, emperor);
                return;
            }
            if (favourite == null)
            {
                empire.data.favourite_id = -1L;
                TryPick(empire, emperor, core);
                return;
            }
            Sway(empire, favourite, core);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宠臣] {empire.GetEmpireName()} 结算失败: {exception.Message}");
        }
    }

    private static void TryPick(Empire empire, Actor emperor, Kingdom core)
    {
        float chance = RulerQualitySystem.Of(emperor) switch
        {
            RulerQualitySystem.Grade.Wise => 0f,
            RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute => 0.3f,
            RulerQualitySystem.Grade.Tyrant => 0.15f,
            _ => 0.08f
        };
        if (UnityEngine.Random.value >= chance) return;
        Actor favourite = core.units.Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor != emperor &&
                                                    actor.IsOnOffice() && !(core.HasHeir() && core.GetHeir() == actor))
            .OrderByDescending(actor => (actor.hasTrait("greedy") ? 2f : 0f) + (actor.hasTrait("ambitious") ? 1f : 0f) +
                                        UnityEngine.Random.value * 2f - Mathf.Min(actor.renown, 100f) / 100f)
            .FirstOrDefault();
        if (favourite == null) return;
        empire.data.favourite_id = favourite.id;
        empire.data.favourite_emperor_id = emperor.id;
        string text = string.Format(LM.Get("favourite_rise_history"), emperor.getName(), favourite.getName());
        EventRecorder.Record(empire, text, emperor);
        BiographySystem.Note(favourite, string.Format(LM.Get("bio_favourite_rise"), emperor.getName()), emperor);
        Actor leader = empire.GetCabinetLeader();
        if (leader?.data != null && leader != favourite && leader.isAlive())
            PersonalTieSystem.MakeRivals(favourite, leader, PersonalTieSystem.CauseFavourite);
    }

    private static void Sway(Empire empire, Actor favourite, Kingdom core)
    {
        FixedFaction faction = favourite.GetFaction();
        if (faction != null) core.TryIncreaseFactionRatio(faction, FactionGain);
        favourite.city?.AddCorruptionRate(Corruption);
        foreach (FixedFaction other in core.GetRegime()?.GetPlayerFactions() ?? Enumerable.Empty<FixedFaction>())
        {
            if (other == null || other == faction) continue;
            var state = CourtSystem.FactionState(empire, other);
            state.grievance = Mathf.Min(100f, state.grievance + GrievanceGain);
        }
    }

    // 新君即位：半数诛杀先帝宠臣，否则罢黜
    private static void Fall(Empire empire, Actor favourite, Actor emperor)
    {
        empire.data.favourite_id = -1L;
        string name = favourite.getName();
        if (UnityEngine.Random.value < 0.5f)
        {
            EventRecorder.Record(empire, string.Format(LM.Get("favourite_executed_history"), emperor.getName(), name), emperor);
            BiographySystem.Kill(favourite, string.Format(LM.Get("death_cause_favourite"), emperor.getName()), emperor, executed: true);
            return;
        }
        favourite.GetOffice()?.RemoveActor();
        EventRecorder.Record(empire, string.Format(LM.Get("favourite_dismissed_history"), emperor.getName(), name), emperor);
        BiographySystem.Note(favourite, string.Format(LM.Get("bio_favourite_dismissed"), emperor.getName()), emperor);
    }

    // 清君侧得手(见 FactionPurgeSystem)：宠臣一并被诛
    public static void OnPurge(Empire empire, Actor avenger)
    {
        Actor favourite = Of(empire);
        if (favourite == null) return;
        empire.data.favourite_id = -1L;
        EventRecorder.Record(empire, string.Format(LM.Get("favourite_purged_history"), favourite.getName()), favourite);
        BiographySystem.Kill(favourite, LM.Get("death_cause_favourite_purged"), avenger, executed: true);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// Promotes an existing war into a world war when industrial or modern powers
// form genuine multi-state coalitions on both sides. It does not manufacture a
// war; alliances and diplomatic escalation remain responsible for participation.
public static class WorldWarSystem
{
    private sealed class PoliticalBloc
    {
        public string Key;
        public Kingdom Representative;
        public Empire Empire;
        public double Power;
        public bool Modern;
    }

    public static void TryEscalate(War war, bool force = false)
    {
        if (war == null || war.data == null || war.hasEnded() || World.world == null) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        if (data.world_war) return;
        if (!force && data.world_war_last_check >= 0d &&
            Date.getMonthsSince(data.world_war_last_check) < 1) return;
        data.world_war_last_check = World.world.getCurWorldTime();

        List<PoliticalBloc> attackers = BuildBlocs(war._list_attackers);
        List<PoliticalBloc> defenders = BuildBlocs(war._list_defenders);
        if (attackers.Count < 2 || defenders.Count < 2) return;
        List<PoliticalBloc> participants = attackers.Concat(defenders)
            .GroupBy(bloc => bloc.Key).Select(group => group.First()).ToList();
        if (participants.Count < 4 || participants.Count(bloc => bloc.Modern) < 3) return;

        List<PoliticalBloc> world = BuildBlocs(World.world.kingdoms.list);
        double worldPower = world.Sum(bloc => Math.Max(1d, bloc.Power));
        double participantPower = participants.Sum(bloc => Math.Max(1d, bloc.Power));
        float powerShare = worldPower <= 0d ? 0f : (float)(participantPower / worldPower);
        if (powerShare < 0.45f) return;

        data.world_war = true;
        data.world_war_bloc_count = participants.Count;
        data.world_war_power_share = powerShare;
        string attackerName = GetPolityName(war.getMainAttacker());
        string defenderName = GetPolityName(war.getMainDefender());
        war.data.name = string.Format(LM.Get("world_war_name"), attackerName, defenderName);
        string text = string.Format(LM.Get("world_war_started_history"), war.data.name,
            participants.Count, Mathf.RoundToInt(powerShare * 100f));
        RecordForParticipants(participants, text);
        TranslateHelper.LogEventMessage(text, war.getMainAttacker());
    }

    public static void Resolve(War war, WarWinner winner)
    {
        if (war == null) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        if (!data.world_war || data.world_war_end_recorded) return;
        data.world_war_end_recorded = true;
        List<PoliticalBloc> participants = BuildBlocs(war._list_attackers.Concat(war._list_defenders));
        Kingdom winnerKingdom = winner == WarWinner.Attackers ? war.getMainAttacker() :
            winner == WarWinner.Defenders ? war.getMainDefender() : null;
        string text = winnerKingdom == null
            ? string.Format(LM.Get("world_war_peace_history"), war.data.name)
            : string.Format(LM.Get("world_war_victory_history"), GetPolityName(winnerKingdom), war.data.name);
        RecordForParticipants(participants, text);
        TranslateHelper.LogEventMessage(text, winnerKingdom ?? war.getMainAttacker());
    }

    private static List<PoliticalBloc> BuildBlocs(IEnumerable<Kingdom> kingdoms)
    {
        if (kingdoms == null) return new List<PoliticalBloc>();
        var blocs = new Dictionary<string, PoliticalBloc>(StringComparer.Ordinal);
        foreach (Kingdom kingdom in kingdoms)
        {
            if (kingdom == null || kingdom.isRekt()) continue;
            Empire empire = kingdom.GetEmpire();
            string key = empire == null ? $"kingdom:{kingdom.id}" : $"empire:{empire.id}";
            if (blocs.ContainsKey(key)) continue;
            Kingdom representative = empire?.CoreKingdom ?? kingdom;
            string culture = empire == null
                ? CultureService.GetRealmCulture(kingdom)
                : InstitutionSystem.GetPrimaryCulture(empire);
            blocs[key] = new PoliticalBloc
            {
                Key = key,
                Representative = representative,
                Empire = empire,
                Power = empire?.GetNationalPower() ?? kingdom.GetNationalPower(),
                Modern = representative?.GetRegime()?.type == RegimeType.Modern ||
                         !string.IsNullOrWhiteSpace(culture) && TechnologySystem.HasTech(culture, "industrialization")
            };
        }
        return blocs.Values.ToList();
    }

    private static string GetPolityName(Kingdom kingdom)
    {
        if (kingdom == null) return LM.Get("label_none");
        string name = kingdom.GetEmpire()?.GetEmpireFullName();
        if (!string.IsNullOrWhiteSpace(name)) return name;
        name = kingdom.GetKingdomFullName();
        return string.IsNullOrWhiteSpace(name) ? kingdom.name : name;
    }

    private static void RecordForParticipants(IEnumerable<PoliticalBloc> participants, string text)
    {
        var recorded = new HashSet<long>();
        foreach (PoliticalBloc bloc in participants ?? Enumerable.Empty<PoliticalBloc>())
        {
            Empire empire = bloc.Empire;
            if (empire == null || empire.IsArchived() || empire.isRekt() || !recorded.Add(empire.id)) continue;
            empire.RecordHistory(directContent: text, kingdomId: bloc.Representative?.id ?? -1L);
        }
    }
}

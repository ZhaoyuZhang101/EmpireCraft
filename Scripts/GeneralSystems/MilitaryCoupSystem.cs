using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 兵变(陈桥驿式)：武官势大而正统低落时，众将拥立大将、黄袍加身，旧君禅位。
//   · 兵权：本国(帝国按中央)各军团兵力里，某员大将(成年、非宗室、不是君主)统领的占比；身为战时统帅的至少按四成五算；
//   · 每年检定：正统不到 50、兵权四成以上，且由头(幼主在位、军饷欠发、大将威望高过君主)至少占两条时，
//     概率 = (50 − 正统)/50 × (兵权 − 0.4)/0.6 × BaseChance，幼主在位加倍，大将有野心或奸恶再加五成；
//     开国雄主登基头 15 年、上次兵变后 20 年内不发生。
//     以前没有由头也行、基数大：两三座城的小国一员大将统领全军(兵权 1)，正统 30 就每年四成概率兵变；
//   · 发动：大将入主都城登基(照常走易姓的后果：旧宗室诸侯起兵、新朝开国)，
//     以大将驻地(所统最大军团的驻城)的法理名号为新国号，没有就沿用旧号；
//     王朝积弊、六弊减半(改朝换代三档之"减半"，用户 2026-10-26，见 DynasticCycleSystem)。
public static class MilitaryCoupSystem
{
    private const int LegitimacyLine = 50;
    private const float PowerLine = 0.4f;
    private const float CommanderShare = 0.45f;
    private const int CooldownYears = 20;
    private const float BaseChance = 0.25f;

    private static bool Applies(Empire empire)
    {
        RegimeType? regime = empire?.CoreKingdom?.GetRegime()?.type;
        return empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom != null &&
               !empire.CoreKingdom.isRekt() && !RepublicSystem.IsRepublic(empire) && RegimeManager.IsMonarchy(regime) &&
               regime != RegimeType.Modern;
    }

    public static void Yearly(Empire empire)
    {
        try
        {
            if (!Applies(empire)) return;
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            if (state == null || World.world == null) return;
            double now = World.world.getCurWorldTime();
            if (state.last_coup_at >= 0d && now >= state.last_coup_at && Date.getYearsSince(state.last_coup_at) < CooldownYears)
                return;
            int legitimacy = empire.Legitimacy;
            if (legitimacy >= LegitimacyLine || !RebellionSystem.CanAttempt(empire.CoreKingdom)) return;
            (Actor general, float share, City seat) = Strongest(empire);
            if (general == null || share < PowerLine) return;
            Actor emperor = empire.Emperor;
            bool minor = RealisticAgeSystem.IsMinor(emperor);
            // 由头：主少国疑、欠饷军心浮动、将领功高震主，至少占两条
            int pretexts = (minor ? 1 : 0) + (!CorpsSystem.WellPaid(empire.CoreKingdom) ? 1 : 0) +
                           (emperor == null || general.renown > emperor.renown ? 1 : 0);
            if (pretexts < 2) return;
            float chance = (LegitimacyLine - legitimacy) / (float)LegitimacyLine * (share - PowerLine) / (1f - PowerLine) * BaseChance;
            if (minor) chance *= 2f;
            if (general.hasTrait("ambitious") || general.hasTrait("evil")) chance *= 1.5f;
            if (UnityEngine.Random.value >= chance) return;
            Execute(empire, state, general, seat);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][兵变] 失败: {exception.Message}");
        }
    }

    // 宫廷政变而宗室无人可立：禁军统领自立(见 PalaceArmySystem)
    internal static void PalaceSeize(Empire empire, ConstitutionalEconomyState state, Actor general, City seat)
    {
        if (!Applies(empire) || general?.data == null || !Eligible(empire, general)) return;
        Execute(empire, state, general, seat);
    }

    private static bool Eligible(Empire empire, Actor actor) =>
        actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && !actor.isKing() &&
        actor.kingdom == empire.CoreKingdom &&
        !SpecificClanManager.SameLineage(actor.GetSpecificClan(), empire.EmpireSpecificClan);

    // 兵权最重的大将、他的兵权占比、驻地
    private static (Actor general, float share, City seat) Strongest(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        List<CorpsData> corps = CorpsSystem.Of(core);
        float total = corps.Sum(item => Mathf.Max(0f, item.strength));
        var commanded = new Dictionary<long, (float strength, CorpsData biggest)>();
        foreach (CorpsData item in corps)
        {
            if (item.general_id < 0) continue;
            commanded.TryGetValue(item.general_id, out var entry);
            float strength = Mathf.Max(0f, item.strength);
            commanded[item.general_id] = (entry.strength + strength,
                entry.biggest == null || strength > entry.biggest.strength ? item : entry.biggest);
        }
        Actor best = null;
        float bestShare = 0f;
        City seat = null;
        foreach (var pair in commanded)
        {
            Actor actor = World.world.units.get(pair.Key);
            if (!Eligible(empire, actor) || total <= 0f) continue;
            float share = pair.Value.strength / total;
            if (share <= bestShare) continue;
            best = actor;
            bestShare = share;
            seat = CorpsSystem.Home(pair.Value.biggest);
        }
        // 战时统帅节制诸军
        Actor commander = WarCommandSystem.Commander(core);
        if (Eligible(empire, commander))
        {
            float share = commanded.TryGetValue(commander.id, out var own) && total > 0f ? own.strength / total : 0f;
            share = Mathf.Max(share, CommanderShare);
            if (share > bestShare)
            {
                best = commander;
                bestShare = share;
                seat = own.biggest != null ? CorpsSystem.Home(own.biggest) : commander.city;
            }
        }
        return (best, bestShare, seat);
    }

    private static void Execute(Empire empire, ConstitutionalEconomyState state, Actor general, City seat)
    {
        Kingdom core = empire.CoreKingdom;
        City capital = core.capital;
        if (capital == null) return;
        Actor former = empire.Emperor;
        string oldName = empire.GetEmpireName();
        string newName = seat != null && seat.hasTitle() ? seat.GetTitle()?.data?.name : null;
        string place = (seat ?? general.city ?? capital).GetCityName();
        state.last_coup_at = World.world.getCurWorldTime();
        PrinceStruggleSystem.Note(core, general, PrinceStruggleSystem.NoteMilitaryCoup);
        if (former != null && former.id != general.id) core.removeKing();
        general.joinCity(capital);
        general.setKingdom(core);
        // 军阀兵变禅让上位(赵匡胤式)：改朝换代按"减半"一档，王朝积弊、六弊减半，另加篡位的一笔
        // (用户 2026-10-26，见 DynasticCycleSystem.OnDynastyChanged)
        DynasticCycleSystem.Expect(empire, DynasticCycleSystem.Transition.Halve, () => core.setKing(general));
        if (empire.Emperor?.id != general.id) return;
        if (!string.IsNullOrWhiteSpace(newName) && newName != oldName)
        {
            empire.SetEmpireName(newName);
            if (empire.data.currentHistory != null)
            {
                empire.data.currentHistory.empire_name = newName;
                empire.data.currentHistory.dynasty_name = newName;
            }
        }
        string text = string.Format(LM.Get("coup_history"), place, general.getName(),
            former?.getName() ?? LM.Get("label_none"), empire.GetEmpireName());
        empire.RecordHistory(directContent: text, actorId: general.id, kingdomId: core.id);
        TranslateHelper.LogEventMessage(text, core);
        general.RecordPersonalHistory(string.Format(LM.Get("personal_history_coup_leader"), place, oldName),
            relatedActorId: former?.id ?? -1L);
        former?.RecordPersonalHistory(string.Format(LM.Get("personal_history_coup_deposed"), general.getName()),
            relatedActorId: general.id);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战场个人戏(用户 2026-10-19 选"战场个人戏")：军团交战的胜负落到统领身上，写进生平、军报，名人与君主还进史书与世界日志。
//   · 每月战线结算(见 FrontSystem.ResolveCity)：一方大败(战况三比七以上)、或自己的军溃散(组织度见底)，这一方各军统领
//     各有几率阵亡(为胜方统领所斩)、被俘、突围；
//   · 被俘：君主、储君、有官爵封地的不降——赎回或遇害；其余的三成五归降敌国(换了国家，留在对方都城)、四成由本国出金赎回
//     (国库付不起就被杀或放归)、其余不屈而死；
//   · 城破(主城池陷落，见 FrontSystem.Surrender)：守在城里的统领三成殉国、三成被俘，其余趁乱出走。
// 只结算有统领实体的军(关了「军团统领实体」就没有这些事)。
public static class BattleDrama
{
    private const float RoutRatio = 0.7f, SlainChance = 0.06f, CapturedChance = 0.05f, BreakoutChance = 0.05f;
    private const float RoutBonus = 1.5f;
    private const float DefectShare = 0.35f, RansomShare = 0.4f;
    private const float MartyrChance = 0.3f, FallCaptureChance = 0.3f;

    private static Actor General(CorpsData corps)
    {
        if (corps == null || corps.general_id < 0 || World.world == null) return null;
        Actor actor = World.world.units.get(corps.general_id);
        return actor?.data != null && !actor.isRekt() && actor.isAlive() ? actor : null;
    }

    // 每月一处战场结算后：输的一方(attackersLost)各军统领的下场
    public static void AfterBattle(City city, List<(Kingdom kingdom, CorpsData corps)> attackers,
        List<(Kingdom kingdom, CorpsData corps)> holders, float ratio)
    {
        if (city?.data == null || !CorpsSystem.GeneralUnits) return;
        bool attackersLost = ratio <= 1f - RoutRatio, holdersLost = ratio >= RoutRatio;
        try
        {
            Settle(city, attackers, holders, attackersLost);
            Settle(city, holders, attackers, holdersLost);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][战场] {city.data.name} 结算统领下场失败: {exception.Message}");
        }
    }

    private static void Settle(City city, List<(Kingdom kingdom, CorpsData corps)> side,
        List<(Kingdom kingdom, CorpsData corps)> enemy, bool beaten)
    {
        Actor victor = enemy.Where(entry => General(entry.corps) != null).OrderByDescending(entry => entry.corps.strength)
            .Select(entry => General(entry.corps)).FirstOrDefault();
        Kingdom victorKingdom = enemy.OrderByDescending(entry => entry.corps.strength).Select(entry => entry.kingdom).FirstOrDefault();
        foreach ((Kingdom kingdom, CorpsData corps) in side.ToList())
        {
            Actor general = General(corps);
            if (general == null) continue;
            bool routed = corps.org <= 10f || corps.strength < 1f;
            if (!beaten && !routed) continue;
            float scale = beaten && routed ? RoutBonus : 1f;
            float roll = UnityEngine.Random.value;
            if (roll < SlainChance * scale) Slain(city, kingdom, general, victor);
            else if (roll < (SlainChance + CapturedChance) * scale && victorKingdom != null) Captured(city, kingdom, general, victorKingdom, victor);
            else if (roll < (SlainChance + CapturedChance + BreakoutChance) * scale)
                Announce(kingdom, general, string.Format(LM.Get("battle_breakout"), city.GetCityName()), false);
        }
    }

    // 阵亡：为胜方统领所斩
    private static void Slain(City city, Kingdom kingdom, Actor general, Actor victor)
    {
        string place = city.GetCityName(), name = general.getName();
        string cause = victor != null
            ? string.Format(LM.Get("death_cause_battle_slain_by"), place, victor.getName())
            : string.Format(LM.Get("death_cause_battle_slain"), place);
        if (victor != null)
            BiographySystem.Note(victor, string.Format(LM.Get("bio_battle_slew"), place, name), general);
        Report(kingdom, general, string.Format(LM.Get("battle_general_slain_log"), place, name,
            victor != null ? victor.getName() : LM.Get("battle_enemy")));
        BiographySystem.Kill(general, cause, victor);
    }

    // 被俘：归降、赎回、不屈而死
    private static void Captured(City city, Kingdom kingdom, Actor general, Kingdom captor, Actor victor)
    {
        string place = city.GetCityName(), name = general.getName();
        bool sovereign = general.isKing() || kingdom.HasHeir() && kingdom.GetHeir() == general;
        bool loyal = sovereign || general.IsOnOffice() || (general.GetOwnedTitle()?.Count ?? 0) > 0;
        float roll = UnityEngine.Random.value;
        if (!loyal && roll < DefectShare && captor.capital?.data != null && !captor.isRekt())
        {
            CorpsSystem.LoseGeneral(general);
            BiographySystem.Note(general, string.Format(LM.Get("bio_captured_defected"), place, captor.GetKingdomName()), victor);
            Report(kingdom, general, string.Format(LM.Get("battle_general_defected_log"), place, name, captor.GetKingdomName()));
            general.joinCity(captor.capital);
            general.setKingdom(captor);
            return;
        }
        if (loyal || roll < DefectShare + RansomShare)
        {
            int price = Mathf.Max(30, Mathf.RoundToInt(general.renown * 2f)) * (sovereign ? 10 : 1);
            if (TreasurySystem.TrySpend(kingdom, price, TreasuryCategory.Reparations, discretionary: false))
            {
                captor.AddMoney(price, TreasuryCategory.Reparations);
                BiographySystem.Note(general, string.Format(LM.Get("bio_captured_ransomed"), place, captor.GetKingdomName(), price), victor);
                Report(kingdom, general, string.Format(LM.Get("battle_general_ransomed_log"), place, name, price), sovereign);
                return;
            }
            if (UnityEngine.Random.value < 0.5f)
            {
                BiographySystem.Note(general, string.Format(LM.Get("bio_captured_released"), place, captor.GetKingdomName()), victor);
                Report(kingdom, general, string.Format(LM.Get("battle_general_released_log"), place, name), sovereign);
                return;
            }
        }
        Report(kingdom, general, string.Format(LM.Get("battle_general_executed_log"), place, name, captor.GetKingdomName()), sovereign);
        BiographySystem.Kill(general, string.Format(LM.Get("death_cause_captive"), place, captor.GetKingdomName()),
            captor.king, executed: true);
    }

    // 城破：守城统领殉国、被俘或出走
    public static void CityFell(City city, Kingdom attacker, Kingdom owner)
    {
        if (city?.data == null || owner == null || !CorpsSystem.GeneralUnits) return;
        try
        {
            Actor victor = null;
            foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All().ToList())
            {
                if (corps.sector_city_id != city.id || General(corps) == null) continue;
                if (victor == null && FrontSystem.Hostile(kingdom, owner) && corps.task == "attack") victor = General(corps);
            }
            foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All().ToList())
            {
                if (corps.sector_city_id != city.id || corps.task != "defend" || FrontSystem.Hostile(kingdom, owner)) continue;
                Actor general = General(corps);
                if (general == null) continue;
                float roll = UnityEngine.Random.value;
                if (roll < MartyrChance)
                {
                    string place = city.GetCityName();
                    Report(kingdom, general, string.Format(LM.Get("battle_general_martyr_log"), place, general.getName()));
                    BiographySystem.Kill(general, string.Format(LM.Get("death_cause_martyr"), place), victor);
                }
                else if (roll < MartyrChance + FallCaptureChance && attacker != null)
                    Captured(city, kingdom, general, attacker, victor);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][战场] {city.data.name} 城破结算统领失败: {exception.Message}");
        }
    }

    private static void Announce(Kingdom kingdom, Actor general, string text, bool history)
    {
        BiographySystem.Note(general, text);
        FrontSystem.Log(kingdom, $"{general.getName()}：{text}");
    }

    // 军报都记；名将名臣、君主、储君还进史书与世界日志
    private static void Report(Kingdom kingdom, Actor general, string text, bool force = false)
    {
        FrontSystem.Log(kingdom, text);
        bool notable = force || general.isKing() || FamousTalentSystem.IsFamous(general) ||
                       kingdom.HasHeir() && kingdom.GetHeir() == general;
        if (!notable) return;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && !empire.isRekt()) EventRecorder.Record(empire, text, general, kingdom);
        else EventRecorder.Record(kingdom, text, general);
    }
}

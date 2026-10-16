using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 民族统一运动(玩家反馈"进入现代之后国家太碎"，用户 2026-10-16 选定)：进入现代后每个有法理头衔的王国都各自建政，
// 原来的诸侯小国成了一堆小国。现代政权里同文化的小国每年有机会并入接壤的同文化大国(大国至少 6 城、且是小国两倍以上，
// 双方没在打仗)。东西方不同(用户："西方虽然可以是一个帝国但是法理就是很碎""东方一直都很统一")：
//   · 大一统文化(strong_unification，如华夏)：不超过 5 城的小国都会被吸纳，每年 15%，民族国家学说 +8%、大众政治 +5%、电报 +4%；
//   · 其余文化(如西方)：法理本就破碎，小国林立是常态——只有本国采纳了民族国家学说、且与大国同属一个法理帝国核心
//     (如同在神罗之内的德意志、意大利诸邦)，不超过 3 城的小国才会并入，每年 4%，大众政治 +3%、电报 +2%；
//     不同法理核心的小国(如低地、瑞士)一直独立。
// 小国的政府解散，各成员整体并入大国，记进大国史书。
public static class NationalUnificationSystem
{
    private const int SmallCitiesWest = 3, SmallCitiesEast = 5, LargeCities = 6;
    private static readonly List<(long small, long large)> Pending = new();

    public static void ResetWorldState() => Pending.Clear();

    // 每个帝国的年度结算里调用：只登记，真正合并在 Tick 里做(不在遍历帝国列表时解散帝国)
    public static void Consider(Empire small)
    {
        if (!IsModern(small) || small.CoreKingdom.hasEnemies()) return;
        string culture = CultureService.GetRealmCulture(small.CoreKingdom);
        if (!CultureService.IsValidCulture(culture)) return;
        bool east = WarlordEraSystem.HasStrongUnification(culture);
        Kingdom core = small.CoreKingdom;
        if (Cities(small) > (east ? SmallCitiesEast : SmallCitiesWest)) return;
        // 西方：没有民族国家学说就没有统一运动
        if (!east && !TechnologySystem.RealmHasTech(core, "nationalism")) return;
        EmpireCore lawful = Lawful(small);
        Empire large = Neighbours(small)
            .Where(other => IsModern(other) && CultureService.GetRealmCulture(other.CoreKingdom) == culture &&
                            Cities(other) >= Math.Max(LargeCities, Cities(small) * 2) &&
                            !small.CoreKingdom.isEnemy(other.CoreKingdom) && !other.CoreKingdom.hasEnemies() &&
                            (east || lawful != null && Lawful(other) == lawful))
            .OrderByDescending(Cities).FirstOrDefault();
        if (large == null) return;
        float chance;
        if (east)
        {
            chance = 0.15f;
            if (TechnologySystem.RealmHasTech(core, "nationalism")) chance += 0.08f;
            if (TechnologySystem.RealmHasTech(core, "mass_politics")) chance += 0.05f;
            if (TechnologySystem.RealmHasTech(core, "telegraph")) chance += 0.04f;
        }
        else
        {
            chance = 0.04f;
            if (TechnologySystem.RealmHasTech(core, "mass_politics")) chance += 0.03f;
            if (TechnologySystem.RealmHasTech(core, "telegraph")) chance += 0.02f;
        }
        if (IdeologyDynamicsSystem.GetLiberation(large) > 0f) chance += 0.03f;
        if (UnityEngine.Random.value < chance) Pending.Add((small.id, large.id));
    }

    public static void Tick()
    {
        if (Pending.Count == 0 || ModClass.EMPIRE_MANAGER == null) return;
        (long smallId, long largeId) = Pending[0];
        Pending.RemoveAt(0);
        Empire small = ModClass.EMPIRE_MANAGER.ToList().FirstOrDefault(empire => empire?.id == smallId);
        Empire large = ModClass.EMPIRE_MANAGER.ToList().FirstOrDefault(empire => empire?.id == largeId);
        if (!IsModern(small) || !IsModern(large) || small == large || small.CoreKingdom.isEnemy(large.CoreKingdom)) return;
        try
        {
            Merge(small, large);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][民族统一] 合并失败: {exception.Message}");
        }
    }

    private static void Merge(Empire small, Empire large)
    {
        string smallName = small.GetEmpireFullName(), largeName = large.GetEmpireFullName();
        List<Kingdom> members = small.kingdoms_list.Where(member => member != null && !member.isRekt()).ToList();
        if (!members.Contains(small.CoreKingdom)) members.Insert(0, small.CoreKingdom);
        ModClass.EMPIRE_MANAGER.dissolveEmpire(small);
        int joined = 0;
        foreach (Kingdom member in members)
        {
            if (member?.data == null || member.isRekt()) continue;
            if (member.IsInEmpire()) member.GetEmpire()?.leave(member);
            FeudalVassalService.Break(member);
            large.join(member, pForce: true, pLegitimacyTransfer: true);
            if (member.GetEmpire() != large) continue;
            member.GetOrCreate().ideology_country_suffix = "";
            member.updateColor(large.CoreKingdom.getColor());
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
            joined++;
        }
        if (joined == 0) return;
        string text = string.Format(LM.Get("national_unification_history"), smallName, largeName);
        EventRecorder.Record(large, text);
        LogService.LogInfo($"[EmpireCraft][民族统一] {text}");
    }

    // 法理上属于哪个帝国核心(军阀时期另立的临时子核心按上级核心算)
    private static EmpireCore Lawful(Empire empire)
    {
        EmpireCore core = EmpireCoreManager.GetRiseCandidateCore(empire?.CoreKingdom) ?? EmpireCoreManager.Get(empire);
        if (core == null || core.false_core_against_empire_id > 0) return null;
        return core.warlord_parent_core_id > 0 ? EmpireCoreManager.Get(core.warlord_parent_core_id) ?? core : core;
    }

    private static bool IsModern(Empire empire) =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom != null &&
        !empire.CoreKingdom.isRekt() && empire.CoreKingdom.GetRegime()?.type == RegimeType.Modern;

    private static int Cities(Empire empire) =>
        empire.kingdoms_list?.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt()).Sum(kingdom => kingdom.cities.Count) ?? 0;

    private static IEnumerable<Empire> Neighbours(Empire empire)
    {
        var result = new HashSet<Empire>();
        foreach (Kingdom kingdom in empire.kingdoms_list.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt()))
        foreach (City city in kingdom.cities.Where(city => city != null && !city.isRekt()))
        foreach (City neighbour in (IEnumerable<City>)city.neighbours_cities ?? Array.Empty<City>())
        {
            Empire other = neighbour?.kingdom?.GetEmpire();
            if (other != null && other != empire) result.Add(other);
        }
        return result;
    }
}

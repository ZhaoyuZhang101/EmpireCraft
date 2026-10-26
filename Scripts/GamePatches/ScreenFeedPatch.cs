using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 屏幕左上角的提示(原版 HistoryHud)：用户 2026-10-27 嫌"提示太多了""看都看不过来"，问答选"只上大事""至多 5 条"。
// 只管上不上屏，所有提示照旧进世界日志窗口(原版 WorldLogMessage.add 另行写库)，能翻到——
//   · 模组的寻常提示(TranslateHelper.LogEventOnly：任命统帅、转运使、市场、矿藏、理念传播之类)不上屏；
//   · 模组进史书的提示(LogEventMessage)只上帝国或大国(帝国连同成员算，至少 MajorCities 城)的；
//   · 原版：宣战、议和只上牵涉帝国或大国的；君主即位、驾崩、遇刺、出逃只上帝国中央的(皇帝)；
//     立国、亡国、建城、城叛、结盟、王族这类不上屏；天灾、宠儿身死、国家崩解照上；
//   · 模组自己的提示种类：新帝即位、权臣篡位、太后另立、复国、帝国崩溃分治偏安、称帝、宗室即位这类大事上屏，
//     法理、改名、文化、派系、官职、律令、爵位等日常不上屏；
//   · 别的模组的提示照上；
//   · 同一种提示两秒内只上一条；屏上至多 MaxShown 条，满了新的顶掉最旧的。
public class ScreenFeedPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    private const int MaxShown = 5, MajorCities = 8;
    private const float RepeatSeconds = 2f;

    public enum Level { None, Minor, History, Major }

    // 眼下正在发的模组通用提示是哪一档、关于哪国(TranslateHelper 发之前设，原版 add 同步上屏，发完复原)
    public static Level Pending = Level.None;
    public static Kingdom PendingKingdom;
    // 原版的宣战、议和提示不带国家(只有国名)：发之前在原版 WorldLog 里算好牵不牵涉帝国或大国
    private static bool? _warMajor;

    private static readonly Dictionary<string, float> LastShown = new();
    private static HashSet<string> _major;

    private static readonly HashSet<string> VanillaWars = new() { "diplomacy_war_started", "diplomacy_war_ended", "total_war_started" };
    private static readonly HashSet<string> VanillaKings = new() { "king_new", "king_dead", "king_killed", "king_left", "king_fled_capital", "king_fled_city" };
    private static readonly HashSet<string> VanillaAlways = new() { "favorite_dead", "favorite_killed", "kingdom_shattered", "kingdom_fractured" };
    private static readonly HashSet<string> VanillaMinor = new()
    {
        "city_new", "log_city_revolted", "alliance_new", "alliance_dissolved", "kingdom_new", "kingdom_destroyed",
        "kingdom_royal_clan_new", "kingdom_royal_clan_changed", "kingdom_royal_clan_dead", "city_destroyed", "auto_tester"
    };

    public void Initialize()
    {
        try
        {
            var harmony = new Harmony(nameof(ScreenFeedPatch));
            harmony.Patch(AccessTools.Method(typeof(HistoryHud), nameof(HistoryHud.newHistory)),
                prefix: new HarmonyMethod(typeof(ScreenFeedPatch), nameof(BeforeNewHistory)));
            var finalizer = new HarmonyMethod(typeof(ScreenFeedPatch), nameof(AfterWarLog));
            harmony.Patch(AccessTools.Method(typeof(WorldLog), nameof(WorldLog.logNewWar)),
                prefix: new HarmonyMethod(typeof(ScreenFeedPatch), nameof(BeforeNewWar)), finalizer: finalizer);
            harmony.Patch(AccessTools.Method(typeof(WorldLog), nameof(WorldLog.logWarEnded)),
                prefix: new HarmonyMethod(typeof(ScreenFeedPatch), nameof(BeforeWarEnded)), finalizer: finalizer);
            harmony.Patch(AccessTools.Method(typeof(WorldLog), nameof(WorldLog.logNewTotalWar)),
                prefix: new HarmonyMethod(typeof(ScreenFeedPatch), nameof(BeforeTotalWar)), finalizer: finalizer);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 屏幕提示筛选未生效: {exception.Message}");
        }
    }

    public static void BeforeNewWar(Kingdom pKingdom1, Kingdom pKingdom2) => _warMajor = MajorRealm(pKingdom1) || MajorRealm(pKingdom2);

    public static void BeforeWarEnded(War pWar) => _warMajor = MajorRealm(pWar?.main_attacker) || MajorRealm(pWar?.main_defender);

    public static void BeforeTotalWar(Kingdom pKingdom) => _warMajor = MajorRealm(pKingdom);

    public static Exception AfterWarLog(Exception __exception)
    {
        _warMajor = null;
        return __exception;
    }

    // 模组自己的提示种类里算大事的
    private static HashSet<string> Major()
    {
        if (_major != null) return _major;
        _major = new HashSet<string>();
        foreach (WorldLogAsset asset in new[]
                 {
                     EmpireCraftWorldLogLibrary.history_new_emperor, EmpireCraftWorldLogLibrary.history_new_emperor_west,
                     EmpireCraftWorldLogLibrary.powerful_minister_controls_court_log,
                     EmpireCraftWorldLogLibrary.powerful_minister_usurpation_log,
                     EmpireCraftWorldLogLibrary.empress_dowager_installs_son_log, EmpireCraftWorldLogLibrary.restore_historcial_empire,
                     EmpireCraftWorldLogLibrary.empire_pianan, EmpireCraftWorldLogLibrary.empire_crashed,
                     EmpireCraftWorldLogLibrary.empire_division_log, EmpireCraftWorldLogLibrary.become_new_empire_log,
                     EmpireCraftWorldLogLibrary.become_new_empire_west_log, EmpireCraftWorldLogLibrary.minister_aqcuire_empire_log,
                     EmpireCraftWorldLogLibrary.royal_king_become_emperor_log
                 })
            if (asset?.id != null) _major.Add(asset.id);
        return _major;
    }

    private static bool IsModAsset(string id) =>
        EmpireCraftWorldLogLibrary.empirecraft_event_log?.id == id || IsModLibraryAsset(id);

    // 模组提示库里登记过的种类(按字段反射一次)
    private static HashSet<string> _modAssets;

    private static bool IsModLibraryAsset(string id)
    {
        if (_modAssets == null)
        {
            _modAssets = new HashSet<string>();
            foreach (var field in typeof(EmpireCraftWorldLogLibrary).GetFields(global::System.Reflection.BindingFlags.Public |
                                                                              global::System.Reflection.BindingFlags.Static))
                if (field.FieldType == typeof(WorldLogAsset) && field.GetValue(null) is WorldLogAsset asset && asset.id != null)
                    _modAssets.Add(asset.id);
        }
        return _modAssets.Contains(id);
    }

    // 帝国(连同成员)，或至少 MajorCities 城的大国
    private static bool MajorRealm(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        Kingdom realm = FrontSystem.Realm(kingdom) ?? kingdom;
        if (realm.IsEmpire() || kingdom.IsEmpire()) return true;
        return (realm.cities?.Count ?? 0) >= MajorCities;
    }

    private static bool Show(WorldLogMessage message)
    {
        string id = message?.asset_id;
        if (string.IsNullOrEmpty(id)) return true;
        if (id == EmpireCraftWorldLogLibrary.empirecraft_event_log?.id)
            return Pending == Level.Major || Pending == Level.History && MajorRealm(PendingKingdom ?? message.kingdom);
        if (id.StartsWith("worldlog_disaster_", StringComparison.Ordinal) || VanillaAlways.Contains(id)) return true;
        if (VanillaWars.Contains(id)) return _warMajor ?? MajorRealm(message.kingdom);
        if (VanillaKings.Contains(id))
        {
            Kingdom kingdom = message.kingdom;
            return kingdom?.data != null && !kingdom.isRekt() && kingdom.IsEmpire();
        }
        if (VanillaMinor.Contains(id)) return false;
        if (IsModAsset(id)) return Major().Contains(id);
        return true;
    }

    public static bool BeforeNewHistory(HistoryHud __instance, WorldLogMessage pMessage)
    {
        try
        {
            if (!Show(pMessage)) return false;
            // 同一种提示(模组通用提示按内容)两秒内只上一条
            string key = pMessage.asset_id == EmpireCraftWorldLogLibrary.empirecraft_event_log?.id
                ? pMessage.special1 ?? ""
                : pMessage.asset_id ?? "";
            float now = Time.unscaledTime;
            if (LastShown.TryGetValue(key, out float at) && now >= at && now - at < RepeatSeconds) return false;
            if (LastShown.Count > 500) LastShown.Clear();
            LastShown[key] = now;
            // 屏上至多 MaxShown 条：新的顶掉最旧的
            while (__instance._history_items.Count >= MaxShown) __instance.makeInactive(__instance._history_items[0]);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 屏幕提示筛选出错: {exception.Message}");
        }
        return true;
    }
}

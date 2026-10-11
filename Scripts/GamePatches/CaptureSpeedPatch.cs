using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GamePatches;

// 攻城速度。原版 City.updateCapture 里攻城点数(_capturing_units)只决定谁占上风，
// 真正的占领进度 _capture_ticks 每 0.1 秒固定 +1、满 100 占领；这里按倍率放大每次的进度增量：
//   开国气象期间(Empire.InFoundingGrace)的帝国来攻：×FoundingMultiplier；
//   民心归附(LandEconomySystem.WouldSurrenderTo：本城土地兼并严重、来攻者兼并轻得多)：×SurrenderMultiplier，
//   因此陷落时发世界提示"开城归降"。
public class CaptureSpeedPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    private const float FoundingMultiplier = 2f;
    private const float SurrenderMultiplier = 4f;
    private const float MinMilitiaFactor = 0.25f;

    public void Initialize()
    {
        new Harmony(nameof(CaptureSpeedPatch)).Patch(
            AccessTools.Method(typeof(City), "updateCapture"),
            prefix: new HarmonyMethod(typeof(CaptureSpeedPatch), nameof(Before)),
            postfix: new HarmonyMethod(typeof(CaptureSpeedPatch), nameof(After)));
    }

    private static void Before(City __instance, out (float ticks, Kingdom owner) __state)
    {
        __state = (__instance._capture_ticks, __instance.kingdom);
    }

    private static void After(City __instance, (float ticks, Kingdom owner) __state)
    {
        if (__instance == null || __instance.isRekt() ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (__instance.kingdom != __state.owner) return;   // 这一帧已经易手
        Kingdom attacker = __instance.being_captured_by;
        float delta = __instance._capture_ticks - __state.ticks;
        if (attacker == null || attacker.isRekt() || delta <= 0f) return;

        float multiplier = 1f;
        if (attacker.GetEmpire()?.InFoundingGrace == true) multiplier *= FoundingMultiplier;
        bool surrender = LandEconomySystem.WouldSurrenderTo(__instance, attacker, out float landless);
        if (surrender) multiplier *= SurrenderMultiplier;
        // 无小人模式：没有守兵的城由百姓组织团练抵抗，人口越多越难攻(每 100 户慢一倍，最多慢到 1/4)；民心归附时不抵抗
        if (!surrender && CityPopulationSystem.AbstractPopulationEnabled && __instance.countWarriors() == 0)
            multiplier *= UnityEngine.Mathf.Max(MinMilitiaFactor,
                1f / (1f + CityPopulationSystem.Households(__instance) / 100f));
        if (UnityEngine.Mathf.Approximately(multiplier, 1f)) return;

        __instance._capture_ticks += delta * (multiplier - 1f);
        if (__instance._capture_ticks < 100f) return;
        string cityName = __instance.GetCityName();
        Kingdom oldOwner = __instance.kingdom;
        __instance.finishCapture(attacker);
        if (surrender && __instance.kingdom != oldOwner)
            TranslateHelper.LogEventMessage(string.Format(LM.Get("land_surrender_event"), cityName,
                landless * 100f, __instance.kingdom.GetKingdomName()), __instance.kingdom);
    }
}

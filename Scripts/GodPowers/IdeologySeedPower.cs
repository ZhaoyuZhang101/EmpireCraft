using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GodPowers;

// Uses the vanilla seed brush interaction, but applies conviction immediately to residents
// touched by the falling seed instead of creating a biome drop.
public static class IdeologySeedPower
{
    private const string Prefix = "ideology_seed_";
    private static readonly Dictionary<string, PartyIdeology> Ideologies = new();

    // GodPower.getLocaleID() 会把名称转成小写下划线格式；资产 ID 直接使用相同格式，
    // 避免按钮、选中提示与本地化分别请求不同大小写的键。
    public static string Id(PartyIdeology ideology) => Prefix + ideology.ToString().ToLowerInvariant();

    private static bool _tooltipPatched;

    public static void Init()
    {
        PatchTooltip();
        PowerLibrary powers = AssetManager.powers;
        foreach (PartyIdeology ideology in Enum.GetValues(typeof(PartyIdeology)))
        {
            string id = Id(ideology);
            Ideologies[id] = ideology;
            if (powers.get(id) != null) continue;
            var power = new GodPower
            {
                id = id,
                name = id,
                hold_action = true,
                show_tool_sizes = true,
                unselect_when_window = true,
                falling_chance = 0.35f,
                type = PowerActionType.PowerSpawnSeeds,
                mouse_hold_animation = MouseHoldAnimation.Sprinkle,
                force_map_mode = MetaTypeExtension.Ideology,
                sound_drawing = "event:/SFX/POWERS/SeedsGrass",
                surprises_units = false,
                click_power_action = ApplySeed,
                click_power_brush_action = powers.loopWithCurrentBrushPowerForDropsFull
            };
            power.click_power_action += powers.fmodDrawingSound;
            power.click_power_brush_action += powers.flashBrushPixelsDuringClick;
            powers.add(power);
        }
    }

    // 种子类神力的按钮提示走原版"biome_seed"模板，要读生物群系资产；理念种子没有生物群系，
    // 提示就只剩"Normal Tooltip"和一排空图标。改走普通提示，显示本地化的名称和说明。
    private static void PatchTooltip()
    {
        if (_tooltipPatched) return;
        _tooltipPatched = true;
        try
        {
            new Harmony("EmpireCraft.IdeologySeedTooltip").Patch(
                AccessTools.Method(typeof(PowerButton), "showTooltip"),
                prefix: new HarmonyMethod(typeof(IdeologySeedPower), nameof(BeforeShowTooltip)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 理念种子提示补丁失败: {exception.Message}");
        }
    }

    private static bool BeforeShowTooltip(PowerButton __instance)
    {
        GodPower power = __instance?.godPower;
        if (power == null || !Ideologies.ContainsKey(power.id)) return true;
        if (InputHelpers.mouseSupported && !Config.tooltips_active) return false;
        Tooltip.show(__instance, "normal", new TooltipData
        {
            tip_name = power.id,
            tip_description = power.id + "_description"
        });
        return false;
    }

    private static bool ApplySeed(WorldTile tile, GodPower power)
    {
        if (tile == null || power == null || !Ideologies.TryGetValue(power.id, out PartyIdeology ideology))
            return false;
        bool changed = false;
        tile.doUnits(actor =>
        {
            if (actor == null || actor.isRekt() || !actor.isAlive() || actor.city == null || actor.IsWarMachine())
                return;
            IdeologyPopulationSystem.Set(actor, ideology);
            LayerCityCache.Invalidate(actor.city);
            actor.startColorEffect();
            changed = true;
        });
        return changed;
    }
}

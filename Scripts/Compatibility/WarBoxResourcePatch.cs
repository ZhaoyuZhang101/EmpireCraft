using System;
using System.Collections.Generic;
using System.Reflection;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Compatibility;

// WarBox 现代内容接入战略矿产(见 MineralResourceSystem)：
//   - 火药厂：每 20 秒用硝石 2 + 硫磺 1 + 煤 1 造 6 份火药(有大学再 +3)；缺料时退回原来的 20 金子换火药，产量减半；
//   - 钢铁厂(无小人模式，没有工人)：见 MineralResourceSystem.SmeltSteel；
//   - 坦克、装甲车、飞机：工厂出一辆扣一份材料(金属、石油，飞机要铝和稀土)，城里不够就不造；
//   - 核打击：每次消耗全国库存的 UraniumPerStrike 份铀，不够就不能发动(AI 也不会谋划)；
//   - 顺带修 WarBox 动员计划结束时目标国已灭亡的空引用。
// 全部用反射按名字找 WarBox 的方法，找不到(没装 WarBox 或改了名)就跳过，照原版运行。
public class WarBoxResourcePatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public const int UraniumPerStrike = 10;

    public void Initialize()
    {
        var harmony = new Harmony(nameof(WarBoxResourcePatch));
        TryPatch(harmony, "WarBox.Content.Patch_GunpowderMill", "Prefix", nameof(BeforeGunpowderMill), null);
        TryPatch(harmony, "WarBox.Content.VehicleLimits", "CanProduce", null, nameof(AfterCanProduce));
        TryPatch(harmony, "WarMobilization.NuclearStrikeSystem", "CanStart", null, nameof(AfterNuclearCanStart));
        TryPatch(harmony, "WarMobilization.NuclearStrikeSystem", "CanBeForced", null, nameof(AfterNuclearCanStart));
        TryPatch(harmony, "WarMobilization.NuclearStrikeSystem", "StartPaidPlot", nameof(BeforeStartPaidPlot),
            nameof(AfterStartPaidPlot));
        // WarBox 动员计划结束时直接读目标国 id：目标国(多是叛乱政权)已经灭亡时空引用，整条 AI 批处理报错
        TryPatch(harmony, "WarMobilization.MobilizationSystem", "FinishPlot", nameof(BeforeMobilizationFinish), null);
    }

    private static bool Dead(Kingdom kingdom) => kingdom == null || kingdom.data == null || !kingdom.isAlive();

    public static bool BeforeMobilizationFinish(object[] __args, ref bool __result)
    {
        if (__args == null || __args.Length < 1 || __args[0] is not Actor actor) return true;
        if (actor.plot != null && actor.plot.target_kingdom != null && Dead(actor.plot.target_kingdom))
            actor.plot.target_kingdom = null;
        if (actor.plot?.target_kingdom == null && Dead(actor.kingdom))
        {
            __result = true;
            return false;
        }
        return true;
    }

    private static void TryPatch(Harmony harmony, string typeName, string method, string prefix, string postfix)
    {
        try
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo target = type == null ? null : AccessTools.Method(type, method);
            if (target == null) return;
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(WarBoxResourcePatch), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(WarBoxResourcePatch), postfix));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] WarBox 资源接入未生效({typeName}.{method}): {exception.Message}");
        }
    }

    // ---- 城市库存 ----
    private static int Amount(City city, string id)
    {
        try
        {
            return AssetManager.resources.get(id) == null ? 0 : Mathf.Max(0, city.getResourcesAmount(id));
        }
        catch
        {
            return 0;
        }
    }

    private static bool Has(City city, (string id, int amount)[] cost)
    {
        foreach ((string id, int amount) in cost)
            if (Amount(city, id) < amount) return false;
        return true;
    }

    private static void Take(City city, (string id, int amount)[] cost)
    {
        foreach ((string id, int amount) in cost)
            if (amount > 0) city.takeResource(id, amount);
    }

    // ---- 火药厂 ----
    private const float GunpowderInterval = 20f;
    private static readonly (string, int)[] BlackPowder = { ("saltpeter", 2), ("sulfur", 1), ("coal", 1) };
    private static readonly Dictionary<long, float> GunpowderTimers = new();

    // 取代 WarBox 原来的火药厂结算(原方法是 City.update 的前缀，参数为城市和经过时间)
    public static bool BeforeGunpowderMill(object[] __args)
    {
        try
        {
            if (__args == null || __args.Length < 2 || __args[0] is not City city || !city.isAlive()) return false;
            float elapsed = __args[1] is float f ? f : 0f;
            if (city.countBuildingsType("type_gunpowder_mill", true) <= 0) return false;
            if (!GunpowderTimers.TryGetValue(city.data.id, out float timer)) timer = GunpowderInterval;
            timer -= elapsed;
            if (timer > 0f)
            {
                GunpowderTimers[city.data.id] = timer;
                return false;
            }
            GunpowderTimers[city.data.id] = GunpowderInterval;
            int bonus = city.countBuildingsType("type_university", true) > 0 ? 3 : 0;
            if (Has(city, BlackPowder))
            {
                Take(city, BlackPowder);
                city.addResourcesToRandomStockpile("gunpowder", 6 + bonus);
            }
            else if (Amount(city, "gold") >= 20)
            {
                city.takeResource("gold", 20);
                city.addResourcesToRandomStockpile("gunpowder", 3 + bonus / 2);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 火药厂结算失败: {exception.Message}");
        }
        return false;
    }

    // ---- 载具材料 ----
    private static (string, int)[] VehicleCost(string id) => id switch
    {
        "warbox_tank" or "warbox_ifv" => new[] { ("common_metals", 20), ("oil", 5) },
        "warbox_apc" or "warbox_zsu" or "warbox_spg" or "warbox_heavy_artillery" =>
            new[] { ("common_metals", 10), ("oil", 3) },
        "warbox_gunboat" => new[] { ("common_metals", 15), ("oil", 4) },
        "warbox_helicopter" => new[] { ("aluminium", 6), ("oil", 5), ("rare_earth", 1) },
        "warbox_fighter" => new[] { ("aluminium", 10), ("oil", 6), ("rare_earth", 2) },
        "warbox_bomber" => new[] { ("aluminium", 14), ("oil", 8), ("rare_earth", 2) },
        _ => id != null && id.StartsWith("warbox_") ? new[] { ("common_metals", 10), ("oil", 3) } : null
    };

    // 原方法只在工厂马上要造一辆时调用：够料就扣料放行，不够就不造
    public static void AfterCanProduce(object[] __args, ref bool __result)
    {
        if (!__result || __args == null || __args.Length < 2 || __args[0] is not City city) return;
        (string, int)[] cost = VehicleCost(__args[1] as string);
        if (cost == null) return;
        try
        {
            if (!Has(city, cost))
            {
                __result = false;
                return;
            }
            Take(city, cost);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 载具扣料失败: {exception.Message}");
        }
    }

    // ---- 核打击用铀 ----
    public static int KingdomUranium(Kingdom kingdom)
    {
        int total = 0;
        if (kingdom?.cities == null) return 0;
        foreach (City city in kingdom.cities)
            if (city != null && city.isAlive()) total += Amount(city, "uranium");
        return total;
    }

    private static bool TakeKingdomUranium(Kingdom kingdom, int amount)
    {
        if (KingdomUranium(kingdom) < amount) return false;
        foreach (City city in kingdom.cities)
        {
            if (amount <= 0) break;
            if (city == null || !city.isAlive()) continue;
            int take = Mathf.Min(amount, Amount(city, "uranium"));
            if (take <= 0) continue;
            city.takeResource("uranium", take);
            amount -= take;
        }
        return true;
    }

    // 已经付了铀、正在立项的君主(立项时 WarBox 会再检查一遍发起条件，不能再拦)
    private static readonly HashSet<long> Paid = new();

    public static void AfterNuclearCanStart(object[] __args, ref bool __result)
    {
        if (!__result || __args == null || __args.Length < 1 || __args[0] is not Actor actor) return;
        if (Paid.Contains(actor.getID())) return;
        if (KingdomUranium(actor.kingdom) < UraniumPerStrike) __result = false;
    }

    public static bool BeforeStartPaidPlot(object[] __args, ref bool __result)
    {
        if (__args == null || __args.Length < 1 || __args[0] is not Actor actor || actor.kingdom == null) return true;
        if (!TakeKingdomUranium(actor.kingdom, UraniumPerStrike))
        {
            if (__args.Length >= 6) __args[5] = "insufficient_uranium";
            WorldTip.showNow(string.Format(LM.Get("nuclear_no_uranium"), UraniumPerStrike), false, "top", 3f);
            __result = false;
            return false;
        }
        Paid.Add(actor.getID());
        return true;
    }

    public static void AfterStartPaidPlot(object[] __args, bool __result)
    {
        if (__args == null || __args.Length < 1 || __args[0] is not Actor actor) return;
        bool paid = Paid.Remove(actor.getID());
        // 立项失败：铀退回首都
        if (!__result && paid && actor.kingdom?.capital != null)
            actor.kingdom.capital.addResourcesToRandomStockpile("uranium", UraniumPerStrike);
    }
}

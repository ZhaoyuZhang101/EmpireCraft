using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using EmpireCraft.Scripts.HelperFunc;

namespace EmpireCraft.Scripts.GamePatches;

public class TreasuryTooltipPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(TreasuryTooltipPatch)).Patch(
            AccessTools.Method(typeof(TooltipLibrary), "showCity", new[] { typeof(string), typeof(Tooltip), typeof(TooltipData) }),
            transpiler: new HarmonyMethod(GetType(), nameof(CityMoney)));
    }

    // 城市、家园、首都提示共用这条原版入口。仅替换资金读取，保留原版其它提示内容。
    public static IEnumerable<CodeInstruction> CityMoney(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        var leader = AccessTools.Field(typeof(City), nameof(City.leader));
        var personalMoney = AccessTools.PropertyGetter(typeof(Actor), nameof(Actor.money));
        var treasury = AccessTools.Method(typeof(TreasuryTooltipPatch), nameof(CityTreasury));
        var line = AccessTools.Method(typeof(Tooltip), nameof(Tooltip.addLineIntText), new[] { typeof(string), typeof(int), typeof(string), typeof(bool) });
        var formattedLine = AccessTools.Method(typeof(TreasuryTooltipPatch), nameof(AddMoneyLine));
        int replaced = 0;
        for (int i = 0; i + 1 < codes.Count; i++)
        {
            if (codes[i].opcode != OpCodes.Ldfld || !Equals(codes[i].operand, leader) ||
                !Equals(codes[i + 1].operand, personalMoney)) continue;
            codes[i].opcode = OpCodes.Nop;
            codes[i].operand = null;
            codes[i + 1].opcode = OpCodes.Call;
            codes[i + 1].operand = treasury;
            replaced++;
        }
        if (replaced == 0) LogService.LogWarning("[EmpireCraft] City tooltip treasury read could not be patched.");
        foreach (var code in codes)
            if (line != null && Equals(code.operand, line)) { code.opcode = OpCodes.Call; code.operand = formattedLine; }
        return codes;
    }

    public static void AddMoneyLine(Tooltip tooltip, string key, int value, string color, bool localize)
    {
        if (key == "ruler_money") tooltip.addLineText(key, MoneyDisplay.Format(value), color, pLocalize: localize);
        else tooltip.addLineIntText(key, value, color, localize);
    }

    public static int CityTreasury(City city) => city == null ? 0 :
        AncientWarfareCompatibility.OwnsObject(city) ? city.leader?.money ?? 0 : city.GetMoney();
}

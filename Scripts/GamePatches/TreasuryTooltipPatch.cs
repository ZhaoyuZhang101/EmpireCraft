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
        for (int i = 0; i + 4 < codes.Count; i++)
        {
            // 原版链为leader.money、颜色、是否翻译、整数行。完整匹配后再同时
            // 换成字符串读取及字符串行，不能只改一半导致IL栈类型不一致。
            if (codes[i].opcode != OpCodes.Ldfld || !Equals(codes[i].operand, leader) ||
                !Equals(codes[i + 1].operand, personalMoney) || line == null ||
                !Equals(codes[i + 4].operand, line)) continue;
            codes[i].opcode = OpCodes.Nop;
            codes[i].operand = null;
            codes[i + 1].opcode = OpCodes.Call;
            codes[i + 1].operand = treasury;
            codes[i + 4].opcode = OpCodes.Call;
            codes[i + 4].operand = formattedLine;
            replaced++;
        }
        if (replaced == 0) LogService.LogWarning("[EmpireCraft] City tooltip treasury read could not be patched.");
        return codes;
    }

    public static void AddMoneyLine(Tooltip tooltip, string key, string value, string color, bool localize)
        => tooltip.addLineText(key, value, color, pLocalize: localize);

    public static string CityTreasury(City city) => MoneyDisplay.Format(city == null ? 0L :
        AncientWarfareCompatibility.OwnsObject(city) ? city.leader?.money ?? 0 : city.GetTreasuryBalance());
}

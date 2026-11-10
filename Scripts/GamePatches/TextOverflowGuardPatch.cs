using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.GamePatches;

// 一段 UI 文字的网格超过 65000 个顶点(每字 4 个，描边再乘 5)时，Unity 的 VertexHelper.FillMesh 抛
// "Mesh can not have more than 65000 vertices"，之后每次重建都抛，报错控制台一开就刷满(11-11 那局连报 216 次)。
// 这里只接住这一种异常：头一回记下是哪个物体、多少字，下一帧把它的字删到放得下——
// 多行的从头删整行(控制台、日志类最新的在末尾)，单行的去掉格式标记后截短
public class TextOverflowGuardPatch : GamePatch
{
    private const int MaxVertices = 65000;
    // 留一成余量；不确定格式标记、空白算不算顶点，按都算来估
    private const float Margin = 0.9f;
    private static readonly Regex Tags = new("<[^>]*>");
    private static readonly HashSet<int> Reported = new();
    private static readonly List<Text> Pending = new();

    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(TextOverflowGuardPatch)).Patch(
            AccessTools.Method(typeof(Text), "UpdateGeometry"),
            finalizer: new HarmonyMethod(GetType(), nameof(Finalizer))
        );
    }

    public static Exception Finalizer(Text __instance, Exception __exception)
    {
        if (__exception is not ArgumentException || __exception.Message == null ||
            !__exception.Message.Contains(MaxVertices.ToString())) return __exception;
        if (__instance != null)
        {
            if (Reported.Add(__instance.GetInstanceID()))
                LogService.LogWarning($"[EmpireCraft][文字过长] {PathOf(__instance.transform)} 共 {__instance.text?.Length ?? 0} 字，" +
                                      "网格超过 65000 顶点，删减后再显示");
            if (!Pending.Contains(__instance)) Pending.Add(__instance);
        }
        return null;
    }

    public static void Tick()
    {
        if (Pending.Count == 0) return;
        foreach (Text text in Pending)
        {
            if (text == null || string.IsNullOrEmpty(text.text)) continue;
            text.text = Shrink(text.text, Budget(text));
        }
        Pending.Clear();
    }

    // 放得下的字数：每字 4 个顶点，描边(Outline)×5、阴影(Shadow)×2
    private static int Budget(Text text)
    {
        int factor = 1;
        foreach (Shadow effect in text.GetComponents<Shadow>())
            if (effect != null && effect.enabled) factor *= effect is Outline ? 5 : 2;
        return Mathf.Max(100, (int)(MaxVertices * Margin / (4 * factor)));
    }

    private static string Shrink(string value, int budget)
    {
        if (Count(value) <= budget) budget = Count(value) * 6 / 10; // 估少了还是放不下：再砍四成
        if (value.IndexOf('\n') >= 0)
        {
            string[] lines = value.Split('\n');
            int total = 0, start = lines.Length;
            while (start > 0 && total + Count(lines[start - 1]) <= budget) total += Count(lines[--start]);
            if (start < lines.Length) return string.Join("\n", lines, start, lines.Length - start);
        }
        string plain = Tags.Replace(value, "");
        return plain.Length <= budget ? plain : plain.Substring(0, Mathf.Max(0, budget - 1)) + "…";
    }

    private static int Count(string value)
    {
        int count = 0;
        foreach (char c in value)
            if (!char.IsWhiteSpace(c)) count++;
        return count;
    }

    private static string PathOf(Transform transform)
    {
        string path = transform.name;
        for (int i = 0; i < 6 && transform.parent != null; i++)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}

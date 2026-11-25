using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 手机上的像素面板(用户 2026-11-25 看过示意图："边框…很简陋，也没有很像素风""也不一定全是按钮""比如barchart, slide button什么的""种类丰富点，
// 只要能提高逻辑清晰度的都可以")：一页备成几张卡 {cards:[{title,icon,tone,note,items:[…]}]}，卡里一件件控件，手机照着摆(见网页里的 xPanel)。
// 字都在这里按游戏的语言取好。色调字母：g 好 / b 坏 / i 消息 / a 留意 / o 要紧 / m 平常。
// 控件(t)：head 小标题 · note 一句话 · sl 滑块(不给 id 的只是读数条) · sw 开关 · seg 分段选择 · stack 堆叠条 · track 阶段轨道 · todo 还差什么
//   · bars 柱状图 · tiles 指标格 · row 一行 · who 人物卡(读数条 + 筹码) · build 营建格 · acts 当场办的事 · chart 走势。
// 滑块发的命令是 id + ":" + 档位；开关、分段的选项各带自己的命令 id；都照政令走(一个月后办)
public static partial class TerminalHost
{
    private static JObject XCard(string title, string icon, string tone = null)
    {
        var card = new JObject { ["title"] = title, ["icon"] = icon, ["items"] = new JArray() };
        if (tone != null) card["tone"] = tone;
        return card;
    }

    private static JObject XAdd(this JObject card, JObject item)
    {
        if (item != null) ((JArray)card["items"]).Add(item);
        return card;
    }

    private static bool XEmpty(JObject card) => ((JArray)card["items"]).Count == 0;

    private static JObject XPanelOf(string title, IEnumerable<JObject> cards)
    {
        var list = new JArray();
        foreach (JObject card in cards)
            if (card != null && !XEmpty(card)) list.Add(card);
        return list.Count == 0 ? null : new JObject { ["title"] = title, ["cards"] = list };
    }

    private static JObject XHead(string text) => new() { ["t"] = "head", ["text"] = text };

    private static JObject XNote(string text, string tone = null)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var note = new JObject { ["t"] = "note", ["text"] = text };
        if (tone != null) note["tone"] = tone;
        return note;
    }

    // 读数条：到哪了、警戒线在哪
    private static JObject XGauge(string label, float value, float max, string text, string tone = null, string icon = null, float mark = -1f,
        string sub = null)
    {
        var bar = new JObject { ["t"] = "sl", ["label"] = label, ["value"] = Mathf.RoundToInt(value), ["max"] = Mathf.RoundToInt(max), ["text"] = text };
        if (tone != null) bar["tone"] = tone;
        if (icon != null) bar["icon"] = icon;
        if (mark >= 0f) bar["mark"] = Mathf.RoundToInt(mark);
        if (sub != null) bar["sub"] = sub;
        return bar;
    }

    // 滑块：names 是各档的叫法(没有就显示数加 unit)，subs 是拖到各档时底下那句话
    private static JObject XSlider(string id, string label, int value, int min, int max, int step = 1, string unit = null, string icon = null,
        string tone = null, int mark = int.MinValue, string sub = null, string off = null, IEnumerable<string> names = null,
        IEnumerable<string> subs = null)
    {
        var bar = new JObject { ["t"] = "sl", ["id"] = id, ["label"] = label, ["value"] = value, ["min"] = min, ["max"] = max, ["step"] = step };
        if (unit != null) bar["unit"] = unit;
        if (icon != null) bar["icon"] = icon;
        if (tone != null) bar["tone"] = tone;
        if (mark != int.MinValue) bar["mark"] = mark;
        if (sub != null) bar["sub"] = sub;
        if (off != null) bar["off"] = off;
        if (names != null) bar["names"] = new JArray(names);
        if (subs != null) bar["subs"] = new JArray(subs);
        return bar;
    }

    private static JObject XSwitch(string id, string label, bool on, string sub = null, string off = null, string icon = null)
    {
        var sw = new JObject { ["t"] = "sw", ["id"] = id, ["label"] = label, ["on"] = on };
        if (sub != null) sw["sub"] = sub;
        if (off != null) sw["off"] = off;
        if (icon != null) sw["icon"] = icon;
        return sw;
    }

    private static JObject XOpt(string id, string label, bool on, string note = null, string off = null)
    {
        var opt = new JObject { ["label"] = label, ["on"] = on };
        if (!on && id != null) opt["id"] = id;
        if (note != null) opt["note"] = note;
        if (off != null && !on) opt["off"] = off;
        return opt;
    }

    private static JObject XSeg(string label, string icon, IEnumerable<JObject> opts, string sub = null)
    {
        var seg = new JObject { ["t"] = "seg", ["label"] = label, ["opts"] = new JArray(opts) };
        if (icon != null) seg["icon"] = icon;
        if (sub != null) seg["sub"] = sub;
        return seg;
    }

    private static JObject XStack(IEnumerable<(string label, float value, string color)> parts, bool count = false)
    {
        var list = new JArray();
        foreach ((string label, float value, string color) in parts)
            if (value > 0f) list.Add(new JObject { ["label"] = label, ["v"] = Math.Round(value, 1), ["c"] = color });
        if (list.Count == 0) return null;
        var stack = new JObject { ["t"] = "stack", ["parts"] = list };
        if (count) stack["count"] = true;
        return stack;
    }

    // 阶段轨道：now 是走到第几格(从 0 数)，bad 为真时这一格标红
    private static JObject XTrack(IList<string> steps, int now, bool bad = false)
    {
        var list = new JArray();
        for (int index = 0; index < steps.Count; index++)
            list.Add(new JObject { ["label"] = steps[index], ["s"] = index < now ? "done" : index == now ? bad ? "bad" : "now" : "" });
        return new JObject { ["t"] = "track", ["steps"] = list };
    }

    private static JObject XTodo(IEnumerable<(string label, bool ok)> list)
    {
        var rows = new JArray();
        foreach ((string label, bool ok) in list) rows.Add(new JObject { ["label"] = label, ["ok"] = ok });
        return rows.Count == 0 ? null : new JObject { ["t"] = "todo", ["list"] = rows };
    }

    // 柱状图：一组几根柱子，图例按柱子的次序给颜色
    private static JObject XBars(IEnumerable<(string label, string icon, float[] values)> groups, IEnumerable<(string label, string color)> legend,
        bool thin = false)
    {
        var list = new JArray();
        foreach ((string label, string icon, float[] values) in groups)
        {
            var group = new JObject { ["v"] = new JArray(values.Select(value => Math.Round(value, 1))) };
            if (label != null) group["label"] = label;
            if (icon != null) group["icon"] = icon;
            list.Add(group);
        }
        if (list.Count == 0) return null;
        var bars = new JObject { ["t"] = "bars", ["groups"] = list };
        var keys = new JArray();
        foreach ((string label, string color) in legend) keys.Add(new JObject { ["label"] = label, ["c"] = color });
        bars["legend"] = keys;
        if (thin) bars["thin"] = true;
        return bars;
    }

    private static JObject XTile(string icon, string label, string value, string sub = null, string tone = null, string open = null, string go = null)
    {
        var tile = new JObject { ["label"] = label, ["value"] = value };
        if (icon != null) tile["icon"] = icon;
        if (sub != null) tile["sub"] = sub;
        if (tone != null) tile["tone"] = tone;
        if (open != null) tile["open"] = open;
        if (go != null) tile["go"] = go;
        return tile;
    }

    private static JObject XTiles(int cols, IEnumerable<JObject> tiles)
    {
        var list = new JArray(tiles.Where(tile => tile != null));
        return list.Count == 0 ? null : new JObject { ["t"] = "tiles", ["cols"] = cols, ["list"] = list };
    }

    private static JObject XRow(string label, string right, string tone = null, string sub = null, string icon = null, string open = null,
        string go = null)
    {
        var row = new JObject { ["t"] = "row", ["label"] = label ?? "", ["right"] = right ?? "" };
        if (tone != null) row["tone"] = tone;
        if (sub != null) row["sub"] = sub;
        if (icon != null) row["icon"] = icon;
        if (open != null) row["open"] = open;
        if (go != null) row["go"] = go;
        return row;
    }

    // 筹码、营建格里的一件事：点了弹出来确认(note 是代价与后果)，off 是眼下办不了的缘由
    private static JObject XChip(string id, string label, string icon = null, string note = null, string off = null, string tone = null,
        string choice = null)
    {
        var chip = new JObject { ["id"] = id, ["label"] = label };
        if (icon != null) chip["icon"] = icon;
        if (note != null) chip["note"] = note;
        if (off != null) chip["off"] = off;
        if (tone != null) chip["tone"] = tone;
        if (choice != null) chip["choice"] = choice;
        return chip;
    }

    private static JObject XActs(IEnumerable<JObject> acts)
    {
        var list = new JArray(acts.Where(act => act != null));
        return list.Count == 0 ? null : new JObject { ["t"] = "acts", ["list"] = list };
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

// 原版性能面板：打开原版自带(平时藏在调试菜单里)的计时窗口，并把原版的计时录成 CSV——
//   Benchmark All：整帧各段(属性重算、决策、寻敌、元对象检查、统计数据库写入、渲染……)；
//   Benchmark Actors：角色批处理的各个作业。
// 第一次点击：打开窗口并开始录制，每 RecordSeconds 现实秒把原版 Bench 的全部计时组写一次；再点一次停止。
// 计时数据完全来自原版自己的 Bench，模组不额外计时。
// 输出：LocalLow/mkarpenko/WorldBox/EmpireCraftBench/bench_年月日_时分秒.csv
public static class VanillaBenchmarkButton
{
    private static readonly (string id, int x)[] Tools = { ("Benchmark All", 80), ("Benchmark Actors", 380) };

    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = "vanilla_benchmark",
            name = "vanilla_benchmark",
            select_button_action = Toggle
        });
    }

    private static bool Toggle(string pPowerID)
    {
        if (VanillaBenchRecorder.Recording)
        {
            string path = VanillaBenchRecorder.Stop();
            WorldTip.showNow(string.Format(NeoModLoader.General.LM.Get("vanilla_benchmark_stopped"), path), false, "top", 6f);
            return false;
        }
        if (DebugConfig.instance != null)
        {
            foreach ((string id, int x) in Tools)
            {
                try
                {
                    if (AssetManager.debug_tool_library?.get(id) == null) continue;
                    DebugConfig.createTool(id, x);
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft] 打开原版性能面板失败({id}): {exception.Message}");
                }
            }
        }
        Bench.bench_enabled = true;
        string file = VanillaBenchRecorder.Start();
        WorldTip.showNow(string.Format(NeoModLoader.General.LM.Get("vanilla_benchmark_started"), file), false, "top", 6f);
        return false;
    }
}

// 录制原版 Bench 计时：每个窗口一行一个计时项，附帧率、人口、速度、是否暂停
public class VanillaBenchRecorder : MonoBehaviour
{
    private const float RecordSeconds = 5f;
    private static VanillaBenchRecorder _instance;
    private static string _path;

    private float _timer;
    private int _frames;
    private float _frameTime;
    private float _worstFrame;

    public static bool Recording => _instance != null && _instance.enabled;

    public static string Start()
    {
        string folder = Path.Combine(Application.persistentDataPath, "EmpireCraftBench");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, $"bench_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(_path,
            "time,window_s,fps_avg,frame_ms_worst,population,speed,paused,group,entry,avg_ms,max_ms,avg_calls\n",
            Encoding.UTF8);
        if (_instance == null)
        {
            var host = new GameObject("EmpireCraftBenchRecorder");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<VanillaBenchRecorder>();
        }
        _instance.ResetWindow();
        _instance.enabled = true;
        return _path;
    }

    public static string Stop()
    {
        if (_instance != null)
        {
            _instance.Flush();
            _instance.enabled = false;
        }
        return _path;
    }

    private void ResetWindow()
    {
        _timer = 0f;
        _frames = 0;
        _frameTime = 0f;
        _worstFrame = 0f;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _timer += dt;
        _frames++;
        _frameTime += dt;
        if (dt > _worstFrame) _worstFrame = dt;
        if (_timer >= RecordSeconds) Flush();
    }

    private void Flush()
    {
        if (_frames == 0 || string.IsNullOrEmpty(_path)) return;
        try
        {
            var inv = CultureInfo.InvariantCulture;
            string time = DateTime.Now.ToString("HH:mm:ss", inv);
            float fps = _frames / Mathf.Max(0.0001f, _frameTime);
            int population = World.world?.units?.Count ?? 0;
            string speed = Config.time_scale_asset?.id ?? "";
            bool paused = World.world != null && World.world.isPaused();
            string prefix = string.Join(",", time, _timer.ToString("0.0", inv), fps.ToString("0.0", inv),
                (_worstFrame * 1000f).ToString("0.0", inv), population.ToString(inv), speed, paused ? "1" : "0");
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, BenchmarkGroup> group in Bench.dict)
            {
                if (group.Value?.dict_data == null) continue;
                foreach (KeyValuePair<string, ToolBenchmarkData> entry in group.Value.dict_data)
                {
                    ToolBenchmarkData data = entry.Value;
                    if (data == null) continue;
                    double avg = data.getAverage();
                    if (double.IsNaN(avg)) continue;
                    sb.Append(prefix).Append(',').Append(Csv(group.Key)).Append(',').Append(Csv(entry.Key)).Append(',')
                        .Append((avg * 1000.0).ToString("0.000", inv)).Append(',')
                        .Append((data.last_max_value * 1000.0).ToString("0.000", inv)).Append(',')
                        .Append(data.getAverageCount().ToString(inv)).Append('\n');
                }
            }
            File.AppendAllText(_path, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 写入原版性能记录失败: {exception.Message}");
        }
        ResetWindow();
    }

    private static string Csv(string value) =>
        value != null && (value.Contains(",") || value.Contains("\"")) ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}

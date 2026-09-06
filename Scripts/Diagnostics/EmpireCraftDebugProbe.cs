using System;
using System.Collections.Generic;
using System.IO;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Diagnostics;

public sealed class EmpireCraftDebugProbeSettings
{
    public bool enabled;
    public bool pauseOnHit = true;
    public bool includeStackTrace;
    public List<string> probes = new();
}

/// <summary>
/// Runtime probes for retail Unity builds where a managed debugger cannot attach.
/// Settings are reloaded after each file change, so probes can be armed in-game.
/// </summary>
public static class EmpireCraftDebugProbe
{
    private const string ConfigFileName = "debug_probe.local.json";
    private const string LogFileName = "EmpireCraft.debug.log";
    private static readonly Dictionary<string, string> ObservedStates = new();
    private static EmpireCraftDebugProbeSettings _settings = new();
    private static string _configPath;
    private static string _logPath;
    private static DateTime _lastConfigWriteUtc = DateTime.MinValue;
    private static bool _reportedConfigError;

    public static void Initialize()
    {
        string folder = ModClass._declare?.FolderPath;
        if (string.IsNullOrWhiteSpace(folder)) return;
        _configPath = Path.Combine(folder, ConfigFileName);
        _logPath = Path.Combine(folder, LogFileName);
        try
        {
            if (!File.Exists(_configPath))
            {
                var template = new EmpireCraftDebugProbeSettings
                {
                    probes = new List<string>
                    {
                        "powerful_minister.candidate",
                        "powerful_minister.progress"
                    }
                };
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(template, Formatting.Indented));
            }
            ReloadSettings(force: true);
            LogService.LogInfo("[EmpireCraft] Runtime debug probe config: " + _configPath);
        }
        catch (Exception error)
        {
            _settings = new EmpireCraftDebugProbeSettings();
            LogService.LogWarning("[EmpireCraft] Runtime debug probes are unavailable: " + error.Message);
        }
    }

    public static bool Hit(string probe, Func<string> details = null)
    {
        if (!IsArmed(probe)) return false;
        string detailText;
        try
        {
            detailText = details?.Invoke() ?? string.Empty;
        }
        catch (Exception error)
        {
            detailText = "Failed to collect probe details: " + error;
        }

        string message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [PROBE:{probe}] {detailText}";
        if (_settings.includeStackTrace)
            message += Environment.NewLine + Environment.StackTrace;
        LogService.LogWarning(message);
        try
        {
            File.AppendAllText(_logPath, message + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception error)
        {
            LogService.LogWarning("[EmpireCraft] Failed to write runtime probe log: " + error.Message);
        }
        if (_settings.pauseOnHit) Config.paused = true;
        return true;
    }

    public static bool Observe(string probe, string scope, string state, Func<string> details = null)
    {
        if (!IsArmed(probe)) return false;
        scope ??= "<global>";
        state ??= "<null>";
        string observationKey = probe + ":" + scope;
        if (ObservedStates.TryGetValue(observationKey, out string previous) && previous == state) return false;
        ObservedStates[observationKey] = state;
        return Hit(probe, details);
    }

    private static bool IsArmed(string probe)
    {
        ReloadSettings(force: false);
        if (!_settings.enabled || string.IsNullOrWhiteSpace(probe)) return false;
        return _settings.probes == null || _settings.probes.Count == 0 ||
            _settings.probes.Contains("*") || _settings.probes.Contains(probe);
    }

    private static void ReloadSettings(bool force)
    {
        if (string.IsNullOrWhiteSpace(_configPath) || !File.Exists(_configPath)) return;
        DateTime writeTime = File.GetLastWriteTimeUtc(_configPath);
        if (!force && writeTime == _lastConfigWriteUtc) return;
        _lastConfigWriteUtc = writeTime;
        try
        {
            _settings = JsonConvert.DeserializeObject<EmpireCraftDebugProbeSettings>(
                File.ReadAllText(_configPath)) ?? new EmpireCraftDebugProbeSettings();
            _reportedConfigError = false;
        }
        catch (Exception error)
        {
            _settings = new EmpireCraftDebugProbeSettings();
            if (_reportedConfigError) return;
            _reportedConfigError = true;
            LogService.LogWarning("[EmpireCraft] Invalid runtime debug probe config: " + error.Message);
        }
    }
}

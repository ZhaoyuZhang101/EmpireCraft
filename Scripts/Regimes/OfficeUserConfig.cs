using System;
using System.Collections.Generic;
using System.IO;
using NeoModLoader.services;
using Newtonsoft.Json;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.Regimes;
public static class OfficeUserConfig
{
    public static Dictionary<RegimeType, BureauConfig> Config = new();
    private static string GetPath()
    {
        var parentFolder = Directory.GetParent(ModClass._declare.FolderPath)?.FullName;
        return parentFolder == null ? null : Path.Combine(parentFolder, "PlayerOfficeConfig.json");
    }
    public static bool Save(RegimeType regimeType, BureauConfig bureau)
    {
        try
        {
            var path = GetPath();
            if (path == null) return false;
            Config[regimeType] = bureau;
            var text = JsonConvert.SerializeObject(Config, Formatting.Indented);
            File.WriteAllText(path, text);
            LogService.LogInfo("储存用户官位配置成功");
            return true;
        }
        catch (Exception e)
        {
            LogService.LogInfo("储存用户官位配置失败: " + e.Message);
            return false;
        }
    }
    public static void Load()
    {
        try
        {
            var path = GetPath();
            if (path == null) return;
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                Config = JsonConvert.DeserializeObject<Dictionary<RegimeType, BureauConfig>>(text) ?? new();
                LogService.LogInfo("加载用户官位配置成功");
            }
            else
            {
                LogService.LogInfo("无用户官位配置");
            }
        }
        catch (Exception e)
        {
            LogService.LogInfo("加载用户官位配置失败: " + e.Message);
        }
    }
    public static bool Remove(RegimeType regimeType)
    {
        var removed = Config.Remove(regimeType);
        var path = GetPath();
        if (path == null) return false;
        File.WriteAllText(path, JsonConvert.SerializeObject(Config, Formatting.Indented));
        return removed;
    }
}

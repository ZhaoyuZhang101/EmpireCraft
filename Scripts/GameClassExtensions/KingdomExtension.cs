using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class KingdomExtension
{
    private static readonly ConditionalWeakTable<Kingdom, ExtraData> _data
        = new ConditionalWeakTable<Kingdom, ExtraData>();
    private class ExtraData
    {
        public countryLevel country_level = countryLevel.countrylevel_4;
        public long vassaled_kingdom_id = -1L;
        public long empireID = -1L;
        public double timestamp_empire = -1L;
    }

    public static void SetEmpireID(this Kingdom kingdom, long value)
    {
        _data.GetOrCreateValue(kingdom).empireID = value;
    }
    public static long GetEmpireID(this Kingdom kingdom)
    {
        return _data.GetOrCreateValue(kingdom).empireID;
    }    
    public static Empire GetEmpire(this Kingdom kingdom)
    {
        if (ModClass.EMPIRE_MANAGER == null) return null;
        return ModClass.EMPIRE_MANAGER.get(_data.GetOrCreateValue(kingdom).empireID);
    }

    public static void SetTimestampEmpire(this Kingdom kingdom, double value)
    {
        _data.GetOrCreateValue(kingdom).timestamp_empire = value;
    }
    public static double GetTimestampEmpire(this Kingdom kingdom)
    {
        return _data.GetOrCreateValue(kingdom).timestamp_empire;
    }

    public static void empireJoin(this Kingdom kingdom, Empire pEmpire)
    {
        kingdom.SetVassaledKingdomID(pEmpire.empire.id);
        _data.GetOrCreateValue(kingdom).empireID = pEmpire.data.id;
        _data.GetOrCreateValue(kingdom).timestamp_empire = World.world.getCurWorldTime();
    }

    public static bool isEmpire(this Kingdom kingdom)
    {
        return _data.GetOrCreateValue(kingdom).vassaled_kingdom_id == kingdom.id;
    }

    public static void empireLeave (this Kingdom kingdom)
    {
        countryLevel country_level = _data.GetOrCreateValue(kingdom).country_level;
        string country_level_string = "default_" + country_level.ToString();
        string kingdomName = "";
        if (kingdom.cities.Count > 0)
        {
            City city = kingdom.cities.GetRandom();
            kingdomName = city.SelectKingdomName();
        }
         
        if (kingdomName == null || kingdomName == "")
        {
            kingdom.data.name = kingdom.data.name.Split(' ')[0] + " " + LM.Get(country_level_string) + LM.Get("Country");
        }
        else
        {
            kingdom.data.name = kingdomName + " " + LM.Get(country_level_string) + LM.Get("Country");
            LogService.LogInfo("存在历史国家名称" + kingdomName);
        }
        kingdom.generateColor();
        kingdom.updateColor(kingdom.getColor());
        _data.GetOrCreateValue(kingdom).empireID = -1L;
        _data.GetOrCreateValue(kingdom).vassaled_kingdom_id = -1L;
    }

    public static countryLevel GetCountryLevel(this Kingdom kingdom)
    {
        return _data.GetOrCreateValue(kingdom).country_level;
    }

    public static void SetCountryLevel(this Kingdom kingdom, countryLevel value)
    {
        string kingdomOriginalName = kingdom.name.Split(' ')[0];
        string kingdomBack = LM.Get("default_" + value.ToString())+ LM.Get("Country");
        kingdom.data.name = String.Join(" ", kingdomOriginalName, kingdomBack) ;
        _data.GetOrCreateValue(kingdom).country_level = value;
    }    
    public static long GetVassaledKingdomID(this Kingdom kingdom)
    {
        return _data.GetOrCreateValue(kingdom).vassaled_kingdom_id;
    }

    public static bool isInEmpire(this Kingdom kingdom)
    {
        return _data.GetOrCreateValue(kingdom).empireID != -1L;
    }

    public static void SetVassaledKingdomID(this Kingdom kingdom, long value)
    {
        if (value !=-1L)
        {
            //设置国家归属后，将原国家标记为省份， 并依据王国等级决定省份等级
            countryLevel country_level = _data.GetOrCreateValue(kingdom).country_level;
            string level = country_level.ToString().Split('_').Last();
            string province_level_name = "provincelevel";
            string province_level_string = "";
            switch (kingdom.species_id)
            {
                case "human":
                    province_level_string = String.Join("_", ModClass.HUMAN_CULTURE, province_level_name, level);
                    break;
                case "orc":
                    province_level_string = String.Join("_", ModClass.ORC_CULTURE, province_level_name, level);
                    break;
                case "elf":
                    province_level_string = String.Join("_", ModClass.ELF_CULTURE, province_level_name, level);
                    break;
                case "dwarf":
                    province_level_string = String.Join("_", ModClass.DWARF_CULTURE, province_level_name, level);
                    break;
                default:
                    province_level_string = String.Join("_", ModClass.DWARF_CULTURE, province_level_name, level);
                    break;
            }
            foreach (City city in kingdom.cities)
            {
                city.AddKingdomName(kingdom.name.Split(' ')[0]);
            }
            string province_name = kingdom.capital.name.Split(' ')[0] + " " + LM.Get(province_level_string);
            LogService.LogInfo(String.Format("国家{0}已转变为省份：{1}", kingdom.name, province_name));
            kingdom.data.name = province_name;
            _data.GetOrCreateValue(kingdom).vassaled_kingdom_id = value;
        } else
        {
            kingdom.empireLeave();
        }

    }

    public static void removeExtensionData(this Kingdom kingdom)
    {
        LogService.LogInfo($"{kingdom.name}"+"移除，正在清除数据");
        _data.Remove(kingdom);
    }
}
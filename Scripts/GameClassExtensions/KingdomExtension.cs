using EmpireCraft.Scripts.Enums;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class KingdomExtension
{
    private static readonly ConditionalWeakTable<Kingdom, ExtraData> _data
        = new ConditionalWeakTable<Kingdom, ExtraData>();
    private class ExtraData
    {
        public countryLevel country_level;
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
}
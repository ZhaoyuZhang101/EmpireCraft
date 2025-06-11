using EmpireCraft.Scripts.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class CityExtension
{
    private static readonly ConditionalWeakTable<City, ExtraData> _data
        = new ConditionalWeakTable<City, ExtraData>();
    private class ExtraData
    {
        public long province_id;
    }
    public static long GetProvince_id(this City city)
    {
        return _data.GetOrCreateValue(city).province_id;
    }

    public static void SetProvince_id(this City city, long value)
    {
        _data.GetOrCreateValue(city).province_id = value;
    }
}